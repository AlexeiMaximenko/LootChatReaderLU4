using System.Diagnostics;
using System.Globalization;

namespace LootChatReader;

internal sealed class MainForm : Form
{
    private readonly string _settingsPath;
    private readonly AppSettings _settings;
    private readonly System.Windows.Forms.Timer _captureTimer;
    private readonly System.Windows.Forms.Timer _elapsedTimer;
    private readonly Stopwatch _elapsedStopwatch = new();
    private readonly EventSequenceTracker _eventTracker = new();
    private readonly ChatListMotionDetector _chatMotionDetector = new();
    private readonly MouseWheelMonitor _mouseWheelMonitor = new();
    private readonly Icon? _applicationIcon;
    private readonly ItemIconCatalogService _iconCatalog;
    private readonly ImageList _itemImages = new();
    private readonly Dictionary<string, SummaryEntry> _dropSummary = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SummaryEntry> _questSummary = new(StringComparer.OrdinalIgnoreCase);

    private readonly Button _selectRegionButton = new();
    private readonly Button _startStopButton = new();
    private readonly Button _clearButton = new();
    private readonly Label _regionLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _adenaValueLabel = new();
    private readonly Label _xpValueLabel = new();
    private readonly Label _spValueLabel = new();
    private readonly Label _elapsedValueLabel = new();
    private readonly ListView _dropSummaryList = new();
    private readonly ListView _questSummaryList = new();
    private readonly ListView _eventsList = new();
    private readonly PictureBox _preview = new();

    private OcrService? _ocrService;
    private bool _monitoring;
    private bool _captureInProgress;
    private bool _catalogSyncing;
    private bool _primeNextCapture;
    private long _chatWheelRevision;
    private DateTime _ignoreChatWheelUntilUtc;
    private long _totalAdena;
    private long _totalXp;
    private long _totalSp;

    public MainForm()
    {
        ApplicationDataPaths.EnsureRootDirectory();
        _settingsPath = ApplicationDataPaths.SettingsPath;
        _settings = AppSettings.Load(_settingsPath);
        _iconCatalog = new ItemIconCatalogService(ApplicationDataPaths.RootDirectory);
        _applicationIcon = EmbeddedResourceFiles.LoadIcon("LootChatReader.Resources.app.ico");
        _captureTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _captureTimer.Tick += CaptureTimerOnTick;
        _elapsedTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _elapsedTimer.Tick += (_, _) => UpdateElapsedTime();
        _mouseWheelMonitor.WheelScrolled += MouseWheelMonitorOnWheelScrolled;

        _itemImages.ColorDepth = ColorDepth.Depth32Bit;
        _itemImages.ImageSize = new Size(32, 32);
        _itemImages.TransparentColor = Color.Transparent;

        Text = $"LU4 Loot Chat Reader v{AppVersion.Display}";
        if (_applicationIcon is not null)
        {
            Icon = _applicationIcon;
        }
        MinimumSize = new Size(780, 500);
        Size = new Size(980, 650);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        BuildInterface();
        UpdateRegionLabel();
        UpdateStatistics();
        UpdateElapsedTime();
        UpdateControls();
        Shown += MainFormOnShown;
    }

