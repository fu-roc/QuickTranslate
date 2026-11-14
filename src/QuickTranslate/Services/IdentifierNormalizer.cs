using System.Text;

namespace QuickTranslate.Services;

/// <summary>
/// 把「下划线拼接 / 连字符 / 驼峰 / 全大写缩写」这类程序标识符还原成空格分隔的自然语言，
/// 再送给翻译服务。百度翻译会把 hello_world_this_is_a_sentence 或 helloWorldThisIsASentence
/// 当成一个生造词，通常原样返回或音译；拆成空格后才能得到通顺译文。
///
/// 触发条件刻意收得很紧：整串不含空白、只由 ASCII 字母数字与 _ - 组成，且确实存在
/// 分隔符或驼峰边界。普通英文句子、中文、URL、文件路径、代码行一律原样返回，
/// 避免把本来能正常翻译的文本改坏。仅改变发给 API 的 q，不改变界面展示的原文。
/// </summary>
public static class IdentifierNormalizer
{
    private const int MaxLength = 4096;

    public static bool ShouldNormalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim();
        if (s.Length < 2 || s.Length > MaxLength) return false;

        bool hasLetter = false;
        bool hasSeparator = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '_' || c == '-') { hasSeparator = true; continue; }
            if (!IsAsciiAlphaNumeric(c)) return false;
            if (!IsDigit(c)) hasLetter = true;
        }
        if (!hasLetter) return false;
        if (hasSeparator) return true;

        for (int i = 1; i < s.Length; i++)
        {
            if (IsCamelBoundary(s, i)) return true;
        }
        return false;
    }

    /// <summary>不命中时返回原字符串（同一实例），调用方可直接用值比较判断是否被改写。</summary>
    public static string Normalize(string text)
    {
        if (!ShouldNormalize(text)) return text;

        string s = text.Trim();
        var builder = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '_' || c == '-')
            {
                AppendSpace(builder);
                continue;
            }
            if (i > 0 && IsCamelBoundary(s, i)) AppendSpace(builder);
            builder.Append(c);
        }
        return builder.ToString().Trim();
    }

    private static void AppendSpace(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');
    }

    /// <summary>
    /// s[i] 前是否该插入空格：
    /// 1) 小写 / 数字 → 大写：helloWorld、version2Update；
    /// 2) 大写缩写末尾 → 单词首字母：HTTPServer → HTTP|Server。
    /// </summary>
    private static bool IsCamelBoundary(string s, int i)
    {
        char prev = s[i - 1];
        char cur = s[i];
        if (!IsUpper(cur)) return false;
        if (IsLower(prev) || IsDigit(prev)) return true;
        return IsUpper(prev) && i + 1 < s.Length && IsLower(s[i + 1]);
    }

    private static bool IsAsciiAlphaNumeric(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

    private static bool IsUpper(char c) => c >= 'A' && c <= 'Z';
    private static bool IsLower(char c) => c >= 'a' && c <= 'z';
    private static bool IsDigit(char c) => c >= '0' && c <= '9';
}
