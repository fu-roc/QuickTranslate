using System.Drawing;
using System.Windows.Forms;

namespace QuickTranslate.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayService(Action openSettings, Action exitApplication)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("设置", null, (_, _) => openSettings()));
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => exitApplication()));

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "QuickTranslate",
            Visible = true,
            ContextMenuStrip = menu,
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
        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
