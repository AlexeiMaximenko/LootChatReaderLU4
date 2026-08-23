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
    private static volatile bool _rawShiftPressed;
    private static RawInputShiftWindow? _rawInputWindow;

    public static bool IsPressed => _rawShiftPressed
        || _hookShiftPressed
        || (GetAsyncKeyState(VkShift) & 0x8000) != 0
        || (GetAsyncKeyState(VkLeftShift) & 0x8000) != 0
        || (GetAsyncKeyState(VkRightShift) & 0x8000) != 0;

    internal static bool RawInputAvailable => _rawInputWindow is not null;

    public static void AddReference()
    {
        lock (Sync)
        {
            _references++;
            if (_hookHandle == nint.Zero)
            {
                _hookHandle = SetWindowsHookEx(WhKeyboardLl, Hook, GetModuleHandle(null), 0);
            }
            _rawInputWindow ??= RawInputShiftWindow.TryCreate();
        }
    }

    public static void Release()
    {
        lock (Sync)
        {
            _references = Math.Max(0, _references - 1);
            if (_references != 0)
            {
                return;
            }

            if (_hookHandle != nint.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
            }
            _hookHandle = nint.Zero;
            _hookShiftPressed = false;
            _rawShiftPressed = false;
            _rawInputWindow?.Dispose();
            _rawInputWindow = null;
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

    private sealed class RawInputShiftWindow : NativeWindow, IDisposable
    {
        private const int WmInput = 0x00FF;
        private const uint RidInput = 0x10000003;
        private const uint RimTypeKeyboard = 1;
        private const uint RidevInputSink = 0x00000100;
        private const ushort UsagePageGenericDesktop = 0x01;
        private const ushort UsageKeyboard = 0x06;
        private const ushort RiKeyBreak = 0x0001;
        private static readonly nint HwndMessage = new(-3);

        private RawInputShiftWindow()
        {
            CreateHandle(new CreateParams
            {
                Caption = "LU4 Loot Chat Reader Raw Input",
                Parent = HwndMessage
            });
            var device = new RawInputDevice
            {
                UsagePage = UsagePageGenericDesktop,
                Usage = UsageKeyboard,
                Flags = RidevInputSink,
                Target = Handle
            };
            if (!RegisterRawInputDevices(
                    [device],
                    1,
                    (uint)Marshal.SizeOf<RawInputDevice>()))
            {
                throw new InvalidOperationException("Raw keyboard input registration failed.");
            }
        }

        public static RawInputShiftWindow? TryCreate()
        {
            try
            {
                return new RawInputShiftWindow();
            }
            catch
            {
                return null;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmInput)
            {
                ReadKeyboardInput(message.LParam);
            }
            base.WndProc(ref message);
        }

        private static void ReadKeyboardInput(nint rawInputHandle)
        {
            var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
            uint dataSize = 0;
            if (GetRawInputData(rawInputHandle, RidInput, nint.Zero, ref dataSize, headerSize) != 0
                || dataSize < headerSize)
            {
                return;
            }

            var buffer = Marshal.AllocHGlobal((int)dataSize);
            try
            {
                if (GetRawInputData(rawInputHandle, RidInput, buffer, ref dataSize, headerSize) != dataSize)
                {
                    return;
                }

                var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
                if (header.Type != RimTypeKeyboard)
                {
                    return;
                }

                var keyboard = Marshal.PtrToStructure<RawKeyboard>(buffer + (int)headerSize);
                if (keyboard.VirtualKey is VkShift or VkLeftShift or VkRightShift)
                {
                    _rawShiftPressed = (keyboard.Flags & RiKeyBreak) == 0;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose()
        {
            DestroyHandle();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public nint Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputHeader
        {
            public uint Type;
            public uint Size;
            public nint Device;
            public nint WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawKeyboard
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VirtualKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterRawInputDevices(
            [In] RawInputDevice[] devices,
            uint deviceCount,
            uint deviceSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(
            nint rawInput,
            uint command,
            nint data,
            ref uint size,
            uint headerSize);
    }
}
