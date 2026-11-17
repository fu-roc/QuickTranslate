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

/// <summary>
/// 截图 OCR。三条经验规则，全部来自实测（见 README「截图 OCR 是怎么提高准确率的」）：
/// 1) 字号决定成败：Windows.Media.Ocr 对 11~12px 的小字几乎不可用（实测整行识别率 0%），
///    放大到约 24px 行高后同样内容能到 100%。所以先估字高，再按需放大，而不是只在超过
///    MaxImageDimension 时才缩放。
/// 2) 不要单靠 <see cref="OcrResult.Text"/>：它是把每行用空格串起来的，中英文混排时会出现
///    「请 在 设 置 中」这种逐字空格，既难读也影响翻译。改为按每个词的包围盒重建文本。
/// 3) 深色背景不需要反色、也不需要转灰度：实测两者都没有稳定收益，反而会掉字，故不做。
/// </summary>
public static class OcrService
{
    /// <summary>放大后的目标字高（像素）。实测 18~32px 之间扫过一遍，24px 的平均准确率最高（93.7%）。</summary>
    private const double TargetGlyphHeight = 24.0;

    private const double MinScale = 1.0;

    /// <summary>放大上限。超过 4 倍只增加耗时和内存，实测准确率不再提升。</summary>
    private const double MaxScale = 4.0;

    /// <summary>放大后的像素上限（约 64 MB 位图），防止整屏框选 ×4 时把内存吃满。</summary>
    private const long MaxPixels = 16_000_000;

    /// <summary>识别结果少于这个字数就认为「基本没认出来」，换一个倍数再试一次。</summary>
    private const int MinRecognizedCharacters = 12;

    /// <summary>估算字高时先缩到这个宽度再统计行高，大图也不会慢。</summary>
    private const int EstimateSampleWidth = 800;

    /// <summary>
    /// 两个西文词之间算「有空格」的空隙阈值（相对字高）。
    /// 395 组真实词间空隙实测：真空格集中在 0.25~0.45，词内裂缝集中在 0.08~0.15；
    /// 逐个阈值试过后 0.18 的总判对率最高（94.7%）。
    /// </summary>
    private const double SpaceGapRatio = 0.18;

    /// <summary>
    /// 汉字与汉字之间的空隙阈值。汉字排版本就几乎没有字距（实测 0~0.08），
    /// 而识别器又爱把每个字拆成独立词，所以这里必须放得很宽，否则会变成「请 在 设 置 中」。
    /// </summary>
    private const double CjkGapRatio = 0.45;

    /// <summary>紧挨着中文标点时，只有明显的大空隙才算空格（例如「vl ， 23」里的两侧空隙）。</summary>
    private const double PunctuationGapRatio = 0.55;

    // 语言包在会话内是稳定的，没必要每次 OCR 都重新 TryCreateFromLanguage。
    private static readonly Lazy<OcrEngine?> Engine = new(CreateEngine);

    /// <summary>
    /// 全程内存：GDI 位图 → PNG 字节 → InMemoryRandomAccessStream → WinRT BitmapDecoder。
    /// 不再把屏幕截图写成 %TEMP% 下的 PNG —— 进程被杀 / Defender 隔离时那种文件会永久残留敏感画面。
    /// </summary>
    public static async Task<string> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        OcrEngine? engine = Engine.Value;
        if (engine is null)
        {
            throw new OcrException(
                "系统缺少 OCR 语言包：请在「设置 → 时间和语言 → 语言和区域」里给中文或 English (United States) 添加「可选功能 → 光学字符识别」，然后重试。");
        }

        // 第一遍按估算字高放大。若几乎没认出东西（低对比、抗锯齿把小字糊成一团等），
        // 再按另一个倍数补一次 —— 多花约 100ms，换掉「什么都没识别到」的失败。
        double primary = ComputeScale(bitmap);
        string text = await RecognizeOnceAsync(engine, bitmap, primary, cancellationToken);
        if (InkCount(text) >= MinRecognizedCharacters) return text;

        double alternate = ComputeScale(bitmap, primary * 1.7);
        if (Math.Abs(alternate - primary) < 0.2) return text;

        string retry = await RecognizeOnceAsync(engine, bitmap, alternate, cancellationToken);
        LogService.Info($"OCR 补识别：首次得到 {InkCount(text)} 字，改用 {alternate:0.00} 倍后得到 {InkCount(retry)} 字");
        return InkCount(retry) > InkCount(text) ? retry : text;
    }

    private static async Task<string> RecognizeOnceAsync(
        OcrEngine engine, Bitmap bitmap, double scale, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Bitmap? scaled = scale > MinScale + 1e-3 ? Resize(bitmap, scale) : null;
        Bitmap target = scaled ?? bitmap;
        try
        {
            byte[] png;
            using (var memory = new MemoryStream())
            {
                target.Save(memory, ImageFormat.Png);
                png = memory.ToArray();
            }

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                // 不 DetachStream 的话 writer.Close() 会把 stream 一起关掉
                writer.DetachStream();
            }
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var result = await engine.RecognizeAsync(softwareBitmap);
            cancellationToken.ThrowIfCancellationRequested();
            return BuildText(result);
        }
        finally
        {
            scaled?.Dispose();
        }
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

    /// <summary>不需要前置空格的标点（含中文标点与常见西文标点）。</summary>
    private static bool IsCjkPunctuation(char c) =>
        "，。、；：！？（）《》【】「」『』〈〉—…·～“”‘’".IndexOf(c) >= 0;

    /// <summary>估算字高后决定放大倍数；1.0 表示不需要放大，直接复用调用方的位图。</summary>
    private static double ComputeScale(Bitmap source, double? preferred = null)
    {
        double glyphHeight = EstimateGlyphHeight(source);
        // 估不出字高（纯色 / 只有一根横线）时按 2 倍保守放大：小字场景远多于超大字场景。
        double scale = preferred ?? (glyphHeight > 0.5 ? TargetGlyphHeight / glyphHeight : 2.0);
        if (scale < MinScale) return MinScale;
        if (scale > MaxScale) scale = MaxScale;

        int maxDimension = (int)OcrEngine.MaxImageDimension;
        long pixels = (long)source.Width * source.Height;
        double byDimension = maxDimension / (double)Math.Max(source.Width, source.Height);
        double byPixels = pixels > 0 ? Math.Sqrt(MaxPixels / (double)pixels) : MaxScale;
        double limit = Math.Min(byDimension, byPixels);
        if (limit < MinScale) return MinScale;
        return Math.Min(scale, limit);
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
        LogService.Info($"OCR 预处理：放大 {scale:0.00} 倍（{width}×{height}）");
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

    /// <summary>
    /// 优先用能同时读中日韩与西文的引擎：中文引擎读英文实测不比英文引擎差，
    /// 但英文引擎读中文只会得到乱码。因此「先中文后英文」的顺序不会更差，只会更好。
    /// </summary>
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
