using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace LootChatReader;

internal sealed record OverlayItem(string Name, long Total);

internal sealed record OverlaySnapshot(
    long Adena,
    long Xp,
    long Sp,
    IReadOnlyList<OverlayItem> Items,
    IReadOnlyList<OverlayItem> QuestItems);

internal sealed class GameOverlayController : IDisposable
{
    private const int Gap = 6;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private static readonly nint HwndTopmost = new(-1);

    private readonly AppSettings _settings;
    private readonly Func<nint> _targetWindowProvider;
    private readonly Action _saveSettings;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly StatsOverlayForm _stats = new();
    private readonly OverlayMenuForm _menu = new();
    private readonly LootOverlayForm _details = new();
    private OverlaySnapshot _snapshot = new(0, 0, 0, [], []);
    private bool _menuVisible;
    private bool _detailsVisible;
    private bool _showQuestItems;
    private bool _disposed;

    public GameOverlayController(
        AppSettings settings,
        Func<nint> targetWindowProvider,
        Action saveSettings)
    {
        _settings = settings;
        _targetWindowProvider = targetWindowProvider;
        _saveSettings = saveSettings;
        GlobalShiftKeyState.AddReference();
        _settings.OverlayDetailsWidth = Math.Clamp(_settings.OverlayDetailsWidth, 220, 1200);
        _settings.OverlayDetailsHeight = Math.Clamp(_settings.OverlayDetailsHeight, 120, 1200);

        _stats.MoreClicked += ToggleMenu;
        _menu.CategorySelected += ShowCategory;
        _details.CloseClicked += CloseDetails;
        _details.BoundsChangeCompleted += bounds =>
        {
            _settings.OverlayDetailsX = bounds.X;
            _settings.OverlayDetailsY = bounds.Y;
            _settings.OverlayDetailsWidth = Math.Clamp(bounds.Width, 220, 1200);
            _settings.OverlayDetailsHeight = Math.Clamp(bounds.Height, 120, 1200);
            _settings.OverlayDetailsPositionSet = true;
            _saveSettings();
        };
        _details.Size = new Size(_settings.OverlayDetailsWidth, _settings.OverlayDetailsHeight);

        _timer = new System.Windows.Forms.Timer { Interval = 25 };
        _timer.Tick += (_, _) => UpdateOverlayWindows();
        _timer.Start();
    }

    public void SetPlacement(OverlayPlacement placement)
    {
        _settings.OverlayPlacement = placement;
        if (placement == OverlayPlacement.Off)
        {
            _menuVisible = false;
            _detailsVisible = false;
            HideAll();
        }
        else
        {
            UpdateOverlayWindows();
        }
    }

    public void UpdateSnapshot(OverlaySnapshot snapshot)
    {
        _snapshot = snapshot;
        _stats.SetStatistics(snapshot.Adena, snapshot.Xp, snapshot.Sp);
        UpdateDetailContent();
    }

    internal void ShowDetailsForDiagnostic(bool questItems)
    {
        ShowCategory(questItems);
    }

    internal string GetDiagnosticState() =>
        $"stats={_stats.Bounds} client={_stats.ClientSize} visible={_stats.Visible}; " +
        $"details={_details.Bounds} client={_details.ClientSize} visible={_details.Visible}";

    private void ToggleMenu()
    {
        _menuVisible = !_menuVisible;
        if (!_menuVisible)
        {
            _menu.Hide();
        }
        UpdateOverlayWindows();
    }

    private void ShowCategory(bool questItems)
    {
        _showQuestItems = questItems;
        _menuVisible = false;
        _detailsVisible = true;
        _menu.Hide();
        UpdateDetailContent();
        UpdateOverlayWindows();
    }

    private void CloseDetails()
    {
        _detailsVisible = false;
        _details.Hide();
    }

    private void UpdateDetailContent()
    {
        _details.SetItems(
            _showQuestItems ? "Quest Items" : "Items",
            _showQuestItems ? _snapshot.QuestItems : _snapshot.Items);
    }

