using System.Reflection;

namespace QuickTranslate.Services;

public sealed class TrayService : IDisposable
{
    private const string IconResourceName = "QuickTranslate.Assets.QuickTranslate.ico";

    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;

    public TrayService(Action openMainWindow, Action openSettings, Action checkForUpdates, Action exitApplication)
    {
        _menu = CreateMenu(openMainWindow, openSettings, checkForUpdates, exitApplication);

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "QuickTranslate",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _icon.MouseClick += OpenFromTray;
        _icon.MouseDoubleClick += OpenFromTray;

        void OpenFromTray(object? sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left) openMainWindow();
        }
    }

    private static ContextMenuStrip CreateMenu(
        Action openMainWindow, Action openSettings, Action checkForUpdates, Action exitApplication)
    {
        var renderer = new TrayMenuRenderer();
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Renderer = renderer,
        };
        menu.Opening += (_, _) =>
        {
            if (SystemInformation.HighContrast) menu.RenderMode = ToolStripRenderMode.System;
            else if (menu.Renderer != renderer) menu.Renderer = renderer;
        };
        menu.Items.AddRange([
            MenuItem("打开主界面", openMainWindow),
            MenuItem("设置", openSettings),
            MenuItem("检查更新", checkForUpdates),
            new ToolStripSeparator { Margin = new Padding(0, 3, 0, 3) },
            MenuItem("退出", exitApplication),
        ]);
        return menu;

        static ToolStripMenuItem MenuItem(string text, Action action) =>
            new(text, null, (_, _) => action()) { Padding = new Padding(0, 7, 0, 7) };
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
