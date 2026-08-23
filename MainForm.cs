using System.Diagnostics;
using System.Globalization;

namespace LootChatReader;

internal sealed class TrackerView : UserControl
{
    private readonly TrackerProfile _profile;
    private readonly AppSettings _settings;
    private readonly Action _saveWorkspace;
    private readonly System.Windows.Forms.Timer _captureTimer;
    private readonly System.Windows.Forms.Timer _elapsedTimer;
    private readonly Stopwatch _elapsedStopwatch = new();
    private readonly EventSequenceTracker _eventTracker = new();
    private readonly ChatListMotionDetector _chatMotionDetector = new();
    private readonly ChatFrameMotionDetector _chatFrameMotionDetector = new();
    private readonly MouseWheelMonitor _mouseWheelMonitor = new();
    private readonly ItemIconCatalogService _iconCatalog;
    private readonly ImageList _itemImages = new();
    private readonly Dictionary<string, long> _dropSummary = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _questSummary = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HistoryLogEntry> _currentLogs = [];

    private readonly Button _selectRegionButton = new();
    private readonly Button _startStopButton = new();
    private readonly Button _clearButton = new();
    private readonly Label _regionLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _adenaValueLabel = new();
    private readonly Label _xpValueLabel = new();
    private readonly Label _spValueLabel = new();
    private readonly Label _elapsedValueLabel = new();
    private readonly Button _shareButton = new();
    private readonly ComboBox _historyCombo = new();
    private readonly ListView _dropSummaryList = new();
    private readonly ListView _questSummaryList = new();
    private readonly ListView _eventsList = new();
    private readonly PictureBox _preview = new();
    private readonly Dictionary<OverlayPlacement, Button> _overlayPlacementButtons = [];

    private OcrService? _ocrService;
    private GameOverlayController? _overlayController;
    private bool _monitoring;
    private bool _captureInProgress;
    private bool _primeNextCapture;
    private long _chatWheelRevision;
    private DateTime _ignoreChatWheelUntilUtc;
    private long _totalAdena;
    private long _totalXp;
    private long _totalSp;
    private nint _targetWindowHandle;
    private DateTime? _sessionStartedAt;
    private Bitmap? _latestPreview;
    private bool _refreshingHistory;

    public TrackerView(
        TrackerProfile profile,
        ItemIconCatalogService iconCatalog,
        Action saveWorkspace)
    {
        _profile = profile;
        _settings = profile.Settings;
        _iconCatalog = iconCatalog;
        _saveWorkspace = saveWorkspace;
        _captureTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _captureTimer.Tick += CaptureTimerOnTick;
        _elapsedTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _elapsedTimer.Tick += (_, _) => UpdateElapsedTime();
        _mouseWheelMonitor.WheelScrolled += MouseWheelMonitorOnWheelScrolled;

        _itemImages.ColorDepth = ColorDepth.Depth32Bit;
        _itemImages.ImageSize = new Size(32, 32);
        _itemImages.TransparentColor = Color.Transparent;

        Dock = DockStyle.Fill;
        Font = new Font("Segoe UI", 9F);

        BuildInterface();
        _overlayController = new GameOverlayController(
            _settings,
            () => _targetWindowHandle,
            _saveWorkspace);
        UpdateOverlayPlacementButtons();
        NormalizeProfileHistories();
        UpdateRegionLabel();
        UpdateStatistics();
        UpdateElapsedTime();
        UpdateControls();
        RefreshHistoryChoices();
        SetStatus($"Offline icon catalog loaded · {_iconCatalog.Count:N0} items", false);
    }