    private void UpdateOverlayWindows()
    {
        var placement = _settings.OverlayPlacement;
        var targetWindow = _targetWindowProvider();
        if (placement == OverlayPlacement.Off
            || targetWindow == nint.Zero
            || !IsWindow(targetWindow)
            || IsIconic(targetWindow)
            || !_settings.HasCaptureRegion
            || !ScreenCaptureService.TryGetWindowBounds(targetWindow, out var windowBounds))
        {
            HideAll();
            return;
        }

        var captureRegion = ScreenCaptureService.GetScreenRegion(
            targetWindow,
            _settings.CaptureRegion,
            new Size(_settings.ReferenceWindowWidth, _settings.ReferenceWindowHeight));
        if (captureRegion.IsEmpty)
        {
            HideAll();
            return;
        }

        var shiftPressed = GlobalShiftKeyState.IsPressed;
        _stats.InteractionEnabled = shiftPressed;
        _menu.InteractionEnabled = shiftPressed;
        _details.InteractionEnabled = shiftPressed;

        var horizontalStats = placement is OverlayPlacement.Top or OverlayPlacement.Bottom;
        _stats.SetHorizontal(horizontalStats);
        var statsSize = StatsOverlayForm.GetOverlaySize(placement, captureRegion.Size);
        var statsBounds = ClampToBounds(
            PositionBeside(captureRegion, statsSize, placement),
            windowBounds);
        _stats.SetLayeredBounds(statsBounds);
        _stats.ShowInactive();

        Rectangle? menuBounds = null;
        if (_menuVisible)
        {
            menuBounds = PositionAuxiliary(
                statsBounds,
                OverlayMenuForm.OverlaySize,
                placement,
                windowBounds);
            _menu.SetLayeredBounds(menuBounds.Value);
            _menu.ShowInactive();
        }
        else
        {
            _menu.Hide();
        }

        if (_detailsVisible)
        {
            if (!_details.IsInSizeMove)
            {
                var detailSize = new Size(
                    Math.Clamp(_settings.OverlayDetailsWidth, 220, 1200),
                    Math.Clamp(_settings.OverlayDetailsHeight, 120, 1200));
                if (_settings.OverlayDetailsPositionSet)
                {
                    _details.SetLayeredBounds(new Rectangle(
                        _settings.OverlayDetailsX,
                        _settings.OverlayDetailsY,
                        detailSize.Width,
                        detailSize.Height));
                }
                else
                {
                    var anchor = menuBounds ?? statsBounds;
                    _details.SetLayeredBounds(PositionAuxiliary(
                        anchor,
                        detailSize,
                        placement,
                        windowBounds));
                }
            }
            _details.ShowInactive();
        }
        else
        {
            _details.Hide();
        }

        PlaceDirectlyAboveTarget(targetWindow);
    }

    private void PlaceDirectlyAboveTarget(nint _)
    {
        // Keep the independent overlay windows in one stable Z-order regardless
        // of whether the game or this application is currently active. They are
        // hidden separately whenever the selected game window is unavailable or
        // minimized.
        foreach (var window in new LayeredOverlayForm[] { _stats, _menu, _details })
        {
            if (window.Visible && window.IsHandleCreated)
            {
                SetWindowPos(
                    window.Handle,
                    HwndTopmost,
                    0,
                    0,
                    0,
                    0,
                    SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder);
            }
        }
    }

    private static Rectangle PositionBeside(
        Rectangle captureRegion,
        Size overlaySize,
        OverlayPlacement placement)
    {
        return placement switch
        {
            OverlayPlacement.Left => new Rectangle(
                captureRegion.Left - overlaySize.Width - Gap,
                captureRegion.Top,
                overlaySize.Width,
                overlaySize.Height),
            OverlayPlacement.Top => new Rectangle(
                captureRegion.Left,
                captureRegion.Top - overlaySize.Height - Gap,
                overlaySize.Width,
                overlaySize.Height),
            OverlayPlacement.Right => new Rectangle(
                captureRegion.Right + Gap,
                captureRegion.Top,
                overlaySize.Width,
                overlaySize.Height),
            OverlayPlacement.Bottom => new Rectangle(
                captureRegion.Left,
                captureRegion.Bottom + Gap,
                overlaySize.Width,
                overlaySize.Height),
            _ => Rectangle.Empty
        };
    }

