namespace LootChatReader;

internal sealed class OverlaySettingsForm : Form
{
    private readonly Func<Rectangle, string, Task<Rectangle?>> _selectRegion;
    private readonly ComboBox _statsPlacement = new();
    private readonly Label _itemsRegionLabel = new();
    private readonly Label _questRegionLabel = new();
    private Rectangle _itemsRegion;
    private Rectangle _questRegion;
    private bool _itemsRegionSet;
    private bool _questRegionSet;

    public OverlaySettingsForm(
        AppSettings settings,
        Func<Rectangle, string, Task<Rectangle?>> selectRegion)
    {
        _selectRegion = selectRegion;
        _itemsRegion = settings.ItemsOverlayRegion;
        _questRegion = settings.QuestItemsOverlayRegion;
        _itemsRegionSet = settings.ItemsOverlayRegionSet;
        _questRegionSet = settings.QuestItemsOverlayRegionSet;

        Text = "Overlay Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 390);
        Font = new Font("Segoe UI", 9F);

        BuildInterface(settings.OverlayPlacement);
        UpdateRegionLabels();
    }

    public OverlayPlacement SelectedStatsPlacement =>
        _statsPlacement.SelectedItem is PlacementChoice choice
            ? choice.Value
            : OverlayPlacement.Off;

    public Rectangle ItemsRegion => _itemsRegion;
    public Rectangle QuestItemsRegion => _questRegion;
    public bool ItemsRegionSet => _itemsRegionSet;
    public bool QuestItemsRegionSet => _questRegionSet;

    private void BuildInterface(OverlayPlacement placement)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var statsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0)
        };
        statsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        statsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        statsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        statsPanel.Controls.Add(new Label
        {
            Text = "Statistics overlay position",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(Font, FontStyle.Bold)
        }, 0, 0);
        _statsPlacement.DropDownStyle = ComboBoxStyle.DropDownList;
        _statsPlacement.Dock = DockStyle.Fill;
        _statsPlacement.Items.AddRange(new object[]
        {
            new PlacementChoice("Off", OverlayPlacement.Off),
            new PlacementChoice("Left of chat", OverlayPlacement.Left),
            new PlacementChoice("Above chat", OverlayPlacement.Top),
            new PlacementChoice("Right of chat", OverlayPlacement.Right),
            new PlacementChoice("Below chat", OverlayPlacement.Bottom)
        });
        _statsPlacement.SelectedItem = _statsPlacement.Items
            .Cast<PlacementChoice>()
            .First(choice => choice.Value == placement);
        statsPanel.Controls.Add(_statsPlacement, 1, 0);
        statsPanel.Controls.Add(new Label
        {
            Text = "Adena, XP and SP only. The panel is always click-through.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Anchor = AnchorStyles.Left
        }, 0, 1);
        statsPanel.SetColumnSpan(statsPanel.GetControlFromPosition(0, 1)!, 2);
        root.Controls.Add(statsPanel, 0, 0);

        root.Controls.Add(CreateRegionGroup(
            "Obtained items overlay",
            "Choose the exact position and size inside the selected game window.",
            _itemsRegionLabel,
            async () => await SelectRegionAsync(false)), 0, 1);
        root.Controls.Add(CreateRegionGroup(
            "Quest items overlay",
            "Choose an independent position and size for quest items.",
            _questRegionLabel,
            async () => await SelectRegionAsync(true)), 0, 2);

        var note = new Label
        {
            Text = "Visibility is controlled by the two checkboxes on the tracker main page.",
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(note, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0)
        };
        var ok = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Height = 30
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Height = 30
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 4);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(root);
    }

    private static GroupBox CreateRegionGroup(
        string title,
        string description,
        Label regionLabel,
        Func<Task> select)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label
        {
            Text = description,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        var button = new Button
        {
            Text = "Select Area...",
            AutoSize = true,
            Anchor = AnchorStyles.Right
        };
        button.Click += async (_, _) => await select();
        layout.Controls.Add(button, 1, 0);
        regionLabel.AutoSize = true;
        regionLabel.Anchor = AnchorStyles.Left;
        layout.Controls.Add(regionLabel, 0, 1);
        layout.SetColumnSpan(regionLabel, 2);
        group.Controls.Add(layout);
        return group;
    }

    private async Task SelectRegionAsync(bool questItems)
    {
        var initial = questItems
            ? (_questRegionSet ? _questRegion : Rectangle.Empty)
            : (_itemsRegionSet ? _itemsRegion : Rectangle.Empty);
        var label = questItems ? "quest-items overlay" : "obtained-items overlay";
        Enabled = false;
        // Hiding a modal form makes ShowDialog return immediately. Its caller
        // then disposes the form while this asynchronous area selection is still
        // running. Keep the modal window alive and make it visually transparent
        // until the selector closes instead.
        Opacity = 0;
        try
        {
            var selected = await _selectRegion(initial, label);
            if (selected is not { } region)
            {
                return;
            }

            if (questItems)
            {
                _questRegion = region;
                _questRegionSet = true;
            }
            else
            {
                _itemsRegion = region;
                _itemsRegionSet = true;
            }
            UpdateRegionLabels();
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                Opacity = 1;
                Enabled = true;
                Activate();
            }
        }
    }

    private void UpdateRegionLabels()
    {
        _itemsRegionLabel.Text = FormatRegion(_itemsRegion, _itemsRegionSet);
        _questRegionLabel.Text = FormatRegion(_questRegion, _questRegionSet);
    }

    private static string FormatRegion(Rectangle region, bool regionSet) => regionSet
        ? $"Position: {region.X}, {region.Y}  ·  Size: {region.Width} × {region.Height}"
        : "Area not selected — an automatic position will be used.";

    private sealed record PlacementChoice(string Label, OverlayPlacement Value)
    {
        public override string ToString() => Label;
    }
}
