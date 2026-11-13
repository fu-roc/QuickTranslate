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
    private readonly DispatcherTimer _presenceTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(60),
    };

    private string? _copyText;
    private Action? _switchAction;
    private bool _forceClose;
    private bool _lButtonWasDown;
    private bool _clickArmed;
    private bool _pointerEntered;

    /// <summary>窗口被隐藏时通知 App 取消在途的翻译/识别请求，避免迟到的结果把窗口弹回来。</summary>
    public event Action? Hidden;

    public ResultWindow()
    {
        InitializeComponent();

        // 不抢前台焦点：ShowActivated=False + 不调用 Activate()。窗口从不激活，
        // Deactivated 不会触发，窗口外的点击也收不到 WPF 鼠标事件；用显示期轮询统一处理
        // 「点击别处关闭 / 鼠标进出 / 12 秒兜底」，不装任何全局钩子。
        _autoHideTimer.Tick += (_, _) => HideWindow();
        _leaveTimer.Tick += (_, _) => HideWindow();
        _presenceTimer.Tick += OnPresenceTick;

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
        _presenceTimer.Stop();
        if (!IsVisible) return;
        Hide();
        Hidden?.Invoke();
    }

    public void ForceClose()
    {
        _autoHideTimer.Stop();
        _leaveTimer.Stop();
        _presenceTimer.Stop();
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
        HintText.Text = "点击别处关闭 · 12 秒后收起";
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
        HintText.Text = "点击译文复制 · 点击别处关闭 · 12 秒后收起";
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
        HintText.Text = "修好后重新按快捷键 · 点击别处关闭 · 12 秒后收起";
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

    /// <summary>
    /// 每次显示重置点击状态：显示瞬间左键若已按下（刚拖完选区），那次抬升不算新点击，
    /// 必须观察到完整的「按下 → 抬起」周期才判定，否则译文会被自己刚松开的那一下立刻关掉。
    /// </summary>
    private void StartPresenceWatch()
    {
        _presenceTimer.Stop();
        _lButtonWasDown = NativeMethods.IsLeftButtonDown();
        _clickArmed = false;
        _pointerEntered = IsCursorInsideWindow();
        _presenceTimer.Start();
    }

    private void OnPresenceTick(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            _presenceTimer.Stop();
            return;
        }

        bool inside = IsCursorInsideWindow();
        if (inside)
        {
            if (!_pointerEntered) RestartAutoHide();
            _pointerEntered = true;
            _leaveTimer.Stop();
        }
        else if (_pointerEntered)
        {
            // 结果窗出现在光标右下 16px，鼠标通常从未进入过窗口；
            // 只有真正进来过再离开才自动收起，避免刚显示就消失。
            _pointerEntered = false;
            StartLeaveTimer();
        }

        bool down = NativeMethods.IsLeftButtonDown();
        if (!_lButtonWasDown && down) _clickArmed = true;
        if (_clickArmed && _lButtonWasDown && !down)
        {
            _clickArmed = false;
            if (!inside)
            {
                HideWindow();
                return;
            }
        }
        _lButtonWasDown = down;
    }

    /// <summary>
    /// 用窗口的物理屏幕矩形判断光标是否在窗内。PointToScreen 与 GetCursorPos 都是物理像素，
    /// 不需要 DIP 换算；TransformFromDevice 返回的是屏幕坐标而非窗口坐标，用它会把窗内点误判成窗外。
    /// </summary>
    private bool IsCursorInsideWindow()
    {
        if (PresentationSource.FromVisual(this) is null) return false;
        try
        {
            var physical = NativeMethods.GetCursorPosition();
            var topLeft = PointToScreen(new System.Windows.Point(0, 0));
            var bottomRight = PointToScreen(new System.Windows.Point(ActualWidth, ActualHeight));
            return physical.X >= topLeft.X && physical.X <= bottomRight.X
                && physical.Y >= topLeft.Y && physical.Y <= bottomRight.Y;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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
        StartPresenceWatch();
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
