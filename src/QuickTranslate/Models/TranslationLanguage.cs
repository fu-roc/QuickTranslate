namespace QuickTranslate.Models;

internal sealed record TranslationLanguage(string Code, string Name)
{
    public const string Auto = "auto";
    public const string SimplifiedChinese = "zh";

    public static IReadOnlyList<TranslationLanguage> Targets { get; } =
    [
        new(SimplifiedChinese, "简体中文"),
        new("cht", "繁体中文"),
        new("en", "英语"),
        new("jp", "日语"),
        new("kor", "韩语"),
        new("fra", "法语"),
        new("de", "德语"),
        new("spa", "西班牙语"),
        new("ru", "俄语"),
    ];

    public static IReadOnlyList<TranslationLanguage> Sources { get; } = [new(Auto, "自动检测"), ..Targets];
}
