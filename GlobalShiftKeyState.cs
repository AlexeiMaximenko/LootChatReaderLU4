using System.Runtime.InteropServices;

namespace LootChatReader;

/// <summary>
/// Shares one low-level Shift observer between every tracker tab. GetAsyncKeyState
/// remains as a fallback if Windows declines the hook.
/// </summary>
internal static class GlobalShiftKeyState
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkShift = 0x10;
    private const int VkLeftShift = 0xA0;
    private const int VkRightShift = 0xA1;

    private static readonly object Sync = new();
    private static readonly HookProcedure Hook = HookCallback;
    private static nint _hookHandle;
    private static int _references;
    private static volatile bool _hookShiftPressed;

    public static bool IsPressed => _hookShiftPressed
        || (GetAsyncKeyState(VkShift) & 0x8000) != 0
        || (GetAsyncKeyState(VkLeftShift) & 0x8000) != 0
        || (GetAsyncKeyState(VkRightShift) & 0x8000) != 0;

    public static void AddReference()
    {
        lock (Sync)
        {
            _references++;
            if (_hookHandle == nint.Zero)
            {
                _hookHandle = SetWindowsHookEx(WhKeyboardLl, Hook, GetModuleHandle(null), 0);
            }
        }
    }

    public static void Release()
    {
        lock (Sync)
        {
            _references = Math.Max(0, _references - 1);
            if (_references != 0 || _hookHandle == nint.Zero)
            {
                return;
            }

            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
            _hookShiftPressed = false;
        }
    }

    private static nint HookCallback(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var hookData = Marshal.PtrToStructure<LowLevelKeyboardHookData>(data);
            if (hookData.VirtualKeyCode is VkShift or VkLeftShift or VkRightShift)
            {
                if (message == WmKeyDown || message == WmSysKeyDown)
                {
                    _hookShiftPressed = true;
                }
                else if (message == WmKeyUp || message == WmSysKeyUp)
                {
                    _hookShiftPressed = false;
                }
            }
        }

        return CallNextHookEx(_hookHandle, code, message, data);
    }

    private delegate nint HookProcedure(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelKeyboardHookData
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookId,
        HookProcedure hookProcedure,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hookHandle, int code, nint message, nint data);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
