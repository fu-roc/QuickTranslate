using System.Text.Json.Serialization;

namespace QuickTranslate.Models;

public sealed record AppConfig
{
    public const string DefaultTakeWordHotkey = "Alt+T";
    public const string DefaultOcrHotkey = "Alt+S";

    public string AppId { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public string TakeWordHotkey { get; set; } = DefaultTakeWordHotkey;

    public string OcrHotkey { get; set; } = DefaultOcrHotkey;

    public string TargetLanguage { get; set; } = TranslationLanguage.SimplifiedChinese;

    public string MainWindowSourceLanguage { get; set; } = TranslationLanguage.Auto;

    /// <summary>留空时主界面跟随全局目标语言。</summary>
    public string MainWindowTargetLanguage { get; set; } = string.Empty;

    // 配置包含密钥，避免 record 的默认 ToString 生成带密钥的诊断文本。
    public override string ToString() => nameof(AppConfig);

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(Secret);
}
