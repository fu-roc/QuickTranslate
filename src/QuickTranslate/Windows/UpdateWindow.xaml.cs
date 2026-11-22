using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuickTranslate.Services;
using WpfBorder = System.Windows.Controls.Border;
using WpfButton = System.Windows.Controls.Button;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace QuickTranslate.Windows;

public partial class UpdateWindow : Window
{
    private CancellationTokenSource? _cts;
    private string? _releaseUrl;

    public UpdateWindow()
    {
        InitializeComponent();
        MaxHeight = Math.Min(600, SystemParameters.WorkArea.Height);
        Closed += (_, _) =>
        {
            CancelCheck();
            StatusIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        };
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e) => await RunCheckAsync();

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
        SetStatusHeader("正在检查更新", "↻", "AccentTextBrush", "HighlightBrush");
        SetDetail("正在连接 GitHub…");
        if (SystemParameters.ClientAreaAnimation)
        {
            StatusIconRotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        CurrentVersionText.Text = AppVersion.Display;
        LatestVersionPanel.Visibility = Visibility.Collapsed;
        NotesPanel.Visibility = Visibility.Collapsed;
        NotesText.Text = string.Empty;
        CheckedAtText.Text = string.Empty;
        _releaseUrl = null;
        RetryButton.Visibility = Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Collapsed;
    }

    private void Apply(UpdateCheckResult result)
    {
        _releaseUrl = result.ReleaseUrl;
        StatusIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        CurrentVersionText.Text = UpdateService.Format(result.CurrentVersion);
        LatestVersionText.Text = result.LatestVersion is { } latest ? UpdateService.Format(latest) : string.Empty;
        LatestVersionPanel.Visibility = result.LatestVersion is null ? Visibility.Collapsed : Visibility.Visible;

        string detail;
        switch (result.Status)
        {
            case UpdateStatus.Available:
                SetStatusHeader("发现新版本", "↑", "AccentTextBrush", "HighlightBrush");
                detail = result.InstallerName is { } installer ? $"发布页提供系统级安装包 {installer}。" : string.Empty;
                break;
            case UpdateStatus.UpToDate:
                SetStatusHeader("已是最新版本", "✓", "SuccessBrush", "UpdateSuccessTint");
                detail = string.Empty;
                break;
            default:
                SetStatusHeader("检查更新失败", "!", "ErrorBrush", "UpdateErrorTint");
                detail = result.Message;
                break;
        }

        SetDetail(detail);
        bool available = result.Status == UpdateStatus.Available;
        bool failed = result.Status == UpdateStatus.Failed;
        LatestVersionText.SetResourceReference(WpfTextBlock.ForegroundProperty, available ? "AccentTextBrush" : "TextBrush");
        NotesText.Text = result.Notes ?? string.Empty;
        NotesPanel.Visibility = NotesText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (NotesPanel.Visibility == Visibility.Visible) NotesScroll.ScrollToTop();
        CheckedAtText.Text = $"检查于 {DateTime.Now:HH:mm:ss}";
        RetryButton.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.Content = available ? "打开下载页面" : "查看发布页面";
        DownloadButton.SetResourceReference(WpfButton.StyleProperty, available ? "PrimaryButton" : "ActionButton");
        DownloadButton.Visibility = Visibility.Visible;
    }

    private void SetStatusHeader(string title, string icon, string foreground, string background)
    {
        TitleText.Text = title;
        StatusIcon.Text = icon;
        StatusIcon.SetResourceReference(WpfTextBlock.ForegroundProperty, foreground);
        StatusTile.SetResourceReference(WpfBorder.BackgroundProperty, background);
    }

    private void SetDetail(string detail, bool isError = false)
    {
        StatusText.Text = detail;
        StatusText.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        StatusText.SetResourceReference(WpfTextBlock.ForegroundProperty, isError ? "ErrorBrush" : "MutedBrush");
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
            SetDetail("无法自动打开浏览器，请手动访问：" + url, isError: true);
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
}
