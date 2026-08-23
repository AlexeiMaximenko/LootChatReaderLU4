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

        if (args.Length >= 1 && args[0].Equals("--overlay-isolation-test", StringComparison.OrdinalIgnoreCase))
        {
            RunOverlayIsolationTest();
            return;
        }

        if (args.Length >= 2 && args[0].Equals("--overlay-render-test", StringComparison.OrdinalIgnoreCase))
        {
            var options = args.Skip(2).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var placement = options.Contains("top")
                ? OverlayPlacement.Top
                : options.Contains("bottom")
                    ? OverlayPlacement.Bottom
                    : options.Contains("left")
                        ? OverlayPlacement.Left
                        : OverlayPlacement.Right;
            RunOverlayRenderTest(
                args[1],
                options.Contains("details"),
                placement,
                options.Contains("topmost"),
                options.Contains("inactive"));
            return;
        }

        if (args.Length >= 2 && args[0].Equals("--overlay-settings-render-test", StringComparison.OrdinalIgnoreCase))
        {
            RunOverlaySettingsRenderTest(args[1]);
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--overlay-settings-lifetime-test", StringComparison.OrdinalIgnoreCase))
        {
            RunOverlaySettingsLifetimeTest();
            return;
        }

        if (args.Length >= 2 && args[0].Equals("--tracker-render-test", StringComparison.OrdinalIgnoreCase))
        {
            RunTrackerRenderTest(args[1]);
            return;
        }

        if (args.Length >= 1 && args[0].Equals("--window-unavailable-test", StringComparison.OrdinalIgnoreCase))
        {
            RunWindowUnavailableTest();
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

        if (args.Length >= 1 && args[0].Equals("--frame-motion-test", StringComparison.OrdinalIgnoreCase))
        {
            RunFrameMotionTest();
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
            var first = new TrackerProfile
            {
                Name = "Window 1",
                Settings = new AppSettings
                {
                    OverlayPlacement = OverlayPlacement.Top,
                    ShowItemsOverlay = true,
                    ItemsOverlayX = 420,
                    ItemsOverlayY = 180,
                    ItemsOverlayWidth = 480,
                    ItemsOverlayHeight = 360,
                    ItemsOverlayRegionSet = true,
                    ShowQuestItemsOverlay = true,
                    QuestItemsOverlayX = 40,
                    QuestItemsOverlayY = 220,
                    QuestItemsOverlayWidth = 300,
                    QuestItemsOverlayHeight = 240,
                    QuestItemsOverlayRegionSet = true
                }
            };
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
                || loadedHistory.Logs.Single().SummaryName != "Stem"
                || loaded.Profiles.Single(profile => profile.Name == "Window 1").Settings.ItemsOverlayWidth != 480
                || loaded.Profiles.Single(profile => profile.Name == "Window 1").Settings.ItemsOverlayX != 420
                || !loaded.Profiles.Single(profile => profile.Name == "Window 1").Settings.ShowQuestItemsOverlay
                || loaded.Profiles.Single(profile => profile.Name == "Window 1").Settings.QuestItemsOverlayHeight != 240)
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
        if (settings.ShowItemsOverlay || settings.ShowQuestItemsOverlay)
        {
            throw new InvalidOperationException("Loot overlays must be off by default.");
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
        var captureSize = new Size(382, 160);
        var horizontalSize = StatsOverlayForm.GetOverlaySize(OverlayPlacement.Top, captureSize);
        var verticalSize = StatsOverlayForm.GetOverlaySize(OverlayPlacement.Left, captureSize);
        if ((statsStyle & wsExLayered) == 0
            || (statsStyle & wsExToolWindow) == 0
            || (statsStyle & wsExNoActivate) == 0
            || (statsStyle & wsExTransparent) == 0
            || horizontalSize.Width != captureSize.Width
            || horizontalSize.Height != StatsOverlayForm.HorizontalHeight
            || verticalSize.Width != StatsOverlayForm.SideWidth
            || verticalSize.Height != captureSize.Height)
        {
            throw new InvalidOperationException("Overlay window styles or capture-relative sizes are incorrect.");
        }

        if ((GetWindowLongPtr(statsHandle, -20).ToInt64() & wsExTransparent) == 0
            || (GetWindowLongPtr(detailsHandle, -20).ToInt64() & wsExTransparent) == 0)
        {
            throw new InvalidOperationException("Overlay panels must remain permanently click-through.");
        }

        Console.WriteLine("OVERLAY: permanently click-through; no More/Shift input; configured loot rectangles");
    }

    private static void RunOverlayIsolationTest()
    {
        using var firstTarget = new Form
        {
            Text = "First game diagnostic",
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(80, 80, 520, 420),
            ShowInTaskbar = false
        };
        using var secondTarget = new Form
        {
            Text = "Second game diagnostic",
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(640, 80, 520, 420),
            ShowInTaskbar = false
        };
        var firstHandle = firstTarget.Handle;
        var secondHandle = secondTarget.Handle;
        static AppSettings Settings() => new()
        {
            CaptureX = 40,
            CaptureY = 100,
            CaptureWidth = 300,
            CaptureHeight = 150,
            ReferenceWindowWidth = 520,
            ReferenceWindowHeight = 420,
            TargetProcessName = "diagnostic",
            OverlayPlacement = OverlayPlacement.Right
        };
        using var firstOverlay = new GameOverlayController(Settings(), () => firstHandle, () => { });
        using var secondOverlay = new GameOverlayController(Settings(), () => secondHandle, () => { });
        firstTarget.Show();
        secondTarget.Show();
        firstTarget.Activate();
        firstTarget.BringToFront();

        Exception? failure = null;
        var timer = new System.Windows.Forms.Timer { Interval = 350 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            firstOverlay.ApplyZOrderForDiagnostic(firstHandle, true);
            secondOverlay.ApplyZOrderForDiagnostic(secondHandle, false);
            var first = firstOverlay.GetZOrderDiagnostic();
            var second = secondOverlay.GetZOrderDiagnostic();
            if (first.Owner != firstHandle
                || second.Owner != secondHandle
                || !first.Topmost
                || second.Topmost)
            {
                failure = new InvalidOperationException(
                    $"Overlay isolation failed: first owner=0x{first.Owner:X} topmost={first.Topmost}; " +
                    $"second owner=0x{second.Owner:X} topmost={second.Topmost}.");
            }
            firstTarget.Close();
        };
        timer.Start();
        Application.Run(firstTarget);
        timer.Dispose();
        secondTarget.Close();
        if (failure is not null)
        {
            throw failure;
        }
        Console.WriteLine("OVERLAY ISOLATION: foreground overlay topmost; inactive overlay bound only to its owner");
    }

    private static void RunOverlayRenderTest(
        string outputPath,
        bool showDetails,
        OverlayPlacement placement,
        bool targetTopmost,
        bool targetInactive)
    {
        using var target = new Form
        {
            Text = "Overlay diagnostic target",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(120, 100, 900, 620),
            BackColor = Color.FromArgb(48, 58, 68),
            ShowInTaskbar = false,
            TopMost = targetTopmost
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
            OverlayPlacement = placement,
            ItemsOverlayX = 560,
            ItemsOverlayY = 330,
            ItemsOverlayWidth = 300,
            ItemsOverlayHeight = 230,
            ItemsOverlayRegionSet = true
        };
        using var overlay = new GameOverlayController(settings, () => targetHandle, () => { });
        overlay.UpdateSnapshot(new OverlaySnapshot(
            123456,
            789012,
            34567,
            [new OverlayItem("Animal Skin", 12), new OverlayItem("Stem", 8), new OverlayItem("Iron Ore", 3)],
            [new OverlayItem("Monster Eye Meat", 7), new OverlayItem("Basilisk's Gizzard", 2)]));
        Form? foregroundWindow = null;
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
            foregroundWindow?.Close();
            target.Close();
        };
        target.Shown += (_, _) =>
        {
            if (!targetTopmost)
            {
                target.TopMost = true;
                target.TopMost = false;
            }
            if (targetInactive)
            {
                foregroundWindow = new Form
                {
                    Text = "Foreground diagnostic window",
                    StartPosition = FormStartPosition.Manual,
                    Bounds = new Rectangle(1060, 120, 240, 180),
                    ShowInTaskbar = false
                };
                foregroundWindow.Show();
                foregroundWindow.Activate();
                foregroundWindow.BringToFront();
                overlay.UpdateSnapshot(new OverlaySnapshot(
                    654321,
                    987654,
                    45678,
                    [new OverlayItem("Updated while inactive", 2)],
                    []));
            }
            else
            {
                target.Activate();
                target.BringToFront();
            }
            timer.Start();
        };
        Application.Run(target);
        timer.Dispose();
        foregroundWindow?.Dispose();
        Console.WriteLine($"OVERLAY RENDER: {outputPath}");
    }

    private static void RunOverlaySettingsRenderTest(string outputPath)
    {
        var settings = new AppSettings
        {
            OverlayPlacement = OverlayPlacement.Top,
            ItemsOverlayX = 460,
            ItemsOverlayY = 120,
            ItemsOverlayWidth = 340,
            ItemsOverlayHeight = 260,
            ItemsOverlayRegionSet = true,
            QuestItemsOverlayX = 60,
            QuestItemsOverlayY = 360,
            QuestItemsOverlayWidth = 320,
            QuestItemsOverlayHeight = 220,
            QuestItemsOverlayRegionSet = true
        };
        using var form = new OverlaySettingsForm(
            settings,
            (_, _) => Task.FromResult<Rectangle?>(null));
        CaptureFormForDiagnostic(form, outputPath);
    }

    private static void RunOverlaySettingsLifetimeTest()
    {
        using var host = new Form
        {
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(100, 100, 240, 160)
        };
        Exception? failure = null;
        host.Shown += (_, _) => host.BeginInvoke(() =>
        {
            var callbackCompleted = false;
            using var dialog = new OverlaySettingsForm(
                new AppSettings(),
                async (_, _) =>
                {
                    await Task.Delay(120);
                    callbackCompleted = true;
                    return new Rectangle(10, 20, 300, 220);
                });
            var selectButton = dialog.Controls
                .Cast<Control>()
                .SelectMany(DescendantsAndSelf)
                .OfType<Button>()
                .First(button => button.Text == "Select Area...");
            var clickTimer = new System.Windows.Forms.Timer { Interval = 50 };
            clickTimer.Tick += (_, _) =>
            {
                clickTimer.Stop();
                selectButton.PerformClick();
            };
            var closeTimer = new System.Windows.Forms.Timer { Interval = 350 };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                if (!dialog.IsDisposed)
                {
                    dialog.DialogResult = DialogResult.Cancel;
                    dialog.Close();
                }
            };
            dialog.Shown += (_, _) =>
            {
                clickTimer.Start();
                closeTimer.Start();
            };
            dialog.ShowDialog(host);
            clickTimer.Dispose();
            closeTimer.Dispose();
            if (!callbackCompleted)
            {
                failure = new InvalidOperationException(
                    "Overlay settings modal closed while area selection was still running.");
            }
            host.Close();
        });
        Application.Run(host);
        if (failure is not null)
        {
            throw failure;
        }
        Console.WriteLine("OVERLAY SETTINGS LIFETIME: OK");

        static IEnumerable<Control> DescendantsAndSelf(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
            {
                foreach (var descendant in DescendantsAndSelf(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static void RunTrackerRenderTest(string outputPath)
    {
        using var catalog = new ItemIconCatalogService(ApplicationDataPaths.RootDirectory);
        var profile = new TrackerProfile
        {
            Name = "Diagnostic",
            Settings = new AppSettings
            {
                OverlayPlacement = OverlayPlacement.Right,
                ShowItemsOverlay = true
            }
        };
        using var form = new Form
        {
            Text = "Tracker diagnostic",
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(100, 80, 1000, 700),
            ShowInTaskbar = false
        };
        form.Controls.Add(new TrackerView(profile, catalog, () => { }));
        CaptureFormForDiagnostic(form, outputPath);
    }

    private static void CaptureFormForDiagnostic(Form form, string outputPath)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            using var screenshot = new Bitmap(
                form.Width,
                form.Height,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(screenshot))
            {
                graphics.CopyFromScreen(form.Left, form.Top, 0, 0, screenshot.Size);
            }
            screenshot.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            form.Close();
        };
        form.Shown += (_, _) =>
        {
            form.TopMost = true;
            form.Activate();
            form.BringToFront();
            timer.Start();
        };
        Application.Run(form);
        timer.Dispose();
    }

    private static void RunWindowUnavailableTest()
    {
        using var target = new Form
        {
            Text = "Minimize diagnostic target",
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(100, 100, 640, 480),
            ShowInTaskbar = false
        };
        target.Show();
        Application.DoEvents();

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var settings = new AppSettings
        {
            TargetProcessName = process.ProcessName,
            ReferenceWindowWidth = target.Width,
            ReferenceWindowHeight = target.Height
        };
        var handle = target.Handle;
        target.WindowState = FormWindowState.Minimized;
        Application.DoEvents();

        var resolved = ScreenCaptureService.ResolveWindow(settings, handle)
            ?? throw new InvalidOperationException("A minimized preferred window was lost.");
        if (resolved.Handle != handle || !resolved.IsMinimized)
        {
            throw new InvalidOperationException("The minimized preferred window state was not preserved.");
        }

        try
        {
            using var _ = ScreenCaptureService.CaptureWindowRegion(
                handle,
                new Rectangle(0, 0, 320, 160),
                new Size(target.Width, target.Height));
            throw new InvalidOperationException("A minimized capture should have been paused.");
        }
        catch (WindowCaptureUnavailableException)
        {
            // Expected: the monitoring loop keeps running and retries later.
        }

        target.Close();
        Console.WriteLine("WINDOW UNAVAILABLE: minimized handle retained; capture paused without a fatal error");
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
            ["Key Imprint"] = "Key Imprint",
            ["Pure Mana Crystall"] = "Pure Mana Crystal",
            ["Pure Mana CrystaI"] = "Pure Mana Crystal"
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

    private static void RunFrameMotionTest()
    {
        static Bitmap CreateFrame(int offset)
        {
            var bitmap = new Bitmap(360, 160, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(24, 27, 25));
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            using var font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel);
            using var white = new SolidBrush(Color.FromArgb(217, 205, 183));
            using var yellow = new SolidBrush(Color.FromArgb(235, 220, 20));
            using var green = new SolidBrush(Color.FromArgb(80, 220, 95));
            var lines = new[]
            {
                ("Power of the spirits enabled.", white),
                ("Kelias landed a critical hit!", green),
                ("You have acquired 600 XP and 42 SP.", white),
                ("You have obtained 123 Adena.", yellow),
                ("Use Soulshot (D-Grade).", white)
            };
            for (var index = 0; index < lines.Length; index++)
            {
                graphics.DrawString(lines[index].Item1, font, lines[index].Item2, 8, 24 + index * 20 + offset);
            }
            return bitmap;
        }

        static Bitmap CreateGrayOnlyFrame(int offset)
        {
            var bitmap = new Bitmap(360, 160, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(24, 27, 25));
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            using var font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel);
            using var gray = new SolidBrush(Color.FromArgb(185, 185, 185));
            var lines = new[]
            {
                "Power of the spirits enabled.",
                "Use Soulshot (D-Grade).",
                "You have acquired 600 XP and 42 SP.",
                "Power of the spirits enabled."
            };
            for (var index = 0; index < lines.Length; index++)
            {
                graphics.DrawString(lines[index], font, gray, 8, 35 + index * 20 + offset);
            }
            return bitmap;
        }

        static Bitmap CreateRepeatedExperienceFrame(bool appendNew)
        {
            var bitmap = new Bitmap(360, 160, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(24, 27, 25));
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            using var font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel);
            using var gray = new SolidBrush(Color.FromArgb(185, 185, 185));
            var offset = appendNew ? -15 : 0;
            var lines = new[]
            {
                "Power of the spirits enabled.",
                "Use Soulshot (D-Grade).",
                "You have acquired 2000 XP and 160 SP.",
                "Power of the spirits enabled."
            };
            for (var index = 0; index < lines.Length; index++)
            {
                graphics.DrawString(lines[index], font, gray, 8, 35 + index * 20 + offset);
            }
            if (appendNew)
            {
                graphics.DrawString(
                    "You have acquired 2000 XP and 160 SP.",
                    font,
                    gray,
                    8,
                    115);
            }
            return bitmap;
        }

        static Bitmap CreatePureManaCrystalFrame()
        {
            var bitmap = new Bitmap(360, 80, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(24, 27, 25));
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            using var font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel);
            using var yellow = new SolidBrush(Color.FromArgb(235, 220, 20));
            graphics.DrawString("You have obtained Pure Mana Crystal.", font, yellow, 8, 35);
            return bitmap;
        }

        using var first = CreateFrame(0);
        using var moved = CreateFrame(-15);
        var detector = new ChatFrameMotionDetector();
        detector.Observe(first);
        var shift = detector.Observe(moved);
        if (Math.Abs(shift + 15) > 2)
        {
            throw new InvalidOperationException(
                $"Expected visual chat shift near -15, got {shift} (confidence {detector.LastConfidence:F2}).");
        }

        var stationaryDetector = new ChatFrameMotionDetector();
        stationaryDetector.Observe(first);
        var stationary = stationaryDetector.Observe(first);
        if (stationary != 0)
        {
            throw new InvalidOperationException($"Stationary chat was reported as shift {stationary}.");
        }

        using var grayFirst = CreateGrayOnlyFrame(0);
        using var grayMoved = CreateGrayOnlyFrame(-15);
        var grayDetector = new ChatFrameMotionDetector();
        grayDetector.Observe(grayFirst);
        var grayShift = grayDetector.Observe(grayMoved);
        var grayLineBounds = OcrImagePreprocessor.FindLineBounds(grayFirst, TextMask.White);
        using var grayOcr = new OcrService(ApplicationDataPaths.RootDirectory);
        var grayExperience = grayOcr.ReadEvents(grayFirst)
            .SingleOrDefault(item => item.Kind == DetectedEventKind.Experience);
        if (Math.Abs(grayShift + 15) > 2
            || grayLineBounds.Count == 0
            || grayExperience?.Xp != 600
            || grayExperience.Sp != 42)
        {
            throw new InvalidOperationException(
                $"Gray XP/SP text was not tracked: shift={grayShift}, lines={grayLineBounds.Count}, " +
                $"event={grayExperience?.Value ?? "none"}, confidence={grayDetector.LastConfidence:F2}.");
        }

        using var repeatedFirst = CreateRepeatedExperienceFrame(false);
        using var repeatedCurrent = CreateRepeatedExperienceFrame(true);
        var repeatedDetector = new ChatFrameMotionDetector();
        repeatedDetector.Observe(repeatedFirst);
        var repeatedShift = repeatedDetector.Observe(repeatedCurrent);
        var firstExperienceEvents = grayOcr.ReadEvents(repeatedFirst);
        var currentExperienceEvents = grayOcr.ReadEvents(repeatedCurrent);
        var repeatedTracker = new EventSequenceTracker();
        repeatedTracker.SetBaselineImmediately(firstExperienceEvents);
        var newlyAccepted = repeatedTracker.Observe(
            currentExperienceEvents,
            repeatedShift,
            repeatedDetector.LastNewLineBands);
        if (Math.Abs(repeatedShift + 15) > 2
            || repeatedDetector.LastNewLineBands.Count == 0
            || currentExperienceEvents.Count(item => item.Kind == DetectedEventKind.Experience) != 2
            || newlyAccepted.Count(item => item.Kind == DetectedEventKind.Experience) != 1)
        {
            throw new InvalidOperationException(
                $"Repeated XP row failed: shift={repeatedShift}, bands={repeatedDetector.LastNewLineBands.Count}, " +
                $"visible events={currentExperienceEvents.Count}, accepted={newlyAccepted.Count}.");
        }

        using var pureManaFrame = CreatePureManaCrystalFrame();
        var pureMana = grayOcr.ReadEvents(pureManaFrame)
            .SingleOrDefault(item => item.Kind == DetectedEventKind.Drop);
        if (!string.Equals(pureMana?.SummaryName, "Pure Mana Crystal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Pure Mana Crystal OCR failed: {pureMana?.RawText ?? "none"}.");
        }

        Console.WriteLine(
            $"FRAME MOTION: colored={shift}; gray={grayShift}; " +
            $"repeated XP accepted={newlyAccepted.Count}; Pure Mana Crystal=OK; stationary=0");
    }

    private static void RunSequenceTest()
    {
        static void RequireCount(string name, IReadOnlyList<DetectedEvent> actual, int expected)
        {
            if (actual.Count != expected)
            {
                throw new InvalidOperationException($"{name}: expected {expected}, got {actual.Count}.");
            }
        }

        var tracker = new EventSequenceTracker();
        static DetectedEvent Positioned(string value, int top) => new(
            DetectedEventKind.Drop, value, value, top, value, 1, 0, 0, 0);
        static Rectangle Band(int top) => new(0, top, 360, 16);

        tracker.SetBaselineImmediately(new[] { Positioned("A", 100), Positioned("B", 120) });
        RequireCount(
            "stationary OCR change is ignored",
            tracker.Observe(
                new[] { Positioned("A", 100), Positioned("B", 120), Positioned("artifact", 140) },
                0,
                []),
            0);

        tracker.SetBaselineImmediately(new[] { Positioned("A", 100), Positioned("B", 120) });
        RequireCount(
            "background shift without a new physical row is ignored",
            tracker.Observe(
                new[] { Positioned("A", 100), Positioned("B", 120), Positioned("artifact", 140) },
                -15,
                []),
            0);

        tracker.SetBaselineImmediately(new[] { Positioned("A", 100), Positioned("B", 120) });
        RequireCount(
            "new row during upward chat advancement",
            tracker.Observe(
                new[] { Positioned("A", 85), Positioned("B", 105), Positioned("C", 125) },
                -15,
                [Band(125)]),
            1);
        RequireCount(
            "stationary frame does not replay accepted row",
            tracker.Observe(
                new[] { Positioned("A", 85), Positioned("B", 105), Positioned("C", 125) },
                0,
                []),
            0);

        tracker.SetBaselineImmediately(new[] { Positioned("A", 100) });
        RequireCount(
            "consecutive identical row is a separate instance",
            tracker.Observe(
                new[] { Positioned("A", 85), Positioned("A", 105) },
                -15,
                [Band(105)]),
            1);

        tracker.SetBaselineImmediately(new[] { Positioned("XP 600", 100), Positioned("Adena 123", 120) });
        RequireCount(
            "two identical loot pairs during one upward movement",
            tracker.Observe(
                new[]
                {
                    Positioned("XP 600", 85),
                    Positioned("Adena 123", 105),
                    Positioned("XP 600", 125),
                    Positioned("Adena 123", 145)
                },
                -15,
                [Band(125), Band(145)]),
            2);

        tracker.SetBaselineImmediately(Array.Empty<DetectedEvent>());
        RequireCount(
            "new physical row is retained when first OCR attempt fails",
            tracker.Observe(
                [],
                -15,
                [Band(140)]),
            0);
        RequireCount(
            "retained row accepts a later OCR result",
            tracker.Observe(
                new[] { Positioned("C", 140) },
                0,
                []),
            1);

        tracker.SetBaselineImmediately(Array.Empty<DetectedEvent>());
        RequireCount(
            "pending row follows the next upward movement",
            tracker.Observe(
                [],
                -15,
                [Band(140)]),
            0);
        RequireCount(
            "moved pending row accepts delayed OCR",
            tracker.Observe(
                new[] { Positioned("C", 125) },
                -15,
                []),
            1);

        var experience = new DetectedEvent(
            DetectedEventKind.Experience,
            "600 XP, 42 SP",
            "You have acquired 600 XP and 42 SP.",
            140,
            string.Empty,
            0,
            600,
            42,
            0);
        var damagedExperience = OcrService.ParseDiagnosticText(
            "You have acguired 2,000 XP ancl 160 SP.",
            TextMask.White);
        if (damagedExperience?.Xp != 2000 || damagedExperience.Sp != 160)
        {
            throw new InvalidOperationException(
                $"Damaged XP OCR text was not recovered: {damagedExperience?.Value ?? "none"}.");
        }
        tracker.SetBaselineImmediately(Array.Empty<DetectedEvent>());
        RequireCount(
            "gray XP/SP without yellow loot",
            tracker.Observe(
                new[] { experience },
                -15,
                [Band(140)]),
            1);

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