    private static Rectangle PositionOutward(
        Rectangle anchor,
        Size overlaySize,
        OverlayPlacement placement)
    {
        return placement switch
        {
            OverlayPlacement.Left => new Rectangle(
                anchor.Left - overlaySize.Width - Gap,
                anchor.Top,
                overlaySize.Width,
                overlaySize.Height),
            OverlayPlacement.Top => new Rectangle(
                anchor.Left,
                anchor.Top - overlaySize.Height - Gap,
                overlaySize.Width,
                overlaySize.Height),
            OverlayPlacement.Right => new Rectangle(
                anchor.Right + Gap,
                anchor.Top,
                overlaySize.Width,
                overlaySize.Height),
            OverlayPlacement.Bottom => new Rectangle(
                anchor.Left,
                anchor.Bottom + Gap,
                overlaySize.Width,
                overlaySize.Height),
            _ => anchor
        };
    }

    private static Rectangle PositionAuxiliary(
        Rectangle anchor,
        Size overlaySize,
        OverlayPlacement placement,
        Rectangle bounds)
    {
        var desired = PositionOutward(anchor, overlaySize, placement);
        if (bounds.Contains(desired))
        {
            return desired;
        }

        // If there is no room farther in the selected direction, keep the
        // auxiliary panel clear of the stats panel instead of clamping the two
        // on top of each other.
        var below = new Rectangle(
            anchor.Left,
            anchor.Bottom + Gap,
            overlaySize.Width,
            overlaySize.Height);
        var above = new Rectangle(
            anchor.Left,
            anchor.Top - overlaySize.Height - Gap,
            overlaySize.Width,
            overlaySize.Height);
        var right = new Rectangle(
            anchor.Right + Gap,
            anchor.Top,
            overlaySize.Width,
            overlaySize.Height);
        var left = new Rectangle(
            anchor.Left - overlaySize.Width - Gap,
            anchor.Top,
            overlaySize.Width,
            overlaySize.Height);

        var fallbacks = placement is OverlayPlacement.Left or OverlayPlacement.Right
            ? new[] { below, above, right, left }
            : new[] { right, left, below, above };
        var fitting = fallbacks.FirstOrDefault(bounds.Contains);
        return fitting.IsEmpty ? ClampToBounds(desired, bounds) : fitting;
    }

    private static Rectangle ClampToBounds(Rectangle rectangle, Rectangle bounds)
    {
        var width = Math.Min(rectangle.Width, bounds.Width);
        var height = Math.Min(rectangle.Height, bounds.Height);
        return new Rectangle(
            Math.Clamp(rectangle.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - width)),
            Math.Clamp(rectangle.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height)),
            width,
            height);
    }

    private void HideAll()
    {
        _stats.Hide();
        _menu.Hide();
        _details.Hide();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        _stats.Dispose();
        _menu.Dispose();
        _details.Dispose();
        GlobalShiftKeyState.Release();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

}

internal abstract class LayeredOverlayForm : Form
{
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTransparent = 0x00000020;
    private const int GwlExStyle = -20;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcLButtonUp = 0x00A2;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonUp = 0x0202;
    private const int WmMouseWheel = 0x020A;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int HtTransparent = -1;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtRight = 11;
    private const int HtBottom = 15;
    private const int HtBottomRight = 17;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const int UlwAlpha = 0x00000002;

    private bool _interactionEnabled;
    private OverlayHitTest _manualOperation = OverlayHitTest.Transparent;
    private NativePoint _operationStartCursor;
    private Rectangle _operationStartBounds;

    protected LayeredOverlayForm(Size initialSize)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Size = initialSize;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool InteractionEnabled
    {
        get => _interactionEnabled;
        set
        {
            if (_interactionEnabled == value)
            {
                return;
            }

            _interactionEnabled = value;
            UpdateClickThroughStyle();
            RenderLayer();
        }
    }

