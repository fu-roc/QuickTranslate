using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTranslate.Native;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace QuickTranslate.Windows;

public partial class SelectionOverlayWindow : Window
{
    private System.Windows.Point? _startDip;
    private System.Drawing.Point _startPhysical;

    /// <summary>返回 Win32 物理像素矩形；null 表示用户取消或框选过小。</summary>
    public System.Drawing.Rectangle? Selection { get; private set; }

    public SelectionOverlayWindow(System.Drawing.Rectangle screenBounds, double scale)
    {
        InitializeComponent();
        double effectiveScale = scale > 0 ? scale : 1.0;
        Left = screenBounds.Left / effectiveScale;
        Top = screenBounds.Top / effectiveScale;
        Width = screenBounds.Width / effectiveScale;
        Height = screenBounds.Height / effectiveScale;
        Loaded += (_, _) => Focus();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _startDip = e.GetPosition(OverlayCanvas);
        _startPhysical = NativeMethods.GetCursorPosition();
        SelectionRectangle.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRectangle, _startDip.Value.X);
        Canvas.SetTop(SelectionRectangle, _startDip.Value.Y);
        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = 0;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_startDip is null || Mouse.LeftButton != MouseButtonState.Pressed) return;

        var current = e.GetPosition(OverlayCanvas);
        double x = Math.Min(_startDip.Value.X, current.X);
        double y = Math.Min(_startDip.Value.Y, current.Y);
        double width = Math.Abs(current.X - _startDip.Value.X);
        double height = Math.Abs(current.Y - _startDip.Value.Y);
        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_startDip is null) return;

        // 用 Win32 光标物理坐标确定选区，截图不再受 WPF DIP / 多屏缩放影响
        var endPhysical = NativeMethods.GetCursorPosition();
        ReleaseMouseCapture();
        _startDip = null;

        int x = Math.Min(_startPhysical.X, endPhysical.X);
        int y = Math.Min(_startPhysical.Y, endPhysical.Y);
        int width = Math.Abs(endPhysical.X - _startPhysical.X);
        int height = Math.Abs(endPhysical.Y - _startPhysical.Y);
        Selection = width >= 8 && height >= 8
            ? new System.Drawing.Rectangle(x, y, width, height)
            : null;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Selection = null;
            Close();
        }
    }
}
