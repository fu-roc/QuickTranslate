using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
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
    private bool _ownsSingletonMutex;
    private AppConfig _config = null!;
    private HotkeyService _hotkeys = null!;
    private TrayService _tray = null!;
    private ResultWindow _result = null!;
    private UpdateWindow? _updateWindow;
    private BaiduTranslator _translator = null!;
    private bool _busy;
    private CancellationTokenSource? _chainCts;
    private bool _chainCancelled;

    protected override void OnStartup(StartupEventArgs e)
    {
        InstallExceptionHooks();
        LogService.Info($"启动 QuickTranslate v{AppVersion.Display}");
        base.OnStartup(e);

        _singletonMutex = new Mutex(true, @"Local\QuickTranslate.Singleton", out bool createdNew);
        _ownsSingletonMutex = createdNew;
        if (!createdNew)
        {
            MessageBox.Show("QuickTranslate 已经在运行了。", "QuickTranslate",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _config = ConfigService.Load(out bool createdNewConfig, out string? configNotice);
        _translator = new BaiduTranslator(_config);
        _result = new ResultWindow();
        _result.Hidden += CancelCurrentChain;
        _hotkeys = new HotkeyService();
        _hotkeys.HotkeyPressed += OnHotkeyPressed;
        _tray = new TrayService(OpenSettings, ShowUpdateWindow, ExitApplication);

        RegisterHotkeys();
        CheckAutoStart();

        if (configNotice is not null) _tray.Notify(configNotice);
        if (createdNewConfig || !_config.IsConfigured)
        {
            _tray.Notify("请先填写百度翻译 APPID 和密钥");
            OpenSettings();
        }
    }

    private void InstallExceptionHooks()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogService.Error("AppDomain 未处理异常", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogService.Error("未观察的任务异常", args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.Error("UI 线程未处理异常", e.Exception);
        e.Handled = true;
        try { _tray?.Notify("程序遇到意外错误，已写入日志"); }
        catch (Exception) { /* 通知失败不影响已记录的日志 */ }
    }

    /// <summary>
    /// 只读检查开机自启状态，绝不在此处写注册表。
    /// 未签名 exe 在启动阶段写 HKCU\...\Run 会被 Defender 的
    /// Behavior:Win32/Persistence.A!ml 判为「持久化」并隔离删除整个程序。
    /// </summary>
    private void CheckAutoStart()
    {
        string? notice = AutoStartService.Check();
        if (notice is not null) _tray.Notify($"开机自启：{notice}");
    }

    /// <summary>唯一的注册点：启动时、设置窗口关闭后都走这里，按当前 _config 注册。</summary>
    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
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
        if (_busy)
        {
            _result.HideWindow();
            CancelCurrentChain();
            return;
        }
        if (_result.IsShowing)
        {
            _result.HideWindow();
            return;
        }
        await RunChainAsync(id);
    }

    private void RunChainLater(int hotkeyId) =>
        Dispatcher.BeginInvoke(new Action(() => _ = RunChainAsync(hotkeyId)));

    private async Task RunChainAsync(int hotkeyId)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (hotkeyId == TakeWordHotkeyId) await RunTakeWordAsync();
            else if (hotkeyId == OcrHotkeyId) await RunOcrAsync();
        }
        catch (OperationCanceledException) when (_chainCancelled)
        {
            // 用户已经关掉窗口：迟到的结果不许再把悬浮窗弹回来
        }
        catch (Exception ex)
        {
            LogService.Error($"链路 {hotkeyId} 异常", ex);
            ShowFailure(ex, hotkeyId);
        }
        finally
        {
            _busy = false;
        }
    }

    private CancellationToken BeginChain()
    {
        CancelCurrentChain();
        _chainCts = new CancellationTokenSource();
        _chainCancelled = false;
        return _chainCts.Token;
    }

    private void CancelCurrentChain()
    {
        var cts = _chainCts;
        _chainCts = null;
        if (cts is null) return;
        _chainCancelled = true;
        try { cts.Cancel(); }
        catch (Exception ex) { LogService.Warn("取消在途链路失败", ex); }
        finally { cts.Dispose(); }
    }

    private async Task RunTakeWordAsync()
    {
        CancellationToken token = BeginChain();

        // 取词阶段绝不显示或激活任何窗口，否则会抢走前台焦点，Ctrl+C 会发给自己
        string? selected = await ClipboardService.CaptureSelectedTextAsync(token);
        token.ThrowIfCancellationRequested();
        if (selected is null)
        {
            _result.ShowError(
                "未检测到选中文本：请确认已选中文字，并完全松开快捷键后再试；管理员权限窗口、终端或禁止复制的软件可能无法读取。下方按钮是手动切换，不会自动开始截图。",
                "改用截图 OCR", () => RunChainLater(OcrHotkeyId));
            return;
        }

        _result.ShowLoading("翻译中…");
        string translated = await _translator.TranslateAsync(selected, token);
        token.ThrowIfCancellationRequested();
        _result.ShowResult(selected, translated);
    }

    private async Task RunOcrAsync()
    {
        CancellationToken token = BeginChain();

        var cursor = NativeMethods.GetCursorPosition();
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        double scale = NativeMethods.GetMonitorScale(cursor);
        var overlay = new SelectionOverlayWindow(screen.Bounds, scale);
        overlay.ShowDialog();

        token.ThrowIfCancellationRequested();
        var rect = overlay.Selection;
        if (rect is null) return; // 用户按 Esc 主动取消，不打扰

        using var bitmap = ScreenCaptureService.Capture(rect.Value);
        if (bitmap is null)
        {
            _result.ShowError("框选区域太小或超出屏幕，请重新框选。",
                "重新框选", () => RunChainLater(OcrHotkeyId));
            return;
        }

        _result.ShowLoading("识别文字中…");
        string recognized = await OcrService.RecognizeAsync(bitmap, token);
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(recognized))
        {
            _result.ShowError("OCR 没有识别到文字，试试框得更准一点或换更清晰的区域。",
                "改用取词翻译", () => RunChainLater(TakeWordHotkeyId));
            return;
        }

        _result.ShowLoading("翻译中…");
        string translated = await _translator.TranslateAsync(recognized, token);
        token.ThrowIfCancellationRequested();
        _result.ShowResult(recognized, translated);
    }

    /// <summary>底层异常一律先映射成中文原因，再按错误类别决定是否给「打开设置」/「重试」。</summary>
    private void ShowFailure(Exception ex, int failedHotkeyId)
    {
        FailureInfo info = ErrorMapper.Map(ex);
        switch (info.Action)
        {
            case ErrorAction.Settings:
                _result.ShowError(info.Message, "打开设置", OpenSettings);
                break;
            case ErrorAction.Retry:
                _result.ShowError(info.Message, "重试", () => RunChainLater(failedHotkeyId));
                break;
            default:
                _result.ShowError(info.Message);
                break;
        }
    }

    private void OpenSettings()
    {
        _result.HideWindow();
        CancelCurrentChain(); // 窗口当时不可见时 Hidden 不会触发，这里兜底

        // 录制快捷键期间必须注销旧热键，否则用户想录入的组合键会被旧热键抢走并触发翻译
        _hotkeys.UnregisterAll();

        var window = new SettingsWindow(_config) { ApplyConfig = ApplyConfig };
        window.CheckForUpdates = ShowUpdateWindow;
        window.ShowDialog();

        // 无论保存成功、失败还是取消，都由唯一的注册点按当前生效的 _config 恢复
        RegisterHotkeys();
    }

    /// <summary>
    /// 应用新配置：热键只做「能否注册」的校验且不保留注册状态，配置写盘成功后才提交内存状态；
    /// 任一步失败都不留下新状态，窗口保持打开，旧热键在窗口关闭时由 RegisterHotkeys 统一恢复。
    /// </summary>
    private string? ApplyConfig(AppConfig config, bool autoStart)
    {
        // 1) 无持久副作用的热键校验（校验完立即注销，不留下「新键已生效但配置没保存」的状态）
        string failure = _hotkeys.ValidatePair(
            TakeWordHotkeyId, config.TakeWordHotkey,
            OcrHotkeyId, config.OcrHotkey);
        if (!string.IsNullOrEmpty(failure)) return failure;

        bool previousAutoStart = AutoStartService.IsEnabled();
        bool pathWasStale = previousAutoStart && !AutoStartService.IsPathCurrent();

        // 2) 原子写盘；此步失败时没有任何其他副作用，直接返回
        try
        {
            ConfigService.Save(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or System.Security.SecurityException
                                   or System.Security.Cryptography.CryptographicException)
        {
            LogService.Error("配置写盘失败", ex);
            return "配置保存失败：无法写入配置文件，请检查 %APPDATA% 目录权限，详情见日志";
        }

        // 3) 每次保存都调用 Apply（内部幂等：值已正确时不写注册表，不增加 Defender 行为）。
        //    这样程序搬家后指向旧路径的自启项，用户点一次「保存」就能自动修正。
        string? autoStartError = AutoStartService.Apply(autoStart);
        if (autoStartError is not null)
        {
            try { ConfigService.Save(_config); }
            catch (Exception rollbackEx)
            {
                LogService.Error("自启失败后的配置回滚也失败", rollbackEx);
                return $"开机自启：{autoStartError}；且配置回滚失败，请检查目录权限";
            }
            return $"开机自启：{autoStartError}（配置已回滚）";
        }

        bool hotkeysChanged =
            config.TakeWordHotkey != _config.TakeWordHotkey ||
            config.OcrHotkey != _config.OcrHotkey;

        _config = config;
        _translator = new BaiduTranslator(config);

        var notices = new List<string>();
        if (hotkeysChanged) notices.Add($"快捷键已更新：{config.TakeWordHotkey} 取词 / {config.OcrHotkey} 截图");
        if (autoStart != previousAutoStart) notices.Add(autoStart ? "开机自启已开启" : "开机自启已关闭");
        else if (pathWasStale && autoStart) notices.Add("开机自启路径已更新");
        if (notices.Count > 0) _tray.Notify(string.Join("；", notices));
        return null;
    }

    /// <summary>
    /// 托盘菜单与设置页共用的唯一入口。窗口是用户主动点开的，所以会正常激活；
    /// 重复点击时只激活已有窗口，不叠加多个检查请求。
    /// </summary>
    private void ShowUpdateWindow()
    {
        if (_updateWindow is { IsVisible: true })
        {
            try { _updateWindow.Activate(); }
            catch (Exception ex) { LogService.Warn("激活更新窗口失败", ex); }
            return;
        }

        var window = new UpdateWindow();
        _updateWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_updateWindow, window)) _updateWindow = null;
        };
        window.Show();
        try { window.Activate(); }
        catch (Exception ex) { LogService.Warn("激活更新窗口失败", ex); }
    }

    private void ExitApplication()
    {
        CancelCurrentChain();
        _result.ForceClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _chainCts?.Dispose();
        LogService.Info("QuickTranslate 退出");
        // 命名 Mutex 已存在时当前线程并不拥有它，无条件 ReleaseMutex 会在退出时抛未处理异常
        if (_ownsSingletonMutex)
        {
            try { _singletonMutex?.ReleaseMutex(); }
            catch (ApplicationException) { /* 退出阶段无需上报 */ }
        }
        _singletonMutex?.Dispose();
        base.OnExit(e);
    }
}
