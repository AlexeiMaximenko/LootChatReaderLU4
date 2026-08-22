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

        if (args.Length >= 1 && args[0].Equals("--icon-sync-test", StringComparison.OrdinalIgnoreCase))
        {
            RunIconSyncTest();
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--icon-cache-test", StringComparison.OrdinalIgnoreCase))
        {
            RunIconCacheTest();
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

    private static void RunIconSyncTest()
    {
        using var catalog = new ItemIconCatalogService(ApplicationDataPaths.RootDirectory);
        var progress = new Progress<IconCatalogProgress>(value =>
            Console.WriteLine($"PAGE {value.Page}/{value.TotalPages}: {value.ItemCount}"));
        var count = catalog.SyncAsync(progress).GetAwaiter().GetResult();
        Console.WriteLine($"TOTAL: {count}");

        foreach (var name in new[] { "Stem", "Monster Eye Meat", "Manster Eye Meat", "Basilisk's Gizzard", "Basili'" })
        {
            var match = catalog.Resolve(name);
            Console.WriteLine(match is null
                ? $"NO MATCH: {name}"
                : $"MATCH: {name} -> {match.Entry.Name} ({match.Entry.IconPath}), fuzzy={match.IsFuzzyMatch}");
        }
    }

    private static void RunIconCacheTest()
    {
        using var catalog = new ItemIconCatalogService(ApplicationDataPaths.RootDirectory);
        var match = catalog.Resolve("Stem")
            ?? throw new InvalidOperationException("Stem was not found in the cached catalog.");
        using var image = catalog.LoadIconAsync(match.Entry).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("The Stem icon could not be downloaded.");
        Console.WriteLine($"CATALOG: {catalog.Count}; STEM ICON: {image.Width}x{image.Height}");
    }

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
