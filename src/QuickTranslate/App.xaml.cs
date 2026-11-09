using System.Threading;
using System.Windows;
using QuickTranslate.Models;
using QuickTranslate.Native;
using QuickTranslate.Services;
using QuickTranslate.Windows;
using MessageBox = System.Windows.MessageBox;

namespace QuickTranslate;

public partial class App : System.Windows.Application
{
    private const int TakeWordHotkeyId = 1;
    private const int OcrHotkeyId = 2;

    private Mutex? _singletonMutex;
    private AppConfig _config = null!;
    private HotkeyService _hotkeys = null!;
    private TrayService _tray = null!;
    private ResultWindow _result = null!;
    private BaiduTranslator _translator = null!;
    private bool _busy;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singletonMutex = new Mutex(true, @"Local\QuickTranslate.Singleton", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("QuickTranslate 已经在运行了。", "QuickTranslate",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _config = ConfigService.Load(out bool createdNewConfig);
        _translator = new BaiduTranslator(_config);
        _result = new ResultWindow();
        _hotkeys = new HotkeyService();
        _hotkeys.HotkeyPressed += OnHotkeyPressed;
        _tray = new TrayService(OpenSettings, ExitApplication);

        RegisterHotkeys();
        SyncAutoStart();

        if (createdNewConfig || !_config.IsConfigured)
        {
            _tray.Notify("请先填写百度翻译 APPID 和密钥");
            OpenSettings();
        }
    }

    /// <summary>
    /// 已开启自启时，校正 Run 项指向当前 exe：程序被移动或重新发布后启动项不会指向旧文件。
    /// 路径本来就正确时不做任何写入。
    /// </summary>
    private void SyncAutoStart()
    {
        if (!AutoStartService.IsEnabled()) return;
        string? error = AutoStartService.Sync();
        if (error is not null) _tray.Notify($"开机自启：{error}");
    }

    private void RegisterHotkeys()
    {
        var errors = new List<string>();
        string first = _hotkeys.Register(TakeWordHotkeyId, _config.TakeWordHotkey);
        string second = _hotkeys.Register(OcrHotkeyId, _config.OcrHotkey);
        if (!string.IsNullOrEmpty(first)) errors.Add($"取词翻译：{first}");
        if (!string.IsNullOrEmpty(second)) errors.Add($"截图 OCR：{second}");
        if (errors.Count > 0)
        {
            _result.ShowError(string.Join("；", errors), "打开设置", OpenSettings);
        }
    }

    private async void OnHotkeyPressed(int id)
    {
        if (_busy) return;
        if (_result.IsShowing)
        {
            _result.HideWindow();
            return;
        }

        _busy = true;
        try
        {
            if (id == TakeWordHotkeyId) await RunTakeWordAsync();
            else if (id == OcrHotkeyId) await RunOcrAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex.Message, id);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RunTakeWordAsync()
    {
        // 取词阶段绝不显示或激活任何窗口，否则会抢走前台焦点，Ctrl+C 会发给自己
        string? selected = await ClipboardService.CaptureSelectedTextAsync();
        if (selected is null)
        {
            _result.ShowError(
                "未检测到选中文本：请确认已选中文字，并完全松开快捷键后再试；管理员权限窗口、终端或禁止复制的软件可能无法读取。下方按钮是手动切换，不会自动开始截图。",
                "改用截图 OCR", () => SwitchTo(OcrHotkeyId));
            return;
        }

        _result.ShowLoading("翻译中…");
        string translated = await _translator.TranslateAsync(selected);
        _result.ShowResult(selected, translated);
    }

    private async Task RunOcrAsync()
    {
        var cursor = NativeMethods.GetCursorPosition();
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        double scale = NativeMethods.GetMonitorScale(cursor);
        var overlay = new SelectionOverlayWindow(screen.Bounds, scale);
        overlay.ShowDialog();

        var rect = overlay.Selection;
        if (rect is null) return; // 用户按 Esc 主动取消，不打扰

        using var bitmap = ScreenCaptureService.Capture(rect.Value);
        if (bitmap is null)
        {
            _result.ShowError("框选区域太小或截图失败，请重新框选。",
                "重新框选", () => SwitchTo(OcrHotkeyId));
            return;
        }

        _result.ShowLoading("识别文字中…");
        string recognized = await OcrService.RecognizeAsync(bitmap);
        if (string.IsNullOrWhiteSpace(recognized))
        {
            _result.ShowError("OCR 没有识别到文字，试试框得更准一点或换更清晰的区域。",
                "改用取词翻译", () => SwitchTo(TakeWordHotkeyId));
            return;
        }

        _result.ShowLoading("翻译中…");
        string translated = await _translator.TranslateAsync(recognized);
        _result.ShowResult(recognized, translated);
    }

    private void ShowFailure(string message, int failedHotkeyId)
    {
        bool fromTakeWord = failedHotkeyId == TakeWordHotkeyId;
        _result.ShowError(message,
            fromTakeWord ? "改用截图 OCR" : "改用取词翻译",
            () => SwitchTo(fromTakeWord ? OcrHotkeyId : TakeWordHotkeyId));
    }

    private void SwitchTo(int hotkeyId) =>
        Dispatcher.BeginInvoke(new Action(() => _ = RunSwitchAsync(hotkeyId)));

    private async Task RunSwitchAsync(int hotkeyId)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (hotkeyId == TakeWordHotkeyId) await RunTakeWordAsync();
            else await RunOcrAsync();
        }
        catch (Exception ex)
        {
            _result.ShowError(ex.Message, "打开设置", OpenSettings);
        }
        finally
        {
            _busy = false;
        }
    }

