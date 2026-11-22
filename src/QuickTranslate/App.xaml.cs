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
    private MainWindow? _mainWindow;
    private UpdateWindow? _updateWindow;
    private BaiduTranslator _translator = null!;
    private CancellationTokenSource? _chainCts;

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

        _config = ConfigService.Load(out string? configNotice, out bool firstRun);
        _translator = new BaiduTranslator(_config);
        _result = new ResultWindow();
        _result.Hidden += CancelCurrentChain;
        _hotkeys = new HotkeyService();
        _hotkeys.HotkeyPressed += OnHotkeyPressed;
        _tray = new TrayService(ShowMainWindow, () => OpenSettings(), ShowUpdateWindow, ExitApplication);

        bool hotkeysRegistered = RegisterHotkeys();
        CheckAutoStart();

        if (configNotice is not null) _tray.Notify(configNotice);
        else if (firstRun && hotkeysRegistered)
            _tray.Notify($"选中英文单词，按 {_config.TakeWordHotkey} 离线查词。");
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

    private void CheckAutoStart()
    {
        string? notice = AutoStartService.Check();
        if (notice is not null) _tray.Notify($"开机自启：{notice}");
    }

    /// <summary>唯一的注册点：启动时、设置窗口关闭后都走这里，按当前 _config 注册。</summary>
    private bool RegisterHotkeys()
    {
        string error = _hotkeys.RegisterPair(TakeWordHotkeyId, _config.TakeWordHotkey, OcrHotkeyId, _config.OcrHotkey);
        if (error.Length == 0) return true;
        _result.ShowError(error, "打开设置", () => OpenSettings());
        return false;
    }

    private async void OnHotkeyPressed(int id)
    {
        if (_chainCts is not null || _result.IsVisible)
        {
            _result.HideWindow();
            CancelCurrentChain();
            return;
        }
        await RunChainAsync(id);
    }

    private void RunChainLater(int hotkeyId) =>
        Dispatcher.BeginInvoke(new Action(() => _ = RunChainAsync(hotkeyId)));

    private async Task RunChainAsync(int hotkeyId)
    {
        if (_chainCts is not null) return;
        using var cts = new CancellationTokenSource();
        _chainCts = cts;
        try
        {
            if (hotkeyId == TakeWordHotkeyId) await RunTakeWordAsync(cts.Token);
            else if (hotkeyId == OcrHotkeyId) await RunOcrAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            LogService.Error($"链路 {hotkeyId} 异常", ex);
            if (!cts.IsCancellationRequested) ShowFailure(ex, hotkeyId);
        }
        finally
        {
            _chainCts = null;
        }
    }

    private void CancelCurrentChain()
    {
        try { _chainCts?.Cancel(); }
        catch (Exception ex) { LogService.Warn("取消在途链路失败", ex); }
    }

    private async Task RunTakeWordAsync(CancellationToken token)
    {
        // 先从前台窗口复制选区，再显示悬浮窗。
        string? selected = await ClipboardService.CaptureSelectedTextAsync(token);
        token.ThrowIfCancellationRequested();
        if (selected is null)
        {
            _result.ShowError(
                "未检测到选中文本：请确认已选中文字后重试；管理员权限窗口、终端或禁止复制的软件可能无法读取。下方按钮是手动切换，不会自动开始截图。",
                "改用截图 OCR", () => RunChainLater(OcrHotkeyId));
            return;
        }

        string? pronunciationSource = PronunciationService.UsesEnglishAlphabet(selected) ? selected.Trim() : null;
        BaiduTranslator translator = _translator;
        if (_config.TargetLanguage == TranslationLanguage.SimplifiedChinese)
        {
            _result.ShowLoading("查词中…", pronunciationSource);
            string[]? definitions = await EnglishChineseDictionary.FindAsync(selected, token);
            token.ThrowIfCancellationRequested();
            if (definitions is not null)
            {
                _result.ShowDefinitions(selected, definitions);
                return;
            }
        }

        _result.ShowLoading("翻译中…", pronunciationSource);
        await ShowTranslationAsync(selected, translator, token, pronunciationSource);
    }

    private async Task RunOcrAsync(CancellationToken token)
    {
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
        await ShowTranslationAsync(recognized, _translator, token);
    }

    private async Task ShowTranslationAsync(
        string source, BaiduTranslator translator, CancellationToken token, string? pronunciationSource = null)
    {
        string translated = await translator.TranslateAsync(source, token);
        token.ThrowIfCancellationRequested();
        _result.ShowResult(source, translated, pronunciationSource);
    }

    /// <summary>底层异常一律先映射成中文原因，再按错误类别决定是否给「打开设置」/「重试」。</summary>
    private void ShowFailure(Exception ex, int failedHotkeyId)
    {
        FailureInfo info = ErrorMapper.Map(ex);
        (string? Label, Action? Action) recovery = info.Action switch
        {
            ErrorAction.Settings => ("配置翻译服务", () => OpenSettings(focusTranslationService: true)),
            ErrorAction.Retry => ("重试", () => RunChainLater(failedHotkeyId)),
            _ => (null, null),
        };
        _result.ShowError(info.Message, recovery.Label, recovery.Action,
            keepPronunciation: failedHotkeyId == TakeWordHotkeyId);
    }

    private void ShowMainWindow()
    {
        _mainWindow ??= new MainWindow(_config)
        {
            TranslateAsync = (text, from, to, token) => _translator.TranslateAsync(text, from, to, token),
            SaveLanguage = SaveMainWindowLanguage,
            OpenSettings = OpenSettings,
        };
        if (!_mainWindow.IsVisible) _mainWindow.Show();
        try { _mainWindow.Activate(); }
        catch (Exception ex) { LogService.Warn("激活主界面失败", ex); }
    }

    private string? SaveMainWindowLanguage(string from, string to)
    {
        _config.MainWindowSourceLanguage = from;
        _config.MainWindowTargetLanguage = to;
        try
        {
            ConfigService.Save(_config);
            return null;
        }
        catch (Exception ex) when (ConfigService.IsSaveFailure(ex))
        {
            LogService.Error("主界面语言偏好保存失败", ex);
            return "语言偏好保存失败：无法写入配置文件，本次选择仍可用，重启后会恢复。详情见日志";
        }
    }

    private void OpenSettings(bool focusTranslationService = false)
    {
        _result.HideWindow();
        CancelCurrentChain();

        // 录制期间由设置窗口接收按键。
        _hotkeys.UnregisterAll();
        try
        {
            new SettingsWindow(_config, focusTranslationService)
            {
                ApplyConfig = ApplyConfig,
                CheckForUpdates = ShowUpdateWindow,
            }.ShowDialog();
        }
        finally { RegisterHotkeys(); }
    }

    private string? ApplyConfig(AppConfig config, bool autoStart)
    {
        // 主界面单独保存语种偏好。
        config.MainWindowSourceLanguage = _config.MainWindowSourceLanguage;
        config.MainWindowTargetLanguage = _config.MainWindowTargetLanguage;

        string failure = _hotkeys.ValidatePair(
            TakeWordHotkeyId, config.TakeWordHotkey,
            OcrHotkeyId, config.OcrHotkey);
        if (!string.IsNullOrEmpty(failure)) return failure;

        var (previousAutoStart, pathWasCurrent) = AutoStartService.GetStatus();

        try
        {
            ConfigService.Save(config);
        }
        catch (Exception ex) when (ConfigService.IsSaveFailure(ex))
        {
            LogService.Error("配置写盘失败", ex);
            return "配置保存失败：无法写入配置文件，请检查 %APPDATA% 目录权限，详情见日志";
        }

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
        _mainWindow?.ApplyConfig(config);

        var notices = new List<string>();
        if (hotkeysChanged) notices.Add($"快捷键已更新：{config.TakeWordHotkey} 取词 / {config.OcrHotkey} 截图");
        if (autoStart != previousAutoStart) notices.Add(autoStart ? "开机自启已开启" : "开机自启已关闭");
        else if (!pathWasCurrent && autoStart) notices.Add("开机自启路径已更新");
        if (notices.Count > 0) _tray.Notify(string.Join("；", notices));
        return null;
    }

    private void ShowUpdateWindow()
    {
        if (_updateWindow is null)
        {
            _updateWindow = new UpdateWindow();
            _updateWindow.Closed += (_, _) => _updateWindow = null;
            _updateWindow.Show();
        }
        try { _updateWindow?.Activate(); }
        catch (Exception ex) { LogService.Warn("激活更新窗口失败", ex); }
    }

    private void ExitApplication()
    {
        CancelCurrentChain();
        _result.ForceClose();
        // 主窗口的 Closing 默认会 Cancel 并隐藏，退出时必须先放行，否则 Shutdown 会被挡住
        _mainWindow?.ForceClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
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
