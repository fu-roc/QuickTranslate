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
    public static async Task<string?> CaptureSelectedTextAsync(CancellationToken cancellationToken = default)
    {
        // 全局热键触发的瞬间，Alt/Ctrl/Win 和主键通常还被物理按住。
        // 此时直接注入 Ctrl+C 会变成 Alt+Ctrl+C，目标应用不会执行复制；
        // 而且 Alt 会让部分应用进入菜单模式、丢掉文本选区。必须先等按键全部松开。
        await WaitForKeysReleasedAsync(cancellationToken);

        uint beforeSequence = NativeMethods.ClipboardSequenceNumber;
        string beforeText = SafeGetText();

        if (!NativeMethods.SendCtrlC()) return null;

        // 等目标应用把选区写入剪贴板，最多约 600ms
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(40, cancellationToken);
            if (NativeMethods.ClipboardSequenceNumber != beforeSequence) break;
        }

        string afterText = SafeGetText();
        bool sequenceChanged = NativeMethods.ClipboardSequenceNumber != beforeSequence;

        if (string.IsNullOrWhiteSpace(afterText)) return null;
        if (!sequenceChanged && afterText == beforeText) return null;
        return afterText;
    }

    private static async Task WaitForKeysReleasedAsync(CancellationToken cancellationToken)
    {
        const int maxWaitMs = 300;
        var stopwatch = Stopwatch.StartNew();
        bool stillPressed;
        do
        {
            stillPressed = NativeMethods.AreHotkeyKeysPressed();
            if (!stillPressed) break;
            await Task.Delay(15, cancellationToken);
        }
        while (stopwatch.ElapsedMilliseconds < maxWaitMs);

        // 只有超时（确实还有键被按住）才补发修饰键 keyup。
        // 正常情况下无条件补发 5 个 keyup 会清掉用户随后正按住的 Ctrl/Shift/Alt。
        if (stillPressed) NativeMethods.ReleaseModifierKeys();

        // 给前台应用一点时间处理 keyup
        await Task.Delay(45, cancellationToken);
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
            catch (Exception ex)
            {
                LogService.Warn("读取剪贴板失败", ex);
                return string.Empty;
            }
        }
        LogService.Warn("读取剪贴板失败：重试 5 次仍被占用");
        return string.Empty;
    }

    /// <summary>返回是否真的写进剪贴板，UI 不能无条件显示「已复制译文」。</summary>
    public static bool Copy(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(20);
            }
            catch (Exception ex)
            {
                LogService.Warn("复制译文到剪贴板失败", ex);
                return false;
            }
        }
        LogService.Warn("复制译文到剪贴板失败：重试 5 次仍被占用");
        return false;
    }
}
