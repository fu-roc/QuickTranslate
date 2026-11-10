using System.Runtime.InteropServices;
using System.Windows.Interop;
using QuickTranslate.Native;

namespace QuickTranslate.Services;

public sealed class HotkeyService : IDisposable
{
    private HwndSource? _source;
    private readonly List<int> _registeredIds = new();

    public event Action<int>? HotkeyPressed;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("QuickTranslate.HotkeySink")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0x00000080,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>注册成功返回空字符串，失败返回可直接展示给用户的中文原因。</summary>
    public string Register(int id, string hotkey)
    {
        if (_source is null) return "热键窗口未初始化";

        if (!HotkeyCodec.TryParse(hotkey, out uint modifiers, out uint virtualKey, out string error))
        {
            return error;
        }

        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers, virtualKey))
        {
            // 1409 = ERROR_HOTKEY_ALREADY_REGISTERED，其余情况多半是系统保留组合
            int code = Marshal.GetLastWin32Error();
            string reason = code == 1409 ? "已被其他软件或系统占用" : $"系统拒绝注册（错误码 {code}）";
            return $"「{hotkey}」{reason}，请换一组";
        }

        if (!_registeredIds.Contains(id)) _registeredIds.Add(id);
        return string.Empty;
    }

    /// <summary>
    /// 校验两个热键能否注册（占用检测必须真的调 RegisterHotKey），但不保留任何注册状态。
    /// 设置窗口还开着，最终注册统一由 App.RegisterHotkeys 完成，避免「新键先生效、后续步骤失败」。
    /// 返回空字符串表示成功，否则是可直接展示的中文原因。
    /// </summary>
    public string ValidatePair(int takeWordId, string takeWordHotkey, int ocrId, string ocrHotkey)
    {
        UnregisterAll();
        var errors = new List<string>();

        string first = Register(takeWordId, takeWordHotkey);
        if (!string.IsNullOrEmpty(first)) errors.Add($"取词翻译：{first}");
        string second = Register(ocrId, ocrHotkey);
        if (!string.IsNullOrEmpty(second)) errors.Add($"截图 OCR：{second}");

        // 无论成功失败都不留注册：失败时旧键也已在设置窗口打开时注销，
        // 关闭窗口后会由唯一的注册点按旧配置恢复，不会出现两个键全失灵的死状态。
        UnregisterAll();
        return errors.Count == 0 ? string.Empty : string.Join("；", errors);
    }

    public void UnregisterAll()
    {
        if (_source is null) return;
        foreach (int id in _registeredIds)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }
        _registeredIds.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey)
        {
            handled = true;
            HotkeyPressed?.Invoke(wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