    private void BuildInterface()
    {
        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 132,
            Padding = new Padding(12, 10, 12, 8),
            ColumnCount = 5,
            RowCount = 3
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        _selectRegionButton.Text = "Select Area";
        _selectRegionButton.AutoSize = true;
        _selectRegionButton.Height = 32;
        _selectRegionButton.Click += SelectRegionButtonOnClick;

        _startStopButton.AutoSize = true;
        _startStopButton.Height = 32;
        _startStopButton.Click += StartStopButtonOnClick;

        _clearButton.Text = "Clear All";
        _clearButton.AutoSize = true;
        _clearButton.Height = 32;
        _clearButton.Click += (_, _) => ClearData();

        _regionLabel.AutoEllipsis = true;
        _regionLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _statusLabel.ForeColor = Color.DimGray;

        var statisticsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 3, 0, 3)
        };
        statisticsPanel.Controls.Add(CreateStatisticBlock("Adena", _adenaValueLabel));
        statisticsPanel.Controls.Add(CreateStatisticBlock("XP", _xpValueLabel));
        statisticsPanel.Controls.Add(CreateStatisticBlock("SP", _spValueLabel));

        _preview.Dock = DockStyle.Fill;
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BackColor = Color.FromArgb(32, 32, 32);
        _preview.BorderStyle = BorderStyle.FixedSingle;

        topPanel.Controls.Add(_selectRegionButton, 0, 0);
        topPanel.Controls.Add(_startStopButton, 1, 0);
        topPanel.Controls.Add(_clearButton, 2, 0);
        topPanel.Controls.Add(_regionLabel, 3, 0);
        topPanel.Controls.Add(statisticsPanel, 0, 1);
        topPanel.SetColumnSpan(statisticsPanel, 4);
        topPanel.Controls.Add(_statusLabel, 0, 2);
        topPanel.SetColumnSpan(_statusLabel, 4);
        topPanel.Controls.Add(_preview, 4, 0);
        topPanel.SetRowSpan(_preview, 3);

        ConfigureSummaryList(_dropSummaryList, _itemImages);
        ConfigureSummaryList(_questSummaryList, _itemImages);
        ConfigureFullLogList();

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(14, 5)
        };

        var summaryPage = new TabPage("Summary");
        var logsPage = new TabPage("Full Logs");

        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BorderStyle = BorderStyle.None,
            SplitterWidth = 6,
            IsSplitterFixed = true
        };
        void ResizeSummaryPanels()
        {
            var availableWidth = splitContainer.ClientSize.Width - splitContainer.SplitterWidth;
            if (availableWidth > 0)
            {
                splitContainer.SplitterDistance = availableWidth / 2;
            }
        }

        splitContainer.SizeChanged += (_, _) => ResizeSummaryPanels();
        splitContainer.HandleCreated += (_, _) => ResizeSummaryPanels();
        splitContainer.Panel1.Controls.Add(CreateSummaryGroup("Obtained Items", _dropSummaryList));
        splitContainer.Panel2.Controls.Add(CreateSummaryGroup("Quest Items", _questSummaryList));
        summaryPage.Controls.Add(splitContainer);

        logsPage.Controls.Add(_eventsList);
        tabControl.TabPages.Add(summaryPage);
        tabControl.TabPages.Add(logsPage);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Padding = new Padding(12, 8, 12, 0),
            ColumnCount = 2,
            RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var hint = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Color.DimGray,
            Text = "Yellow obtained/earned lines and white acquired XP/SP lines are recognized. Screenshots are not saved."
        };
        _elapsedValueLabel.AutoSize = true;
        _elapsedValueLabel.Anchor = AnchorStyles.Right;
        _elapsedValueLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _elapsedValueLabel.ForeColor = Color.DimGray;

        footer.Controls.Add(hint, 0, 0);
        footer.Controls.Add(_elapsedValueLabel, 1, 0);

        Controls.Add(tabControl);
        Controls.Add(footer);
        Controls.Add(topPanel);
    }

    private static Control CreateStatisticBlock(string title, Label valueLabel)
    {
        var panel = new Panel
        {
            Width = 170,
            Height = 34,
            Margin = new Padding(0, 0, 10, 0),
            BackColor = Color.FromArgb(242, 242, 242),
            BorderStyle = BorderStyle.FixedSingle
        };

        var titleLabel = new Label
        {
            Text = $"{title}:",
            AutoSize = true,
            Location = new Point(10, 8),
            ForeColor = Color.DimGray
        };
        valueLabel.AutoSize = true;
        valueLabel.Location = new Point(72, 7);
        valueLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(valueLabel);
        return panel;
    }

    private static GroupBox CreateSummaryGroup(string title, ListView listView)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        group.Controls.Add(listView);
        return group;
    }

    private static void ConfigureSummaryList(ListView listView, ImageList imageList)
    {
        listView.Dock = DockStyle.Fill;
        listView.View = View.Details;
        listView.FullRowSelect = true;
        listView.GridLines = true;
        listView.HideSelection = false;
        listView.Sorting = SortOrder.Ascending;
        listView.SmallImageList = imageList;
        listView.Columns.Add("Item");
        listView.Columns.Add("Total", -2, HorizontalAlignment.Right);
        void ResizeColumns()
        {
            var usableWidth = Math.Max(
                0,
                listView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            var firstColumnWidth = usableWidth / 2;
            listView.Columns[0].Width = firstColumnWidth;
            listView.Columns[1].Width = usableWidth - firstColumnWidth;
        }

        listView.ClientSizeChanged += (_, _) => ResizeColumns();
        listView.HandleCreated += (_, _) => ResizeColumns();
    }

    private void ConfigureFullLogList()
    {
        _eventsList.Dock = DockStyle.Fill;
        _eventsList.View = View.Details;
        _eventsList.FullRowSelect = true;
        _eventsList.GridLines = true;
        _eventsList.HideSelection = false;
        _eventsList.ShowItemToolTips = true;
        _eventsList.SmallImageList = _itemImages;
        _eventsList.Columns.Add("Time", 95);
        _eventsList.Columns.Add("Type", 115);
        _eventsList.Columns.Add("Received");
        void ResizeFullLogColumns()
        {
            _eventsList.Columns[2].Width = Math.Max(
                100,
                _eventsList.ClientSize.Width
                - _eventsList.Columns[0].Width
                - _eventsList.Columns[1].Width
                - SystemInformation.VerticalScrollBarWidth
                - 4);
        }

        _eventsList.ClientSizeChanged += (_, _) => ResizeFullLogColumns();
        _eventsList.HandleCreated += (_, _) => ResizeFullLogColumns();
    }

    private async void MainFormOnShown(object? sender, EventArgs e)
    {
        if (_iconCatalog.Count > 0)
        {
            SetStatus($"Icon catalog loaded · {_iconCatalog.Count:N0} items", false);
            await ApplyIconsToExistingRowsAsync();
            if (!_iconCatalog.ShouldRefresh(TimeSpan.FromDays(7)))
            {
                return;
            }
        }

        await SyncIconCatalogAsync(false);
    }

    private async Task SyncIconCatalogAsync(bool showErrors)
    {
        if (_catalogSyncing)
        {
            return;
        }

        _catalogSyncing = true;
        UpdateControls();
        try
        {
            var progress = new Progress<IconCatalogProgress>(value =>
            {
                var totalPages = value.TotalPages > 0 ? value.TotalPages.ToString() : "?";
                SetStatus(
                    $"Syncing icon catalog · page {value.Page}/{totalPages} · {value.ItemCount:N0} items",
                    true);
            });

            var count = await _iconCatalog.SyncAsync(progress);
            SetStatus($"Icon catalog ready · {count:N0} items", false);
            await ApplyIconsToExistingRowsAsync();
        }
        catch (Exception exception)
        {
            SetStatus("Icon catalog could not be updated. OCR monitoring is still available.", false);
            if (showErrors)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Icon Catalog Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _catalogSyncing = false;
            UpdateControls();
        }
    }

    private async Task ApplyIconsToExistingRowsAsync()
    {
        var items = _eventsList.Items.Cast<ListViewItem>()
            .Concat(_dropSummaryList.Items.Cast<ListViewItem>())
            .Concat(_questSummaryList.Items.Cast<ListViewItem>())
            .ToArray();

        foreach (var item in items)
        {
            if (item.Tag is not string name)
            {
                continue;
            }

            var match = _iconCatalog.Resolve(name);
            if (match is not null)
            {
                await ApplyIconAsync(match.Entry, item);
            }
        }
    }

    private async Task ApplyIconAsync(ItemIconEntry entry, params ListViewItem?[] targets)
    {
        try
        {
            using var image = await _iconCatalog.LoadIconAsync(entry);
            if (image is null || IsDisposed || Disposing)
            {
                return;
            }

            var imageKey = entry.IconPath;
            if (!_itemImages.Images.ContainsKey(imageKey))
            {
                _itemImages.Images.Add(imageKey, new Bitmap(image));
            }

            foreach (var target in targets.Where(item => item is not null))
            {
                target!.ImageKey = imageKey;
            }
        }
        catch
        {
            // Missing icons do not affect OCR or statistics.
        }
    }

    private void SelectRegionButtonOnClick(object? sender, EventArgs e)
    {
        var wasVisible = Visible;
        Hide();
        using var selector = new RegionSelectorForm(_settings.CaptureRegion);
        var result = selector.ShowDialog();
        if (wasVisible)
        {
            Show();
            Activate();
        }

        if (result != DialogResult.OK)
        {
            return;
        }

        StopMonitoring();
        _eventTracker.Reset();
        _chatMotionDetector.Reset();
        _settings.SetCaptureRegion(selector.SelectedRegion);
        _settings.Save(_settingsPath);
        UpdateRegionLabel();
        SetStatus("Area selected. Ready to start recognition.", false);
        UpdateControls();
    }

    private void StartStopButtonOnClick(object? sender, EventArgs e)
    {
        if (_monitoring)
        {
            StopMonitoring();
            return;
        }

        StartMonitoring();
    }

    private void StartMonitoring()
    {
        if (!_settings.HasCaptureRegion)
        {
            MessageBox.Show(
                this,
                "Select the system chat area first.",
                "Area Not Selected",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            _ocrService ??= new OcrService(ApplicationDataPaths.RootDirectory);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "OCR Could Not Start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _monitoring = true;
        // Starting or resuming must never replay rows already visible in the chat.
        _primeNextCapture = true;
        _elapsedStopwatch.Start();
        _elapsedTimer.Start();
        UpdateElapsedTime();
        try
        {
            _mouseWheelMonitor.Start();
        }
        catch
        {
            // OCR remains usable if Windows does not allow installing the mouse hook.
        }
        _captureTimer.Start();
        SetStatus("Monitoring is running.", true);
        UpdateControls();
        _ = CaptureAndRecognizeAsync();
    }

    private void StopMonitoring()
    {
        _captureTimer.Stop();
        _mouseWheelMonitor.Stop();
        _monitoring = false;
        _elapsedStopwatch.Stop();
        _elapsedTimer.Stop();
        UpdateElapsedTime();
        SetStatus("Monitoring stopped.", false);
        UpdateControls();
    }

    private async void CaptureTimerOnTick(object? sender, EventArgs e)
    {
        await CaptureAndRecognizeAsync();
    }

    private async Task CaptureAndRecognizeAsync()
    {
        if (!_monitoring || _captureInProgress || _ocrService is null)
        {
            return;
        }

        _captureInProgress = true;
        var chatWheelRevisionAtCaptureStart = _chatWheelRevision;
        try
        {
            using var screenshot = ScreenCaptureService.Capture(_settings.CaptureRegion);
            var oldPreview = _preview.Image;
            _preview.Image = new Bitmap(screenshot);
            oldPreview?.Dispose();

            var events = await Task.Run(() => _ocrService.ReadEvents(screenshot));
            events = CanonicalizeForTracking(events);
            var listMotion = _chatMotionDetector.Observe(events);
            var chatWheelActive = chatWheelRevisionAtCaptureStart != _chatWheelRevision
                || DateTime.UtcNow < _ignoreChatWheelUntilUtc;
            var scrollReplaySuppressed = listMotion == ChatListMotion.ScrollUp || chatWheelActive;
            if (_primeNextCapture)
            {
                _eventTracker.SetBaselineImmediately(events);
                _primeNextCapture = false;
                SetStatus(
                    $"Monitoring is running · baseline captured · recognized lines: {events.Count}",
                    true);
                return;
            }


            if (scrollReplaySuppressed)
            {
                _eventTracker.BeginResynchronization();
                SetStatus(
                    $"Monitoring is running · chat scroll ignored · recognized lines: {events.Count}",
                    true);
                return;
            }

            foreach (var detectedEvent in _eventTracker.Observe(events))
            {
                AddEvent(detectedEvent);
            }

            SetStatus($"Monitoring is running · recognized lines in view: {events.Count}", true);
        }
        catch (Exception exception)
        {
            StopMonitoring();
            MessageBox.Show(
                this,
                $"The selected area could not be captured or recognized:\n{exception.Message}",
                "Recognition Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _captureInProgress = false;
        }
    }

    private void AddEvent(DetectedEvent detectedEvent)
    {
        ItemIconMatch? iconMatch = null;
        if (detectedEvent.Kind != DetectedEventKind.Experience)
        {
            iconMatch = _iconCatalog.Resolve(detectedEvent.SummaryName);
            if (iconMatch is not null)
            {
                detectedEvent = CanonicalizeEvent(detectedEvent, iconMatch.Entry.Name);
            }
        }

        var catalogRejected = detectedEvent.Kind != DetectedEventKind.Experience
            && detectedEvent.Adena == 0
            && _iconCatalog.Count > 0
            && iconMatch is null;

        if (catalogRejected)
        {
            // Unconfirmed item candidates are almost always fragments produced by
            // background highlights or adjacent fast-moving chat rows. Do not let
            // them pollute either Full Logs or Summary.
            return;
        }

        var logItem = new ListViewItem(DateTime.Now.ToString("HH:mm:ss"));
        logItem.SubItems.Add(detectedEvent.KindLabel);
        logItem.SubItems.Add(detectedEvent.Value);
        logItem.ToolTipText = detectedEvent.RawText;
        logItem.Tag = detectedEvent.SummaryName;
        _eventsList.Items.Add(logItem);
        logItem.EnsureVisible();

        ListViewItem? summaryItem = null;

        switch (detectedEvent.Kind)
        {
            case DetectedEventKind.Experience:
                _totalXp += detectedEvent.Xp;
                _totalSp += detectedEvent.Sp;
                break;

            case DetectedEventKind.Drop when detectedEvent.Adena > 0:
                _totalAdena += detectedEvent.Adena;
                break;

            case DetectedEventKind.Drop when !catalogRejected:
                summaryItem = AddToSummary(
                    _dropSummaryList,
                    _dropSummary,
                    detectedEvent.SummaryName,
                    detectedEvent.Quantity);
                break;

            case DetectedEventKind.QuestItem when !catalogRejected:
                summaryItem = AddToSummary(
                    _questSummaryList,
                    _questSummary,
                    detectedEvent.SummaryName,
                    detectedEvent.Quantity);
                break;
        }

        UpdateStatistics();

        if (iconMatch is not null)
        {
            _ = ApplyIconAsync(iconMatch.Entry, logItem, summaryItem);
        }
    }

    private static ListViewItem? AddToSummary(
        ListView listView,
        IDictionary<string, SummaryEntry> entries,
        string name,
        long quantity)
    {
        if (string.IsNullOrWhiteSpace(name) || quantity <= 0)
        {
            return null;
        }

        if (entries.TryGetValue(name, out var existing))
        {
            existing.Total += quantity;
            existing.Item.SubItems[1].Text = FormatNumber(existing.Total);
            return existing.Item;
        }

        var item = new ListViewItem(name);
        item.SubItems.Add(FormatNumber(quantity));
        item.Tag = name;
        listView.Items.Add(item);
        entries[name] = new SummaryEntry(item, quantity);
        listView.Sort();
        return item;
    }

    private static DetectedEvent CanonicalizeEvent(DetectedEvent detectedEvent, string canonicalName)
    {
        var value = detectedEvent.Quantity > 1
            ? $"{detectedEvent.Quantity} {canonicalName}"
            : canonicalName;
        return detectedEvent with
        {
            Value = value,
            SummaryName = canonicalName
        };
    }

    private IReadOnlyList<DetectedEvent> CanonicalizeForTracking(IReadOnlyList<DetectedEvent> events)
    {
        return events.Select(detectedEvent =>
        {
            if (detectedEvent.Kind == DetectedEventKind.Experience)
            {
                return detectedEvent;
            }

            var match = _iconCatalog.Resolve(detectedEvent.SummaryName);
            return match is null
                ? detectedEvent
                : CanonicalizeEvent(detectedEvent, match.Entry.Name);
        }).ToArray();
    }

    private void ClearData()
    {
        _eventTracker.BeginResynchronization();
        _chatMotionDetector.Reset();
        _eventsList.Items.Clear();
        _dropSummaryList.Items.Clear();
        _questSummaryList.Items.Clear();
        _dropSummary.Clear();
        _questSummary.Clear();
        _totalAdena = 0;
        _totalXp = 0;
        _totalSp = 0;
        UpdateStatistics();
        _elapsedStopwatch.Reset();
        if (_monitoring)
        {
            _elapsedStopwatch.Start();
            _elapsedTimer.Start();
        }
        else
        {
            _elapsedTimer.Stop();
        }

        UpdateElapsedTime();
    }

    private void MouseWheelMonitorOnWheelScrolled(object? sender, MouseWheelActivity activity)
    {
        if (!_monitoring || !_settings.CaptureRegion.Contains(activity.ScreenLocation))
        {
            return;
        }

        _chatWheelRevision++;
        _ignoreChatWheelUntilUtc = DateTime.UtcNow.AddMilliseconds(1200);
    }

    private void UpdateElapsedTime()
    {
        var elapsed = _elapsedStopwatch.Elapsed;
        var totalHours = (long)elapsed.TotalHours;
        _elapsedValueLabel.Text = $"Elapsed: {totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void UpdateStatistics()
    {
        _adenaValueLabel.Text = FormatNumber(_totalAdena);
        _xpValueLabel.Text = FormatNumber(_totalXp);
        _spValueLabel.Text = FormatNumber(_totalSp);
    }

    private static string FormatNumber(long value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private void UpdateRegionLabel()
    {
        _regionLabel.Text = _settings.HasCaptureRegion
            ? $"Area: {_settings.CaptureWidth}×{_settings.CaptureHeight} ({_settings.CaptureX}, {_settings.CaptureY})"
            : "Area not selected";
    }

    private void UpdateControls()
    {
        _startStopButton.Text = _monitoring ? "Stop" : "Start";
        _startStopButton.Enabled = _monitoring || _settings.HasCaptureRegion;
        _selectRegionButton.Enabled = !_monitoring;
    }

    private void SetStatus(string text, bool active)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = active ? Color.ForestGreen : Color.DimGray;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _captureTimer.Stop();
        _captureTimer.Dispose();
        _elapsedTimer.Stop();
        _elapsedTimer.Dispose();
        _mouseWheelMonitor.Dispose();
        _applicationIcon?.Dispose();
        _ocrService?.Dispose();
        _iconCatalog.Dispose();
        _itemImages.Dispose();
        _preview.Image?.Dispose();
        base.OnFormClosed(e);
    }

    private sealed class SummaryEntry(ListViewItem item, long total)
    {
        public ListViewItem Item { get; } = item;
        public long Total { get; set; } = total;
    }
}
