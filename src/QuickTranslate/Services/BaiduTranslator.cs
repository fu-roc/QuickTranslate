using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuickTranslate.Models;

namespace QuickTranslate.Services;

public sealed class BaiduTranslator
{
    private const string Endpoint = "https://fanyi-api.baidu.com/api/trans/vip/translate";

    // 官方限制是单次 q ≤ 6000 字节（汉字约 2000 个），不是 6000 字符，也不是 2000 字符。
    private const int MaxQueryBytes = 6000;
    private const int MaxChunks = 8;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly AppConfig _config;

    public BaiduTranslator(AppConfig config) => _config = config;

    /// <summary>划词 / 截图 OCR 走这里：恒为「自动检测 → 设置页的目标语言」，不受主界面语种影响。</summary>
    public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default) =>
        TranslateAsync(text, "auto", _config.TargetLanguage, cancellationToken);

    /// <summary>主界面手动翻译走这里，源语言与目标语言由界面传入。</summary>
    public async Task<string> TranslateAsync(
        string text, string from, string to, CancellationToken cancellationToken = default)
    {
        if (!_config.IsConfigured)
        {
            throw new TranslationException(
                "尚未填写百度翻译 APPID 和密钥，请在托盘菜单打开「设置」。",
                TranslationErrorCategory.Configuration);
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new TranslationException("没有可翻译的文本。", TranslationErrorCategory.Service);
        }

        // 下划线拼接 / 驼峰 / 全大写缩写这类程序标识符，百度会当成生造词（原样返回或漏翻）。
        // 先还原成空格分隔的自然语言再发送；普通句子、中文、URL、代码行原样不动。
        // 只改变发给 API 的 q，界面展示的原文仍是用户选中的字符串。
        string queryText = IdentifierNormalizer.Normalize(text);
        List<string> chunks = SplitByUtf8Bytes(queryText);
        if (chunks.Count > MaxChunks)
        {
            throw new TranslationException(
                $"文本过长（{Encoding.UTF8.GetByteCount(text)} 字节）；单次最多处理 {MaxChunks} 段、每段 {MaxQueryBytes} 字节，请缩小选区或分段翻译。",
                TranslationErrorCategory.Service);
        }

        string source = string.IsNullOrWhiteSpace(from) ? "auto" : from;
        string target = string.IsNullOrWhiteSpace(to) ? _config.TargetLanguage : to;

        var builder = new StringBuilder();
        foreach (string chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (builder.Length > 0) builder.AppendLine();
            builder.Append(await TranslateChunkAsync(chunk, source, target, cancellationToken));
        }

        string translated = builder.ToString().TrimEnd();
        if (string.IsNullOrWhiteSpace(translated))
        {
            throw new TranslationException("翻译服务没有返回译文。", TranslationErrorCategory.Service);
        }
        return translated;
    }

    private async Task<string> TranslateChunkAsync(
        string query, string from, string to, CancellationToken cancellationToken)
    {
        string salt = Random.Shared.NextInt64(100000, 999999999).ToString(CultureInfo.InvariantCulture);
        string sign = ComputeSign(query, salt);

        var form = new Dictionary<string, string>
        {
            ["q"] = query,
            ["from"] = from,
            ["to"] = to,
            ["appid"] = _config.AppId,
            ["salt"] = salt,
            ["sign"] = sign,
        };

        using var content = new FormUrlEncodedContent(form);
        using var response = await Http.PostAsync(Endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("error_code", out var errorCode))
        {
            string code = errorCode.GetString() ?? "unknown";
            string message = root.TryGetProperty("error_msg", out var msg) ? msg.GetString() ?? string.Empty : string.Empty;
            var mapped = MapError(code, message);
            throw new TranslationException(mapped.Message, mapped.Category);
        }

        if (!root.TryGetProperty("trans_result", out var results))
        {
            throw new TranslationException("翻译服务返回了无法识别的数据格式。", TranslationErrorCategory.Service);
        }

        var builder = new StringBuilder();
        foreach (var item in results.EnumerateArray())
        {
            if (item.TryGetProperty("dst", out var dst)) builder.AppendLine(dst.GetString());
        }
        return builder.ToString().TrimEnd();
    }

    /// <summary>按 UTF-8 字节切分，且不切断代理对（emoji / 生僻字）。</summary>
    private static List<string> SplitByUtf8Bytes(string text)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        int bytes = 0;

        for (int i = 0; i < text.Length;)
        {
            int length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                ? 2 : 1;
            string unit = text.Substring(i, length);
            int unitBytes = Encoding.UTF8.GetByteCount(unit);

            if (bytes + unitBytes > MaxQueryBytes && current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
                bytes = 0;
            }

            current.Append(unit);
            bytes += unitBytes;
            i += length;
        }

        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks;
    }

    // 官方约定：appid + q + salt + 密钥 的 MD5，q 在签名阶段不做 URL 编码
    private string ComputeSign(string query, string salt)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(_config.AppId + query + salt + _config.Secret));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static (string Message, TranslationErrorCategory Category) MapError(string code, string message) => code switch
    {
        "54000" => ("必填参数为空：请检查配置是否完整。", TranslationErrorCategory.Configuration),
        "54001" => ("签名错误：请检查 APPID 与密钥是否填写正确。", TranslationErrorCategory.Configuration),
        "54003" => ("调用频率受限，请稍后再试。", TranslationErrorCategory.Network),
        "54004" => ("账户余额不足，请前往百度翻译控制台查看。", TranslationErrorCategory.Service),
        "52001" => ("请求超时，请稍后再试。", TranslationErrorCategory.Network),
        "52002" => ("翻译服务系统错误，请稍后再试。", TranslationErrorCategory.Service),
        "52003" => ("未授权用户：请确认已开通「通用文本翻译 API」服务。", TranslationErrorCategory.Configuration),
        "58001" => ("译文语言方向不支持，请在设置里换一个目标语言。", TranslationErrorCategory.Configuration),
        "58002" => ("服务当前已关闭，请前往控制台开启。", TranslationErrorCategory.Service),
        "59003" => ($"请求文本过长（单次最多 {MaxQueryBytes} 字节）。", TranslationErrorCategory.Service),
        "90107" => ("开发者认证未通过或未生效，请前往控制台查看认证进度。", TranslationErrorCategory.Configuration),
        _ => ($"翻译服务返回错误 {code}：{message}", TranslationErrorCategory.Service),
    };
}
