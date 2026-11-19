using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace QuickTranslate.Services;

public enum UpdateStatus
{
    UpToDate,
    Available,
    Failed,
}

public sealed record UpdateCheckResult(
    UpdateStatus Status,
    string Message,
    Version CurrentVersion,
    Version? LatestVersion = null,
    string? ReleaseUrl = null,
    string? Notes = null);

public static class UpdateService
{
    private const string Repository = "fu-roc/QuickTranslate";
    private const string ApiUrl = $"https://api.github.com/repos/{Repository}/releases/latest";
    public const string ReleasesPageUrl = $"https://github.com/{Repository}/releases/latest";

    private const int NotesMaxLines = 14;
    private const int NotesMaxChars = 900;

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        // GitHub API 要求 User-Agent，否则直接 403
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"QuickTranslate/{AppVersion.Display}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return client;
    }

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken token)
    {
        Version current = AppVersion.Value;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var response = await Client
                .GetAsync(ApiUrl, HttpCompletionOption.ResponseHeadersRead, budget.Token)
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

            await using var stream = await response.Content.ReadAsStreamAsync(budget.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: budget.Token).ConfigureAwait(false);
            return ParseRelease(document.RootElement, current);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
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

        if (GetBool(root, "draft"))
        {
            return Failed(current, "检查更新失败：远端最新发布还是草稿，请直接打开发布页面查看。");
        }
        if (GetBool(root, "prerelease"))
        {
            return Failed(current, "检查更新失败：远端最新发布是预发布版本，请直接打开发布页面查看。");
        }

        string url = GetString(root, "html_url");
        string notes = FormatNotes(GetString(root, "body"));
        if (latest.CompareTo(current) <= 0)
        {
            return new UpdateCheckResult(
                UpdateStatus.UpToDate,
                $"已是最新版本（当前 {Format(current)}）。",
                current, latest, url, notes);
        }

        string message = $"发现新版本 {Format(latest)}，当前是 {Format(current)}。";
        string? installer = FindInstallerName(root);
        if (installer is not null) message += $" 发布页里有系统级安装包 {installer}。";

        return new UpdateCheckResult(
            UpdateStatus.Available,
            message,
            current, latest, url, notes);
    }

    /// <summary>将发布标签中的版本号转换为四段版本。</summary>
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
        new(UpdateStatus.Failed, message, current);

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    private static string? FindInstallerName(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out JsonElement assets) ||
            assets.ValueKind != JsonValueKind.Array) return null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = GetString(asset, "name");
            if (name.Contains("setup", StringComparison.OrdinalIgnoreCase) &&
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
}
