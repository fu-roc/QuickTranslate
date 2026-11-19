using System.Diagnostics;
using System.Windows;
using QuickTranslate.Services;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace QuickTranslate.Windows;

public partial class UpdateWindow : Window
{
    private CancellationTokenSource? _cts;
    private string? _releaseUrl;

    public UpdateWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RunCheckAsync();
        Closed += (_, _) => CancelCheck();
    }

    private async Task RunCheckAsync()
    {
        CancelCheck();
        using var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            SetChecking();
            var result = await UpdateService.CheckAsync(cts.Token);
            if (!cts.IsCancellationRequested) Apply(result);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            LogService.Error("更新窗口：检查流程异常", ex);
            if (!cts.IsCancellationRequested)
            {
                Apply(new UpdateCheckResult(UpdateStatus.Failed,
                    "检查更新失败：程序遇到意外错误，详情已写入日志。", AppVersion.Value));
            }
        }
        finally
        {
            if (ReferenceEquals(_cts, cts)) _cts = null;
        }
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
        DownloadButton.Visibility = Visibility.Collapsed;
    }

    private void Apply(UpdateCheckResult result)
    {
        _releaseUrl = result.ReleaseUrl;
        string local = UpdateService.Format(result.CurrentVersion);
        string remote = result.LatestVersion is null
            ? "未知"
            : UpdateService.Format(result.LatestVersion);

        (string title, string color, string caption) = result.Status switch
        {
            UpdateStatus.Available => ($"发现新版本 {remote}", "#2F6BED", "打开下载页面"),
            UpdateStatus.UpToDate => ("已是最新版本", "#1D6F42", "查看发布页面"),
            _ => ("检查更新失败", "#C0392B", "手动打开发布页面"),
        };
        bool failed = result.Status == UpdateStatus.Failed;
        TitleText.Text = title;
        TitleText.Foreground = Brush(color);
        StatusText.Text = result.Message;
        StatusText.Foreground = Brush(failed ? color : "#1F2937");
        NotesText.Text = result.Notes ?? string.Empty;
        NotesPanel.Visibility = NotesText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        string remoteVersion = result.Status == UpdateStatus.UpToDate ? $" · 远端版本 {remote}" : string.Empty;
        VersionText.Text = $"当前版本 {local}{remoteVersion} · 检查时间 {DateTime.Now:HH:mm:ss}";
        RetryButton.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.Content = caption;
        DownloadButton.Visibility = Visibility.Visible;
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

    private async void Retry_OnClick(object sender, RoutedEventArgs e) => await RunCheckAsync();

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
    }

    private static System.Windows.Media.SolidColorBrush Brush(string hex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return new System.Windows.Media.SolidColorBrush(color);
    }
}
