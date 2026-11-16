namespace QuickTranslate.Services;

/// <summary>错误归类决定失败窗口要不要给「打开设置」或「重试」按钮，避免引导用户去做必然失败的切换。</summary>
public enum TranslationErrorCategory
{
    Configuration, // APPID / 密钥 / 目标语言等需要在设置页修改
    Network,       // 网络、超时、解析失败，重试通常有用
    Service,       // 账户、额度、服务端状态，重试和换链路都没用
}

public sealed class TranslationException : Exception
{
    public TranslationErrorCategory Category { get; }

    /// <summary>服务端错误码（如 54003）。只用来判断该不该重试，永远不展示给用户。</summary>
    public string? Code { get; }

    public TranslationException(string message, TranslationErrorCategory category, string? code = null)
        : base(message)
        => (Category, Code) = (category, code);
}