    public bool IsInSizeMove { get; private set; }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExLayered | WsExToolWindow | WsExNoActivate;
            if (!InteractionEnabled)
            {
                parameters.ExStyle |= WsExTransparent;
            }
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateClickThroughStyle();
    }

    public void ShowInactive()
    {
        if (!Visible)
        {
            Show();
            RenderLayer();
        }
    }

    public void SetLayeredBounds(Rectangle bounds)
    {
        if (Bounds != bounds)
        {
            Bounds = bounds;
            RenderLayer();
        }
    }

    protected void RenderLayer()
    {
        if (!IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0 || IsDisposed)
        {
            return;
        }

        if (!GetWindowRect(Handle, out var nativeBounds))
        {
            return;
        }

        var nativeWidth = Math.Max(1, nativeBounds.Right - nativeBounds.Left);
        var nativeHeight = Math.Max(1, nativeBounds.Bottom - nativeBounds.Top);
        using var bitmap = new Bitmap(nativeWidth, nativeHeight, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.ScaleTransform(
                nativeWidth / (float)Math.Max(1, ClientSize.Width),
                nativeHeight / (float)Math.Max(1, ClientSize.Height));
            DrawLayer(graphics, ClientSize);
        }

        var screenDc = GetDC(nint.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = SelectObject(memoryDc, bitmapHandle);
        try
        {
            var destination = new NativePoint(nativeBounds.Left, nativeBounds.Top);
            var source = new NativePoint(0, 0);
            var size = new NativeSize(bitmap.Width, bitmap.Height);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };
            UpdateLayeredWindow(
                Handle,
                screenDc,
                ref destination,
                ref size,
                memoryDc,
                ref source,
                0,
                ref blend,
                UlwAlpha);
        }
        finally
        {
            SelectObject(memoryDc, oldBitmap);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(nint.Zero, screenDc);
        }
    }

    protected abstract void DrawLayer(Graphics graphics, Size size);

    protected virtual OverlayHitTest HitTestInteractive(Point point) => OverlayHitTest.Transparent;

    protected virtual void HandleClick(Point point)
    {
    }

    protected virtual void HandleMouseWheel(int delta)
    {
    }

    protected static void FillInvisibleHitArea(Graphics graphics, Rectangle rectangle)
    {
        using var brush = new SolidBrush(Color.FromArgb(1, 0, 0, 0));
        graphics.FillRectangle(brush, rectangle);
    }

    protected static void DrawOutlinedText(
        Graphics graphics,
        string text,
        Font font,
        PointF location,
        Color? color = null)
    {
        using var blackBrush = new SolidBrush(Color.FromArgb(245, Color.Black));
        for (var x = -2; x <= 2; x++)
        {
            for (var y = -2; y <= 2; y++)
            {
                if (x == 0 && y == 0)
                {
                    continue;
                }
                graphics.DrawString(text, font, blackBrush, location.X + x, location.Y + y);
            }
        }

        using var foreground = new SolidBrush(color ?? Color.White);
        graphics.DrawString(text, font, foreground, location);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        RenderLayer();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        ResizeFinished();
    }

    protected virtual void ResizeFinished()
    {
    }

    protected override void WndProc(ref Message message)
    {
        switch (message.Msg)
        {
            case WmNcHitTest:
                if (!IsShiftPressed())
                {
                    message.Result = HtTransparent;
                    return;
                }

                var screenPoint = new Point(
                    unchecked((short)((long)message.LParam & 0xFFFF)),
                    unchecked((short)(((long)message.LParam >> 16) & 0xFFFF)));
                var hit = HitTestInteractive(PointToClient(screenPoint));
                message.Result = hit switch
                {
                    OverlayHitTest.Client => HtClient,
                    OverlayHitTest.Move => HtCaption,
                    OverlayHitTest.ResizeRight => HtRight,
                    OverlayHitTest.ResizeBottom => HtBottom,
                    OverlayHitTest.ResizeBottomRight => HtBottomRight,
                    _ => HtTransparent
                };
                return;

            case WmLButtonUp when IsShiftPressed():
                if (_manualOperation is not OverlayHitTest.Transparent)
                {
                    FinishManualOperation();
                    return;
                }
                HandleClick(PointFromLParam(message.LParam));
                break;

            case WmNcLButtonDown when IsShiftPressed():
                var operation = (int)message.WParam switch
                {
                    HtCaption => OverlayHitTest.Move,
                    HtRight => OverlayHitTest.ResizeRight,
                    HtBottom => OverlayHitTest.ResizeBottom,
                    HtBottomRight => OverlayHitTest.ResizeBottomRight,
                    _ => OverlayHitTest.Transparent
                };
                if (operation is not OverlayHitTest.Transparent
                    && GetCursorPos(out _operationStartCursor))
                {
                    _manualOperation = operation;
                    _operationStartBounds = Bounds;
                    IsInSizeMove = true;
                    Capture = true;
                    message.Result = nint.Zero;
                    return;
                }
                break;

            case WmMouseMove when _manualOperation is not OverlayHitTest.Transparent:
                if (IsShiftPressed() && GetCursorPos(out var currentPoint))
                {
                    ApplyManualOperation(currentPoint);
                }
                else
                {
                    FinishManualOperation();
                }
                message.Result = nint.Zero;
                return;

            case WmNcLButtonUp when _manualOperation is not OverlayHitTest.Transparent:
                FinishManualOperation();
                message.Result = nint.Zero;
                return;

            case WmMouseWheel when IsShiftPressed():
                HandleMouseWheel(unchecked((short)(((long)message.WParam >> 16) & 0xFFFF)));
                message.Result = nint.Zero;
                return;

            case WmEnterSizeMove:
                IsInSizeMove = true;
                break;

            case WmExitSizeMove:
                IsInSizeMove = false;
                ResizeFinished();
                break;
        }

        base.WndProc(ref message);
    }

    private static Point PointFromLParam(nint value)
    {
        return new Point(
            unchecked((short)((long)value & 0xFFFF)),
            unchecked((short)(((long)value >> 16) & 0xFFFF)));
    }

    private static bool IsShiftPressed() => GlobalShiftKeyState.IsPressed;

    private void UpdateClickThroughStyle()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var current = GetWindowLongPtr(Handle, GwlExStyle).ToInt64();
        var updated = InteractionEnabled
            ? current & ~WsExTransparent
            : current | WsExTransparent;
        if (updated != current)
        {
            SetWindowLongPtr(Handle, GwlExStyle, new nint(updated));
            // WS_EX_TRANSPARENT affects hit testing. Force Windows to invalidate
            // its cached non-client/style state so pressing Shift takes effect
            // even while a DirectX game owns the foreground input queue.
            SetWindowPos(
                Handle,
                nint.Zero,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
    }

    private void ApplyManualOperation(NativePoint currentPoint)
    {
        var deltaX = currentPoint.X - _operationStartCursor.X;
        var deltaY = currentPoint.Y - _operationStartCursor.Y;
        if (_manualOperation == OverlayHitTest.Move)
        {
            Location = new Point(
                _operationStartBounds.X + deltaX,
                _operationStartBounds.Y + deltaY);
            return;
        }

        var minimumWidth = Math.Max(1, MinimumSize.Width);
        var minimumHeight = Math.Max(1, MinimumSize.Height);
        var maximumWidth = MaximumSize.Width > 0 ? MaximumSize.Width : 1600;
        var maximumHeight = MaximumSize.Height > 0 ? MaximumSize.Height : 1600;
        var width = _manualOperation is OverlayHitTest.ResizeRight or OverlayHitTest.ResizeBottomRight
            ? Math.Clamp(_operationStartBounds.Width + deltaX, minimumWidth, maximumWidth)
            : _operationStartBounds.Width;
        var height = _manualOperation is OverlayHitTest.ResizeBottom or OverlayHitTest.ResizeBottomRight
            ? Math.Clamp(_operationStartBounds.Height + deltaY, minimumHeight, maximumHeight)
            : _operationStartBounds.Height;
        Size = new Size(width, height);
    }

    private void FinishManualOperation()
    {
        _manualOperation = OverlayHitTest.Transparent;
        IsInSizeMove = false;
        Capture = false;
        ResizeFinished();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int width, int height)
    {
        public int Width = width;
        public int Height = height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        nint window,
        nint destinationDc,
        ref NativePoint destination,
        ref NativeSize size,
        nint sourceDc,
        ref NativePoint source,
        int colorKey,
        ref BlendFunction blend,
        int flags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint value);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}

