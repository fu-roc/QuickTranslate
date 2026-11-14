using System.Text;
using System.Windows;
using QuickTranslate.Models;
using QuickTranslate.Services;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfSelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;

namespace QuickTranslate.Windows;

/// <summary>
/// 手动翻译的双栏主界面。源语言 / 目标语言是本窗口独立的偏好，只影响手动翻译；
/// 划词与截图 OCR 恒用「自动检测 → 设置页目标语言」。翻译能力由 App 注入，关闭只隐藏。
/// </summary>
public partial class MainWindow : Window
{
    private static readonly System.Windows.Media.SolidColorBrush MutedBrush = Brush("#6B7280");
    private static readonly System.Windows.Media.SolidColorBrush ErrorBrush = Brush("#C0392B");
    private static readonly System.Windows.Media.SolidColorBrush CountNormalBrush = Brush("#9AA0A6");

    /// <summary>由 App 注入：text, from, to, token。始终指向当前生效的翻译器。</summary>
    public Func<string, string, string, CancellationToken, Task<string>>? TranslateAsync { get; set; }

    /// <summary>由 App 注入：把主界面语种偏好单独落盘，返回 null 表示成功，否则返回中文原因。</summary>
    public Func<string, string, string?>? SaveLanguage { get; set; }

    /// <summary>由 App 注入：打开设置页（错误为配置类时也会显示按钮）。</summary>
    public Action? OpenSettings { get; set; }

    private CancellationTokenSource? _cts;
    private bool _forceClose;
    private bool _busy;
    private bool _loadingLanguage;

    public MainWindow(AppConfig config)
    {
        InitializeComponent();
        ApplyConfig(config);
        UpdateCount();
        Loaded += (_, _) => SourceBox.Focus();
    }

    /// <summary>
    /// 构造时与设置页保存后由 App 调用。只同步主界面自己的偏好；
    /// 目标语言偏好为空（用户还没在主界面选过）时跟随设置页，之后互不干扰。
    /// </summary>
    public void ApplyConfig(AppConfig config)
    {
        _loadingLanguage = true;
        try
        {
            SelectLanguage(SourceLanguageBox, config.MainWindowSourceLanguage, "auto");
            SelectLanguage(TargetLanguageBox, EffectiveTarget(config), "zh");
        }
        finally { _loadingLanguage = false; }
    }

    private static string EffectiveTarget(AppConfig config) =>
        string.IsNullOrWhiteSpace(config.MainWindowTargetLanguage)
            ? config.TargetLanguage
            : config.MainWindowTargetLanguage;

    /// <summary>
    /// 手改过 config.json 时值可能不在下拉里（含大小写不一致），逐级回退到安全项，
    /// 别让 SelectedValue 变成 null 导致翻译时拿到未知语种。
    /// </summary>
    private static void SelectLanguage(WpfComboBox box, string code, string fallback)
    {
        string? match = FindTag(box, code) ?? FindTag(box, fallback);
        box.SelectedValue = match
            ?? (box.Items[0] as WpfComboBoxItem)?.Tag as string
            ?? string.Empty;
    }

    private static string? FindTag(WpfComboBox box, string code) =>
        box.Items.OfType<WpfComboBoxItem>()
            .Select(item => item.Tag as string)
            .FirstOrDefault(tag => string.Equals(tag, code, StringComparison.OrdinalIgnoreCase));

    private string SelectedFrom => SourceLanguageBox.SelectedValue as string ?? "auto";
    private string SelectedTo => TargetLanguageBox.SelectedValue as string ?? "zh";

    private void Language_OnChanged(object sender, WpfSelectionChangedEventArgs e)
    {
        // 初始化下拉也会触发 SelectionChanged，不能当成用户操作去写盘
        if (_loadingLanguage || !IsLoaded) return;

        // 旧语种的译文与在途请求都不该留下，切完语言必须重新翻译
        CancelTranslate();
        if (!string.IsNullOrEmpty(ResultBox.Text))
        {
            ResultBox.Text = string.Empty;
            CopyButton.IsEnabled = false;
            SetStatus("语言已切换，点「翻译」重新翻译。", isError: false);
        }

        string? failure = SaveLanguage?.Invoke(SelectedFrom, SelectedTo);
        if (failure is not null) SetStatus(failure, isError: true);
    }

    public void HideWindow()
    {
        CancelTranslate();
        if (IsVisible) Hide();
    }

    /// <summary>托盘「退出」时调用：跳过 Closing 的 Cancel，避免 Shutdown 被挡住。</summary>
    public void ForceClose()
    {
        _forceClose = true;
        CancelTranslate();
        Close();
    }

