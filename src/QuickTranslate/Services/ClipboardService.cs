using System.Runtime.InteropServices;
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
        // 绝不能等用户松开 Alt。浏览器在 Alt 的 keyup 后会把页面选区变成非活动状态
        // （蓝色高亮变灰）、焦点落到菜单栏/工具栏，那时 Ctrl+C 已经复制不到页面选区了。
        // 这就是「马上松 Alt 报未检测到选中文本，一直按住 Alt 反而正常」的原因：
        // 长按时旧逻辑靠 300ms 超时补发 keyup 并在 Alt keyup 之前完成了复制。
        // 现在把这个成功路径提前到热键到达的瞬间，手势不再影响结果。
        for (int attempt = 0; attempt < 2; attempt++)
        {
            // RegisterHotKey 截获了修饰键的 keydown，先补发 keyup 清掉系统里残留的
            // Alt 按下状态，否则注入的 Ctrl+C 会变成 Alt+Ctrl+C。
            NativeMethods.ReleaseModifierKeys();
            // 只给系统分发 keyup 的极短时间，不等待任何用户手势；
            // 首轮 keydown 若仍被前台窗口吞掉，第二轮补发一次即可恢复。
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