internal enum OverlayHitTest
{
    Transparent,
    Client,
    Move,
    ResizeRight,
    ResizeBottom,
    ResizeBottomRight
}

internal sealed class StatsOverlayForm : LayeredOverlayForm
{
    public const int SideWidth = 205;
    public const int HorizontalHeight = 34;
    private readonly Font _textFont = new("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _moreFont = new("Segoe UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel);
    private long _adena;
    private long _xp;
    private long _sp;
    private bool _horizontal;
    private Rectangle _moreBounds = new(2, 68, 105, 25);

    public StatsOverlayForm() : base(new Size(SideWidth, 96))
    {
    }

    public event Action? MoreClicked;

    public static Size GetOverlaySize(OverlayPlacement placement, Size captureSize) => placement switch
    {
        OverlayPlacement.Top or OverlayPlacement.Bottom => new Size(
            Math.Max(1, captureSize.Width),
            HorizontalHeight),
        OverlayPlacement.Left or OverlayPlacement.Right => new Size(
            SideWidth,
            Math.Max(1, captureSize.Height)),
        _ => new Size(SideWidth, 96)
    };

    public void SetHorizontal(bool horizontal)
    {
        if (_horizontal == horizontal)
        {
            return;
        }

        _horizontal = horizontal;
        RenderLayer();
    }

