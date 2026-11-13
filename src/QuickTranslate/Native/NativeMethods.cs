using System.Drawing;
using System.Runtime.InteropServices;

namespace QuickTranslate.Native;

internal static class NativeMethods
{
    public const int WmHotkey = 0x0312;

    private const ushort VkControl = 0x11;
    private const ushort VkC = 0x43;
    private const int VkLButton = 0x01;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint InputKeyboard = 1;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mi;
        [FieldOffset(0)] public KEYBDINPUT Ki;
        [FieldOffset(0)] public HARDWAREINPUT Hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint UMsg;
        public ushort WParamL;
        public ushort WParamH;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    private static extern bool RegisterHotKeyNative(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    private static extern bool UnregisterHotKeyNative(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "GetClipboardSequenceNumber")]
    private static extern uint GetClipboardSequenceNumberNative();

    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    private static extern bool GetCursorPosNative(out POINT lpPoint);

    [DllImport("user32.dll", EntryPoint = "MonitorFromPoint")]
    private static extern IntPtr MonitorFromPointNative(POINT pt, uint dwFlags);

    [DllImport("shcore.dll", EntryPoint = "GetDpiForMonitor")]
    private static extern int GetDpiForMonitorNative(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
    private static extern uint SendInputNative(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    private static extern short GetAsyncKeyStateNative(int virtualKey);

    public static uint ClipboardSequenceNumber => GetClipboardSequenceNumberNative();

    public static bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk) =>
        RegisterHotKeyNative(hWnd, id, modifiers, vk);

    public static void UnregisterHotKey(IntPtr hWnd, int id) =>
        UnregisterHotKeyNative(hWnd, id);

    public static Point GetCursorPosition()
    {
        GetCursorPosNative(out var point);
        return new Point(point.X, point.Y);
    }

    /// <summary>全局物理左键状态。结果窗不激活，收不到窗口外点击的 WPF 事件，只能靠它轮询。</summary>
    public static bool IsLeftButtonDown() => (GetAsyncKeyStateNative(VkLButton) & 0x8000) != 0;

    /// <summary>返回光标所在显示器的 DPI 缩放比，用于把物理像素换算成 WPF 的 DIP。</summary>
    public static double GetMonitorScale(Point physicalPoint)
    {
        var point = new POINT { X = physicalPoint.X, Y = physicalPoint.Y };
        IntPtr monitor = MonitorFromPointNative(point, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return 1.0;
        if (GetDpiForMonitorNative(monitor, 0, out uint dpiX, out _) != 0) return 1.0;
        return dpiX == 0 ? 1.0 : dpiX / 96.0;
    }

    /// <summary>
    /// 检测热键的修饰键 / 主键是否仍被物理按住。
    /// 覆盖 0x08-0x87（修饰键、字母数字、导航键、F1-F24）与左右专用修饰键 0xA0-0xA5；
    /// 不含鼠标键（0x01-0x07），用户刚拖完选区按着鼠标不会导致一直等待。
    /// </summary>
    public static bool AreHotkeyKeysPressed()
    {
        for (int vk = 0x08; vk <= 0x87; vk++)
        {
            if ((GetAsyncKeyStateNative(vk) & 0x8000) != 0) return true;
        }
        for (int vk = 0xA0; vk <= 0xA5; vk++)
        {
            if ((GetAsyncKeyStateNative(vk) & 0x8000) != 0) return true;
        }
        return false;
    }

    /// <summary>长按热键不放时的兜底：主动清除系统里残留的修饰键按下状态。</summary>
    public static void ReleaseModifierKeys()
    {
        ushort[] modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C]; // Shift / Ctrl / Alt / LWin / RWin
        var inputs = new INPUT[modifiers.Length];
        for (int i = 0; i < modifiers.Length; i++)
        {
            inputs[i] = MakeKeyboardInput(modifiers[i], keyUp: true);
        }
        SendInputNative((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>向当前前台窗口注入 Ctrl+C。程序自身必须保持不抢焦点。</summary>
    public static bool SendCtrlC()
    {
        var inputs = new INPUT[4];
        inputs[0] = MakeKeyboardInput(VkControl, keyUp: false);
        inputs[1] = MakeKeyboardInput(VkC, keyUp: false);
        inputs[2] = MakeKeyboardInput(VkC, keyUp: true);
        inputs[3] = MakeKeyboardInput(VkControl, keyUp: true);
        return SendInputNative((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    private static INPUT MakeKeyboardInput(ushort virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Ki = new KEYBDINPUT
            {
                WVk = virtualKey,
                DwFlags = keyUp ? KeyeventfKeyup : 0,
            },
        },
    };
}
