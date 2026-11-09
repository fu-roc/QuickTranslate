using System.Drawing;
using System.Drawing.Imaging;

namespace QuickTranslate.Services;

public static class ScreenCaptureService
{
    /// <summary>参数为 Win32 物理像素坐标，避免 WPF DIP 在多屏不同缩放下的换算误差。</summary>
    public static Bitmap? Capture(Rectangle physicalRect)
    {
        if (physicalRect.Width < 8 || physicalRect.Height < 8) return null;

        var bitmap = new Bitmap(physicalRect.Width, physicalRect.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                physicalRect.X, physicalRect.Y, 0, 0,
                bitmap.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch (Exception)
        {
            bitmap.Dispose();
            return null;
        }
    }
}