    public void SetStatistics(long adena, long xp, long sp)
    {
        if (_adena == adena && _xp == xp && _sp == sp)
        {
            return;
        }

        _adena = adena;
        _xp = xp;
        _sp = sp;
        RenderLayer();
    }

    protected override void DrawLayer(Graphics graphics, Size size)
    {
        var values = new[]
        {
            $"Adena: {_adena:N0}",
            $"XP: {_xp:N0}",
            $"SP: {_sp:N0}",
            "More ▾"
        };
        if (_horizontal)
        {
            var cellWidth = size.Width / 4F;
            for (var index = 0; index < values.Length; index++)
            {
                var cell = RectangleF.FromLTRB(
                    index * cellWidth,
                    0,
                    (index + 1) * cellWidth,
                    size.Height);
                if (index == 3)
                {
                    _moreBounds = Rectangle.Round(cell);
                    FillInvisibleHitArea(graphics, _moreBounds);
                }
                DrawFittedOutlinedText(
                    graphics,
                    values[index],
                    index == 3 ? _moreFont : _textFont,
                    cell);
            }
            return;
        }

        var rowHeight = size.Height / 4F;
        for (var index = 0; index < values.Length; index++)
        {
            var row = RectangleF.FromLTRB(
                0,
                index * rowHeight,
                size.Width,
                (index + 1) * rowHeight);
            if (index == 3)
            {
                _moreBounds = Rectangle.Round(row);
                FillInvisibleHitArea(graphics, _moreBounds);
            }
            DrawFittedOutlinedText(
                graphics,
                values[index],
                index == 3 ? _moreFont : _textFont,
                row);
        }
    }

    protected override OverlayHitTest HitTestInteractive(Point point) =>
        _moreBounds.Contains(point) ? OverlayHitTest.Client : OverlayHitTest.Transparent;

    protected override void HandleClick(Point point)
    {
        if (_moreBounds.Contains(point))
        {
            MoreClicked?.Invoke();
        }
    }

