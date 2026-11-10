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

    public TranslationException(string message, TranslationErrorCategory category) : base(message)
        => Category = category;
}
