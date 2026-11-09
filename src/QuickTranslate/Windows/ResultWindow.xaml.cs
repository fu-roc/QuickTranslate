using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using QuickTranslate.Native;
using QuickTranslate.Services;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;

namespace QuickTranslate.Windows;

public partial class ResultWindow : Window
{
    private static readonly SolidColorBrush NormalBrush = new(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xC0, 0x39, 0x2B));

    private string? _copyText;
    private Action? _switchAction;
    private bool _forceClose;

    public ResultWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => HideWindow();
        Closing += (_, e) =>
        {
            if (_forceClose) return;
            e.Cancel = true;
            HideWindow();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) HideWindow();
        };
    }

    public bool IsShowing => IsVisible;

    public void HideWindow()
    {
        if (IsVisible) Hide();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    public void ShowLoading(string message)
    {
        HeaderText.Text = "QuickTranslate";
        SourceText.Text = string.Empty;
        ResultText.Text = message;
        ResultText.Foreground = MutedBrush;
        ResultBorder.Cursor = Cursors.Arrow;
        SwitchButton.Visibility = Visibility.Collapsed;
        HintText.Text = "Esc 关闭";
        _copyText = null;
        _switchAction = null;
        ShowNearCursor();
    }

    public void ShowResult(string source, string translated)
    {
        HeaderText.Text = "译文";
        SourceText.Text = source.Replace('\r', ' ').Replace('\n', ' ');
        ResultText.Text = translated;
        ResultText.Foreground = NormalBrush;
        ResultBorder.Cursor = Cursors.Hand;
        SwitchButton.Visibility = Visibility.Collapsed;
        HintText.Text = "点击译文复制 · Esc 关闭";
        _copyText = translated;
        _switchAction = null;
        ShowNearCursor();
    }

    public void ShowError(string message, string switchLabel, Action switchAction)
    {
        HeaderText.Text = "出错了";
        SourceText.Text = string.Empty;
        ResultText.Text = message;
        ResultText.Foreground = ErrorBrush;
        ResultBorder.Cursor = Cursors.Arrow;
        SwitchButton.Content = switchLabel;
        SwitchButton.Visibility = Visibility.Visible;
        HintText.Text = "修好后重新按快捷键即可";
        _copyText = null;
        _switchAction = switchAction;
        ShowNearCursor();
    }

    private void ShowNearCursor()
    {
        if (IsVisible)
        {
            Activate();
        }
        else
        {
            Show();
            Activate();
        }
        UpdateLayout();

        var cursor = NativeMethods.GetCursorPosition();
        double scale = NativeMethods.GetMonitorScale(cursor);
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        double left = cursor.X / scale + 16;
        double top = cursor.Y / scale + 16;
        double maxLeft = screen.Bounds.Right / scale - ActualWidth - 12;
        double maxTop = screen.Bounds.Bottom / scale - ActualHeight - 12;
        Left = Math.Min(left, Math.Max(screen.Bounds.Left / scale + 12, maxLeft));
        Top = Math.Min(top, Math.Max(screen.Bounds.Top / scale + 12, maxTop));
    }

    private void ResultBorder_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_copyText)) return;
        ClipboardService.Copy(_copyText);
        HintText.Text = "已复制译文";
        _copyText = null;
    }

    private void SwitchButton_OnClick(object sender, RoutedEventArgs e)
    {
        var action = _switchAction;
        HideWindow();
        action?.Invoke();
    }
}
