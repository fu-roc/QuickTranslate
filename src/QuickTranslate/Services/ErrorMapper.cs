using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QuickTranslate.Services;

public enum ErrorAction
{
    None,      // 只展示中文原因
    Settings,  // 「打开设置」——配置/账户类错误
    Retry,     // 「重试」——网络/超时类错误
}

public sealed record FailureInfo(string Message, ErrorAction Action);

/// <summary>
/// 把底层异常映射成用户能看懂的中文原因。绝不把 ex.Message 直接塞进窗口：
/// HttpRequestException / JsonException / WinRT 异常都是英文技术串。
/// </summary>
public static class ErrorMapper
{
    public static FailureInfo Map(Exception ex) => ex switch
    {
        TranslationException translation => translation.Category switch
        {
            TranslationErrorCategory.Configuration => new(translation.Message, ErrorAction.Settings),
            TranslationErrorCategory.Network => new(translation.Message, ErrorAction.Retry),
            _ => new(translation.Message, ErrorAction.None),
        },
        HttpRequestException =>
            new("无法连接百度翻译服务：请检查网络、代理或防火墙后重试。", ErrorAction.Retry),
        OperationCanceledException or TimeoutException =>
            new("翻译请求超时或被取消，请稍后重试。", ErrorAction.Retry),
        JsonException =>
            new("翻译服务返回了无法解析的数据，请稍后重试。", ErrorAction.Retry),
        IOException or UnauthorizedAccessException =>
            new("本地读写出错，请把日志文件发给开发者。", ErrorAction.None),
        _ =>
            new("翻译失败：程序遇到意外错误，详情已写入日志。", ErrorAction.None),
    };
}
