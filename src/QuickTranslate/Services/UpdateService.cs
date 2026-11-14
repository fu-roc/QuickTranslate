using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace QuickTranslate.Services;

public enum UpdateStatus
{
    UpToDate,   // 已是最新版
    Available,  // 远端有新版本
    Failed,     // 检查失败，Message 是可直接展示的中文原因
    Cancelled,  // 用户关窗取消了检查
}

/// <summary>
/// 一次检查的结果。所有对外文案都是中文；底层异常只写日志，绝不把 ex.Message 交给界面。
/// </summary>
public sealed record UpdateCheckResult(
    UpdateStatus Status,
    string Message,
    Version CurrentVersion,
    Version? LatestVersion = null,
    string? ReleaseUrl = null,
    string? Notes = null,
    string? InstallerName = null,
    bool CanOpenReleasePage = false);

/// <summary>
/// 手动检查更新：只访问 GitHub 公开 Release API，不下载、不执行任何文件。
/// 程序启动阶段不调用这里 —— 只有用户点「检查更新」时才联网。
/// </summary>
public static class UpdateService
{
    public const string Repository = "fu-roc/QuickTranslate";
    public const string ApiUrl = $"https://api.github.com/repos/{Repository}/releases/latest";
    public const string ReleasesPageUrl = $"https://github.com/{Repository}/releases/latest";

    private const int NotesMaxLines = 14;
    private const int NotesMaxChars = 900;

    private static readonly HttpClient Client = CreateClient();

    /// <summary>程序集版本（csproj 的统一版本源），归一化为 4 段便于比较。</summary>
    public static Version CurrentVersion =>
        Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        // GitHub API 要求 User-Agent，否则直接 403
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"QuickTranslate/{AppVersion.Display}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return client;
    }

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken token)
    {
        Version current = CurrentVersion;
        try
        {
            using var response = await Client
                .GetAsync(ApiUrl, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                return Failed(current,
                    "GitHub 暂时限制了本次请求（未登录的接口每小时有次数上限），请稍后再试，或直接打开发布页面查看。");
            }
            if (!response.IsSuccessStatusCode)
            {
                return Failed(current,
                    $"检查更新失败：GitHub 返回 {(int)response.StatusCode}，请稍后再试，或直接打开发布页面查看。");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
            return ParseRelease(document.RootElement, current);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateStatus.Cancelled, string.Empty, current);
        }
        catch (TaskCanceledException)
        {
            return Failed(current, "检查更新超时：连接 GitHub 时间过长，请检查网络或代理后重试。");
        }
        catch (HttpRequestException ex)
        {
            LogService.Warn("检查更新：网络请求失败", ex);
            return Failed(current, "无法连接 GitHub：请检查网络、代理或防火墙后重试，也可以直接打开发布页面。");
        }
        catch (JsonException ex)
        {
            LogService.Warn("检查更新：响应不是合法 JSON", ex);
            return Failed(current, "检查更新失败：GitHub 返回的数据无法解析，请稍后再试。");
        }
        catch (Exception ex)
        {
            LogService.Error("检查更新：未预期的异常", ex);
            return Failed(current, "检查更新失败：程序遇到意外错误，详情已写入日志。");
        }
    }

    /// <summary>纯函数：解析 releases/latest 的 JSON 并与当前版本比较，便于隔离测试。</summary>
    public static UpdateCheckResult ParseRelease(JsonElement root, Version current)
    {
        current = Normalize(current);
        string tag = GetString(root, "tag_name");
        if (!TryParseTag(tag, out Version latest))
        {
            return Failed(current, "检查更新失败：GitHub 上的版本号格式无法识别，请直接打开发布页面查看。");
        }

        latest = Normalize(latest);
        string url = FirstNonEmpty(GetString(root, "html_url"), ReleasesPageUrl);
        string notes = FormatNotes(GetString(root, "body"));
        string? installer = FindInstallerName(root);
        bool isDraft = GetBool(root, "draft");
        bool isPrerelease = GetBool(root, "prerelease");

        if (isDraft)
        {
            return Failed(current, "检查更新失败：远端最新发布还是草稿，请直接打开发布页面查看。");
        }
        if (isPrerelease)
        {
            return Failed(current, "检查更新失败：远端最新发布是预发布版本，请直接打开发布页面查看。");
        }

        if (latest.CompareTo(current) <= 0)
        {
            return new UpdateCheckResult(
                UpdateStatus.UpToDate,
                $"已是最新版本（当前 {Format(current)}）。",
                current, latest, url, notes, installer,
                CanOpenReleasePage: true);
        }

        string message = $"发现新版本 {Format(latest)}，当前是 {Format(current)}。";
        if (installer is not null) message += $" 发布页里有系统级安装包 {installer}。";

        return new UpdateCheckResult(
            UpdateStatus.Available,
            message,
            current, latest, url, notes, installer,
            CanOpenReleasePage: true);
    }

    /// <summary>把 v1.1.0 / 1.1.0 / 1.1.0-beta.2 之类 tag 解析成版本；失败返回 false。</summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        string text = tag.Trim().TrimStart('v', 'V');
        int dash = text.IndexOf('-');
        if (dash >= 0) text = text[..dash];
        int plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];
        if (text.Length == 0) return false;
        if (!text.Contains('.')) text += ".0";

        if (!Version.TryParse(text, out Version? parsed) || parsed is null) return false;
        version = Normalize(parsed);
        return true;
    }

    /// <summary>
    /// Release 说明是 Markdown（表格 / 标题 / 加粗），直接塞进 TextBlock 很难读。
    /// 这里只保留能当要点看的正文行，去掉标记符号并限制长度。
    /// </summary>
    public static string FormatNotes(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        var lines = new List<string>();
        foreach (string raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('|') || line.StartsWith('#') || line.StartsWith("---")) continue;

            line = line.Replace("**", string.Empty).Replace("`", string.Empty);
            if (line.StartsWith("- ")) line = "• " + line[2..];
            if (line.Length == 0) continue;
            lines.Add(line);
            if (lines.Count >= NotesMaxLines) break;
        }

        string result = string.Join(Environment.NewLine, lines);
        if (result.Length > NotesMaxChars)
        {
            result = result[..NotesMaxChars].TrimEnd() + Environment.NewLine + "……（完整说明见发布页面）";
        }
        return result;
    }

    public static string Format(Version version)
    {
        Version v = Normalize(version);
        return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
    }

    private static UpdateCheckResult Failed(Version current, string message) =>
        new(UpdateStatus.Failed, message, current, null, ReleasesPageUrl, null, null,
            CanOpenReleasePage: true);

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    private static string? FindInstallerName(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out JsonElement assets) ||
            assets.ValueKind != JsonValueKind.Array) return null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string? name = GetString(asset, "name");
            if (name is not null && name.Contains("setup", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }
        return null;
    }

    private static string GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetBool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.True;

    private static string FirstNonEmpty(string first, string fallback) =>
        string.IsNullOrWhiteSpace(first) ? fallback : first;
}
