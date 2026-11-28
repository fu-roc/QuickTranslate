using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace QuickTranslate.Services;

public static partial class OcrService
{
    private const double TargetGlyphHeight = 24.0;
    private const double MaxScale = 4.0;

    /// <summary>放大后的像素上限（约 64 MB 位图），防止整屏框选 ×4 时把内存吃满。</summary>
    private const long MaxPixels = 16_000_000;

    private const int EstimateSampleWidth = 800;

    // 空隙按字高归一化，中日韩文字和标点使用更宽松的阈值。
    private const double SpaceGapRatio = 0.18;
    private const double CjkGapRatio = 0.45;
    private const double PunctuationGapRatio = 0.55;

    [GeneratedRegex(@"[A-Za-z]+(?:['’-][A-Za-z]+)*")]
    private static partial Regex LatinWords();

    private readonly record struct ImageProfile(double GlyphHeight, int Dark, int Light, bool LightText)
    {
        public bool LowContrast => Light - Dark is >= 12 and < 155;
    }

    private readonly record struct Candidate(string Text, int Ink, int Latin, int EastAsian, int SpokenScript, double Score);

    /// <summary>在内存中缩放和识别截图。</summary>
    public static async Task<string> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Language[] languages = GetAvailableLanguages();
        OcrEngine? engine = languages.Select(TryCreateEngine).FirstOrDefault(candidate => candidate is not null)
            ?? TryCreateProfileEngine();
        return await RecognizeAsync(bitmap, languages, engine, cancellationToken);
    }

    private static async Task<string> RecognizeAsync(
        Bitmap bitmap, Language[] languages, OcrEngine? engine, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ImageProfile profile = InspectImage(bitmap);
        double scale = LimitScale(bitmap, profile.GlyphHeight > 0.5 ? TargetGlyphHeight / profile.GlyphHeight : 2.0);
        Candidate? windows = null;
        if (engine is not null)
        {
            try
            {
                windows = await ReadWindowsAsync(bitmap, languages, engine, profile, scale, cancellationToken);
            }
            catch (COMException ex)
            {
                LogService.Warn("Windows OCR 识别失败", ex);
            }
        }

        if (windows is not { } best)
        {
            return ReadBundledEnglish(bitmap, scale, profile, cancellationToken)?.Text
                ?? throw new OcrException("内置英文 OCR 无法启动。请重新解压或安装 QuickTranslate。");
        }

        // Windows 的中日韩引擎可能把英文误认成少量汉字或假名。复核时要求更高置信度和完整度。
        bool hasEastAsianText = best.EastAsian > 1;
        if ((!hasEastAsianText || best.EastAsian * 2 < best.Ink)
            && ReadBundledEnglish(bitmap, scale, profile, cancellationToken) is { } english
            && english.Confidence >= (hasEastAsianText ? 85 : 55))
        {
            Candidate reading = await ScoreAsync(english.Text, japanese: false, korean: false, cancellationToken);
            if (reading.Latin > 0 && (!hasEastAsianText || reading.Ink >= best.Ink) && reading.Score > best.Score + 0.25)
            {
                LogService.Info($"OCR：Tesseract 英文，识别 {reading.Ink} 个非空白字符");
                return reading.Text;
            }
        }

        return best.Text;
    }

    private static TesseractEnglishOcr.Reading? ReadBundledEnglish(
        Bitmap bitmap, double scale, ImageProfile profile, CancellationToken cancellationToken)
    {
        using Bitmap? scaled = scale == 1.0 && !profile.LowContrast ? null
            : Resize(bitmap, scale, profile.LowContrast ? profile : null);
        return TesseractEnglishOcr.TryRecognize(scaled ?? bitmap, cancellationToken);
    }

    private static async Task<Candidate> ReadWindowsAsync(
        Bitmap bitmap, Language[] languages, OcrEngine engine, ImageProfile profile,
        double scale, CancellationToken cancellationToken)
    {
        string firstLanguage = engine.RecognizerLanguage.LanguageTag;
        Candidate best;
        using (var prepared = await PrepareAsync(bitmap, scale, null, cancellationToken))
        {
            (engine, best) = await ReadWithMatchingLanguageAsync(languages, engine, prepared, cancellationToken);
        }

        bool tryVariant = profile.LowContrast || best.Ink <= 10 || scale >= 1.5;
        if (tryVariant)
        {
            double alternate = LimitScale(bitmap, profile.LowContrast ? scale * 0.9
                : best.Ink <= 10 ? scale * 1.45 : scale * (2.0 / 3));
            if (profile.LowContrast || Math.Abs(alternate - scale) >= 0.2)
            {
                using var otherImage = await PrepareAsync(bitmap, alternate,
                    profile.LowContrast ? profile : null, cancellationToken);
                Candidate reading = await ReadAsync(engine, otherImage, cancellationToken);
                if (reading.Score > best.Score + 0.25) best = reading;
            }
        }

        LogService.Info($"OCR：{firstLanguage} → {engine.RecognizerLanguage.LanguageTag}，识别 {best.Ink} 个非空白字符");
        return best;
    }

    private static async Task<(OcrEngine Engine, Candidate Reading)> ReadWithMatchingLanguageAsync(
        Language[] languages, OcrEngine engine, SoftwareBitmap prepared, CancellationToken cancellationToken)
    {
        Candidate best = await ReadAsync(engine, prepared, cancellationToken);
        // 中文模型也能读拉丁字母。原文主要是拉丁字母或识别遗漏严重时，让英语模型再试一次。
        if (!HasLanguage(engine.RecognizerLanguage, "en") && best.EastAsian <= 1)
        {
            OcrEngine? english = FindEngine(languages, "en");
            if (english is not null)
            {
                Candidate reading = await ReadAsync(english, prepared, cancellationToken);
                if (reading.Latin > 0 && reading.Score >= best.Score * 0.85)
                    (engine, best) = (english, reading);
            }
        }

        // 连续的假名或谚文比单个误识字符更能说明日文、韩文模型适合这张图。
        if (HasLanguage(engine.RecognizerLanguage, "zh") && (best.EastAsian > 0 || best.Ink <= 10))
        {
            foreach (string language in new[] { "ja", "ko" })
            {
                OcrEngine? native = FindEngine(languages, language);
                if (native is null) continue;
                Candidate reading = await ReadAsync(native, prepared, cancellationToken);
                if (reading.SpokenScript >= 3 && reading.Score > best.Score)
                    (engine, best) = (native, reading);
            }
        }

        return (engine, best);
    }

    private static async Task<SoftwareBitmap> PrepareAsync(
        Bitmap bitmap, double scale, ImageProfile? contrast, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using Bitmap? scaled = scale == 1.0 && contrast is null ? null : Resize(bitmap, scale, contrast);
        Bitmap target = scaled ?? bitmap;
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            using var memory = new MemoryStream();
            target.Save(memory, ImageFormat.Png);
            writer.WriteBytes(memory.ToArray());
            await writer.StoreAsync().AsTask(cancellationToken);
            writer.DetachStream(); // 解码前让流独立于 writer 的生命周期。
        }
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        return await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken);
    }

    private static async Task<Candidate> ReadAsync(
        OcrEngine engine, SoftwareBitmap bitmap, CancellationToken cancellationToken)
    {
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
        Language language = engine.RecognizerLanguage;
        bool chinese = HasLanguage(language, "zh");
        bool japanese = HasLanguage(language, "ja");
        bool korean = HasLanguage(language, "ko");
        string text = BuildText(result, chinese || japanese || korean);
        return await ScoreAsync(text, japanese, korean, cancellationToken);
    }

    private static async Task<Candidate> ScoreAsync(
        string text, bool japanese, bool korean, CancellationToken cancellationToken)
    {
        int ink = 0;
        int letters = 0;
        int eastAsian = 0;
        int scriptRun = 0;
        int longestScriptRun = 0;
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c)) ink++;
            if (char.IsLetterOrDigit(c)) letters++;
            if (c is >= '\u3400' and <= '\u9FFF' or >= '\u3040' and <= '\u30FF' or >= '\uAC00' and <= '\uD7AF')
                eastAsian++;
            if ((japanese && c is (>= '\u3041' and <= '\u3096' or >= '\u30A1' and <= '\u30FA')) ||
                (korean && c is >= '\uAC00' and <= '\uD7AF'))
                longestScriptRun = Math.Max(longestScriptRun, ++scriptRun);
            else
                scriptRun = 0;
        }

        int latin = 0;
        int dictionaryLetters = 0;
        if (text.Any(char.IsAsciiLetter))
        {
            var dictionary = await EnglishChineseDictionary.GetEntriesAsync(cancellationToken);
            foreach (Match word in LatinWords().Matches(text))
            {
                latin += word.Length;
                if (word.Length >= 2 && dictionary.ContainsKey(word.Value.Replace('’', '\'')))
                    dictionaryLetters += word.Length;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        double score = letters + dictionaryLetters * 0.85 + longestScriptRun * 1.5 + (ink - letters) * 0.15;
        return new Candidate(text, ink, latin, eastAsian, longestScriptRun, score);
    }

    /// <summary>
    /// 按包围盒把识别结果拼回文本：同一行内依据词间空隙决定要不要补空格，
    /// 行与行之间保留换行。识别不到任何词时退回原始文本。
    /// </summary>
    private static string BuildText(OcrResult result, bool rebuildSpacing)
    {
        var builder = new StringBuilder();
        foreach (OcrLine line in result.Lines)
        {
            if (builder.Length > 0) builder.Append('\n');

            IReadOnlyList<OcrWord> words = line.Words;
            if (!rebuildSpacing || words.Count == 0)
            {
                builder.Append(line.Text);
                continue;
            }

            for (int i = 0; i < words.Count; i++)
            {
                OcrWord current = words[i];
                if (i == 0)
                {
                    builder.Append(current.Text);
                    continue;
                }

                OcrWord previous = words[i - 1];
                double gap = current.BoundingRect.X - (previous.BoundingRect.X + previous.BoundingRect.Width);
                double height = Math.Max(previous.BoundingRect.Height, current.BoundingRect.Height);
                if (NeedsSpace(previous.Text, current.Text, height > 0 ? gap / height : double.PositiveInfinity))
                {
                    builder.Append(' ');
                }
                builder.Append(current.Text);
            }
        }

        string text = builder.ToString().Trim();
        return text.Length > 0 ? text : (result.Text?.Trim() ?? string.Empty);
    }

    private static bool NeedsSpace(string previous, string current, double gapRatio)
    {
        if (previous.Length == 0 || current.Length == 0) return false;

        char last = previous[^1];
        char first = current[0];
        if (IsCjkPunctuation(last) || IsCjkPunctuation(first)) return gapRatio >= PunctuationGapRatio;

        // 只有「汉字挨着汉字」才用宽阈值。中文夹英文时（例如「点击 Download 按钮」）
        // 那段空隙是真实存在的，仍按西文阈值判断，否则会粘成「点击Download按钮」。
        if (IsCjk(last) && IsCjk(first)) return gapRatio >= CjkGapRatio;
        return gapRatio >= SpaceGapRatio;
    }

    /// <summary>汉字、假名、谚文与全角符号：这类文字本身自带字距，不能一律按空格处理。</summary>
    private static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) ||
        (c >= 0x3400 && c <= 0x4DBF) ||
        (c >= 0x3000 && c <= 0x303F) ||
        (c >= 0xFF00 && c <= 0xFFEF) ||
        (c >= 0x3040 && c <= 0x30FF) ||
        (c >= 0xAC00 && c <= 0xD7AF);

    private static bool IsCjkPunctuation(char c) =>
        "，。、；：！？（）《》【】「」『』〈〉—…·～“”‘’".Contains(c);

    private static double LimitScale(Bitmap source, double preferred)
    {
        double byDimension = OcrEngine.MaxImageDimension / (double)Math.Max(source.Width, source.Height);
        double byPixels = Math.Sqrt(MaxPixels / (double)source.Width / source.Height);
        return Math.Min(Math.Clamp(preferred, 1.0, MaxScale), Math.Min(byDimension, byPixels));
    }

    private static Bitmap Resize(Bitmap source, double scale, ImageProfile? contrast)
    {
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        if (contrast is { } profile)
        {
            float gain = 255f / (profile.Light - profile.Dark);
            float offset = -profile.Dark * gain / 255f;
            if (profile.LightText)
            {
                gain = -gain;
                offset = 1 - offset;
            }

            float red = 0.299f * gain;
            float green = 0.587f * gain;
            float blue = 0.114f * gain;
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix([
                [red, red, red, 0, 0],
                [green, green, green, 0, 0],
                [blue, blue, blue, 0, 0],
                [0, 0, 0, 1, 0],
                [offset, offset, offset, 0, 1],
            ]));
            graphics.DrawImage(source, new Rectangle(0, 0, width, height),
                0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
        else
        {
            graphics.DrawImage(source, 0, 0, width, height);
        }
        LogService.Info($"OCR 预处理：缩放 {scale:0.00} 倍（{width}×{height}）");
        return scaled;
    }

    /// <summary>
    /// 估计一行文字的高度：Otsu 二值化后按行统计墨迹像素，取连续墨迹带的长度中位数。
    /// 这是「该放大多少」的唯一依据 —— 小字必须放大，大字不该放大。
    /// </summary>
    private static ImageProfile InspectImage(Bitmap source)
    {
        if (source.Width < 2 || source.Height < 2) return default;

        int sampleWidth = Math.Min(source.Width, EstimateSampleWidth);
        double factor = (double)source.Width / sampleWidth;
        int sampleHeight = Math.Max(1, (int)Math.Round(source.Height / factor));
        if (sampleHeight < 2) return default;

        byte[] gray = new byte[sampleWidth * sampleHeight];
        using (var sample = new Bitmap(sampleWidth, sampleHeight, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(sample))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, sampleWidth, sampleHeight);
            }

            BitmapData data = sample.LockBits(
                new Rectangle(0, 0, sampleWidth, sampleHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] buffer = new byte[Math.Abs(data.Stride) * sampleHeight];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                for (int y = 0; y < sampleHeight; y++)
                {
                    int row = y * data.Stride;
                    for (int x = 0; x < sampleWidth; x++)
                    {
                        int i = row + x * 4;
                        gray[y * sampleWidth + x] = (byte)(buffer[i + 2] * 0.299 + buffer[i + 1] * 0.587 + buffer[i] * 0.114);
                    }
                }
            }
            finally
            {
                sample.UnlockBits(data);
            }
        }

        int[] histogram = new int[256];
        long sum = 0;
        foreach (byte value in gray)
        {
            histogram[value]++;
            sum += value;
        }
        int dark = Percentile(histogram, Math.Max(1, gray.Length / 2000));
        int light = Percentile(histogram, gray.Length - gray.Length / 2000);
        bool lightText = sum / gray.Length < (dark + light) / 2;
        int threshold = OtsuThreshold(histogram, gray.Length, sum);

        int minInk = Math.Max(1, sampleWidth / 100);
        var bands = new List<int>();
        int start = -1;
        for (int y = 0; y <= sampleHeight; y++)
        {
            bool ink = false;
            if (y < sampleHeight)
            {
                int count = 0;
                for (int x = 0; x < sampleWidth; x++)
                {
                    byte value = gray[y * sampleWidth + x];
                    if (lightText ? value > threshold : value <= threshold) count++;
                }
                ink = count >= minInk;
            }

            if (ink && start < 0) start = y;
            else if (!ink && start >= 0)
            {
                bands.Add(y - start);
                start = -1;
            }
        }

        bands.Sort();
        double glyphHeight = bands.Count == 0 ? 0 : bands[bands.Count / 2] * factor;
        return new ImageProfile(glyphHeight, dark, light, lightText);
    }

    private static int Percentile(int[] histogram, int rank)
    {
        for (int value = 0; value < histogram.Length; value++)
        {
            rank -= histogram[value];
            if (rank <= 0) return value;
        }
        return 255;
    }

    private static int OtsuThreshold(int[] histogram, int total, long sumAll)
    {
        double sumBackground = 0;
        long countBackground = 0;
        double best = -1;
        int bestThreshold = 128;
        for (int t = 0; t < 256; t++)
        {
            countBackground += histogram[t];
            if (countBackground == 0) continue;
            long countForeground = total - countBackground;
            if (countForeground == 0) break;

            sumBackground += t * histogram[t];
            double meanBackground = sumBackground / countBackground;
            double meanForeground = (sumAll - sumBackground) / countForeground;
            double between = (double)countBackground * countForeground *
                             (meanBackground - meanForeground) * (meanBackground - meanForeground);
            if (between > best)
            {
                best = between;
                bestThreshold = t;
            }
        }
        return bestThreshold;
    }

    private static int EnginePreference(Language language)
    {
        if (HasLanguage(language, "zh")) return 0;
        if (HasLanguage(language, "ja") || HasLanguage(language, "ko")) return 1;
        return HasLanguage(language, "en") ? 2 : 3;
    }

    private static Language[] GetAvailableLanguages()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages.OrderBy(EnginePreference).ToArray();
        }
        catch (Exception ex)
        {
            LogService.Warn("OCR 语言包列表读取失败", ex);
            return [];
        }
    }

    private static OcrEngine? TryCreateProfileEngine()
    {
        try
        {
            return OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch (Exception ex)
        {
            LogService.Warn("OCR 引擎创建失败", ex);
            return null;
        }
    }

    private static OcrEngine? FindEngine(IEnumerable<Language> languages, string name) =>
        languages.Where(language => HasLanguage(language, name))
            .Select(TryCreateEngine).FirstOrDefault(candidate => candidate is not null);

    private static bool HasLanguage(Language language, string name) =>
        language.LanguageTag.Equals(name, StringComparison.OrdinalIgnoreCase) ||
        language.LanguageTag.StartsWith(name + "-", StringComparison.OrdinalIgnoreCase);

    private static OcrEngine? TryCreateEngine(Language language)
    {
        try
        {
            return OcrEngine.TryCreateFromLanguage(language);
        }
        catch (Exception ex)
        {
            LogService.Warn($"OCR 语言包 {language.LanguageTag} 不可用", ex);
            return null;
        }
    }
}
