using System.Drawing;
using System.Drawing.Imaging;

namespace QuickTranslate.Services;

public static class ScreenCaptureService
{
    /// <summary>参数为 Win32 物理像素坐标，避免 WPF DPI 在多屏不同缩放下的换算误差。</summary>
    public static Bitmap? Capture(Rectangle physicalRect)
    {
        // 鼠标捕获期间光标可能越过当前显示器 / 虚拟桌面边界，CopyFromScreen 传负值会拿到黑图或异常。
        Rectangle clipped = Rectangle.Intersect(
            physicalRect, System.Windows.Forms.SystemInformation.VirtualScreen);
        if (clipped.Width < 8 || clipped.Height < 8)
        {
            LogService.Warn("框选区域超出屏幕边界或过小，已放弃截图");
            return null;
        }

        var bitmap = new Bitmap(clipped.Width, clipped.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                clipped.X, clipped.Y, 0, 0,
                bitmap.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch (Exception ex)
        {
            LogService.Warn("GDI 截图失败（受保护窗口或硬件叠加层可能无法捕获）", ex);
            bitmap.Dispose();
            return null;
        }
    }
}
