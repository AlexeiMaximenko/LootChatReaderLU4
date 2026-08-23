namespace LootChatReader;

internal sealed class WindowPickerForm : Form
{
    private readonly ListBox _windowsList = new();
    private readonly Button _selectButton = new();
    private IReadOnlyList<WindowDescriptor> _windows = Array.Empty<WindowDescriptor>();

    public WindowDescriptor? SelectedWindow { get; private set; }

    public WindowPickerForm(string preferredProcessName, string preferredWindowTitle)
    {
        Text = "Select Game Window";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(620, 330);
        Font = new Font("Segoe UI", 9F);

        var instruction = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 10, 10, 4),
            Text = "Select the LU4 game window. It may be covered after the chat area has been selected."
        };

        _windowsList.Dock = DockStyle.Fill;
        _windowsList.IntegralHeight = false;
        _windowsList.DoubleClick += (_, _) => SelectCurrent();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft
        };
        _selectButton.Text = "Select";
        _selectButton.AutoSize = true;
        _selectButton.Click += (_, _) => SelectCurrent();
        var cancelButton = new Button { Text = "Cancel", AutoSize = true };
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        var refreshButton = new Button { Text = "Refresh", AutoSize = true };
        refreshButton.Click += (_, _) => RefreshWindows(preferredProcessName, preferredWindowTitle);
        buttons.Controls.Add(_selectButton);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(refreshButton);

        Controls.Add(_windowsList);
        Controls.Add(buttons);
        Controls.Add(instruction);
        AcceptButton = _selectButton;
        CancelButton = cancelButton;
        Shown += (_, _) => RefreshWindows(preferredProcessName, preferredWindowTitle);
    }

    private void RefreshWindows(string preferredProcessName, string preferredWindowTitle)
    {
        _windows = ScreenCaptureService.EnumerateWindows();
        _windowsList.BeginUpdate();
        _windowsList.Items.Clear();
        foreach (var window in _windows)
        {
            _windowsList.Items.Add(window.DisplayName + (window.IsMinimized ? " [minimized]" : string.Empty));
        }
        _windowsList.EndUpdate();

        var preferredIndex = _windows
            .Select((window, index) => new { window, index })
            .OrderByDescending(item => preferredWindowTitle.Length > 0
                && item.window.Title.Equals(preferredWindowTitle, StringComparison.Ordinal)
                && item.window.ProcessName.Equals(preferredProcessName, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(item => item.window.ProcessName.Contains("lu4", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index)
            .FirstOrDefault(-1);
        if (_windows.Count > 0)
        {
            _windowsList.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
        }

        _selectButton.Enabled = _windows.Count > 0;
    }

    private void SelectCurrent()
    {
        if (_windowsList.SelectedIndex < 0 || _windowsList.SelectedIndex >= _windows.Count)
        {
            return;
        }

        SelectedWindow = _windows[_windowsList.SelectedIndex];
        DialogResult = DialogResult.OK;
        Close();
    }
}
