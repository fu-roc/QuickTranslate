using System.Text.Json.Serialization;

namespace QuickTranslate.Models;

public sealed class AppConfig
{
    public string AppId { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public string TakeWordHotkey { get; set; } = "Alt+T";

    public string OcrHotkey { get; set; } = "Alt+S";

    public string TargetLanguage { get; set; } = "zh";

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(Secret);
}
