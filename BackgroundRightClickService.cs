using System.Runtime.InteropServices;

namespace LootChatReader;

internal static class BackgroundRightClickService
{
    public const int MinimumIntervalMilliseconds = 50;
    public const int MaximumIntervalMilliseconds = 500;

    private const uint WmMouseMove = 0x0200;
    private const uint WmRightButtonDown = 0x0204;
    private const uint WmRightButtonUp = 0x0205;
    private const nuint MkRightButton = 0x0002;
    private const uint CwpSkipInvisible = 0x0001;
    private const uint CwpSkipDisabled = 0x0002;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;

    public static int NextInterval() =>
        Random.Shared.Next(MinimumIntervalMilliseconds, MaximumIntervalMilliseconds + 1);

    public static bool TryPostRandomClick(
        nint windowHandle,
        Rectangle relativeRegion,
        Size referenceWindowSize)
    {
        if (windowHandle == nint.Zero
            || relativeRegion.Width <= 0
            || relativeRegion.Height <= 0)
        {
            return false;
        }

        var screenRegion = ScreenCaptureService.GetScreenRegion(
            windowHandle,
            relativeRegion,
            referenceWindowSize);
        if (screenRegion.Width <= 0 || screenRegion.Height <= 0)
        {
            return false;
        }

        var screenPoint = new NativePoint(
            Random.Shared.Next(screenRegion.Left, screenRegion.Right),
            Random.Shared.Next(screenRegion.Top, screenRegion.Bottom));
        var foreground = GetForegroundWindow();
        if (foreground == windowHandle || (foreground != nint.Zero && IsChild(windowHandle, foreground)))
        {
            return TrySendForegroundClick(screenPoint);
        }

        var messageTarget = FindDeepestChildAtPoint(windowHandle, screenPoint);
        var clientPoint = screenPoint;
        if (!ScreenToClient(messageTarget, ref clientPoint))
        {
            return false;
        }

        var messagePosition = MakeLParam(clientPoint.X, clientPoint.Y);
        // Post directly to the selected LU4 HWND. The foreground window and any
        // browser covering the client are not involved in message routing.
        _ = PostMessage(messageTarget, WmMouseMove, 0, messagePosition);
        var pressed = PostMessage(messageTarget, WmRightButtonDown, MkRightButton, messagePosition);
        // Always enqueue button-up even if Windows reports a failed down message,
        // so a partially delivered click cannot leave the client in a held state.
        var released = PostMessage(messageTarget, WmRightButtonUp, 0, messagePosition);
        return pressed && released;
    }

    private static nint FindDeepestChildAtPoint(nint root, NativePoint screenPoint)
    {
        var current = root;
        for (var depth = 0; depth < 8; depth++)
        {
            var local = screenPoint;
            if (!ScreenToClient(current, ref local))
            {
                break;
            }
            var child = ChildWindowFromPointEx(
                current,
                local,
                CwpSkipInvisible | CwpSkipDisabled);
            if (child == nint.Zero || child == current)
            {
                break;
            }
            current = child;
        }
        return current;
    }

    private static bool TrySendForegroundClick(NativePoint screenPoint)
    {
        if (!GetCursorPos(out var originalPoint)
            || !SetCursorPos(screenPoint.X, screenPoint.Y))
        {
            return false;
        }

        try
        {
            var inputs = new[]
            {
                MouseInput.Create(MouseEventRightDown),
                MouseInput.Create(MouseEventRightUp)
            };
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<MouseInput>()) == inputs.Length;
        }
        finally
        {
            _ = SetCursorPos(originalPoint.X, originalPoint.Y);
        }
    }

    private static nint MakeLParam(int x, int y) =>
        (nint)((y & 0xFFFF) << 16 | (x & 0xFFFF));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public uint Type;
        public MouseInputData Data;

        public static MouseInput Create(uint flags) => new()
        {
            Type = 0,
            Data = new MouseInputData { Flags = flags }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint ChildWindowFromPointEx(nint parent, NativePoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(nint parent, nint child);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint inputCount, MouseInput[] inputs, int inputSize);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
