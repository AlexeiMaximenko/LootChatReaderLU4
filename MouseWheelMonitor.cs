using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LootChatReader;

internal sealed class MouseWheelMonitor : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;

    private readonly HookProcedure _hookProcedure;
    private nint _hookHandle;

    public MouseWheelMonitor()
    {
        _hookProcedure = HookCallback;
    }

    public event EventHandler<MouseWheelActivity>? WheelScrolled;

    public void Start()
    {
        if (_hookHandle != nint.Zero)
        {
            return;
        }

        _hookHandle = SetWindowsHookEx(WhMouseLl, _hookProcedure, GetModuleHandle(null), 0);
        if (_hookHandle == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Mouse-wheel monitoring could not be enabled.");
        }
    }

    public void Stop()
    {
        if (_hookHandle == nint.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = nint.Zero;
    }

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code >= 0 && (message == WmMouseWheel || message == WmMouseHWheel))
        {
            var hookData = Marshal.PtrToStructure<LowLevelMouseHookData>(data);
            var wheelDelta = unchecked((short)(hookData.MouseData >> 16));
            WheelScrolled?.Invoke(
                this,
                new MouseWheelActivity(new Point(hookData.Point.X, hookData.Point.Y), wheelDelta));
        }

        return CallNextHookEx(_hookHandle, code, message, data);
    }

    public void Dispose()
    {
        Stop();
    }

    private delegate nint HookProcedure(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseHookData
    {
        public NativePoint Point;
        public uint MouseData;
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}

internal sealed record MouseWheelActivity(Point ScreenLocation, int Delta);
