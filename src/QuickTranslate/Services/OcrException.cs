namespace QuickTranslate.Services;

public sealed class OcrException : Exception
{
    public OcrException(string message, Exception? inner = null) : base(message, inner) { }
}
