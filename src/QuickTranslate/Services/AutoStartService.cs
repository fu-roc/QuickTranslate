using System.IO;
using Microsoft.Win32;

namespace QuickTranslate.Services;

/// <summary>
/// 当前用户的开机自启：HKCU\Software\Microsoft\Windows\CurrentVersion\Run。
/// 不需要管理员权限，也不需要写启动文件夹快捷方式。
///
/// 注册表项本身就是唯一的持久化状态，不额外写进 config.json —— 否则用户用任务管理器
/// 禁用、或清理软件删掉启动项之后，配置和系统真实状态会各说各话。
///
/// 安全软件注意：未签名的 exe 在启动过程中写 Run 项，会被 Microsoft Defender 的
/// Behavior:Win32/Persistence.A!ml（行为型「持久化」检测，机器学习启发式）判定为可疑，
/// 轻则报警、重则直接隔离删除整个 exe。因此本类只在用户在设置页主动勾选/取消时写入；
/// 程序启动阶段绝不写注册表，Check() 只读取并返回提示。
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuickTranslate";
    private const string ExecutableName = "QuickTranslate.exe";

    /// <summary>
    /// 当前可执行文件绝对路径。用 ProcessPath 而不是 Assembly.Location（单文件发布时后者可能拿到 dll）；
    /// 若进程宿主是 dotnet.exe（以 dll 方式启动），回退到输出目录里的 apphost。
    /// </summary>
    public static string ExecutablePath
    {
        get
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) &&
                string.Equals(Path.GetFileName(processPath), ExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                return processPath;
            }

            return Path.Combine(AppContext.BaseDirectory, ExecutableName);
        }
    }

    /// <summary>Run 项的值：路径加引号，避免含空格的目录被系统截断。</summary>
    public static string Command => $"\"{ExecutablePath}\"";

    /// <summary>启动项是否存在且指向本程序（允许是旧路径，Check 会提示用户手动更新）。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return IsOurCommand(key?.GetValue(ValueName) as string);
        }
        catch (Exception ex)
        {
            // 注册表读取失败不应阻止程序启动或设置页打开；按「未启用」呈现
            System.Diagnostics.Debug.WriteLine($"AutoStartService.IsEnabled: {ex}");
            return false;
        }
    }

    /// <summary>设置开机自启。返回 null 表示成功，否则是可直接展示的中文原因。</summary>
    public static string? Apply(bool enable)
    {
        try
        {
            if (enable && !File.Exists(ExecutablePath))
            {
                return $"找不到可执行文件「{ExecutablePath}」，无法设置开机自启";
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return "无法打开「启动」注册表项";

            var existing = key.GetValue(ValueName) as string;
            if (existing is not null && !IsOurCommand(existing))
            {
                return enable
                    ? "同名启动项指向其他程序，已保留不动；请手动检查注册表后再试"
                    : "同名启动项不属于本程序，已保留不动";
            }

            if (enable)
            {
                // 幂等：已经是目标值就不写。避免每次保存设置都重复产生「写自启项」这一行为。
                if (string.Equals(existing, Command, StringComparison.OrdinalIgnoreCase)) return null;
                key.SetValue(ValueName, Command, RegistryValueKind.String);
            }
            else
            {
                // 本来就没启用时不产生删除动作
                if (existing is null) return null;
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"写入启动项失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 启动时调用：只读检查启动项状态，绝不写入注册表。
    /// 程序被移动后启动项指向旧路径时仅返回提示，由用户在设置页重新勾选后再写 ——
    /// 自动校正路径曾是 Defender Persistence.A!ml 检测的直接触发点。
    /// </summary>
    public static string? Check()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var existing = key?.GetValue(ValueName) as string;
            if (existing is null) return null;
            if (string.Equals(existing, Command, StringComparison.OrdinalIgnoreCase)) return null;
            if (!IsOurCommand(existing)) return "同名启动项已被其他程序占用";

            return "启动项仍指向旧路径；如需继续使用，请在设置里重新勾选开机自启";
        }
        catch (Exception ex)
        {
            return $"读取启动项失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 只按可执行文件名判断归属：既能识别程序搬家后留下的旧路径项，
    /// 也不会误删别的应用恰好同名的启动项。
    /// </summary>
    private static bool IsOurCommand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        string command = value.Trim();
        string exePath;
        if (command.StartsWith('"'))
        {
            int closing = command.IndexOf('"', 1);
            exePath = closing > 1 ? command[1..closing] : command.Trim('"');
        }
        else
        {
            int space = command.IndexOf(' ');
            exePath = space > 0 ? command[..space] : command;
        }

        return string.Equals(Path.GetFileName(exePath), ExecutableName, StringComparison.OrdinalIgnoreCase);
    }
}
