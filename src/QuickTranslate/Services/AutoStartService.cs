using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace QuickTranslate.Services;

/// <summary>读取当前用户的 Run 项，并在保存设置时按需更新。</summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuickTranslate";
    private const string ExecutableName = "QuickTranslate.exe";
    private const string ProductName = "QuickTranslate";

    /// <summary>
    /// 当前可执行文件绝对路径。用 ProcessPath 而不是 Assembly.Location（单文件发布时后者可能拿到 dll）；
    /// 若进程宿主是 dotnet.exe（以 dll 方式启动），回退到输出目录里的 apphost。
    /// </summary>
    private static string ExecutablePath
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
    private static string Command => $"\"{ExecutablePath}\"";

    public static (bool Enabled, bool PathCurrent) GetStatus()
    {
        try
        {
            string? command = ReadCommand();
            return (IsOurCommand(command), string.Equals(command, Command, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            LogService.Warn("读取开机自启状态失败", ex);
            return default;
        }
    }

    private static string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) as string;
    }

    /// <summary>设置开机自启。返回 null 表示成功，否则是可直接展示的中文原因。幂等。</summary>
    public static string? Apply(bool enable)
    {
        try
        {
            if (enable && !File.Exists(ExecutablePath))
            {
                return $"找不到可执行文件「{ExecutablePath}」，无法设置开机自启";
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? (enable ? Registry.CurrentUser.CreateSubKey(RunKeyPath) : null);
            if (key is null) return enable ? "无法打开「启动」注册表项" : null;

            var existing = key.GetValue(ValueName) as string;
            if (enable)
            {
                if (existing is not null && !IsOurCommand(existing))
                {
                    return "同名启动项指向其他程序，已保留不动；请手动检查注册表后再试";
                }
                if (string.Equals(existing, Command, StringComparison.OrdinalIgnoreCase)) return null;
                key.SetValue(ValueName, Command, RegistryValueKind.String);
            }
            else
            {
                if (!IsOurCommand(existing)) return null;
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return null;
        }
        catch (Exception ex)
        {
            LogService.Error("写入开机自启项失败", ex);
            return "写入启动项失败：请检查注册表权限，详情见日志";
        }
    }

    /// <summary>启动时检查配置并提示路径问题。</summary>
    public static string? Check()
    {
        try
        {
            string? existing = ReadCommand();
            if (existing is null) return null;
            if (!IsOurCommand(existing)) return "同名启动项已被其他程序占用";
            if (!string.Equals(existing, Command, StringComparison.OrdinalIgnoreCase))
            {
                return "启动项仍指向旧路径；打开设置点一次「保存」即可更新到当前程序位置";
            }
            return null;
        }
        catch (Exception ex)
        {
            LogService.Warn("检查开机自启项失败", ex);
            return "读取启动项失败，详情见日志";
        }
    }

    /// <summary>按文件名和产品名识别归属，支持修复指向已移走程序的启动项。</summary>
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

        if (!string.Equals(Path.GetFileName(exePath), ExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.Equals(command, Command, StringComparison.OrdinalIgnoreCase)) return true;
        if (!File.Exists(exePath)) return true;

        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            return string.Equals(info.ProductName, ProductName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(info.FileDescription, ProductName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LogService.Warn("读取启动项指向的文件版本信息失败", ex);
            return false;
        }
    }
}