    private async void Translate_OnClick(object sender, RoutedEventArgs e) => await RunTranslateAsync();

    private async Task RunTranslateAsync()
    {
        if (_busy) return;

        string text = SourceBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("请先输入要翻译的文本。", isError: false);
            SourceBox.Focus();
            return;
        }
        if (TranslateAsync is null)
        {
            SetStatus("翻译服务未就绪，请重启程序后重试。", isError: true);
            return;
        }

        string from = SelectedFrom;
        string to = SelectedTo;
        // 百度 58001：同语种方向不支持，前端直接拦掉，省一次请求与配额
        if (!string.Equals(from, "auto", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("源语言和目标语言相同，请换一个再翻译。", isError: true);
            return;
        }

        CancelTranslate();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _busy = true;
        SetBusy(true);
        ResultBox.Text = string.Empty;
        SetStatus("正在翻译…", isError: false);

        try
        {
            string translated = await TranslateAsync(text, from, to, cts.Token);
            // 关窗 / 清空 / 切换语言会取消在途请求，迟到的结果不许覆盖当前界面
            if (cts.IsCancellationRequested || !ReferenceEquals(_cts, cts)) return;
            ResultBox.Text = translated;
            CopyButton.IsEnabled = true;
            SetStatus($"翻译完成 · {DateTime.Now:HH:mm:ss}", isError: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // 用户主动取消，不把取消当成失败打扰用户
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_cts, cts)) return;
            LogService.Error("主窗口翻译失败", ex);
            FailureInfo info = ErrorMapper.Map(ex);
            SetStatus(info.Message, isError: true);
            SettingsOnErrorButton.Visibility =
                info.Action == ErrorAction.Settings ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
                cts.Dispose();
                _busy = false;
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool busy)
    {
        TranslateButton.IsEnabled = !busy;
        TranslateButton.Content = busy ? "翻译中…" : "翻译 (Ctrl+Enter)";
        if (busy) CopyButton.IsEnabled = false;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? ErrorBrush : MutedBrush;
        if (!isError) SettingsOnErrorButton.Visibility = Visibility.Collapsed;
    }

    private void SourceBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        UpdateCount();

    private void UpdateCount()
    {
        string text = SourceBox.Text;
        int bytes = Encoding.UTF8.GetByteCount(text);
        CountText.Text = $"{text.Length} 字符 · {bytes:N0} 字节";
        CountText.Foreground = bytes > 48000 ? ErrorBrush : CountNormalBrush;
        CountText.ToolTip = bytes > 48000
            ? "超过单次上限（8 段 × 6000 字节），请分成多次翻译"
            : "百度单次上限 6000 字节，程序会自动分段（最多 8 段）";
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        CancelTranslate();
        SourceBox.Text = string.Empty;
        ResultBox.Text = string.Empty;
        CopyButton.IsEnabled = false;
        SetStatus("输入文本后点「翻译」或按 Ctrl+Enter", isError: false);
        SourceBox.Focus();
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        string result = ResultBox.Text;
        if (string.IsNullOrEmpty(result)) return;
        bool ok = ClipboardService.Copy(result);
        SetStatus(ok ? "译文已复制到剪贴板" : "复制失败：剪贴板被其他程序占用，请再点一次重试", !ok);
    }

    private void Settings_OnClick(object sender, RoutedEventArgs e) => OpenSettings?.Invoke();

    internal static bool IsTranslateShortcut(WpfKey key, System.Windows.Input.ModifierKeys modifiers) =>
        key == WpfKey.Enter &&
        (modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;

    private void Window_OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (IsTranslateShortcut(e.Key, System.Windows.Input.Keyboard.Modifiers))
        {
            e.Handled = true;
            _ = RunTranslateAsync();
            return;
        }
        if (e.Key == WpfKey.Escape)
        {
            e.Handled = true;
            HideWindow();
        }
    }

    private void Window_OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose) return;
        e.Cancel = true;
        HideWindow();
    }

    private void CancelTranslate()
    {
        var cts = _cts;
        _cts = null;
        _busy = false;
        SetBusy(false);
        if (cts is null) return;
        try { cts.Cancel(); }
        catch (Exception ex) { LogService.Warn("主窗口：取消在途翻译失败", ex); }
        finally { cts.Dispose(); }
    }

    private static System.Windows.Media.SolidColorBrush Brush(string hex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return new System.Windows.Media.SolidColorBrush(color);
    }
}
