using System.Runtime.InteropServices;
using System.Text;
using WpfKey = System.Windows.Input.Key;
using KeyInterop = System.Windows.Input.KeyInterop;

namespace QuickTranslate.Native;

/// <summary>
/// 热键字符串与 RegisterHotKey 的 (modifiers, vk) 之间的双向编解码。
/// 手动输入、按键录制、配置校验共用同一张键表，避免三处规则不一致。
/// </summary>
internal static class HotkeyCodec
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const uint VkDelete = 0x2E;
    private const uint VkTab = 0x09;
    private const uint VkEscape = 0x1B;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static readonly Dictionary<string, uint> ModifierAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alt"] = ModAlt,
        ["ctrl"] = ModControl,
        ["control"] = ModControl,
        ["shift"] = ModShift,
        ["win"] = ModWin,
        ["windows"] = ModWin,
        ["super"] = ModWin,
        ["meta"] = ModWin,
    };

    private static readonly (uint Vk, string Display, string[] Aliases)[] NamedKeys =
    [
        (0x08, "Backspace", ["backspace", "back"]),
        (0x09, "Tab", ["tab"]),
        (0x0D, "Enter", ["enter", "return"]),
        (0x1B, "Esc", ["esc", "escape"]),
        (0x20, "Space", ["space", "空格"]),
        (0x21, "PageUp", ["pageup", "pgup"]),
        (0x22, "PageDown", ["pagedown", "pgdn"]),
        (0x23, "End", ["end"]),
        (0x24, "Home", ["home"]),
        (0x25, "Left", ["left", "arrowleft"]),
        (0x26, "Up", ["up", "arrowup"]),
        (0x27, "Right", ["right", "arrowright"]),
        (0x28, "Down", ["down", "arrowdown"]),
        (0x2C, "PrintScreen", ["printscreen", "prtsc"]),
        (0x2D, "Insert", ["insert", "ins"]),
        (0x2E, "Delete", ["delete", "del"]),
        (0xBA, ";", [";", "oemsemicolon", "semicolon"]),
        (0xBB, "=", ["=", "oemplus", "plus"]),
        (0xBC, ",", [",", "oemcomma", "comma"]),
        (0xBD, "-", ["-", "oemminus", "minus"]),
        (0xBE, ".", [".", "oemperiod", "period"]),
        (0xBF, "/", ["/", "oemquestion", "question"]),
        (0xC0, "`", ["`", "oemtilde", "tilde"]),
        (0xDB, "[", ["[", "oemopenbrackets", "openbracket"]),
        (0xDC, "\\", ["\\", "oempipe", "pipe"]),
        (0xDD, "]", ["]", "oemclosebrackets", "closebracket"]),
        (0xDE, "'", ["'", "oemquotes", "quote"]),
    ];

    private static readonly Dictionary<uint, string> DisplayByVk = new();
    private static readonly Dictionary<string, uint> VkByAlias = new(StringComparer.OrdinalIgnoreCase);

    static HotkeyCodec()
    {
        foreach (var (vk, display, aliases) in NamedKeys)
        {
            DisplayByVk[vk] = display;
            foreach (string alias in aliases) VkByAlias[alias] = vk;
        }

        for (char c = 'A'; c <= 'Z'; c++) AddNamed((uint)c, c.ToString());
        for (char c = '0'; c <= '9'; c++) AddNamed((uint)c, c.ToString());
        for (int i = 1; i <= 24; i++) AddNamed((uint)(0x70 + i - 1), $"F{i}");
        for (int i = 0; i <= 9; i++) AddNamed((uint)(0x60 + i), $"NumPad{i}");
    }

    private static void AddNamed(uint vk, string display)
    {
        DisplayByVk[vk] = display;
        VkByAlias[display] = vk;
    }

    public static string SupportedKeysHint =>
        "A-Z / 0-9 / F1-F24 / 方向键 / Space / Tab / Enter / Home / End / PageUp / PageDown / Insert / Delete / NumPad0-9 / ; = , - . / ` [ \\ ] '";

    /// <summary>解析配置或手动输入的字符串。返回 false 时 error 是可直接展示的中文原因。</summary>
    public static bool TryParse(string text, out uint modifiers, out uint virtualKey, out string error)
    {
        modifiers = 0;
        virtualKey = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "快捷键不能为空";
            return false;
        }

        string[] parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "快捷键不能为空";
            return false;
        }

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!ModifierAliases.TryGetValue(parts[i], out uint modifier))
            {
                error = $"无法识别修饰键「{parts[i]}」，可用 Alt / Ctrl / Shift / Win";
                return false;
            }
            modifiers |= modifier;
        }

        if (text.TrimEnd().EndsWith('+'))
        {
            error = "快捷键末尾多了一个「+」，请补上按键，如 Ctrl+Alt+T";
            return false;
        }
        if (ModifierAliases.ContainsKey(parts[^1]))
        {
            error = $"「{parts[^1]}」是修饰键，请再跟一个按键，如 Ctrl+Alt+T";
            return false;
        }

        if (!TryResolveKey(parts[^1], out virtualKey))
        {
            error = $"无法识别按键「{parts[^1]}」，支持 {SupportedKeysHint}";
            return false;
        }

        if (modifiers == 0)
        {
            error = "请至少带一个修饰键（Ctrl / Alt / Shift / Win），否则会拦截普通输入";
            return false;
        }

        error = ReservedReason(modifiers, virtualKey);
        return error.Length == 0;
    }

    /// <summary>把录制到的 WPF 按键转成规范字符串。pureModifierOnly 表示只按下了修饰键，应继续等待。</summary>
    public static bool TryFromKey(WpfKey key, out string text, out string error, out bool pureModifierOnly)
    {
        text = string.Empty;
        error = string.Empty;
        pureModifierOnly = false;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0 || IsModifierVk(vk))
        {
            pureModifierOnly = true;
            return false;
        }

        uint modifiers = ReadPressedModifiers();
        if (modifiers == 0)
        {
            error = "请按住 Ctrl / Alt / Shift / Win 中至少一个，再按一个字母或功能键";
            return false;
        }

        if (!DisplayByVk.ContainsKey(vk))
        {
            error = $"按键「{key}」暂不支持，可用 {SupportedKeysHint}";
            return false;
        }

        error = ReservedReason(modifiers, vk);
        if (error.Length > 0) return false;

        return TryFormat(modifiers, vk, out text, out error);
    }

    public static bool TryFormat(uint modifiers, uint virtualKey, out string text, out string error)
    {
        text = string.Empty;
        error = string.Empty;
        if (!DisplayByVk.TryGetValue(virtualKey, out string? display))
        {
            error = "不支持的按键";
            return false;
        }

        var builder = new StringBuilder();
        if ((modifiers & ModControl) != 0) builder.Append("Ctrl+");
        if ((modifiers & ModAlt) != 0) builder.Append("Alt+");
        if ((modifiers & ModShift) != 0) builder.Append("Shift+");
        if ((modifiers & ModWin) != 0) builder.Append("Win+");
        builder.Append(display);
        text = builder.ToString();
        return true;
    }

    /// <summary>规范化后用于判断两个快捷键是否重复（别名、大小写、修饰键顺序均视为相同）。</summary>
    public static string Normalize(string text)
    {
        if (TryParseCore(text, out uint modifiers, out uint virtualKey))
        {
            return $"{modifiers}:{virtualKey}";
        }
        return text.Trim().ToUpperInvariant();
    }

    /// <summary>返回系统保留/内部冲突的中文原因，空字符串表示可用。</summary>
    public static string ReservedReason(uint modifiers, uint virtualKey)
    {
        bool ctrl = (modifiers & ModControl) != 0;
        bool alt = (modifiers & ModAlt) != 0;
        bool shift = (modifiers & ModShift) != 0;
        bool win = (modifiers & ModWin) != 0;

        if (ctrl && !alt && !shift && !win && virtualKey == 'C')
        {
            return "Ctrl+C 是程序内部取词要用的复制操作，不能设为热键（可改用 Ctrl+Shift+C）";
        }
        if (ctrl && alt && !shift && !win && virtualKey == VkDelete)
        {
            return "Ctrl+Alt+Delete 由 Windows 保留，请换一组";
        }
        if (ctrl && shift && !alt && !win && virtualKey == VkEscape)
        {
            return "Ctrl+Shift+Esc 是任务管理器，请换一组";
        }
        if (alt && !ctrl && !shift && !win && (virtualKey == VkTab || virtualKey == VkEscape || virtualKey == 0x73))
        {
            return "该组合被 Windows 用于切换窗口 / 关闭窗口，请换一组";
        }
        if (win && !ctrl && !alt && !shift && virtualKey is 'L' or 'D' or 'R' or 'E' or 'I' or 'U' or 'G')
        {
            return "该 Win 组合是 Windows 系统快捷键，请换一组";
        }
        if (win && virtualKey == VkTab)
        {
            return "Win+Tab 是任务视图，请换一组";
        }
        if (win && shift && !ctrl && !alt && virtualKey == 'S')
        {
            return "Win+Shift+S 是 Windows 自带截图工具，请换一组";
        }
        if (alt && !ctrl && !shift && !win && virtualKey == 0x2C)
        {
            return "Alt+PrintScreen 是 Windows 截图快捷键，请换一组";
        }
        return string.Empty;
    }

    private static bool TryParseCore(string text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string[] parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!ModifierAliases.TryGetValue(parts[i], out uint modifier)) return false;
            modifiers |= modifier;
        }
        return parts.Length > 0 && TryResolveKey(parts[^1], out virtualKey);
    }

    private static bool TryResolveKey(string token, out uint virtualKey)
    {
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                virtualKey = c;
                return true;
            }
        }
        return VkByAlias.TryGetValue(token, out virtualKey);
    }

    private static uint ReadPressedModifiers()
    {
        uint modifiers = 0;
        if (IsDown(VkControl) || IsDown(VkLControl) || IsDown(VkRControl)) modifiers |= ModControl;
        if (IsDown(VkMenu) || IsDown(VkLMenu) || IsDown(VkRMenu)) modifiers |= ModAlt;
        if (IsDown(VkShift) || IsDown(VkLShift) || IsDown(VkRShift)) modifiers |= ModShift;
        if (IsDown(VkLWin) || IsDown(VkRWin)) modifiers |= ModWin;
        return modifiers;
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static bool IsModifierVk(uint virtualKey) => virtualKey is
        0x10 or 0x11 or 0x12 or 0x5B or 0x5C or
        0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or
        0x14 or 0x90 or 0x91; // CapsLock / NumLock / ScrollLock 不作为热键主键
}
