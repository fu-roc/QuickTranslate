using System.Diagnostics;
using System.Windows;
using QuickTranslate.Services;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace QuickTranslate.Windows;

/// <summary>
/// 手动检查更新的结果窗。它是用户主动点开的，所以会正常激活并接收键盘；
/// 与翻译结果窗「绝不抢焦点」的策略相反。
/// </summary>
public partial class UpdateWindow : Window
{
    private CancellationTokenSource? _cts;
    private string? _releaseUrl;
    private bool _closed;

    public UpdateWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RunCheckAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            CancelCheck();
        };
    }

    private async Task RunCheckAsync()
    {
        if (_closed) return;

        CancelCheck();
        var cts = new CancellationTokenSource();
        _cts = cts;
        SetChecking();

        UpdateCheckResult result;
        try
        {
            result = await UpdateService.CheckAsync(cts.Token);
        }
        catch (Exception ex)
        {
            // CheckAsync 内部已经分类兜底，这里是最后一道保险，绝不让 async void 抛到全局钩子
            LogService.Error("更新窗口：检查流程异常", ex);
            result = new UpdateCheckResult(
                UpdateStatus.Failed,
                "检查更新失败：程序遇到意外错误，详情已写入日志。",
                UpdateService.CurrentVersion);
        }

        // 用户在检查期间点了「重试」或关了窗，旧请求的结果不许覆盖新状态
        if (_closed || cts.IsCancellationRequested || !ReferenceEquals(_cts, cts)) return;
        try { Apply(result); }
        catch (Exception ex) { LogService.Error("更新窗口：呈现检查结果失败", ex); }
    }

    private void SetChecking()
    {
        TitleText.Text = "检查更新";
        TitleText.Foreground = Brush("#6B7280");
        StatusText.Text = "正在连接 GitHub 检查最新版本…";
        StatusText.Foreground = Brush("#6B7280");
        NotesPanel.Visibility = Visibility.Collapsed;
        NotesText.Text = string.Empty;
        VersionText.Text = string.Empty;
        _releaseUrl = null;
        RetryButton.Visibility = Visibility.Collapsed;
        RetryButton.IsEnabled = false;
        DownloadButton.Visibility = Visibility.Collapsed;
        DownloadButton.IsEnabled = false;
    }

    private void Apply(UpdateCheckResult result)
    {
        _releaseUrl = result.ReleaseUrl;
        string local = UpdateService.Format(result.CurrentVersion);
        string remote = result.LatestVersion is null
            ? "未知"
            : UpdateService.Format(result.LatestVersion);

        switch (result.Status)
        {
            case UpdateStatus.Available:
                TitleText.Text = $"发现新版本 {remote}";
                TitleText.Foreground = Brush("#2F6BED");
                StatusText.Text = result.Message;
                StatusText.Foreground = Brush("#1F2937");
                ShowNotes(result.Notes);
                VersionText.Text = $"当前版本 {local} · 检查时间 {DateTime.Now:HH:mm:ss}";
                ShowDownloadButton("打开下载页面");
                RetryButton.Visibility = Visibility.Collapsed;
                break;

            case UpdateStatus.UpToDate:
                TitleText.Text = "已是最新版本";
                TitleText.Foreground = Brush("#1D6F42");
                StatusText.Text = result.Message;
                StatusText.Foreground = Brush("#1F2937");
                ShowNotes(result.Notes);
                VersionText.Text = $"当前版本 {local} · 远端版本 {remote} · 检查时间 {DateTime.Now:HH:mm:ss}";
                ShowDownloadButton("查看发布页面");
                RetryButton.Visibility = Visibility.Collapsed;
                break;

            case UpdateStatus.Failed:
                TitleText.Text = "检查更新失败";
                TitleText.Foreground = Brush("#C0392B");
                StatusText.Text = result.Message;
                StatusText.Foreground = Brush("#C0392B");
                NotesPanel.Visibility = Visibility.Collapsed;
                VersionText.Text = $"当前版本 {local} · 检查时间 {DateTime.Now:HH:mm:ss}";
                RetryButton.Visibility = Visibility.Visible;
                RetryButton.IsEnabled = true;
                ShowDownloadButton("手动打开发布页面");
                break;

            case UpdateStatus.Cancelled:
            default:
                return;
        }
    }

    private void ShowNotes(string? notes)
    {
        bool hasNotes = !string.IsNullOrWhiteSpace(notes);
        NotesPanel.Visibility = hasNotes ? Visibility.Visible : Visibility.Collapsed;
        NotesText.Text = hasNotes ? notes! : string.Empty;
    }

    private void ShowDownloadButton(string caption)
    {
        DownloadButton.Content = caption;
        DownloadButton.Visibility = Visibility.Visible;
        DownloadButton.IsEnabled = true;
    }

    private void Download_OnClick(object sender, RoutedEventArgs e) => OpenReleasePage();

    private void OpenReleasePage()
    {
        string url = SafeReleaseUrl(_releaseUrl);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            LogService.Info("检查更新：已打开发布页面");
        }
        catch (Exception ex)
        {
            LogService.Error("检查更新：打开发布页面失败", ex);
            StatusText.Text = "无法自动打开浏览器，请手动访问：" + url;
            StatusText.Foreground = Brush("#C0392B");
        }
    }

    /// <summary>只接受 GitHub 的 https 链接，避免把接口返回的任意字符串交给 ShellExecute。</summary>
    private static string SafeReleaseUrl(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)))
        {
            return uri.AbsoluteUri;
        }
        return UpdateService.ReleasesPageUrl;
    }

    private async void Retry_OnClick(object sender, RoutedEventArgs e)
    {
        try { await RunCheckAsync(); }
        catch (Exception ex) { LogService.Error("更新窗口：重试失败", ex); }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void Window_OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == WpfKey.Escape) Close();
    }

    private void CancelCheck()
    {
        var cts = _cts;
        _cts = null;
        if (cts is null) return;
        try { cts.Cancel(); }
        catch (Exception ex) { LogService.Warn("检查更新：取消在途请求失败", ex); }
        finally { cts.Dispose(); }
    }

    private static System.Windows.Media.SolidColorBrush Brush(string hex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return new System.Windows.Media.SolidColorBrush(color);
    }
}
