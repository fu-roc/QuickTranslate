using System.Drawing.Drawing2D;
using WpfApplication = System.Windows.Application;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace QuickTranslate.Services;

internal sealed class TrayMenuRenderer : ToolStripRenderer
{
    private readonly Color _background = ThemeColor("SurfaceBrush", SystemColors.Window);
    private readonly Color _border = ThemeColor("BorderBrush", SystemColors.ControlLight);
    private readonly Color _text = ThemeColor("TextBrush", SystemColors.WindowText);
    private readonly Color _highlight = ThemeColor("HighlightBrush", SystemColors.Highlight);
    private readonly Color _highlightText = ThemeColor("AccentTextBrush", SystemColors.HighlightText);

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        Rectangle bounds = e.ToolStrip.ClientRectangle;
        using var pen = new Pen(_border);
        e.Graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed)) return;

        int insetX = Scale(e.ToolStrip, 5);
        int insetY = Scale(e.ToolStrip, 2);
        int radius = Scale(e.ToolStrip, 6);
        var bounds = new Rectangle(insetX, insetY, e.Item.Width - 2 * insetX, e.Item.Height - 2 * insetY);
        using var outline = new GraphicsPath();
        outline.AddRoundedRectangle(bounds, new Size(radius, radius));
        using var brush = new SolidBrush(_highlight);
        var state = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, outline);
        e.Graphics.Restore(state);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool active = e.Item.Selected || e.Item.Pressed;
        e.TextColor = active ? _highlightText : _text;
        if (!e.Item.Enabled) e.TextColor = SystemColors.GrayText;

        int inset = Scale(e.ToolStrip, 14);
        e.TextRectangle = new Rectangle(inset, 0, e.Item.Width - 2 * inset, e.Item.Height);
        e.TextFormat |= TextFormatFlags.VerticalCenter;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int inset = Scale(e.ToolStrip, 12);
        int y = e.Item.Height / 2;
        using var pen = new Pen(_border);
        e.Graphics.DrawLine(pen, inset, y, e.Item.Width - inset - 1, y);
    }

    private static int Scale(ToolStrip? menu, int value) => menu?.LogicalToDeviceUnits(value) ?? value;

    private static Color ThemeColor(string key, Color fallback)
    {
        if (WpfApplication.Current?.TryFindResource(key) is not WpfSolidColorBrush brush) return fallback;
        return Color.FromArgb(brush.Color.R, brush.Color.G, brush.Color.B);
    }
}
