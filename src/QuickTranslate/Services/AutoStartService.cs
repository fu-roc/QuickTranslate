using System.IO;
using Microsoft.Win32;

namespace QuickTranslate.Services;

/// <summary>
/// 当前用户的开机自启：HKCU\Software\Microsoft\Windows\CurrentVersion\Run。
/// 不需要管理员权限，也不需要写启动文件夹快捷方式。
///
/// 注册表项本身就是唯一的持久化状态，不额外写进 config.json —— 否则用户用任务管理器
/// 禁用、或清理软件删掉启动项之后，配置和系统真实状态会各说各话。
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

    /// <summary>启动项是否存在且指向本程序（允许是旧路径，Sync 会把它校正到当前 exe）。</summary>
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

            if (enable)
            {
                var existing = key.GetValue(ValueName) as string;
                if (existing is not null && !IsOurCommand(existing))
                {
                    return "同名启动项指向其他程序，已保留不动；请手动检查注册表后再试";
                }

                key.SetValue(ValueName, Command, RegistryValueKind.String);
            }
            else
            {
                var existing = key.GetValue(ValueName) as string;
                if (existing is not null && !IsOurCommand(existing))
                {
                    return "同名启动项不属于本程序，已保留不动";
                }

                // throwOnMissingValue: false —— 本来就没启用时关闭不应报错
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
    /// 启动时调用：程序被移动或重新发布后，把旧的 Run 项路径校正到当前 exe。
    /// 已经正确时不做任何写入，避免每次启动都动注册表。
    /// </summary>
    public static string? Sync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var existing = key?.GetValue(ValueName) as string;
            if (string.Equals(existing, Command, StringComparison.OrdinalIgnoreCase)) return null;
            if (existing is not null && !IsOurCommand(existing)) return "同名启动项已被其他程序占用";

            return Apply(enable: true);
        }
        catch (Exception ex)
        {
            return $"校正启动项路径失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 只按可执行文件名判断归属：既能删掉程序搬家后留下的旧路径项，
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
