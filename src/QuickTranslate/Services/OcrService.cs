using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace QuickTranslate.Services;

public static class OcrService
{
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
            throw new InvalidOperationException(
                "系统缺少 OCR 语言包：请在「设置 → 时间和语言」中添加 English (United States) 或中文，并勾选文字识别可选功能。");
        }

        int maxDimension = (int)OcrEngine.MaxImageDimension;
        Bitmap? scaled = ScaleToLimit(bitmap, maxDimension);
        Bitmap target = scaled ?? bitmap;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

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
            return result.Text?.Trim() ?? string.Empty;
        }
        finally
        {
            scaled?.Dispose();
        }
    }

    private static OcrEngine? CreateEngine()
    {
        foreach (string tag in new[] { "en-US", "zh-Hans-CN" })
        {
            try
            {
                var engine = OcrEngine.TryCreateFromLanguage(new Language(tag));
                if (engine is not null) return engine;
            }
            catch (Exception ex)
            {
                LogService.Warn($"OCR 语言包 {tag} 不可用", ex);
            }
        }
        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    /// <summary>返回 null 表示无需缩放，直接复用调用方的位图，避免无意义的一份拷贝。</summary>
    private static Bitmap? ScaleToLimit(Bitmap source, int maxDimension)
    {
        if (maxDimension <= 0 || (source.Width <= maxDimension && source.Height <= maxDimension))
        {
            return null;
        }

        double ratio = Math.Min((double)maxDimension / source.Width, (double)maxDimension / source.Height);
        int width = Math.Max(1, (int)(source.Width * ratio));
        int height = Math.Max(1, (int)(source.Height * ratio));
        var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, 0, 0, width, height);
        }
        return scaled;
    }
}
