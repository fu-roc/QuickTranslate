using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using QuickTranslate.Native;
using QuickTranslate.Services;
using Cursors = System.Windows.Input.Cursors;
using Button = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace QuickTranslate.Windows;

public partial class ResultWindow : Window
{
    private readonly DispatcherTimer _presenceTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(60),
    };

    private string? _copyText;
    private System.Drawing.Point _anchor;
    private Action? _switchAction;
    private bool _forceClose;
    private bool _lButtonWasDown;
    private bool _clickArmed;

    /// <summary>窗口被隐藏时通知 App 取消在途的翻译/识别请求，避免迟到的结果把窗口弹回来。</summary>
    public event Action? Hidden;

    public ResultWindow()
    {
        InitializeComponent();
        PronounceSourceButton.FeedbackChanged += message =>
        {
            PronunciationStatus.Text = message ?? string.Empty;
            PronunciationStatus.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        };

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
        SetSource(null, null);
        Hidden?.Invoke();
    }

    public void ForceClose()
    {
        _presenceTimer.Stop();
        _forceClose = true;
        Close();
    }

    public void ShowLoading(string message, string? pronunciationSource = null)
    {
        SetDefinitionLines(null);
        HeaderText.Text = "QuickTranslate";
        SetSource(pronunciationSource, pronunciationSource);
        ResultText.Text = message;
        ResultText.SetResourceReference(WpfTextBlock.ForegroundProperty, "MutedBrush");
        ResultBorder.Cursor = Cursors.Arrow;
        SetAction(null, null);
        SetHint(string.Empty);
        _copyText = null;
        ShowNearCursor();
    }

    public void ShowResult(string source, string translated, string? pronunciationSource = null)
    {
        SetDefinitionLines(null);
        HeaderText.Text = "译文";
        SetSource(source, pronunciationSource);
        ResultText.Text = translated;
        ResultText.SetResourceReference(WpfTextBlock.ForegroundProperty, "TextBrush");
        ResultBorder.Cursor = Cursors.Hand;
        SetAction(null, null);
        SetHint("点击译文复制");
        _copyText = translated;
        ShowNearCursor();
    }

    public void ShowDefinitions(string source, IReadOnlyList<string> definitions)
    {
        HeaderText.Text = "词典释义";
        SetDefinitionLines(definitions);
        SetSource(source, EnglishChineseDictionary.NormalizeWord(source));
        SetAction(null, null);
        SetHint("点击任一条释义复制");
        _copyText = null;
        ShowNearCursor();
    }

    public void ShowError(
        string message, string? switchLabel = null, Action? switchAction = null, bool keepPronunciation = false)
    {
        SetDefinitionLines(null);
        HeaderText.Text = "处理失败";
        string? pronunciationSource = keepPronunciation ? PronounceSourceButton.SpeechText : null;
        SetSource(pronunciationSource, pronunciationSource);
        ResultText.Text = message;
        ResultText.SetResourceReference(WpfTextBlock.ForegroundProperty, "ErrorBrush");
        ResultBorder.Cursor = Cursors.Arrow;
        SetAction(switchLabel, switchAction);
        SetHint(string.Empty);
        _copyText = null;
        ShowNearCursor();
    }

    private void SetDefinitionLines(IReadOnlyList<string>? definitions)
    {
        DefinitionItems.ItemsSource = definitions;
        DefinitionsScroll.Visibility = definitions is null ? Visibility.Collapsed : Visibility.Visible;
        ResultScroll.Visibility = definitions is null ? Visibility.Visible : Visibility.Collapsed;
        if (definitions is null) ResultScroll.ScrollToTop();
        else DefinitionsScroll.ScrollToTop();
    }

    private void SetSource(string? source, string? pronunciationSource)
    {
        SourceText.Text = source?.Replace('\r', ' ').Replace('\n', ' ') ?? string.Empty;
        PronounceSourceButton.SpeechText = pronunciationSource;
        PronounceSourceButton.Visibility = pronunciationSource is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetAction(string? label, Action? action)
    {
        _switchAction = action;
        SwitchButton.Content = label ?? string.Empty;
        SwitchButton.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetHint(string text, bool isError = false)
    {
        HintText.Text = text;
        HintText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        HintText.SetResourceReference(WpfTextBlock.ForegroundProperty, isError ? "ErrorBrush" : "MutedBrush");
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
        bool opening = !IsVisible;
        if (opening)
        {
            _anchor = NativeMethods.GetCursorPosition();
            Show();
        }
        UpdateLayout();

        // 翻译完成时沿用最初的锚点，避免用户点发音后悬浮窗追着鼠标跳动。
        var cursor = _anchor;
        double scale = NativeMethods.GetMonitorScale(cursor);
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        double left = cursor.X / scale + 16;
        double top = cursor.Y / scale + 16;
        double maxLeft = screen.Bounds.Right / scale - ActualWidth - 12;
        double maxTop = screen.Bounds.Bottom / scale - ActualHeight - 12;
        Left = Math.Min(left, Math.Max(screen.Bounds.Left / scale + 12, maxLeft));
        Top = Math.Min(top, Math.Max(screen.Bounds.Top / scale + 12, maxTop));

        if (opening) StartPresenceWatch();
    }

    private void ResultBorder_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_copyText)) return;
        if (ClipboardService.Copy(_copyText))
        {
            SetHint("已复制译文");
            _copyText = null;
            ResultBorder.Cursor = Cursors.Arrow;
        }
        else
        {
            SetHint("剪贴板被占用，请再点一次译文。", isError: true);
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
        bool copied = ClipboardService.Copy(definition);
        SetHint(copied ? "已复制释义" : "剪贴板被占用，请再点一次释义。", isError: !copied);
    }
}
