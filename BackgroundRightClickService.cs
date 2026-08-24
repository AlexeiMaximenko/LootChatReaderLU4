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
        if (!ScreenToClient(windowHandle, ref screenPoint))
        {
            return false;
        }

        var messagePosition = MakeLParam(screenPoint.X, screenPoint.Y);
        // Post directly to the selected LU4 HWND. The foreground window and any
        // browser covering the client are not involved in message routing.
        _ = PostMessage(windowHandle, WmMouseMove, 0, messagePosition);
        var pressed = PostMessage(windowHandle, WmRightButtonDown, MkRightButton, messagePosition);
        // Always enqueue button-up even if Windows reports a failed down message,
        // so a partially delivered click cannot leave the client in a held state.
        var released = PostMessage(windowHandle, WmRightButtonUp, 0, messagePosition);
        return pressed && released;
    }

    private static nint MakeLParam(int x, int y) =>
        (nint)((y & 0xFFFF) << 16 | (x & 0xFFFF));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
