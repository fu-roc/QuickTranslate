using System.Text.Json.Serialization;

namespace QuickTranslate.Models;

public sealed class AppConfig
{
    public string AppId { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public string TakeWordHotkey { get; set; } = "Alt+T";

    public string OcrHotkey { get; set; } = "Alt+S";

    public string TargetLanguage { get; set; } = "zh";

    /// <summary>主界面手动翻译的源语言（auto = 自动检测）。只影响主界面；划词 / OCR 恒用 auto。</summary>
    public string MainWindowSourceLanguage { get; set; } = "auto";

    /// <summary>
    /// 主界面手动翻译的目标语言。只影响主界面；留空表示跟随全局 TargetLanguage，
    /// 用户在主界面选过一次后写入具体值，此后与设置页互不干扰。
    /// </summary>
    public string MainWindowTargetLanguage { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(Secret);
}
