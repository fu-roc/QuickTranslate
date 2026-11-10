using System.Diagnostics;
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
/// Behavior:Win32/Persistence.A!ml（行为型「持久化」检测，机器学习启发式）判定为可疑。
/// 因此本类只在用户在设置页主动点「保存」时写入；程序启动阶段绝不写注册表。
/// Apply 内部幂等（值已经正确时不产生任何写动作），所以每次保存都调用它是安全的，
/// 顺带就能把程序搬家后残留的旧路径修正过来。
/// </summary>
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

    /// <summary>启动项是否存在且属于本程序（允许是旧路径，用 IsPathCurrent 再区分）。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return IsOurCommand(key?.GetValue(ValueName) as string);
        }
        catch (Exception ex)
        {
            LogService.Warn("读取开机自启状态失败", ex);
            return false;
        }
    }

    /// <summary>启动项是否正好指向当前 exe。搬家后旧路径存在但这里会返回 false。</summary>
    public static bool IsPathCurrent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var existing = key?.GetValue(ValueName) as string;
            return existing is not null &&
                   string.Equals(existing, Command, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LogService.Warn("检查开机自启路径失败", ex);
            return false;
        }
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
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return "无法打开「启动」注册表项";

            var existing = key.GetValue(ValueName) as string;
            if (enable)
            {
                if (existing is not null && !IsOurCommand(existing))
                {
                    return "同名启动项指向其他程序，已保留不动；请手动检查注册表后再试";
                }
                // 已经指向当前 exe 就不写，避免每次保存都产生一次「写自启项」行为。
                if (string.Equals(existing, Command, StringComparison.OrdinalIgnoreCase)) return null;
                key.SetValue(ValueName, Command, RegistryValueKind.String);
            }
            else
            {
                // 关闭时只删「确实属于本程序」的值。第三方占用同名值不该阻止用户保存其它设置。
                if (existing is null || !IsOurCommand(existing)) return null;
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

    /// <summary>
    /// 启动时调用：只读检查启动项状态，绝不写入注册表。
    /// 程序被移动后启动项指向旧路径时仅返回提示，由用户在设置页点一次「保存」修正 ——
    /// 自动校正路径曾是 Defender Persistence.A!ml 检测的直接触发点。
    /// </summary>
    public static string? Check()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var existing = key?.GetValue(ValueName) as string;
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

    /// <summary>
    /// 先按可执行文件名判断归属（这样才能识别程序搬家后留下的旧路径项），
    /// 再看文件版本信息里的产品名，避免误删「值名与 exe 都恰好同名」的第三方启动项。
    /// 旧路径若已不存在，说明那个启动项本来就是死的，允许清理。
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
