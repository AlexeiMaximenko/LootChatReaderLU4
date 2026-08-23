namespace LootChatReader;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ApplicationDataPaths.EnsureRootDirectory();

        if (args.Length >= 2 && args[0].Equals("--ocr-test", StringComparison.OrdinalIgnoreCase))
        {
            RunOcrTest(args.Skip(1));
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--icon-cache-test", StringComparison.OrdinalIgnoreCase))
        {
            RunIconCacheTest();
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--embedded-icons-test", StringComparison.OrdinalIgnoreCase))
        {
            RunEmbeddedIconsTest();
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--workspace-test", StringComparison.OrdinalIgnoreCase))
        {
            RunWorkspaceTest();
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--adena-test", StringComparison.OrdinalIgnoreCase))
        {
            RunAdenaTest();
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--overlay-test", StringComparison.OrdinalIgnoreCase))
        {
            RunOverlayTest();
            return;
        }

        if (args.Length >= 2 && args[0].Equals("--overlay-render-test", StringComparison.OrdinalIgnoreCase))
        {
            RunOverlayRenderTest(args[1], args.Length >= 3 && args[2].Equals("details", StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--catalog-resolve-test", StringComparison.OrdinalIgnoreCase))
        {
            RunCatalogResolveTest();
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--mouse-hook-test", StringComparison.OrdinalIgnoreCase))
        {
            using var mouseWheelMonitor = new MouseWheelMonitor();
            mouseWheelMonitor.Start();
            mouseWheelMonitor.Stop();
            Console.WriteLine("MOUSE HOOK: OK");
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--motion-test", StringComparison.OrdinalIgnoreCase))
        {
            RunMotionTest();
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--sequence-test", StringComparison.OrdinalIgnoreCase))
        {
            RunSequenceTest();
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--window-list-test", StringComparison.OrdinalIgnoreCase))
        {
            RunWindowListTest();
            return;
        }

        if (args.Length >= 2 && args[0].Equals("--window-capture-test", StringComparison.OrdinalIgnoreCase))
        {
            RunWindowCaptureTest(args[1]);
            return;
        }

        if (args.Length >= 6 && args[0].Equals("--window-region-ocr-test", StringComparison.OrdinalIgnoreCase))
        {
            RunWindowRegionOcrTest(
                args[1],
                int.Parse(args[2]),
                int.Parse(args[3]),
                int.Parse(args[4]),
                int.Parse(args[5]));
            return;
        }

        Application.Run(new MainForm());
    }

    private static void RunOcrTest(IEnumerable<string> imagePaths)
    {
        using var ocr = new OcrService(ApplicationDataPaths.RootDirectory);
        foreach (var imagePath in imagePaths)
        {
            Console.WriteLine($"IMAGE: {imagePath}");
            using var image = new Bitmap(imagePath);
            foreach (var item in ocr.ReadEvents(image))
            {
                Console.WriteLine($"{item.Kind}\t{item.Value}\t{item.RawText}");
            }
        }
    }

    private static void RunIconCacheTest()
    {
        using var catalog = new ItemIconCatalogService(ApplicationDataPaths.RootDirectory);
        var match = catalog.Resolve("Stem")
            ?? throw new InvalidOperationException("Stem was not found in the cached catalog.");
        using var image = catalog.LoadIconAsync(match.Entry).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("The embedded Stem icon could not be loaded.");
        Console.WriteLine($"CATALOG: {catalog.Count}; STEM ICON: {image.Width}x{image.Height}");
    }

    private static void RunEmbeddedIconsTest()
    {
        var assembly = typeof(Program).Assembly;
        using var stream = assembly.GetManifestResourceStream("LootChatReader.Resources.item-icons.json")
            ?? throw new InvalidOperationException("The embedded item catalog is missing.");
        var entries = System.Text.Json.JsonSerializer.Deserialize<ItemIconEntry[]>(stream)
            ?? throw new InvalidOperationException("The embedded item catalog is empty.");
        var untypedEntries = entries.Where(entry => string.IsNullOrWhiteSpace(entry.Type)).ToArray();
        if (untypedEntries.Length > 0)
        {
            throw new InvalidOperationException(
                $"Embedded catalog entries without a type: {string.Join(", ", untypedEntries.Take(10).Select(entry => entry.Name))}");
        }

        var resources = assembly.GetManifestResourceNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unavailableAtSource = entries
            .Select(entry => entry.IconPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !path.EndsWith("/none.png", StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                var fileName = Path.GetFileName(new Uri(new Uri("https://mw2.wiki/"), path).AbsolutePath);
                return !resources.Contains($"LootChatReader.Resources.ItemIcons.{fileName}");
            })
            .ToArray();
        if (unavailableAtSource.Length > 0
            && !resources.Contains("LootChatReader.Resources.ItemIcons.etc_jewel_box_i00.png"))
        {
            throw new InvalidOperationException("The generic fallback for unavailable wiki icons is missing.");
        }

        var iconCount = resources.Count(name => name.StartsWith(
            "LootChatReader.Resources.ItemIcons.",
            StringComparison.OrdinalIgnoreCase));
        Console.WriteLine(
            $"EMBEDDED ICONS: {iconCount}; CATALOG ENTRIES: {entries.Length}; " +
            $"TYPES: {entries.Select(entry => entry.Type).Distinct(StringComparer.OrdinalIgnoreCase).Count()}; " +
            $"QUEST ITEMS: {entries.Count(entry => entry.IsQuestItem)}; " +
            $"SOURCE FALLBACKS: {unavailableAtSource.Length}");
    }

    private static void RunWorkspaceTest()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"LootChatReader-{Guid.NewGuid():N}");
        var workspacePath = Path.Combine(testDirectory, "workspace.json");
        var legacyPath = Path.Combine(testDirectory, "settings.json");
        try
        {
            var first = new TrackerProfile { Name = "Window 1" };
            first.Histories.Add(new TrackingHistory
            {
                StartedAt = new DateTime(2026, 8, 23, 12, 0, 0),
                EndedAt = new DateTime(2026, 8, 23, 12, 10, 0),
                ElapsedTicks = TimeSpan.FromMinutes(8).Ticks,
                Adena = 1234,
                Xp = 5678,
                Sp = 90,
                Items = [new HistoryItem { Name = "Stem", Total = 3 }],
                QuestItems = [new HistoryItem { Name = "Monster Eye Meat", Total = 2 }],
                Logs =
                [
                    new HistoryLogEntry
                    {
                        Time = new DateTime(2026, 8, 23, 12, 1, 0),
                        Type = "Drop",
                        Value = "Stem",
                        SummaryName = "Stem"
                    }
                ]
            });
            var second = new TrackerProfile { Name = "Window 2" };
            var source = new WorkspaceState
            {
                SelectedProfileId = second.Id,
                Profiles = [first, second]
            };
            source.Save(workspacePath);

            var loaded = WorkspaceState.Load(workspacePath, legacyPath);
            var loadedHistory = loaded.Profiles.Single(profile => profile.Name == "Window 1").Histories.Single();
            if (loaded.Profiles.Count != 2
                || loaded.SelectedProfileId != second.Id
                || loadedHistory.Adena != 1234
                || loadedHistory.Items.Single().Total != 3
                || loadedHistory.Logs.Single().SummaryName != "Stem")
            {
                throw new InvalidOperationException("Workspace/history round-trip failed.");
            }

            Console.WriteLine("WORKSPACE: OK; PROFILES: 2; HISTORIES: 1");
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }

    private static void RunAdenaTest()
    {
        foreach (var verb in new[] { "obtained", "earned" })
        {
            var detected = OcrService.ParseDiagnosticText($"You have {verb} 4 Adena.", TextMask.Yellow)
                ?? throw new InvalidOperationException($"The {verb} Adena diagnostic line was not parsed.");
            if (detected.Adena != 4
                || detected.Kind != DetectedEventKind.Drop
                || !detected.SummaryName.Equals("Adena", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The {verb} Adena diagnostic line was parsed as an item.");
            }
        }

        using var catalog = new ItemIconCatalogService(ApplicationDataPaths.RootDirectory);
        var profile = new TrackerProfile { Name = "Adena migration" };
        profile.Histories.Add(new TrackingHistory
        {
            StartedAt = DateTime.Now.AddMinutes(-1),
            EndedAt = DateTime.Now,
            Items =
            [
                new HistoryItem { Name = "Adena", Total = 4 },
                new HistoryItem { Name = "Stem", Total = 1 }
            ]
        });
        using (var tracker = new TrackerView(profile, catalog, () => { }))
        {
            var history = profile.Histories.Single();
            if (history.Adena != 4
                || history.Items.Any(item => item.Name.Equals("Adena", StringComparison.OrdinalIgnoreCase))
                || history.Items.Single(item => item.Name == "Stem").Total != 1)
            {
                throw new InvalidOperationException("Historical Adena was not removed from the item list.");
            }
        }

        Console.WriteLine("ADENA: obtained/earned -> counter; historical item -> counter; item rows: 0");
    }

    private static void RunOverlayTest()
    {
        var settings = new AppSettings();
        if (settings.OverlayPlacement != OverlayPlacement.Off)
        {
            throw new InvalidOperationException("Overlay must be off by default.");
        }

        using var stats = new StatsOverlayForm();
        using var details = new LootOverlayForm();
        var statsHandle = stats.Handle;
        var detailsHandle = details.Handle;
        var statsStyle = GetWindowLongPtr(statsHandle, -20).ToInt64();
        const long wsExLayered = 0x00080000;
        const long wsExToolWindow = 0x00000080;
        const long wsExNoActivate = 0x08000000;
        const long wsExTransparent = 0x00000020;
        if ((statsStyle & wsExLayered) == 0
            || (statsStyle & wsExToolWindow) == 0
            || (statsStyle & wsExNoActivate) == 0
            || (statsStyle & wsExTransparent) == 0
            || details.MinimumSize.Height != 120
            || details.MaximumSize.Height != 1200)
        {
            throw new InvalidOperationException("Overlay window styles or resize limits are incorrect.");
        }

        stats.InteractionEnabled = true;
        if ((GetWindowLongPtr(statsHandle, -20).ToInt64() & wsExTransparent) != 0)
        {
            throw new InvalidOperationException("Shift interaction did not disable click-through mode.");
        }
        stats.InteractionEnabled = false;
        if ((GetWindowLongPtr(statsHandle, -20).ToInt64() & wsExTransparent) == 0)
        {
            throw new InvalidOperationException("Releasing Shift did not restore click-through mode.");
        }

        Console.WriteLine("OVERLAY: default off; click-through without Shift; interactive with Shift; height 120..1200");
    }

    private static void RunOverlayRenderTest(string outputPath, bool showDetails)
    {
        using var target = new Form
        {
            Text = "Overlay diagnostic target",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(120, 100, 900, 620),
            BackColor = Color.FromArgb(48, 58, 68),
            ShowInTaskbar = false
        };
        var targetHandle = target.Handle;
        var settings = new AppSettings
        {
            CaptureX = 90,
            CaptureY = 120,
            CaptureWidth = 330,
            CaptureHeight = 160,
            ReferenceWindowWidth = 900,
            ReferenceWindowHeight = 620,
            TargetProcessName = "diagnostic",
            OverlayPlacement = OverlayPlacement.Right,
            OverlayDetailsHeight = 230
        };
        using var overlay = new GameOverlayController(settings, () => targetHandle, () => { });
        overlay.UpdateSnapshot(new OverlaySnapshot(
            123456,
            789012,
            34567,
            [new OverlayItem("Animal Skin", 12), new OverlayItem("Stem", 8), new OverlayItem("Iron Ore", 3)],
            [new OverlayItem("Monster Eye Meat", 7), new OverlayItem("Basilisk's Gizzard", 2)]));
        if (showDetails)
        {
            overlay.ShowDetailsForDiagnostic(false);
        }

        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            using var screenshot = new Bitmap(target.Width, target.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(screenshot))
            {
                graphics.CopyFromScreen(target.Left, target.Top, 0, 0, screenshot.Size);
            }
            screenshot.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine(overlay.GetDiagnosticState());
            target.Close();
        };
        target.Shown += (_, _) =>
        {
            target.TopMost = true;
            target.TopMost = false;
            target.Activate();
            target.BringToFront();
            timer.Start();
        };
        Application.Run(target);
        timer.Dispose();
        Console.WriteLine($"OVERLAY RENDER: {outputPath}");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    private static void RunCatalogResolveTest()
    {
        using var catalog = new ItemIconCatalogService(ApplicationDataPaths.RootDirectory);
        var cases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Iran Ore"] = "Iron Ore",
            ["Stern"] = "Stem",
            ["Meanster Eye Meat"] = "Monster Eye Meat",
            ["Manster Eye Meat"] = "Monster Eye Meat",
            ["Ashen Reagent Cache"] = "Ashen Reagent Cache Tier D",
            ["Charcoal"] = "Charcoal",
            ["Cargo Box"] = "Cargo Box",
            ["Key Imprint"] = "Key Imprint"
        };

        foreach (var testCase in cases)
        {
            var match = catalog.Resolve(testCase.Key);
            if (match?.Entry.Name.Equals(testCase.Value, StringComparison.OrdinalIgnoreCase) != true)
            {
                throw new InvalidOperationException(
                    $"Catalog resolution failed: {testCase.Key} -> {match?.Entry.Name ?? "<none>"}.");
            }

            Console.WriteLine($"RESOLVE: {testCase.Key} -> {match.Entry.Name}");
        }

        if (catalog.Resolve("-") is not null)
        {
            throw new InvalidOperationException("Punctuation-only OCR artifact was accepted by the catalog.");
        }

        Console.WriteLine("REJECT: -");

        var stem = catalog.Resolve("Stem")
            ?? throw new InvalidOperationException("Stem was not resolved for the type test.");
        var monsterEyeMeat = catalog.Resolve("Monster Eye Meat")
            ?? throw new InvalidOperationException("Monster Eye Meat was not resolved for the type test.");
        if (stem.Entry.Type != "Other / Material"
            || monsterEyeMeat.Entry.Type != "Quest Item")
        {
            throw new InvalidOperationException("Catalog item types were not loaded correctly.");
        }

        var earnedStem = new DetectedEvent(
            DetectedEventKind.QuestItem,
            "Stem",
            "You have earned Stem.",
            0,
            "Stem",
            1,
            0,
            0,
            0);
        var obtainedQuestItem = earnedStem with
        {
            Kind = DetectedEventKind.Drop,
            Value = "Monster Eye Meat",
            RawText = "You have obtained Monster Eye Meat.",
            SummaryName = "Monster Eye Meat"
        };
        if (CatalogItemClassifier.Classify(earnedStem, stem.Entry) != DetectedEventKind.Drop
            || CatalogItemClassifier.Classify(obtainedQuestItem, monsterEyeMeat.Entry) != DetectedEventKind.QuestItem)
        {
            throw new InvalidOperationException("Catalog-based item classification failed.");
        }

        Console.WriteLine("TYPE: earned Stem -> Drop; obtained Monster Eye Meat -> Quest item");

        var reagent = catalog.Resolve("Ashen Reagent Cache")
            ?? throw new InvalidOperationException("Ashen Reagent Cache was not resolved for the icon test.");
        using var reagentIcon = catalog.LoadIconAsync(reagent.Entry).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("The reagent-cache fallback icon could not be loaded.");
        Console.WriteLine($"REAGENT ICON: {reagentIcon.Width}x{reagentIcon.Height}");
    }

    private static void RunMotionTest()
    {
        static DetectedEvent CreateEvent(string value, int top) => new(
            DetectedEventKind.Drop,
            value,
            value,
            top,
            value,
            1,
            0,
            0,
            0);

        var initial = new[] { CreateEvent("A", 30), CreateEvent("B", 50) };
        var contentMovedDown = new[] { CreateEvent("A", 45), CreateEvent("B", 65) };
        var contentMovedUp = new[] { CreateEvent("A", 15), CreateEvent("B", 35) };

        var detector = new ChatListMotionDetector();
        detector.Observe(initial);
        var scrollUp = detector.Observe(contentMovedDown);
        detector.Reset();
        detector.Observe(initial);
        var scrollDown = detector.Observe(contentMovedUp);

        if (scrollUp != ChatListMotion.ScrollUp || scrollDown != ChatListMotion.ScrollDown)
        {
            throw new InvalidOperationException(
                $"Unexpected motion result: up={scrollUp}, down={scrollDown}.");
        }

        Console.WriteLine($"MOTION: up={scrollUp}; down={scrollDown}");
    }

    private static void RunSequenceTest()
    {
        static DetectedEvent E(string value) => new(
            DetectedEventKind.Drop, value, value, 0, value, 1, 0, 0, 0);

        static void RequireCount(string name, IReadOnlyList<DetectedEvent> actual, int expected)
        {
            if (actual.Count != expected)
            {
                throw new InvalidOperationException($"{name}: expected {expected}, got {actual.Count}.");
            }
        }

        var tracker = new EventSequenceTracker();
        tracker.SetBaselineImmediately(new[] { E("A"), E("B") });
        RequireCount("new suffix first frame", tracker.Observe(new[] { E("A"), E("B"), E("C") }), 1);
        RequireCount("new suffix not replayed", tracker.Observe(new[] { E("A"), E("B"), E("C") }), 0);

        tracker.SetBaselineImmediately(new[] { E("A"), E("C") });
        tracker.Observe(new[] { E("A"), E("B"), E("C") });
        RequireCount("recovered middle row", tracker.Observe(new[] { E("A"), E("B"), E("C") }), 0);

        tracker.SetBaselineImmediately(new[] { E("A"), E("B") });
        tracker.BeginResynchronization();
        tracker.Observe(new[] { E("X"), E("Y") });
        RequireCount("scroll baseline", tracker.Observe(new[] { E("X"), E("Y") }), 0);
        RequireCount("after scroll new suffix", tracker.Observe(new[] { E("X"), E("Y"), E("Z") }), 1);
        RequireCount("after scroll suffix not replayed", tracker.Observe(new[] { E("X"), E("Y"), E("Z") }), 0);

        tracker.SetBaselineImmediately(new[] { E("A") });
        RequireCount("legitimate repeated row", tracker.Observe(new[] { E("A"), E("A") }), 1);
        RequireCount("repeated row not replayed", tracker.Observe(new[] { E("A"), E("A") }), 0);

        tracker.SetBaselineImmediately(new[] { E("A") });
        RequireCount("old anchor aged out", tracker.Observe(new[] { E("B") }), 1);

        tracker.SetBaselineImmediately(Array.Empty<DetectedEvent>());
        RequireCount("first event after empty baseline", tracker.Observe(new[] { E("C") }), 1);

        Console.WriteLine("SEQUENCE: OK");
    }

    private static void RunWindowListTest()
    {
        foreach (var window in ScreenCaptureService.EnumerateWindows())
        {
            Console.WriteLine(
                $"0x{window.Handle:X}\t{window.ProcessName}\t{window.Bounds.Width}x{window.Bounds.Height}\tminimized={window.IsMinimized}\t{window.Title}");
        }
    }

    private static void RunWindowCaptureTest(string processName)
    {
        var window = ScreenCaptureService.EnumerateWindows()
            .FirstOrDefault(candidate => candidate.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No capturable window was found for {processName}.");
        using var bitmap = ScreenCaptureService.CaptureWindowRegion(
            window.Handle,
            new Rectangle(Point.Empty, window.Bounds.Size),
            window.Bounds.Size);
        Console.WriteLine(
            $"WINDOW CAPTURE: {window.ProcessName}; {bitmap.Width}x{bitmap.Height}; minimized={window.IsMinimized}");
    }

    private static void RunWindowRegionOcrTest(
        string processName,
        int x,
        int y,
        int width,
        int height)
    {
        var window = ScreenCaptureService.EnumerateWindows()
            .FirstOrDefault(candidate => candidate.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No capturable window was found for {processName}.");
        using var bitmap = ScreenCaptureService.CaptureWindowRegion(
            window.Handle,
            new Rectangle(x, y, width, height),
            window.Bounds.Size);
        using var ocr = new OcrService(ApplicationDataPaths.RootDirectory);
        var events = ocr.ReadEvents(bitmap);
        Console.WriteLine($"WINDOW REGION OCR: {bitmap.Width}x{bitmap.Height}; events={events.Count}");
        foreach (var item in events)
        {
            Console.WriteLine($"{item.Kind}\t{item.Value}\t{item.RawText}");
        }
    }
}
