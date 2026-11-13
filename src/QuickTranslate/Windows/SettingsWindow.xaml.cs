using System.Windows;
using QuickTranslate.Models;
using QuickTranslate.Native;
using QuickTranslate.Services;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfButton = System.Windows.Controls.Button;
using AutoStart = QuickTranslate.Services.AutoStartService;

namespace QuickTranslate.Windows;

public partial class SettingsWindow : Window
{
    private const string DefaultTakeWordHotkey = "Alt+T";
    private const string DefaultOcrHotkey = "Alt+S";
    private const string CapturePlaceholder = "请按下组合键…";

    /// <summary>
    /// 由 App 注入：原子地应用配置与开机自启开关，返回 null 表示成功，否则返回可直接展示的中文原因。
    /// 第二个参数是设置页里的自启勾选状态（注册表为唯一真相源，不写进 config.json）。
    /// </summary>
    public Func<AppConfig, bool, string?>? ApplyConfig { get; set; }

    /// <summary>由 App 注入：打开「检查更新」窗口。</summary>
    public Action? CheckForUpdates { get; set; }

    private WpfTextBox? _captureBox;
    private WpfButton? _captureButton;
    private string _captureOriginal = string.Empty;

    public SettingsWindow(AppConfig config)
    {
        InitializeComponent();
        AppIdBox.Text = config.AppId;
        SecretBox.Password = config.Secret;
        TakeWordBox.Text = config.TakeWordHotkey;
        OcrBox.Text = config.OcrHotkey;
        LanguageBox.SelectedValue = config.TargetLanguage;
        // 以注册表真实状态为准，而不是缓存的勾选值：用户在任务管理器里禁用后这里会如实反映
        AutoStartBox.IsChecked = AutoStart.IsEnabled();

        VersionText.Text = $"版本 {AppVersion.Display} · 日志：%LOCALAPPDATA%\\QuickTranslate\\app.log";

        // 手改过 config.json 时目标语言可能不在下拉列表里，别静默回退成 zh
        if (LanguageBox.SelectedItem is null)
        {
            SetStatus($"配置里的目标语言「{config.TargetLanguage}」不在列表中，保存时会回到「简体中文」。", isError: true);
        }
    }

    private void Record_OnClick(object sender, RoutedEventArgs e)
    {
        var button = (WpfButton)sender;
        WpfTextBox box = ReferenceEquals(button, TakeWordRecordButton) ? TakeWordBox : OcrBox;

        if (ReferenceEquals(_captureBox, box))
        {
            EndCapture(restore: true);
            return;
        }

        EndCapture(restore: true);
        BeginCapture(box, button);
    }

    private void ResetHotkeys_OnClick(object sender, RoutedEventArgs e)
    {
        EndCapture(restore: true);
        TakeWordBox.Text = DefaultTakeWordHotkey;
        OcrBox.Text = DefaultOcrHotkey;
        SetStatus("已恢复为 Alt+T / Alt+S，点「保存」后生效", isError: false);
    }

    private void BeginCapture(WpfTextBox box, WpfButton button)
    {
        _captureBox = box;
        _captureButton = button;
        _captureOriginal = box.Text;
        box.IsReadOnly = true;
        box.Text = CapturePlaceholder;
        box.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xE8, 0xF0, 0xFE));
        button.Content = "取消";
        SetStatus("正在录制：请按住 Ctrl / Alt / Shift / Win 中至少一个，再按一个按键；Esc 取消，Backspace 清空。", false);
        box.Focus();
    }

    private void EndCapture(bool restore)
    {
        if (_captureBox is null) return;

        _captureBox.IsReadOnly = false;
        _captureBox.Background = System.Windows.Media.Brushes.White;
        if (restore) _captureBox.Text = _captureOriginal;
        if (_captureButton is not null) _captureButton.Content = "录制";
        _captureBox = null;
        _captureButton = null;
        SetStatus(string.Empty, isError: false);
    }

    private void Window_OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        // 按 Alt 时 WPF 会把真实按键放在 SystemKey 里
        WpfKey key = e.Key == WpfKey.System ? e.SystemKey : e.Key;

        if (_captureBox is null)
        {
            if (key == WpfKey.Escape) Close();
            return;
        }

        e.Handled = true;

        if (key == WpfKey.Escape)
        {
            EndCapture(restore: true);
            return;
        }

        if (key is WpfKey.Back or WpfKey.Delete)
        {
            _captureBox.Text = string.Empty;
            EndCapture(restore: false);
            return;
        }

        if (!HotkeyCodec.TryFromKey(key, out string text, out string error, out bool pureModifierOnly))
        {
            // 只按下修饰键时不报错、不结束录制，等用户按下主键
            if (!pureModifierOnly) SetStatus(error, isError: true);
            return;
        }

        _captureBox.Text = text;
        EndCapture(restore: false);
        SetStatus($"已录入 {text}，点「保存」后生效", isError: false);
    }

    private void Window_OnPreviewMouseDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (_captureBox is null) return;

        // 点在任意按钮内（含按钮的文字子元素）都交给 Click 处理；点在录制框内保持录制
        if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
        if (ReferenceEquals(e.OriginalSource, _captureBox)) return;
        if (e.OriginalSource is DependencyObject d && _captureBox.IsAncestorOf(d)) return;

        EndCapture(restore: true);
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is WpfButton) return true;
            source = source is System.Windows.Media.Visual visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(visual)
                : null;
        }
        return false;
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (_captureBox is not null)
        {
            SetStatus("请先按下组合键完成录制，或按 Esc 取消录制", isError: true);
            return;
        }

        string takeWord = TakeWordBox.Text.Trim();
        string ocr = OcrBox.Text.Trim();

        if (!HotkeyCodec.TryParse(takeWord, out _, out _, out string takeWordError))
        {
            SetStatus($"取词快捷键：{takeWordError}", isError: true);
            TakeWordBox.Focus();
            return;
        }
        if (!HotkeyCodec.TryParse(ocr, out _, out _, out string ocrError))
        {
            SetStatus($"截图快捷键：{ocrError}", isError: true);
            OcrBox.Focus();
            return;
        }
        if (HotkeyCodec.Normalize(takeWord) == HotkeyCodec.Normalize(ocr))
        {
            SetStatus("两个快捷键不能相同，请给取词翻译和截图 OCR 各留一组", isError: true);
            return;
        }

        var config = new AppConfig
        {
            AppId = AppIdBox.Text.Trim(),
            Secret = SecretBox.Password.Trim(),
            TakeWordHotkey = takeWord,
            OcrHotkey = ocr,
            TargetLanguage = LanguageBox.SelectedValue as string ?? "zh",
        };

        bool autoStart = AutoStartBox.IsChecked == true;

        // 任一副作用失败时配置不落盘、窗口不关闭、旧热键在窗口关闭后自动恢复
        string? failure = ApplyConfig?.Invoke(config, autoStart);
        if (failure is not null)
        {
            SetStatus(failure, isError: true);
            return;
        }

        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => Close();

    private void CheckUpdate_OnClick(object sender, RoutedEventArgs e) => CheckForUpdates?.Invoke();

    private void SetStatus(string message, bool isError)
    {
        StatusText.Foreground = isError
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1D, 0x6F, 0x42));
        StatusText.Text = message;
    }
}