    private static void DrawFittedOutlinedText(
        Graphics graphics,
        string text,
        Font baseFont,
        RectangleF bounds)
    {
        var availableWidth = Math.Max(1F, bounds.Width - 8F);
        var availableHeight = Math.Max(1F, bounds.Height - 4F);
        var measured = graphics.MeasureString(text, baseFont);
        var scale = Math.Min(1F, Math.Min(
            availableWidth / Math.Max(1F, measured.Width),
            availableHeight / Math.Max(1F, measured.Height)));
        using var fittedFont = scale < 0.99F
            ? new Font(
                baseFont.FontFamily,
                Math.Max(7F, baseFont.Size * scale),
                baseFont.Style,
                GraphicsUnit.Pixel)
            : null;
        var font = fittedFont ?? baseFont;
        measured = graphics.MeasureString(text, font);
        var location = new PointF(
            bounds.Left + Math.Max(4F, (bounds.Width - measured.Width) / 2F),
            bounds.Top + Math.Max(1F, (bounds.Height - measured.Height) / 2F));
        DrawOutlinedText(graphics, text, font, location);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _textFont.Dispose();
            _moreFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class OverlayMenuForm : LayeredOverlayForm
{
    public static readonly Size OverlaySize = new(155, 60);
    private readonly Font _font = new("Segoe UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Rectangle _itemsBounds = new(0, 0, 150, 28);
    private readonly Rectangle _questBounds = new(0, 30, 150, 28);

    public OverlayMenuForm() : base(OverlaySize)
    {
    }

    public event Action<bool>? CategorySelected;

    protected override void DrawLayer(Graphics graphics, Size size)
    {
        FillInvisibleHitArea(graphics, _itemsBounds);
        FillInvisibleHitArea(graphics, _questBounds);
        DrawOutlinedText(graphics, "Items", _font, new PointF(3, 3));
        DrawOutlinedText(graphics, "Quest Items", _font, new PointF(3, 33));
    }

    protected override OverlayHitTest HitTestInteractive(Point point) =>
        _itemsBounds.Contains(point) || _questBounds.Contains(point)
            ? OverlayHitTest.Client
            : OverlayHitTest.Transparent;

    protected override void HandleClick(Point point)
    {
        if (_itemsBounds.Contains(point))
        {
            CategorySelected?.Invoke(false);
        }
        else if (_questBounds.Contains(point))
        {
            CategorySelected?.Invoke(true);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class LootOverlayForm : LayeredOverlayForm
{
    private const int HeaderHeight = 31;
    private const int RowHeight = 23;
    private const int ResizeGrip = 14;
    private readonly Font _titleFont = new("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _itemFont = new("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _hintFont = new("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel);
    private IReadOnlyList<OverlayItem> _items = [];
    private string _title = "Items";
    private int _scrollOffset;

    public LootOverlayForm() : base(new Size(320, 320))
    {
        MinimumSize = new Size(220, 120);
        MaximumSize = new Size(1200, 1200);
    }

    public event Action? CloseClicked;
    public event Action<Rectangle>? BoundsChangeCompleted;

    public void SetItems(string title, IReadOnlyList<OverlayItem> items)
    {
        _title = title;
        _items = items;
        ClampScrollOffset();
        RenderLayer();
    }

    protected override void DrawLayer(Graphics graphics, Size size)
    {
        FillInvisibleHitArea(graphics, ClientRectangle);
        DrawOutlinedText(graphics, _title, _titleFont, new PointF(3, 3));
        DrawOutlinedText(graphics, "×", _titleFont, new PointF(size.Width - 25, 2));

        var visibleRows = Math.Max(1, (size.Height - HeaderHeight - ResizeGrip) / RowHeight);
        foreach (var (item, index) in _items.Skip(_scrollOffset).Take(visibleRows).Select((item, index) => (item, index)))
        {
            var y = HeaderHeight + index * RowHeight;
            DrawOutlinedText(graphics, item.Name, _itemFont, new PointF(3, y));
            var total = item.Total.ToString("N0");
            var totalWidth = graphics.MeasureString(total, _itemFont).Width;
            DrawOutlinedText(graphics, total, _itemFont, new PointF(size.Width - totalWidth - 6, y));
        }

        if (_items.Count == 0)
        {
            DrawOutlinedText(graphics, "No items", _itemFont, new PointF(3, HeaderHeight));
        }

        if (InteractionEnabled)
        {
            DrawOutlinedText(
                graphics,
                "Shift: move / resize",
                _hintFont,
                new PointF(Math.Max(3, size.Width - 125), size.Height - 15),
                Color.Gainsboro);
        }
    }

    protected override OverlayHitTest HitTestInteractive(Point point)
    {
        var nearRight = point.X >= Width - ResizeGrip;
        var nearBottom = point.Y >= Height - ResizeGrip;
        if (nearRight && nearBottom)
        {
            return OverlayHitTest.ResizeBottomRight;
        }
        if (nearRight)
        {
            return OverlayHitTest.ResizeRight;
        }
        if (nearBottom)
        {
            return OverlayHitTest.ResizeBottom;
        }
        if (point.Y <= HeaderHeight && point.X < Width - 34)
        {
            return OverlayHitTest.Move;
        }
        return ClientRectangle.Contains(point) ? OverlayHitTest.Client : OverlayHitTest.Transparent;
    }

    protected override void HandleClick(Point point)
    {
        if (point.X >= Width - 34 && point.Y <= HeaderHeight)
        {
            CloseClicked?.Invoke();
        }
    }

    protected override void HandleMouseWheel(int delta)
    {
        _scrollOffset += delta > 0 ? -1 : 1;
        ClampScrollOffset();
        RenderLayer();
    }

    protected override void ResizeFinished()
    {
        ClampScrollOffset();
        BoundsChangeCompleted?.Invoke(Bounds);
        RenderLayer();
    }

    private void ClampScrollOffset()
    {
        var visibleRows = Math.Max(1, (Height - HeaderHeight - ResizeGrip) / RowHeight);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, _items.Count - visibleRows));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _itemFont.Dispose();
            _hintFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
