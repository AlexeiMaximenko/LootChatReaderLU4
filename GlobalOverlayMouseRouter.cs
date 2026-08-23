using System.Runtime.InteropServices;

namespace LootChatReader;

internal enum GlobalMouseAction
{
    Move,
    LeftDown,
    LeftUp,
    Wheel
}

internal sealed record GlobalMouseActivity(
    GlobalMouseAction Action,
    Point ScreenLocation,
    int WheelDelta = 0);

/// <summary>
/// Routes physical mouse input to transparent overlays before a foreground
/// DirectInput game can consume the corresponding legacy window message.
/// </summary>
internal static class GlobalOverlayMouseRouter
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmMouseWheel = 0x020A;

    private static readonly object Sync = new();
    private static readonly HookProcedure Hook = HookCallback;
    private static readonly Dictionary<long, Func<GlobalMouseActivity, bool>> Handlers = [];
    private static nint _hookHandle;
    private static long _nextRegistrationId;

    internal static bool HookAvailable => _hookHandle != nint.Zero;

    public static IDisposable Register(Func<GlobalMouseActivity, bool> handler)
    {
        lock (Sync)
        {
            var id = ++_nextRegistrationId;
            Handlers[id] = handler;
            if (_hookHandle == nint.Zero)
            {
                _hookHandle = SetWindowsHookEx(WhMouseLl, Hook, GetModuleHandle(null), 0);
            }
            return new Registration(id);
        }
    }

    private static nint HookCallback(int code, nint message, nint data)
    {
        if (code < 0 || !TryGetAction(message, out var action))
        {
            return CallNextHookEx(_hookHandle, code, message, data);
        }

        var hookData = Marshal.PtrToStructure<LowLevelMouseHookData>(data);
        var activity = new GlobalMouseActivity(
            action,
            new Point(hookData.Point.X, hookData.Point.Y),
            action == GlobalMouseAction.Wheel
                ? unchecked((short)(hookData.MouseData >> 16))
                : 0);
        Func<GlobalMouseActivity, bool>[] handlers;
        lock (Sync)
        {
            handlers = Handlers
                .OrderByDescending(pair => pair.Key)
                .Select(pair => pair.Value)
                .ToArray();
        }

        foreach (var handler in handlers)
        {
            try
            {
                if (handler(activity))
                {
                    return new nint(1);
                }
            }
            catch
            {
                // Input must continue to the next application if an overlay is
                // being destroyed concurrently with the hook callback.
            }
        }

        return CallNextHookEx(_hookHandle, code, message, data);
    }

    private static bool TryGetAction(nint message, out GlobalMouseAction action)
    {
        action = (int)message switch
        {
            WmMouseMove => GlobalMouseAction.Move,
            WmLButtonDown => GlobalMouseAction.LeftDown,
            WmLButtonUp => GlobalMouseAction.LeftUp,
            WmMouseWheel => GlobalMouseAction.Wheel,
            _ => (GlobalMouseAction)(-1)
        };
        return (int)action >= 0;
    }

    private static void Unregister(long id)
    {
        lock (Sync)
        {
            Handlers.Remove(id);
            if (Handlers.Count != 0 || _hookHandle == nint.Zero)
            {
                return;
            }
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
        }
    }

    private sealed class Registration(long id) : IDisposable
    {
        private long _id = id;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _id, 0);
            if (current != 0)
            {
                Unregister(current);
            }
        }
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
