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
    private const uint GwHwndPrev = 3;
    private const uint GaRootOwner = 3;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private static readonly nint HwndTop = nint.Zero;
    private static readonly nint HwndTopmost = new(-1);
    private static readonly nint HwndNotTopmost = new(-2);

    private readonly AppSettings _settings;
    private readonly Func<nint> _targetWindowProvider;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly StatsOverlayForm _stats = new();
    private readonly LootOverlayForm _items = new();
    private readonly LootOverlayForm _questItems = new();
    private bool _temporarilyHidden;
    private bool _disposed;

    public GameOverlayController(
        AppSettings settings,
        Func<nint> targetWindowProvider,
        Action saveSettings)
    {
        _settings = settings;
        _targetWindowProvider = targetWindowProvider;
        _items.SetItems("Items", []);
        _questItems.SetItems("Quest Items", []);

        _timer = new System.Windows.Forms.Timer { Interval = 40 };
        _timer.Tick += (_, _) => UpdateOverlayWindows();
        _timer.Start();
    }

    public void SetPlacement(OverlayPlacement placement)
    {
        _settings.OverlayPlacement = placement;
        UpdateOverlayWindows();
    }

    public void RefreshConfiguration()
    {
        UpdateOverlayWindows();
    }

    public void SetTemporarilyHidden(bool hidden)
    {
        _temporarilyHidden = hidden;
        if (hidden)
        {
            HideAll();
        }
        else
        {
            UpdateOverlayWindows();
        }
    }

    public void UpdateSnapshot(OverlaySnapshot snapshot)
    {
        _stats.SetStatistics(snapshot.Adena, snapshot.Xp, snapshot.Sp);
        _items.SetItems("Items", snapshot.Items);
        _questItems.SetItems("Quest Items", snapshot.QuestItems);
    }

    internal void ShowDetailsForDiagnostic(bool questItems)
    {
        if (questItems)
        {
            _settings.ShowQuestItemsOverlay = true;
        }
        else
        {
            _settings.ShowItemsOverlay = true;
        }
        UpdateOverlayWindows();
    }

    internal string GetDiagnosticState() =>
        $"stats={_stats.Bounds} visible={_stats.Visible}; " +
        $"items={_items.Bounds} visible={_items.Visible}; " +
        $"quest={_questItems.Bounds} visible={_questItems.Visible}";

    internal (nint Owner, bool Topmost) GetZOrderDiagnostic()
    {
        var window = new LayeredOverlayForm[] { _stats, _items, _questItems }
            .First(candidate => candidate.Visible || candidate == _stats);
        return (window.NativeOwner, window.IsNativeTopmost);
    }

    internal void ApplyZOrderForDiagnostic(nint targetWindow, bool targetIsForeground) =>
        PlaceDirectlyAboveTarget(targetWindow, targetIsForeground);

    private void UpdateOverlayWindows()
    {
        var showStats = _settings.OverlayPlacement != OverlayPlacement.Off;
        var showItems = _settings.ShowItemsOverlay;
        var showQuestItems = _settings.ShowQuestItemsOverlay;
        var targetWindow = _targetWindowProvider();
        if (_temporarilyHidden
            || (!showStats && !showItems && !showQuestItems)
            || targetWindow == nint.Zero
            || !IsWindow(targetWindow)
            || IsIconic(targetWindow)
            || !_settings.HasCaptureRegion
            || !ScreenCaptureService.TryGetWindowBounds(targetWindow, out var windowBounds))
        {
            HideAll();
            return;
        }

        var referenceSize = new Size(
            _settings.ReferenceWindowWidth,
            _settings.ReferenceWindowHeight);
        var captureRegion = ScreenCaptureService.GetScreenRegion(
            targetWindow,
            _settings.CaptureRegion,
            referenceSize);
        if (captureRegion.IsEmpty)
        {
            HideAll();
            return;
        }

        foreach (var window in OverlayWindows)
        {
            window.SetNativeOwner(targetWindow);
        }

        Rectangle? statsBounds = null;
        if (showStats)
        {
            var placement = _settings.OverlayPlacement;
            _stats.SetPlacement(placement);
            var size = StatsOverlayForm.GetOverlaySize(placement, captureRegion.Size);
            statsBounds = ClampToBounds(PositionBeside(captureRegion, size, placement), windowBounds);
            _stats.SetLayeredBounds(statsBounds.Value);
            _stats.ShowInactive();
        }
        else
        {
            _stats.Hide();
        }

        Rectangle? itemsBounds = null;
        if (showItems)
        {
            var fallbackAnchor = statsBounds ?? captureRegion;
            var fallback = PositionAuxiliary(
                fallbackAnchor,
                DefaultLootSize(windowBounds),
                OverlayPlacement.Right,
                windowBounds);
            itemsBounds = ResolveConfiguredBounds(
                targetWindow,
                _settings.ItemsOverlayRegion,
                _settings.ItemsOverlayRegionSet,
                referenceSize,
                fallback,
                windowBounds);
            _items.SetLayeredBounds(itemsBounds.Value);
            _items.ShowInactive();
        }
        else
        {
            _items.Hide();
        }

        if (showQuestItems)
        {
            var fallbackAnchor = itemsBounds ?? statsBounds ?? captureRegion;
            var fallback = PositionAuxiliary(
                fallbackAnchor,
                DefaultLootSize(windowBounds),
                OverlayPlacement.Bottom,
                windowBounds);
            var bounds = ResolveConfiguredBounds(
                targetWindow,
                _settings.QuestItemsOverlayRegion,
                _settings.QuestItemsOverlayRegionSet,
                referenceSize,
                fallback,
                windowBounds);
            _questItems.SetLayeredBounds(bounds);
            _questItems.ShowInactive();
        }
        else
        {
            _questItems.Hide();
        }

        PlaceDirectlyAboveTarget(targetWindow);
    }

    private IEnumerable<LayeredOverlayForm> OverlayWindows
    {
        get
        {
            yield return _stats;
            yield return _items;
            yield return _questItems;
        }
    }

    private static Rectangle ResolveConfiguredBounds(
        nint targetWindow,
        Rectangle relativeRegion,
        bool regionSet,
        Size referenceSize,
        Rectangle fallback,
        Rectangle windowBounds)
    {
        if (!regionSet || relativeRegion.Width < 80 || relativeRegion.Height < 30)
        {
            return ClampToBounds(fallback, windowBounds);
        }

        var screenRegion = ScreenCaptureService.GetScreenRegion(
            targetWindow,
            relativeRegion,
            referenceSize);
        return ClampToBounds(screenRegion, windowBounds);
    }

    private static Size DefaultLootSize(Rectangle windowBounds) => new(
        Math.Clamp(320, 80, Math.Max(80, windowBounds.Width)),
        Math.Clamp(250, 30, Math.Max(30, windowBounds.Height)));

    private void PlaceDirectlyAboveTarget(nint targetWindow, bool? foregroundOverride = null)
    {
        var targetIsForeground = foregroundOverride ?? IsTargetForeground(targetWindow);
        if (targetIsForeground)
        {
            foreach (var window in OverlayWindows.Where(window => window.Visible && window.IsHandleCreated))
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
            return;
        }

        var overlayHandles = OverlayWindows
            .Where(window => window.IsHandleCreated)
            .Select(window => window.Handle)
            .ToHashSet();
        var insertAfter = GetWindow(targetWindow, GwHwndPrev);
        while (insertAfter != nint.Zero && overlayHandles.Contains(insertAfter))
        {
            insertAfter = GetWindow(insertAfter, GwHwndPrev);
        }

        var zOrderAnchor = insertAfter == nint.Zero ? HwndTop : insertAfter;
        foreach (var window in OverlayWindows.Where(window => window.Visible && window.IsHandleCreated))
        {
            SetWindowPos(
                window.Handle,
                HwndNotTopmost,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder);
            SetWindowPos(
                window.Handle,
                zOrderAnchor,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder);
        }
    }

    private static Rectangle PositionBeside(
        Rectangle captureRegion,
        Size overlaySize,
        OverlayPlacement placement) => placement switch
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

    private static Rectangle PositionAuxiliary(
        Rectangle anchor,
        Size overlaySize,
        OverlayPlacement placement,
        Rectangle bounds)
    {
        var desired = PositionBeside(anchor, overlaySize, placement);
        if (bounds.Contains(desired))
        {
            return desired;
        }

        var candidates = new[]
        {
            PositionBeside(anchor, overlaySize, OverlayPlacement.Right),
            PositionBeside(anchor, overlaySize, OverlayPlacement.Bottom),
            PositionBeside(anchor, overlaySize, OverlayPlacement.Left),
            PositionBeside(anchor, overlaySize, OverlayPlacement.Top)
        };
        return candidates.FirstOrDefault(bounds.Contains) is { IsEmpty: false } fitting
            ? fitting
            : ClampToBounds(desired, bounds);
    }

    private static Rectangle ClampToBounds(Rectangle rectangle, Rectangle bounds)
    {
        var width = Math.Min(Math.Max(1, rectangle.Width), bounds.Width);
        var height = Math.Min(Math.Max(1, rectangle.Height), bounds.Height);
        return new Rectangle(
            Math.Clamp(rectangle.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - width)),
            Math.Clamp(rectangle.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height)),
            width,
            height);
    }

    private void HideAll()
    {
        foreach (var window in OverlayWindows)
        {
            window.Hide();
        }
    }

    internal static bool IsTargetForeground(nint targetWindow)
    {
        if (targetWindow == nint.Zero)
        {
            return false;
        }

        var foreground = GetForegroundWindow();
        if (foreground == nint.Zero)
        {
            return false;
        }
        if (foreground == targetWindow)
        {
            return true;
        }

        var targetRoot = GetAncestor(targetWindow, GaRootOwner);
        var foregroundRoot = GetAncestor(foreground, GaRootOwner);
        if (targetRoot != nint.Zero && targetRoot == foregroundRoot)
        {
            return true;
        }

        GetWindowThreadProcessId(targetWindow, out var targetProcess);
        GetWindowThreadProcessId(foreground, out var foregroundProcess);
        return targetProcess != 0 && targetProcess == foregroundProcess;
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
        _items.Dispose();
        _questItems.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

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
    private const int GwlpHwndParent = -8;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const int UlwAlpha = 0x00000002;

    private nint _nativeOwner;

    protected LayeredOverlayForm(Size initialSize)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Size = initialSize;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
    }

    internal nint NativeOwner => GetWindowLongPtr(Handle, GwlpHwndParent);

    internal bool IsNativeTopmost =>
        (GetWindowLongPtr(Handle, GwlExStyle).ToInt64() & 0x00000008) != 0;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExLayered | WsExToolWindow | WsExNoActivate | WsExTransparent;
            return parameters;
        }
    }

    public void ShowInactive()
    {
        if (!Visible)
        {
            Show();
            RenderLayer();
        }
    }

    public void SetNativeOwner(nint owner)
    {
        if (_nativeOwner == owner)
        {
            return;
        }

        _nativeOwner = owner;
        SetWindowLongPtr(Handle, GwlpHwndParent, owner);
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
                if (x != 0 || y != 0)
                {
                    graphics.DrawString(text, font, blackBrush, location.X + x, location.Y + y);
                }
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}