    private void BuildInterface()
    {
        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 182,
            Padding = new Padding(12, 10, 12, 8),
            ColumnCount = 5,
            RowCount = 3
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 215));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        _selectRegionButton.Text = "Select Window / Area";
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
        var historyPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        historyPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        historyPanel.Controls.Add(new Label
        {
            Text = "Tracking history",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 2)
        }, 0, 0);
        _historyCombo.Dock = DockStyle.Top;
        _historyCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _historyCombo.SelectedIndexChanged += HistoryComboOnSelectedIndexChanged;
        historyPanel.Controls.Add(_historyCombo, 0, 1);

        topPanel.Controls.Add(historyPanel, 4, 0);
        var overlaySelector = CreateOverlayPlacementSelector();
        topPanel.Controls.Add(overlaySelector, 4, 1);
        topPanel.SetRowSpan(overlaySelector, 2);

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
            ColumnCount = 3,
            RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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

        _shareButton.Text = "Share";
        _shareButton.AutoSize = true;
        _shareButton.Margin = new Padding(0, -3, 14, 0);
        _shareButton.Click += (_, _) => CopySummaryToClipboard();

        footer.Controls.Add(hint, 0, 0);
        footer.Controls.Add(_shareButton, 1, 0);
        footer.Controls.Add(_elapsedValueLabel, 2, 0);

        Controls.Add(tabControl);
        Controls.Add(footer);
        Controls.Add(topPanel);
    }

    private Control CreateOverlayPlacementSelector()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        Button AddPlacementButton(string text, OverlayPlacement placement, int column, int row)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2),
                Tag = placement,
                TabStop = false
            };
            button.FlatAppearance.BorderSize = 1;
            button.Click += OverlayPlacementButtonOnClick;
            _overlayPlacementButtons[placement] = button;
            panel.Controls.Add(button, column, row);
            return button;
        }

        AddPlacementButton("▲", OverlayPlacement.Top, 1, 0);
        AddPlacementButton("◀", OverlayPlacement.Left, 0, 1);
        AddPlacementButton("▶", OverlayPlacement.Right, 2, 1);
        AddPlacementButton("▼", OverlayPlacement.Bottom, 1, 2);
        panel.Controls.Add(_preview, 1, 1);
        return panel;
    }

    private void OverlayPlacementButtonOnClick(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: OverlayPlacement requested })
        {
            return;
        }

        var placement = _settings.OverlayPlacement == requested
            ? OverlayPlacement.Off
            : requested;
        _overlayController?.SetPlacement(placement);
        UpdateOverlayPlacementButtons();
        UpdateRegionLabel();
        _saveWorkspace();
    }

    private void UpdateOverlayPlacementButtons()
    {
        foreach (var (placement, button) in _overlayPlacementButtons)
        {
            var selected = _settings.OverlayPlacement == placement;
            button.BackColor = selected ? Color.FromArgb(0, 120, 215) : SystemColors.Control;
            button.ForeColor = selected ? Color.White : SystemColors.ControlText;
            button.FlatAppearance.BorderColor = selected ? Color.FromArgb(0, 84, 153) : SystemColors.ControlDark;
        }

        var overlayText = _settings.OverlayPlacement == OverlayPlacement.Off
            ? "Off"
            : _settings.OverlayPlacement.ToString();
        _preview.AccessibleDescription = $"Overlay: {overlayText}. Click the selected direction again to turn it off.";
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

    private async void SelectRegionButtonOnClick(object? sender, EventArgs e)
    {
        using var windowPicker = new WindowPickerForm(_settings.TargetProcessName);
        if (windowPicker.ShowDialog(this) != DialogResult.OK || windowPicker.SelectedWindow is null)
        {
            return;
        }

        var selectedWindow = windowPicker.SelectedWindow;
        StopMonitoring();
        var ownerForm = FindForm();
        ownerForm?.Hide();
        ScreenCaptureService.RestoreAndActivate(selectedWindow.Handle);
        await Task.Delay(350);
        if (!ScreenCaptureService.TryGetWindowBounds(selectedWindow.Handle, out var windowBounds))
        {
            ownerForm?.Show();
            MessageBox.Show(
                this,
                "The selected window is no longer available.",
                "Window Selection",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var initialScreenRegion = Rectangle.Empty;
        if (_settings.HasCaptureRegion
            && selectedWindow.ProcessName.Equals(_settings.TargetProcessName, StringComparison.OrdinalIgnoreCase))
        {
            initialScreenRegion = ScreenCaptureService.GetScreenRegion(
                selectedWindow.Handle,
                _settings.CaptureRegion,
                new Size(_settings.ReferenceWindowWidth, _settings.ReferenceWindowHeight));
        }

        using var selector = new RegionSelectorForm(windowBounds, initialScreenRegion);
        var result = selector.ShowDialog();
        ownerForm?.Show();
        ownerForm?.Activate();
        if (result != DialogResult.OK)
        {
            return;
        }

        _eventTracker.Reset();
        _chatMotionDetector.Reset();
        _chatFrameMotionDetector.Reset();
        selectedWindow = selectedWindow with { Bounds = windowBounds, IsMinimized = false };
        var relativeRegion = new Rectangle(
            selector.SelectedRegion.X - windowBounds.X,
            selector.SelectedRegion.Y - windowBounds.Y,
            selector.SelectedRegion.Width,
            selector.SelectedRegion.Height);
        _settings.SetCaptureTarget(selectedWindow, relativeRegion);
        _targetWindowHandle = selectedWindow.Handle;
        _saveWorkspace();
        UpdateRegionLabel();
        SetStatus("Game window and chat area selected. Ready to start recognition.", false);
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
                "Select the game window and system chat area first.",
                "Capture Target Not Selected",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var targetWindow = ScreenCaptureService.ResolveWindow(_settings, _targetWindowHandle);
        if (targetWindow is null)
        {
            MessageBox.Show(
                this,
                $"The selected game window ({_settings.TargetProcessName}) is not running.",
                "Game Window Not Found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _targetWindowHandle = targetWindow.Handle;

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
        _sessionStartedAt ??= DateTime.Now;
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
            var targetWindow = ScreenCaptureService.ResolveWindow(_settings, _targetWindowHandle);
            if (targetWindow is null)
            {
                _eventTracker.BeginResynchronization();
                _chatFrameMotionDetector.Reset();
                SetStatus("Monitoring is running · OCR paused: game window unavailable.", true);
                return;
            }
            _targetWindowHandle = targetWindow.Handle;
            if (targetWindow.IsMinimized)
            {
                _eventTracker.BeginResynchronization();
                _chatFrameMotionDetector.Reset();
                SetStatus("Monitoring is running · OCR paused while the game window is minimized.", true);
                return;
            }
            using var screenshot = await Task.Run(() => ScreenCaptureService.CaptureWindowRegion(
                targetWindow.Handle,
                _settings.CaptureRegion,
                new Size(_settings.ReferenceWindowWidth, _settings.ReferenceWindowHeight)));
            _latestPreview?.Dispose();
            _latestPreview = new Bitmap(screenshot);
            if (IsViewingCurrentSession)
            {
                _preview.Image = _latestPreview;
            }

            var visualVerticalShift = _chatFrameMotionDetector.Observe(screenshot);
            var events = await Task.Run(() => _ocrService.ReadEvents(screenshot));
            events = CanonicalizeForTracking(events);
            var listMotion = _chatMotionDetector.Observe(events);
            var chatWheelActive = chatWheelRevisionAtCaptureStart != _chatWheelRevision
                || DateTime.UtcNow < _ignoreChatWheelUntilUtc;
            var scrollReplaySuppressed = listMotion == ChatListMotion.ScrollUp
                || visualVerticalShift > 4
                || chatWheelActive;
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

            foreach (var detectedEvent in _eventTracker.Observe(events, visualVerticalShift))
            {
                AddEvent(detectedEvent);
            }

            SetStatus($"Monitoring is running · recognized lines in view: {events.Count}", true);
        }
        catch (WindowCaptureUnavailableException exception)
        {
            _eventTracker.BeginResynchronization();
            _chatFrameMotionDetector.Reset();
            SetStatus($"Monitoring is running · OCR paused: {exception.Message}", true);
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
        if (detectedEvent.Quantity > 0
            && detectedEvent.SummaryName.Equals("Adena", StringComparison.OrdinalIgnoreCase))
        {
            detectedEvent = detectedEvent with
            {
                Kind = DetectedEventKind.Drop,
                Adena = detectedEvent.Quantity,
                SummaryName = "Adena"
            };
        }

        ItemIconMatch? iconMatch = null;
        if (detectedEvent.Kind != DetectedEventKind.Experience)
        {
            iconMatch = _iconCatalog.Resolve(detectedEvent.SummaryName);
            if (iconMatch is not null)
            {
                detectedEvent = ApplyCatalogMetadata(detectedEvent, iconMatch.Entry);
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

        var logEntry = new HistoryLogEntry
        {
            Time = DateTime.Now,
            Type = detectedEvent.KindLabel,
            Value = detectedEvent.Value,
            RawText = detectedEvent.RawText,
            SummaryName = detectedEvent.SummaryName
        };
        _currentLogs.Add(logEntry);
        var logItem = IsViewingCurrentSession ? AddLogRow(logEntry, true) : null;

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

        if (IsViewingCurrentSession)
        {
            UpdateStatistics();
        }

        if (iconMatch is not null)
        {
            _ = ApplyIconAsync(iconMatch.Entry, logItem, summaryItem);
        }

        UpdateOverlaySnapshot();
    }

    private ListViewItem? AddToSummary(
        ListView listView,
        IDictionary<string, long> entries,
        string name,
        long quantity)
    {
        if (string.IsNullOrWhiteSpace(name) || quantity <= 0)
        {
            return null;
        }

        if (name.Equals("Adena", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        entries.TryGetValue(name, out var previousTotal);
        var total = previousTotal + quantity;
        entries[name] = total;
        if (!IsViewingCurrentSession)
        {
            return null;
        }

        var item = listView.Items.Cast<ListViewItem>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, name, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            item = AddSummaryRow(listView, name, total);
            listView.Sort();
        }
        else
        {
            item.SubItems[1].Text = FormatNumber(total);
        }

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

    private static DetectedEvent ApplyCatalogMetadata(
        DetectedEvent detectedEvent,
        ItemIconEntry catalogEntry)
    {
        var canonical = CanonicalizeEvent(detectedEvent, catalogEntry.Name);
        if (catalogEntry.Name.Equals("Adena", StringComparison.OrdinalIgnoreCase)
            && canonical.Quantity > 0)
        {
            return canonical with
            {
                Kind = DetectedEventKind.Drop,
                Adena = canonical.Quantity,
                SummaryName = "Adena"
            };
        }

        return canonical with
        {
            Kind = CatalogItemClassifier.Classify(canonical, catalogEntry)
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
                : ApplyCatalogMetadata(detectedEvent, match.Entry);
        }).ToArray();
    }

    private void NormalizeProfileHistories()
    {
        var changed = false;
        foreach (var history in _profile.Histories)
        {
            var combinedItems = history.Items
                .Select(item => (Item: item, WasQuest: false))
                .Concat(history.QuestItems.Select(item => (Item: item, WasQuest: true)))
                .GroupBy(item => item.Item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var name = group.First().Item.Name;
                    var match = _iconCatalog.Resolve(name);
                    return new
                    {
                        Name = match?.Entry.Name ?? name,
                        Total = group.Sum(item => item.Item.Total),
                        IsQuest = match?.Entry.IsQuestItem ?? group.First().WasQuest
                    };
                })
                .ToArray();

            var misplacedAdena = combinedItems
                .Where(item => item.Name.Equals("Adena", StringComparison.OrdinalIgnoreCase))
                .Sum(item => item.Total);
            if (misplacedAdena > 0)
            {
                history.Adena += misplacedAdena;
                changed = true;
            }

            var normalizedItems = combinedItems
                .Where(item => !item.IsQuest
                    && !item.Name.Equals("Adena", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Name)
                .Select(item => new HistoryItem { Name = item.Name, Total = item.Total })
                .ToList();
            var normalizedQuestItems = combinedItems
                .Where(item => item.IsQuest
                    && !item.Name.Equals("Adena", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Name)
                .Select(item => new HistoryItem { Name = item.Name, Total = item.Total })
                .ToList();
            if (!HistoryItemsEqual(history.Items, normalizedItems)
                || !HistoryItemsEqual(history.QuestItems, normalizedQuestItems))
            {
                history.Items = normalizedItems;
                history.QuestItems = normalizedQuestItems;
                changed = true;
            }

            foreach (var log in history.Logs.Where(log => !log.Type.Equals("XP / SP", StringComparison.OrdinalIgnoreCase)))
            {
                var match = _iconCatalog.Resolve(log.SummaryName);
                if (match is null)
                {
                    continue;
                }

                var normalizedType = match.Entry.IsQuestItem ? "Quest item" : "Drop";
                if (!log.Type.Equals(normalizedType, StringComparison.Ordinal))
                {
                    log.Type = normalizedType;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            _saveWorkspace();
        }
    }

    private static bool HistoryItemsEqual(
        IReadOnlyList<HistoryItem> left,
        IReadOnlyList<HistoryItem> right)
    {
        return left.Count == right.Count
            && left.Zip(right).All(pair =>
                pair.First.Name.Equals(pair.Second.Name, StringComparison.OrdinalIgnoreCase)
                && pair.First.Total == pair.Second.Total);
    }

    private void ClearData()
    {
        ArchiveCurrentSession();
        _eventTracker.BeginResynchronization();
        _chatMotionDetector.Reset();
        _chatFrameMotionDetector.Reset();
        _eventsList.Items.Clear();
        _dropSummaryList.Items.Clear();
        _questSummaryList.Items.Clear();
        _dropSummary.Clear();
        _questSummary.Clear();
        _currentLogs.Clear();
        _totalAdena = 0;
        _totalXp = 0;
        _totalSp = 0;
        _elapsedStopwatch.Reset();
        if (_monitoring)
        {
            _sessionStartedAt = DateTime.Now;
            _elapsedStopwatch.Start();
            _elapsedTimer.Start();
        }
        else
        {
            _sessionStartedAt = null;
            _elapsedTimer.Stop();
        }

        SelectCurrentSession();
        RenderSelectedSession();
        _saveWorkspace();
    }

    private void MouseWheelMonitorOnWheelScrolled(object? sender, MouseWheelActivity activity)
    {
        if (!_monitoring || _targetWindowHandle == nint.Zero)
        {
            return;
        }

        var screenRegion = ScreenCaptureService.GetScreenRegion(
            _targetWindowHandle,
            _settings.CaptureRegion,
            new Size(_settings.ReferenceWindowWidth, _settings.ReferenceWindowHeight));
        if (!screenRegion.Contains(activity.ScreenLocation))
        {
            return;
        }

        _chatWheelRevision++;
        _ignoreChatWheelUntilUtc = DateTime.UtcNow.AddMilliseconds(1200);
    }

    private void UpdateElapsedTime()
    {
        var elapsed = SelectedHistory?.Elapsed ?? _elapsedStopwatch.Elapsed;
        var totalHours = (long)elapsed.TotalHours;
        _elapsedValueLabel.Text = $"Elapsed: {totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void UpdateStatistics()
    {
        var history = SelectedHistory;
        _adenaValueLabel.Text = FormatNumber(history?.Adena ?? _totalAdena);
        _xpValueLabel.Text = FormatNumber(history?.Xp ?? _totalXp);
        _spValueLabel.Text = FormatNumber(history?.Sp ?? _totalSp);
        UpdateOverlaySnapshot();
    }

    private void UpdateOverlaySnapshot()
    {
        _overlayController?.UpdateSnapshot(new OverlaySnapshot(
            _totalAdena,
            _totalXp,
            _totalSp,
            _dropSummary
                .Where(pair => !pair.Key.Equals("Adena", StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key)
                .Select(pair => new OverlayItem(pair.Key, pair.Value))
                .ToArray(),
            _questSummary
                .Where(pair => !pair.Key.Equals("Adena", StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key)
                .Select(pair => new OverlayItem(pair.Key, pair.Value))
                .ToArray()));
    }

    private static string FormatNumber(long value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private void UpdateRegionLabel()
    {
        var overlay = _settings.OverlayPlacement == OverlayPlacement.Off
            ? "off"
            : _settings.OverlayPlacement.ToString().ToLowerInvariant();
        _regionLabel.Text = _settings.HasCaptureRegion
            ? $"Window: {_settings.TargetProcessName} · area: {_settings.CaptureWidth}×{_settings.CaptureHeight} · overlay: {overlay}"
            : $"Window and area not selected · overlay: {overlay}";
    }

    private void UpdateControls()
    {
        var current = IsViewingCurrentSession;
        _startStopButton.Text = _monitoring ? "Stop" : "Start";
        _startStopButton.Enabled = current && (_monitoring || _settings.HasCaptureRegion);
        _selectRegionButton.Enabled = current && !_monitoring;
        _clearButton.Enabled = current;
    }

    private void SetStatus(string text, bool active)
    {
        if (SelectedHistory is not null
            && (text.StartsWith("Monitoring", StringComparison.Ordinal)
                || text.Equals("Monitoring stopped.", StringComparison.Ordinal)))
        {
            return;
        }

        _statusLabel.Text = text;
        _statusLabel.ForeColor = active ? Color.ForestGreen : Color.DimGray;
    }

    private bool IsViewingCurrentSession => SelectedHistory is null;

    private TrackingHistory? SelectedHistory =>
        (_historyCombo.SelectedItem as HistoryChoice)?.History;

    private void HistoryComboOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_refreshingHistory)
        {
            return;
        }

        RenderSelectedSession();
    }

    private void RefreshHistoryChoices(Guid? selectedHistoryId = null)
    {
        _refreshingHistory = true;
        try
        {
            _historyCombo.Items.Clear();
            _historyCombo.Items.Add(new HistoryChoice(null));
            foreach (var history in _profile.Histories.OrderByDescending(item => item.EndedAt))
            {
                _historyCombo.Items.Add(new HistoryChoice(history));
            }

            var selectedIndex = 0;
            if (selectedHistoryId.HasValue)
            {
                for (var index = 1; index < _historyCombo.Items.Count; index++)
                {
                    if ((_historyCombo.Items[index] as HistoryChoice)?.History?.Id == selectedHistoryId)
                    {
                        selectedIndex = index;
                        break;
                    }
                }
            }

            _historyCombo.SelectedIndex = selectedIndex;
        }
        finally
        {
            _refreshingHistory = false;
        }
    }

    private void SelectCurrentSession()
    {
        if (_historyCombo.Items.Count == 0)
        {
            RefreshHistoryChoices();
        }

        _historyCombo.SelectedIndex = 0;
    }

    private void RenderSelectedSession()
    {
        _eventsList.BeginUpdate();
        _dropSummaryList.BeginUpdate();
        _questSummaryList.BeginUpdate();
        try
        {
            _eventsList.Items.Clear();
            _dropSummaryList.Items.Clear();
            _questSummaryList.Items.Clear();

            var history = SelectedHistory;
            var logs = history?.Logs ?? _currentLogs;
            var items = history?.Items
                ?? _dropSummary.OrderBy(pair => pair.Key)
                    .Select(pair => new HistoryItem { Name = pair.Key, Total = pair.Value })
                    .ToList();
            var questItems = history?.QuestItems
                ?? _questSummary.OrderBy(pair => pair.Key)
                    .Select(pair => new HistoryItem { Name = pair.Key, Total = pair.Value })
                    .ToList();

            foreach (var log in logs)
            {
                AddLogRow(log, false);
            }

            foreach (var item in items)
            {
                AddSummaryRow(_dropSummaryList, item.Name, item.Total);
            }

            foreach (var item in questItems)
            {
                AddSummaryRow(_questSummaryList, item.Name, item.Total);
            }

            _dropSummaryList.Sort();
            _questSummaryList.Sort();
            _preview.Image = history is null ? _latestPreview : null;
            UpdateStatistics();
            UpdateElapsedTime();
            UpdateControls();
            if (history is not null)
            {
                SetStatus($"Viewing history · {history.DisplayName}", false);
            }
            else
            {
                SetStatus(_monitoring ? "Monitoring is running." : "Current session.", _monitoring);
            }
        }
        finally
        {
            _eventsList.EndUpdate();
            _dropSummaryList.EndUpdate();
            _questSummaryList.EndUpdate();
        }

        _ = ApplyIconsToExistingRowsAsync();
    }

    private ListViewItem AddLogRow(HistoryLogEntry log, bool ensureVisible)
    {
        var item = new ListViewItem(log.Time.ToString("HH:mm:ss"));
        item.SubItems.Add(log.Type);
        item.SubItems.Add(log.Value);
        item.ToolTipText = log.RawText;
        item.Tag = log.SummaryName;
        _eventsList.Items.Add(item);
        if (ensureVisible)
        {
            item.EnsureVisible();
        }

        return item;
    }

    private static ListViewItem AddSummaryRow(ListView list, string name, long total)
    {
        var item = new ListViewItem(name);
        item.SubItems.Add(FormatNumber(total));
        item.Tag = name;
        list.Items.Add(item);
        return item;
    }

    private bool ArchiveCurrentSession()
    {
        if (!_sessionStartedAt.HasValue)
        {
            return false;
        }

        var history = new TrackingHistory
        {
            StartedAt = _sessionStartedAt.Value,
            EndedAt = DateTime.Now,
            ElapsedTicks = _elapsedStopwatch.Elapsed.Ticks,
            Adena = _totalAdena,
            Xp = _totalXp,
            Sp = _totalSp,
            Items = _dropSummary.OrderBy(pair => pair.Key)
                .Select(pair => new HistoryItem { Name = pair.Key, Total = pair.Value })
                .ToList(),
            QuestItems = _questSummary.OrderBy(pair => pair.Key)
                .Select(pair => new HistoryItem { Name = pair.Key, Total = pair.Value })
                .ToList(),
            Logs = _currentLogs.Select(log => new HistoryLogEntry
            {
                Time = log.Time,
                Type = log.Type,
                Value = log.Value,
                RawText = log.RawText,
                SummaryName = log.SummaryName
            }).ToList()
        };
        _profile.Histories.Insert(0, history);
        _sessionStartedAt = null;
        RefreshHistoryChoices();
        return true;
    }

    private void CopySummaryToClipboard()
    {
        var history = SelectedHistory;
        var elapsed = history?.Elapsed ?? _elapsedStopwatch.Elapsed;
        var adena = history?.Adena ?? _totalAdena;
        var xp = history?.Xp ?? _totalXp;
        var sp = history?.Sp ?? _totalSp;
        var items = history?.Items
            ?? _dropSummary.OrderBy(pair => pair.Key)
                .Select(pair => new HistoryItem { Name = pair.Key, Total = pair.Value })
                .ToList();
        var questItems = history?.QuestItems
            ?? _questSummary.OrderBy(pair => pair.Key)
                .Select(pair => new HistoryItem { Name = pair.Key, Total = pair.Value })
                .ToList();
        var totalHours = (long)elapsed.TotalHours;
        var lines = new List<string>
        {
            $"timer: {totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}",
            string.Empty,
            $"Adena: {FormatNumber(adena)}",
            $"Exp: {FormatNumber(xp)}",
            $"Sp: {FormatNumber(sp)}",
            string.Empty,
            "Items:"
        };
        lines.AddRange(items.OrderBy(item => item.Name).Select(item => $"{item.Name}: {FormatNumber(item.Total)}"));
        lines.Add(string.Empty);
        lines.Add("Quest items:");
        lines.AddRange(questItems.OrderBy(item => item.Name).Select(item => $"{item.Name}: {FormatNumber(item.Total)}"));

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            SetStatus("Summary copied to the clipboard.", false);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Clipboard Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void PrepareForShutdown()
    {
        StopMonitoring();
        if (ArchiveCurrentSession())
        {
            _saveWorkspace();
        }
    }

    public void StopForRemoval()
    {
        StopMonitoring();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _captureTimer.Stop();
            _captureTimer.Dispose();
            _elapsedTimer.Stop();
            _elapsedTimer.Dispose();
            _mouseWheelMonitor.Dispose();
            _overlayController?.Dispose();
            _ocrService?.Dispose();
            _itemImages.Dispose();
            _preview.Image = null;
            _latestPreview?.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class HistoryChoice(TrackingHistory? history)
    {
        public TrackingHistory? History { get; } = history;

        public override string ToString() => History?.DisplayName ?? "Current session";
    }
}
