using System.Runtime.InteropServices;
using QuickTranslate.Native;
using Clipboard = System.Windows.Clipboard;

namespace QuickTranslate.Services;

public static class ClipboardService
{
    /// <summary>模拟 Ctrl+C，读取并在剪贴板中保留选中的原文。</summary>
    public static async Task<string?> CaptureSelectedTextAsync(CancellationToken cancellationToken = default)
    {
        // 及时复制选区，避免焦点随用户松开 Alt 转到浏览器菜单。
        for (int attempt = 0; attempt < 2; attempt++)
        {
            // RegisterHotKey 截获了修饰键的 keydown，先补发 keyup 清掉系统里残留的
            // Alt 按下状态，否则注入的 Ctrl+C 会变成 Alt+Ctrl+C。
            NativeMethods.ReleaseModifierKeys();
            // 等待系统分发修饰键释放事件。
            await Task.Delay(attempt == 0 ? 20 : 60, cancellationToken);

            uint beforeSequence = NativeMethods.ClipboardSequenceNumber;
            string beforeText = SafeGetText();
            bool sent = NativeMethods.SendCtrlC();

            if (sent)
            {
                // 等目标应用把选区写入剪贴板，单轮最多约 480ms
                for (int i = 0; i < 12; i++)
                {
                    await Task.Delay(40, cancellationToken);
                    if (NativeMethods.ClipboardSequenceNumber != beforeSequence) break;
                }
            }

            string afterText = SafeGetText();
            bool sequenceChanged = NativeMethods.ClipboardSequenceNumber != beforeSequence;

            if (sent && !string.IsNullOrWhiteSpace(afterText) && (sequenceChanged || afterText != beforeText))
            {
                return afterText;
            }

            LogService.Info(
                $"取词 #{attempt + 1} 未取到：sent={sent} seqChanged={sequenceChanged} " +
                $"beforeLen={beforeText.Length} afterLen={afterText.Length}");
        }

        LogService.Warn("取词失败：两轮 Ctrl+C 均未取到选中文本");
        return null;
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