    private void OpenSettings()
    {
        _result.HideWindow();

        // 录制快捷键期间必须注销旧热键，否则用户想录入的组合键会被旧热键抢走并触发翻译
        _hotkeys.UnregisterAll();

        var window = new SettingsWindow(_config) { ApplyConfig = ApplyConfig };
        window.ShowDialog();

        // 无论保存成功、失败还是取消，最终都以当前生效的 _config 重新注册，保证不会留下「无热键」状态
        _hotkeys.UnregisterAll();
        RegisterHotkeys();
    }

    /// <summary>
    /// 原子地应用新配置：热键全部注册成功、自启项写入成功、配置文件写盘成功，才算生效。
    /// 任一步失败都会回滚已做的副作用、配置不落盘、窗口保持打开；旧热键在窗口关闭后按 _config 重新注册。
    /// </summary>
    private string? ApplyConfig(AppConfig config, bool autoStart)
    {
        string failure = _hotkeys.RegisterPair(
            TakeWordHotkeyId, config.TakeWordHotkey,
            OcrHotkeyId, config.OcrHotkey);
        if (!string.IsNullOrEmpty(failure)) return failure;

        bool previousAutoStart = AutoStartService.IsEnabled();
        string? autoStartError = AutoStartService.Apply(autoStart);
        if (autoStartError is not null)
        {
            return $"开机自启：{autoStartError}";
        }

        try
        {
            ConfigService.Save(config);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // 配置没写进去就把系统状态改了会造成不一致，尽力回滚
            AutoStartService.Apply(previousAutoStart);
            return $"配置保存失败：{ex.Message}";
        }

        bool hotkeysChanged =
            config.TakeWordHotkey != _config.TakeWordHotkey ||
            config.OcrHotkey != _config.OcrHotkey;

        _config = config;
        _translator = new BaiduTranslator(config);

        var notices = new List<string>();
        if (hotkeysChanged)
        {
            notices.Add($"快捷键已更新：{config.TakeWordHotkey} 取词 / {config.OcrHotkey} 截图");
        }
        if (autoStart != previousAutoStart)
        {
            notices.Add(autoStart ? "开机自启已开启" : "开机自启已关闭");
        }
        if (notices.Count > 0) _tray.Notify(string.Join("；", notices));
        return null;
    }

    private void ExitApplication()
    {
        _result.ForceClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singletonMutex?.ReleaseMutex();
        _singletonMutex?.Dispose();
        base.OnExit(e);
    }
}
