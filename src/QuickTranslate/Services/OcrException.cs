namespace QuickTranslate.Services;

/// <summary>
/// OCR 失败。与 <see cref="TranslationException"/> 分开，是因为两条链路的失败原因完全不同：
/// 缺语言包要让用户去系统设置，翻译失败要让用户查网络或密钥。
/// 不走 InvalidOperationException，否则会被 ErrorMapper 的兜底分支改写成「翻译失败」。
/// </summary>
public sealed class OcrException : Exception
{
    public OcrException(string message, Exception? inner = null) : base(message, inner) { }
}
