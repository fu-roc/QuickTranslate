using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace QuickTranslate.Services;

public static class OcrService
{
    private const double TargetGlyphHeight = 24.0;
    private const double MaxScale = 4.0;

    /// <summary>放大后的像素上限（约 64 MB 位图），防止整屏框选 ×4 时把内存吃满。</summary>
    private const long MaxPixels = 16_000_000;

    private const int MinRecognizedCharacters = 12;
    private const int EstimateSampleWidth = 800;

    // 空隙按字高归一化，中日韩文字和标点使用更宽松的阈值。
    private const double SpaceGapRatio = 0.18;
    private const double CjkGapRatio = 0.45;
    private const double PunctuationGapRatio = 0.55;

    private static readonly Lazy<OcrEngine?> Engine = new(CreateEngine);

    /// <summary>在内存中缩放和识别截图。</summary>
    public static async Task<string> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        OcrEngine? engine = Engine.Value;
        if (engine is null)
        {
            throw new OcrException(
                "系统缺少 OCR 语言包：请在「设置 → 时间和语言 → 语言和区域」里给中文或 English (United States) 添加「可选功能 → 光学字符识别」，然后重试。");
        }

        double glyphHeight = EstimateGlyphHeight(bitmap);
        double primary = LimitScale(bitmap, glyphHeight > 0.5 ? TargetGlyphHeight / glyphHeight : 2.0);
        string text = await RecognizeOnceAsync(engine, bitmap, primary, cancellationToken);
        int count = InkCount(text);
        if (count >= MinRecognizedCharacters) return text;

        double alternate = LimitScale(bitmap, primary * 1.7);
        if (Math.Abs(alternate - primary) < 0.2) return text;

        string retry = await RecognizeOnceAsync(engine, bitmap, alternate, cancellationToken);
        int retryCount = InkCount(retry);
        LogService.Info($"OCR 补识别：首次得到 {count} 字，改用 {alternate:0.00} 倍后得到 {retryCount} 字");
        return retryCount > count ? retry : text;
    }

    private static async Task<string> RecognizeOnceAsync(
        OcrEngine engine, Bitmap bitmap, double scale, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using Bitmap? scaled = scale == 1.0 ? null : Resize(bitmap, scale);
        Bitmap target = scaled ?? bitmap;
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            using var memory = new MemoryStream();
            target.Save(memory, ImageFormat.Png);
            writer.WriteBytes(memory.ToArray());
            await writer.StoreAsync();
            writer.DetachStream(); // 解码前让流独立于 writer 的生命周期。
        }
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(softwareBitmap);
        cancellationToken.ThrowIfCancellationRequested();
        return BuildText(result);
    }

    /// <summary>非空白字符数，用来判断「这一遍到底认出来多少东西」。</summary>
    private static int InkCount(string text)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c)) count++;
        }
        return count;
    }

    /// <summary>
    /// 按包围盒把识别结果拼回文本：同一行内依据词间空隙决定要不要补空格，
    /// 行与行之间保留换行。识别不到任何词时退回原始文本。
    /// </summary>
    private static string BuildText(OcrResult result)
    {
        var builder = new StringBuilder();
        foreach (OcrLine line in result.Lines)
        {
            if (builder.Length > 0) builder.Append('\n');

            IReadOnlyList<OcrWord> words = line.Words;
            if (words.Count == 0)
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

    private static Bitmap Resize(Bitmap source, double scale)
    {
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, width, height);
        LogService.Info($"OCR 预处理：缩放 {scale:0.00} 倍（{width}×{height}）");
        return scaled;
    }

    /// <summary>
    /// 估计一行文字的高度：Otsu 二值化后按行统计墨迹像素，取连续墨迹带的长度中位数。
    /// 这是「该放大多少」的唯一依据 —— 小字必须放大，大字不该放大。
    /// </summary>
    private static double EstimateGlyphHeight(Bitmap source)
    {
        if (source.Width < 2 || source.Height < 2) return 0;

        int sampleWidth = Math.Min(source.Width, EstimateSampleWidth);
        double factor = (double)source.Width / sampleWidth;
        int sampleHeight = Math.Max(1, (int)Math.Round(source.Height / factor));
        if (sampleHeight < 2) return 0;

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

        int threshold = OtsuThreshold(gray);
        long sum = 0;
        foreach (byte value in gray) sum += value;
        bool lightText = sum / gray.Length < 128;

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
                    if (lightText ? value > threshold : value < threshold) count++;
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

        if (bands.Count == 0) return 0;
        bands.Sort();
        return bands[bands.Count / 2] * factor;
    }

    private static int OtsuThreshold(byte[] gray)
    {
        var histogram = new int[256];
        foreach (byte value in gray) histogram[value]++;

        double total = gray.Length;
        double sumAll = 0;
        for (int i = 0; i < 256; i++) sumAll += i * histogram[i];

        double sumBackground = 0;
        long countBackground = 0;
        double best = -1;
        int bestThreshold = 128;
        for (int t = 0; t < 256; t++)
        {
            countBackground += histogram[t];
            if (countBackground == 0) continue;
            long countForeground = (long)total - countBackground;
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

    /// <summary>优先选能处理中文字形的语言包。</summary>
    private static OcrEngine? CreateEngine()
    {
        Language[] available;
        try
        {
            available = OcrEngine.AvailableRecognizerLanguages.ToArray();
        }
        catch (Exception ex)
        {
            LogService.Warn("OCR 语言包列表读取失败", ex);
            available = Array.Empty<Language>();
        }

        static int Rank(string tag)
        {
            tag = tag.ToLowerInvariant();
            if (tag.StartsWith("zh", StringComparison.Ordinal)) return 0;
            if (tag.StartsWith("ja", StringComparison.Ordinal) || tag.StartsWith("ko", StringComparison.Ordinal)) return 1;
            return 2;
        }

        foreach (Language language in available.OrderBy(l => Rank(l.LanguageTag)))
        {
            try
            {
                OcrEngine? engine = OcrEngine.TryCreateFromLanguage(language);
                if (engine is not null)
                {
                    LogService.Info($"OCR 引擎：{language.LanguageTag}");
                    return engine;
                }
            }
            catch (Exception ex)
            {
                LogService.Warn($"OCR 语言包 {language.LanguageTag} 不可用", ex);
            }
        }

        try
        {
            OcrEngine? fallback = OcrEngine.TryCreateFromUserProfileLanguages();
            if (fallback is not null) LogService.Info("OCR 引擎：跟随系统语言偏好");
            return fallback;
        }
        catch (Exception ex)
        {
            LogService.Warn("OCR 引擎创建失败", ex);
            return null;
        }
    }
}
