using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace QuickTranslate.Services;

public sealed class TrayService : IDisposable
{
    private const string IconResourceName = "QuickTranslate.Assets.QuickTranslate.ico";

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

    /// <summary>
    /// 从程序集内嵌的 ico 中挑与系统托盘尺寸最匹配的帧（16/20/24/32），
    /// 避免把 256px 帧缩到托盘导致细节糊掉。内嵌资源在单文件发布下依然可用。
    /// </summary>
    private static Icon CreateIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName)
            ?? throw new InvalidOperationException($"缺少托盘图标资源：{IconResourceName}");
        using var full = new Icon(stream);
        return new Icon(full, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
        _menu.Dispose();
    }
}