internal sealed class StatsOverlayForm : LayeredOverlayForm
{
    public const int SideWidth = 205;
    public const int HorizontalHeight = 34;
    private readonly Font _textFont = new("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel);
    private long _adena;
    private long _xp;
    private long _sp;
    private bool _horizontal;
    private OverlayPlacement _placement = OverlayPlacement.Right;

    public StatsOverlayForm() : base(new Size(SideWidth, 96))
    {
    }

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

    public void SetPlacement(OverlayPlacement placement)
    {
        var horizontal = placement is OverlayPlacement.Top or OverlayPlacement.Bottom;
        if (_horizontal == horizontal && _placement == placement)
        {
            return;
        }

        _horizontal = horizontal;
        _placement = placement;
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
        var values = new[] { $"Adena: {_adena:N0}", $"XP: {_xp:N0}", $"SP: {_sp:N0}" };
        if (_horizontal)
        {
            var cellWidth = size.Width / 3F;
            for (var index = 0; index < values.Length; index++)
            {
                DrawFittedOutlinedText(
                    graphics,
                    values[index],
                    RectangleF.FromLTRB(
                        index * cellWidth,
                        0,
                        (index + 1) * cellWidth,
                        size.Height),
                    TextEdgeAlignment.Center);
            }
            return;
        }

        var rowHeight = size.Height / 3F;
        for (var index = 0; index < values.Length; index++)
        {
            DrawFittedOutlinedText(
                graphics,
                values[index],
                RectangleF.FromLTRB(0, index * rowHeight, size.Width, (index + 1) * rowHeight),
                _placement == OverlayPlacement.Left
                    ? TextEdgeAlignment.Right
                    : TextEdgeAlignment.Left);
        }
    }

    private void DrawFittedOutlinedText(
        Graphics graphics,
        string text,
        RectangleF bounds,
        TextEdgeAlignment alignment)
    {
        var availableWidth = Math.Max(1F, bounds.Width - 8F);
        var availableHeight = Math.Max(1F, bounds.Height - 4F);
        var measured = graphics.MeasureString(text, _textFont);
        var scale = Math.Min(1F, Math.Min(
            availableWidth / Math.Max(1F, measured.Width),
            availableHeight / Math.Max(1F, measured.Height)));
        using var fittedFont = scale < 0.99F
            ? new Font(
                _textFont.FontFamily,
                Math.Max(7F, _textFont.Size * scale),
                _textFont.Style,
                GraphicsUnit.Pixel)
            : null;
        var font = fittedFont ?? _textFont;
        measured = graphics.MeasureString(text, font);
        var x = alignment switch
        {
            TextEdgeAlignment.Left => bounds.Left + 4F,
            TextEdgeAlignment.Right => bounds.Right - measured.Width - 4F,
            _ => bounds.Left + Math.Max(4F, (bounds.Width - measured.Width) / 2F)
        };
        DrawOutlinedText(
            graphics,
            text,
            font,
            new PointF(x, bounds.Top + Math.Max(1F, (bounds.Height - measured.Height) / 2F)));
    }

    private enum TextEdgeAlignment
    {
        Left,
        Center,
        Right
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _textFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class LootOverlayForm : LayeredOverlayForm
{
    private const int HeaderHeight = 29;
    private const int RowHeight = 22;
    private readonly Font _titleFont = new("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _itemFont = new("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _overflowFont = new("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel);
    private IReadOnlyList<OverlayItem> _items = [];
    private string _title = "Items";

    public LootOverlayForm() : base(new Size(320, 250))
    {
    }

    public void SetItems(string title, IReadOnlyList<OverlayItem> items)
    {
        _title = title;
        _items = items;
        RenderLayer();
    }

    protected override void DrawLayer(Graphics graphics, Size size)
    {
        DrawOutlinedText(graphics, _title, _titleFont, new PointF(3, 3), Color.Gold);
        var visibleRows = Math.Max(0, (size.Height - HeaderHeight) / RowHeight);
        var hasOverflow = _items.Count > visibleRows;
        var itemRows = hasOverflow ? Math.Max(0, visibleRows - 1) : visibleRows;
        foreach (var (item, index) in _items.Take(itemRows).Select((item, index) => (item, index)))
        {
            var y = HeaderHeight + index * RowHeight;
            DrawOutlinedText(graphics, item.Name, _itemFont, new PointF(3, y));
            var total = item.Total.ToString("N0");
            var totalWidth = graphics.MeasureString(total, _itemFont).Width;
            DrawOutlinedText(graphics, total, _itemFont, new PointF(size.Width - totalWidth - 6, y));
        }

        if (_items.Count == 0 && visibleRows > 0)
        {
            DrawOutlinedText(graphics, "No items", _itemFont, new PointF(3, HeaderHeight));
        }
        else if (hasOverflow && visibleRows > 0)
        {
            var hidden = _items.Count - itemRows;
            var y = HeaderHeight + itemRows * RowHeight;
            DrawOutlinedText(graphics, $"… +{hidden} more", _overflowFont, new PointF(3, y), Color.Gainsboro);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _itemFont.Dispose();
            _overflowFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
