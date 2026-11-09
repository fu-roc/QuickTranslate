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
    private const int MaxQueryLength = 2000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly AppConfig _config;

    public BaiduTranslator(AppConfig config) => _config = config;

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!_config.IsConfigured)
        {
            throw new InvalidOperationException("尚未填写百度翻译 APPID 和密钥，请在托盘菜单打开「设置」。");
        }

        string query = text.Length > MaxQueryLength ? text[..MaxQueryLength] : text;
        string salt = Random.Shared.NextInt64(100000, 999999999).ToString(CultureInfo.InvariantCulture);
        string sign = ComputeSign(query, salt);

        var form = new Dictionary<string, string>
        {
            ["q"] = query,
            ["from"] = "auto",
            ["to"] = _config.TargetLanguage,
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
            throw new InvalidOperationException(MapError(code, message));
        }

        if (!root.TryGetProperty("trans_result", out var results))
        {
            throw new InvalidOperationException("翻译服务返回了无法识别的数据格式。");
        }

        var builder = new StringBuilder();
        foreach (var item in results.EnumerateArray())
        {
            if (item.TryGetProperty("dst", out var dst))
            {
                builder.AppendLine(dst.GetString());
            }
        }

        string translated = builder.ToString().TrimEnd();
        if (string.IsNullOrWhiteSpace(translated))
        {
            throw new InvalidOperationException("翻译服务没有返回译文。");
        }
        return translated;
    }

    // 官方约定：appid + q + salt + 密钥 的 MD5，q 在签名阶段不做 URL 编码
    private string ComputeSign(string query, string salt)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(_config.AppId + query + salt + _config.Secret));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string MapError(string code, string message) => code switch
    {
        "54000" => "必填参数为空：请检查配置是否完整。",
        "54001" => "签名错误：请检查 APPID 与密钥是否填写正确。",
        "54003" => "调用频率受限，请稍后再试。",
        "54004" => "账户余额不足，请前往百度翻译控制台查看。",
        "52001" => "请求超时，请稍后再试。",
        "52002" => "翻译服务系统错误，请稍后再试。",
        "52003" => "未授权用户：请确认已开通「通用文本翻译 API」服务。",
        "58001" => "译文语言方向不支持，请在设置里换一个目标语言。",
        "58002" => "服务当前已关闭，请前往控制台开启。",
        "59003" => "请求文本过长（超过 6000 字符）。",
        "90107" => "开发者认证未通过或未生效，请前往控制台查看认证进度。",
        _ => $"翻译服务返回错误 {code}：{message}",
    };
}
