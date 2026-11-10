using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickTranslate.Services;

public sealed class TrayService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;

    public TrayService(Action openSettings, Action exitApplication)
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add(new ToolStripMenuItem("设置", null, (_, _) => openSettings()));
        _menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => exitApplication()));

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "QuickTranslate",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _icon.DoubleClick += (_, _) => openSettings();
    }

    public void Notify(string message) =>
        _icon.ShowBalloonTip(3000, "QuickTranslate", message, ToolTipIcon.Info);

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.FillEllipse(Brushes.RoyalBlue, 0, 0, 16, 16);
            using var font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.DrawString("T", font, Brushes.White, 2, 1);
        }

        // GetHicon 返回的是非托管句柄；Icon.FromHandle 并不接管它。
        // 先克隆出托管 Icon 自己拥有的副本，再显式 DestroyIcon 释放原句柄。
        IntPtr handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
        _menu.Dispose();
    }
}
