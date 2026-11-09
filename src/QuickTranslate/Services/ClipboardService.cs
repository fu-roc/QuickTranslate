using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using QuickTranslate.Native;
using Clipboard = System.Windows.Clipboard;

namespace QuickTranslate.Services;

public static class ClipboardService
{
    /// <summary>
    /// 模拟 Ctrl+C 后读取剪贴板。按既定设计：不保存、不恢复剪贴板，
    /// 复制完成后剪贴板里留下的是选中的原文，用户原有剪贴板内容会被覆盖。
    /// </summary>
    public static async Task<string?> CaptureSelectedTextAsync()
    {
        // 全局热键触发的瞬间，Alt/Ctrl/Win 和主键通常还被物理按住。
        // 此时直接注入 Ctrl+C 会变成 Alt+Ctrl+C，目标应用不会执行复制；
        // 而且 Alt 会让部分应用进入菜单模式、丢掉文本选区。必须先等按键全部松开。
        await WaitForKeysReleasedAsync();

        uint beforeSequence = NativeMethods.ClipboardSequenceNumber;
        string beforeText = SafeGetText();

        if (!NativeMethods.SendCtrlC()) return null;

        // 等目标应用把选区写入剪贴板，最多约 600ms
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(40);
            if (NativeMethods.ClipboardSequenceNumber != beforeSequence) break;
        }

        string afterText = SafeGetText();
        bool sequenceChanged = NativeMethods.ClipboardSequenceNumber != beforeSequence;

        if (string.IsNullOrWhiteSpace(afterText)) return null;
        if (!sequenceChanged && afterText == beforeText) return null;
        return afterText;
    }

    private static async Task WaitForKeysReleasedAsync()
    {
        const int maxWaitMs = 300;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < maxWaitMs && NativeMethods.AreHotkeyKeysPressed())
        {
            await Task.Delay(15);
        }

        // 热键组合的按键消息会被系统拦截，Alt/Ctrl 的 keyup 未必同步到目标应用的键盘状态；
        // 无论是否超时都补发一次修饰键释放，确保注入的是纯 Ctrl+C 而不是 Alt+Ctrl+C。
        // 对已经松开的键补发 keyup 是安全的（系统状态已是 up，会被忽略）。
        NativeMethods.ReleaseModifierKeys();

        // 给前台应用一点时间处理 keyup
        await Task.Delay(45);
    }

    private static string SafeGetText()
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                return Clipboard.GetText();
            }
            catch (COMException)
            {
                Thread.Sleep(20);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
        return string.Empty;
    }

    public static void Copy(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(20);
            }
        }
    }
}
