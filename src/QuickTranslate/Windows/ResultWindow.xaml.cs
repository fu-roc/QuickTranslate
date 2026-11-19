using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QuickTranslate.Native;
using QuickTranslate.Services;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Button = System.Windows.Controls.Button;

namespace QuickTranslate.Windows;

public partial class ResultWindow : Window
{
    private static readonly SolidColorBrush NormalBrush = new(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xC0, 0x39, 0x2B));

    private readonly DispatcherTimer _presenceTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(60),
    };

    private string? _copyText;
    private Action? _switchAction;
    private bool _forceClose;
    private bool _lButtonWasDown;
    private bool _clickArmed;

    /// <summary>窗口被隐藏时通知 App 取消在途的翻译/识别请求，避免迟到的结果把窗口弹回来。</summary>
    public event Action? Hidden;

    public ResultWindow()
    {
        InitializeComponent();

        // 未激活的窗口通过轮询检测外部点击。
        _presenceTimer.Tick += OnPresenceTick;

        Closing += (_, e) =>
        {
            if (_forceClose) return;
            e.Cancel = true;
            HideWindow();
        };
    }

    public void HideWindow()
    {
        _presenceTimer.Stop();
        if (!IsVisible) return;
        Hide();
        Hidden?.Invoke();
    }

    public void ForceClose()
    {
        _presenceTimer.Stop();
        _forceClose = true;
        Close();
    }

    public void ShowLoading(string message)
    {
        SetDefinitionLines(null);
        HeaderText.Text = "QuickTranslate";
        SourceText.Text = string.Empty;
        ResultText.Text = message;
        ResultText.Foreground = MutedBrush;
        ResultBorder.Cursor = Cursors.Arrow;
        SetAction(null, null);
        HintText.Text = "点击别处关闭";
        _copyText = null;
        ShowNearCursor();
    }

    public void ShowResult(string source, string translated)
    {
        SetDefinitionLines(null);
        HeaderText.Text = "译文";
        SourceText.Text = source.Replace('\r', ' ').Replace('\n', ' ');
        ResultText.Text = translated;
        ResultText.Foreground = NormalBrush;
        ResultBorder.Cursor = Cursors.Hand;
        SetAction(null, null);
        HintText.Text = "点击译文复制 · 点击别处关闭";
        _copyText = translated;
        ShowNearCursor();
    }

    public void ShowDefinitions(string source, IReadOnlyList<string> definitions)
    {
        HeaderText.Text = "词典释义";
        SourceText.Text = source.Replace('\r', ' ').Replace('\n', ' ');
        SetDefinitionLines(definitions);
        SetAction(null, null);
        HintText.Text = "点击释义行复制 · 点击别处关闭";
        _copyText = null;
        ShowNearCursor();
    }

    public void ShowError(string message, string? switchLabel = null, Action? switchAction = null)
    {
        SetDefinitionLines(null);
        HeaderText.Text = "出错了";
        SourceText.Text = string.Empty;
        ResultText.Text = message;
        ResultText.Foreground = ErrorBrush;
        ResultBorder.Cursor = Cursors.Arrow;
        SetAction(switchLabel, switchAction);
        HintText.Text = "修好后重新按快捷键 · 点击别处关闭";
        _copyText = null;
        ShowNearCursor();
    }

    private void SetDefinitionLines(IReadOnlyList<string>? definitions)
    {
        DefinitionItems.ItemsSource = definitions;
        DefinitionsScroll.Visibility = definitions is null ? Visibility.Collapsed : Visibility.Visible;
        ResultBorder.Visibility = definitions is null ? Visibility.Visible : Visibility.Collapsed;
        if (definitions is not null) DefinitionsScroll.ScrollToTop();
    }

    private void SetAction(string? label, Action? action)
    {
        _switchAction = action;
        SwitchButton.Content = label ?? string.Empty;
        SwitchButton.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
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

        // 只处理悬浮窗出现后的完整点击。
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
            HintText.Text = "复制失败：剪贴板被其他程序占用，请再点一次译文重试";
        }
    }

    private void SwitchButton_OnClick(object sender, RoutedEventArgs e)
    {
        var action = _switchAction;
        HideWindow();
        action?.Invoke();
    }

    private void DefinitionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string definition }) return;
        HintText.Text = ClipboardService.Copy(definition)
            ? "已复制释义 · 点击别处关闭"
            : "复制失败：剪贴板被其他程序占用，请再点一次释义重试";
    }
}
