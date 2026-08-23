namespace LootChatReader;

internal sealed class RegionSelectorForm : Form
{
    private Point _start;
    private Point _current;
    private bool _dragging;
    private readonly string _instruction;

    public Rectangle SelectedRegion { get; private set; }

    public RegionSelectorForm(
        Rectangle selectionBounds,
        Rectangle initialRegion,
        string instruction = "Drag to select the system chat area. Press Esc to cancel.")
    {
        _instruction = instruction;
        Bounds = selectionBounds;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        Opacity = 0.38;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        DoubleBuffered = true;

        if (initialRegion.Width > 0 && initialRegion.Height > 0)
        {
            SelectedRegion = initialRegion;
            _start = PointToClient(initialRegion.Location);
            _current = new Point(_start.X + initialRegion.Width, _start.Y + initialRegion.Height);
        }

        MouseDown += OnSelectorMouseDown;
        MouseMove += OnSelectorMouseMove;
        MouseUp += OnSelectorMouseUp;
        KeyDown += OnSelectorKeyDown;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var selection = GetClientSelection();
        if (selection.Width > 0 && selection.Height > 0)
        {
            using var fill = new SolidBrush(Color.FromArgb(90, Color.Gold));
            using var border = new Pen(Color.Gold, 3);
            e.Graphics.FillRectangle(fill, selection);
            e.Graphics.DrawRectangle(border, selection);
        }

        using var font = new Font("Segoe UI", 16, FontStyle.Bold);
        var textSize = e.Graphics.MeasureString(_instruction, font);
        var textRect = new RectangleF(
            (ClientSize.Width - textSize.Width) / 2,
            28,
            textSize.Width + 24,
            textSize.Height + 12);
        using var textBackground = new SolidBrush(Color.FromArgb(210, 20, 20, 20));
        e.Graphics.FillRectangle(textBackground, textRect);
        e.Graphics.DrawString(_instruction, font, Brushes.White, textRect.X + 12, textRect.Y + 6);
    }

    private void OnSelectorMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        _start = e.Location;
        _current = e.Location;
        Capture = true;
        Invalidate();
    }

    private void OnSelectorMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _current = e.Location;
        Invalidate();
    }

    private void OnSelectorMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = false;
        Capture = false;
        _current = e.Location;

        var selection = GetClientSelection();
        if (selection.Width < 80 || selection.Height < 30)
        {
            Invalidate();
            return;
        }

        SelectedRegion = new Rectangle(PointToScreen(selection.Location), selection.Size);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnSelectorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Escape)
        {
            return;
        }

        DialogResult = DialogResult.Cancel;
        Close();
    }

    private Rectangle GetClientSelection()
    {
        return Rectangle.FromLTRB(
            Math.Min(_start.X, _current.X),
            Math.Min(_start.Y, _current.Y),
            Math.Max(_start.X, _current.X),
            Math.Max(_start.Y, _current.Y));
    }
}
