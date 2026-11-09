using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Drawing.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace QuickTranslate.Services;

public static class OcrService
{
    public static async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        OcrEngine? engine = CreateEngine();
        if (engine is null)
        {
            throw new InvalidOperationException(
                "系统缺少 OCR 语言包：请在「设置 → 时间和语言」中添加 English (United States) 或中文，并勾选文字识别可选功能。");
        }

        int maxDimension = (int)OcrEngine.MaxImageDimension;
        using Bitmap scaled = ScaleToLimit(bitmap, maxDimension);

        // WinRT 的 BitmapDecoder 从临时 PNG 读取，避开 Stream → IRandomAccessStream 的互操作陷阱
        string tempPath = Path.Combine(Path.GetTempPath(), $"qt_ocr_{Guid.NewGuid():N}.png");
        try
        {
            scaled.Save(tempPath, ImageFormat.Png);
            var file = await StorageFile.GetFileFromPathAsync(tempPath);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            var result = await engine.RecognizeAsync(softwareBitmap);
            return result.Text?.Trim() ?? string.Empty;
        }
        finally
        {
            try { File.Delete(tempPath); }
            catch (IOException) { /* 临时文件清理失败不影响本次识别 */ }
            catch (UnauthorizedAccessException) { /* 同上 */ }
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
            catch (Exception)
            {
                // 该语言包不存在时继续尝试下一个候选
            }
        }
        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    private static Bitmap ScaleToLimit(Bitmap source, int maxDimension)
    {
        if (maxDimension <= 0 || (source.Width <= maxDimension && source.Height <= maxDimension))
        {
            return new Bitmap(source);
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
