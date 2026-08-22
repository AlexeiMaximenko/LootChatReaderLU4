using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using WgcSharp;

namespace LootChatReader;

internal static class ScreenCaptureService
{
    private const int DwmwaExtendedFrameBounds = 9;

    public static IReadOnlyList<WindowDescriptor> EnumerateWindows()
    {
        var result = new List<WindowDescriptor>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || handle == nint.Zero)
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == Environment.ProcessId || !TryGetWindowBounds(handle, out var bounds))
            {
                return true;
            }

            var processName = string.Empty;
            try
            {
                processName = System.Diagnostics.Process.GetProcessById((int)processId).ProcessName;
            }
            catch
            {
                // Some elevated system windows do not expose process metadata.
            }

            var title = ReadWindowText(handle);
            var className = ReadClassName(handle);
            if (processName.Length == 0 || (title.Length == 0 && !LooksLikeLu4(processName)))
            {
                return true;
            }

            result.Add(new WindowDescriptor(
                handle,
                processId,
                processName,
                title,
                className,
                bounds,
                IsIconic(handle)));
            return true;
        }, nint.Zero);

        return result
            .OrderByDescending(window => LooksLikeLu4(window.ProcessName))
            .ThenBy(window => window.ProcessName)
            .ThenBy(window => window.Title)
            .ToArray();
    }

    public static WindowDescriptor? ResolveWindow(AppSettings settings, nint preferredHandle = default)
    {
        var windows = EnumerateWindows();
        if (preferredHandle != nint.Zero)
        {
            var preferred = windows.FirstOrDefault(window => window.Handle == preferredHandle);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return windows
            .Where(window => window.ProcessName.Equals(settings.TargetProcessName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(window => settings.TargetWindowClass.Length > 0
                && window.ClassName.Equals(settings.TargetWindowClass, StringComparison.Ordinal))
            .ThenByDescending(window => settings.TargetWindowTitle.Length > 0
                && window.Title.Equals(settings.TargetWindowTitle, StringComparison.Ordinal))
            .FirstOrDefault();
    }

    public static Bitmap CaptureWindowRegion(
        nint windowHandle,
        Rectangle relativeRegion,
        Size referenceWindowSize)
    {
        if (!IsWindow(windowHandle))
        {
            throw new InvalidOperationException("The selected game window is no longer available.");
        }

        using var windowBitmap = WindowCapture.CaptureWindow(
            windowHandle,
            CaptureStrategy.WgcOnly,
            timeoutMs: 900)
            ?? throw new WindowCaptureUnavailableException(
                IsIconic(windowHandle)
                    ? "The minimized game window is not producing capture frames. Restore it and leave it behind other windows."
                    : "Windows Graphics Capture did not return a frame for the selected game window.");

        var crop = ScaleAndClampRegion(relativeRegion, referenceWindowSize, windowBitmap.Size);
        if (crop.Width < 80 || crop.Height < 30)
        {
            throw new InvalidOperationException("The selected chat area is outside the current game-window frame.");
        }

        return windowBitmap.Clone(crop, PixelFormat.Format24bppRgb);
    }

    public static Rectangle GetScreenRegion(
        nint windowHandle,
        Rectangle relativeRegion,
        Size referenceWindowSize)
    {
        if (!TryGetWindowBounds(windowHandle, out var bounds))
        {
            return Rectangle.Empty;
        }

        var scaled = ScaleAndClampRegion(relativeRegion, referenceWindowSize, bounds.Size);
        return new Rectangle(bounds.X + scaled.X, bounds.Y + scaled.Y, scaled.Width, scaled.Height);
    }

    public static bool TryGetWindowBounds(nint handle, out Rectangle bounds)
    {
        if (DwmGetWindowAttribute(
                handle,
                DwmwaExtendedFrameBounds,
                out var nativeBounds,
                Marshal.SizeOf<NativeRectangle>()) != 0
            && !GetWindowRect(handle, out nativeBounds))
        {
            bounds = Rectangle.Empty;
            return false;
        }

        bounds = Rectangle.FromLTRB(
            nativeBounds.Left,
            nativeBounds.Top,
            nativeBounds.Right,
            nativeBounds.Bottom);
        return bounds.Width >= 80 && bounds.Height >= 30;
    }

    public static void RestoreAndActivate(nint handle)
    {
        if (IsIconic(handle))
        {
            ShowWindowAsync(handle, 9); // SW_RESTORE
        }

        SetForegroundWindow(handle);
    }

    private static Rectangle ScaleAndClampRegion(Rectangle region, Size reference, Size current)
    {
        if (reference.Width <= 0 || reference.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reference));
        }

        var left = (int)Math.Floor(region.Left * current.Width / (double)reference.Width);
        var top = (int)Math.Floor(region.Top * current.Height / (double)reference.Height);
        var right = (int)Math.Ceiling(region.Right * current.Width / (double)reference.Width);
        var bottom = (int)Math.Ceiling(region.Bottom * current.Height / (double)reference.Height);
        left = Math.Clamp(left, 0, current.Width);
        top = Math.Clamp(top, 0, current.Height);
        right = Math.Clamp(right, left, current.Width);
        bottom = Math.Clamp(bottom, top, current.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static bool LooksLikeLu4(string processName)
    {
        return processName.Contains("lu4", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadWindowText(nint handle)
    {
        var text = new StringBuilder(512);
        GetWindowText(handle, text, text.Capacity);
        return text.ToString().Trim();
    }

    private static string ReadClassName(nint handle)
    {
        var text = new StringBuilder(256);
        GetClassName(handle, text, text.Capacity);
        return text.ToString().Trim();
    }

    private delegate bool EnumWindowsProcedure(nint handle, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProcedure procedure, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint handle, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out NativeRectangle rectangle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint handle,
        int attribute,
        out NativeRectangle value,
        int valueSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint handle);
}

internal sealed class WindowCaptureUnavailableException(string message) : Exception(message);

internal sealed record WindowDescriptor(
    nint Handle,
    uint ProcessId,
    string ProcessName,
    string Title,
    string ClassName,
    Rectangle Bounds,
    bool IsMinimized)
{
    public string DisplayName => $"{(Title.Length > 0 ? Title : "Untitled window")} — {ProcessName} ({ProcessId})";
}
