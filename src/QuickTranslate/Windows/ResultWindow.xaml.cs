using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

    private readonly DispatcherTimer _autoHideTimer = new() { Interval = TimeSpan.FromSeconds(12) };
    private readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };

    private string? _copyText;
    private Action? _switchAction;
    private bool _forceClose;

    /// <summary>窗口被隐藏时通知 App 取消在途的翻译/识别请求，避免迟到的结果把窗口弹回来。</summary>
    public event Action? Hidden;

    public ResultWindow()
    {
        InitializeComponent();

        // 不抢前台焦点：ShowActivated=False + 不调用 Activate()。
        // 关闭策略改为「鼠标移开 / 12 秒超时 / 再按一次快捷键 / 点一下窗口后按 Esc」，
        // 不再依赖 Deactivated——那样会让用户一点别处窗口就消失，且 Activate 失败时 Esc 根本收不到。
        MouseEnter += (_, _) => RestartAutoHide();
        MouseLeave += (_, _) => StartLeaveTimer();
        _autoHideTimer.Tick += (_, _) => HideWindow();
        _leaveTimer.Tick += (_, _) => HideWindow();

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
        _autoHideTimer.Stop();
        _leaveTimer.Stop();
        if (!IsVisible) return;
        Hide();
        Hidden?.Invoke();
    }

    public void ForceClose()
    {
        _autoHideTimer.Stop();
        _leaveTimer.Stop();
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
        SetAction(null, null);
        HintText.Text = "点一下窗口后按 Esc 关闭 · 12 秒后收起";
        _copyText = null;
        ShowNearCursor();
    }

    public void ShowResult(string source, string translated)
    {
        HeaderText.Text = "译文";
        SourceText.Text = source.Replace('\r', ' ').Replace('\n', ' ');
        ResultText.Text = translated;
        ResultText.Foreground = NormalBrush;
        ResultBorder.Cursor = Cursors.Hand;
        SetAction(null, null);
        HintText.Text = "点击译文复制 · 鼠标移开或 12 秒后收起";
        _copyText = translated;
        ShowNearCursor();
    }

    /// <summary>switchAction 为 null 时不显示按钮，配置/账户类错误不再误导用户去切换链路。</summary>
    public void ShowError(string message, string? switchLabel = null, Action? switchAction = null)
    {
        HeaderText.Text = "出错了";
        SourceText.Text = string.Empty;
        ResultText.Text = message;
        ResultText.Foreground = ErrorBrush;
        ResultBorder.Cursor = Cursors.Arrow;
        SetAction(switchLabel, switchAction);
        HintText.Text = "修好后重新按快捷键即可 · 12 秒后收起";
        _copyText = null;
        ShowNearCursor();
    }

    private void SetAction(string? label, Action? action)
    {
        _switchAction = action;
        SwitchButton.Content = label ?? string.Empty;
        SwitchButton.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RestartAutoHide()
    {
        _leaveTimer.Stop();
        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    private void StartLeaveTimer()
    {
        _leaveTimer.Stop();
        _leaveTimer.Start();
    }

    private void ShowNearCursor()
    {
        // ShowActivated=False：只显示，不激活、不抢走正在打字的焦点。
        if (!IsVisible) Show();
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

        RestartAutoHide();
    }

    private void ResultBorder_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_copyText)) return;
        if (ClipboardService.Copy(_copyText))
        {
            HintText.Text = "已复制译文";
            _copyText = null;
            ResultBorder.Cursor = Cursors.Arrow;
        }
        else
        {
            // 保留 _copyText，用户可以再点一次重试，而不是被一句假「已复制」骗过去
            HintText.Text = "复制失败：剪贴板被其他程序占用，请再点一次译文重试";
        }
        RestartAutoHide();
    }

    private void SwitchButton_OnClick(object sender, RoutedEventArgs e)
    {
        var action = _switchAction;
        HideWindow();
        action?.Invoke();
    }
}
