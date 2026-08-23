namespace LootChatReader;

internal sealed class MainForm : Form
{
    private readonly WorkspaceState _workspace;
    private readonly ItemIconCatalogService _iconCatalog;
    private readonly Icon? _applicationIcon;
    private readonly TabControl _profileTabs = new();
    private readonly TabPage _addPage = new("+");
    private readonly ContextMenuStrip _tabMenu = new();
    private TabPage? _lastProfilePage;
    private bool _handlingAdd;
    private bool _closing;

    public MainForm()
    {
        ApplicationDataPaths.EnsureRootDirectory();
        _workspace = WorkspaceState.Load(
            ApplicationDataPaths.WorkspacePath,
            ApplicationDataPaths.SettingsPath);
        _iconCatalog = new ItemIconCatalogService(ApplicationDataPaths.RootDirectory);
        _applicationIcon = EmbeddedResourceFiles.LoadIcon("LootChatReader.Resources.app.ico");

        Text = $"LU4 Loot Chat Reader v{AppVersion.Display}";
        if (_applicationIcon is not null)
        {
            Icon = _applicationIcon;
        }

        MinimumSize = new Size(800, 540);
        Size = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        BuildInterface();
    }

    private void BuildInterface()
    {
        _profileTabs.Dock = DockStyle.Fill;
        _profileTabs.Padding = new Point(16, 5);
        _profileTabs.SelectedIndexChanged += ProfileTabsOnSelectedIndexChanged;
        _profileTabs.MouseDoubleClick += ProfileTabsOnMouseDoubleClick;
        _profileTabs.MouseUp += ProfileTabsOnMouseUp;

        foreach (var profile in _workspace.Profiles)
        {
            AddProfilePage(profile);
        }

        _profileTabs.TabPages.Add(_addPage);
        var selected = _profileTabs.TabPages.Cast<TabPage>()
            .FirstOrDefault(page => (page.Tag as TrackerProfile)?.Id == _workspace.SelectedProfileId)
            ?? _profileTabs.TabPages[0];
        _profileTabs.SelectedTab = selected;
        _lastProfilePage = selected;

        _tabMenu.Items.Add("Rename", null, (_, _) => RenameSelectedProfile());
        _tabMenu.Items.Add("Delete", null, (_, _) => DeleteSelectedProfile());

        Controls.Add(_profileTabs);
    }

    private TabPage AddProfilePage(TrackerProfile profile)
    {
        var page = new TabPage(profile.Name)
        {
            Tag = profile,
            Padding = new Padding(0)
        };
        page.Controls.Add(new TrackerView(profile, _iconCatalog, SaveWorkspace));

        var addIndex = _profileTabs.TabPages.IndexOf(_addPage);
        if (addIndex >= 0)
        {
            _profileTabs.TabPages.Insert(addIndex, page);
        }
        else
        {
            _profileTabs.TabPages.Add(page);
        }

        return page;
    }

    private void ProfileTabsOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_profileTabs.SelectedTab == _addPage)
        {
            if (!_handlingAdd)
            {
                BeginInvoke(AddNewProfile);
            }
            return;
        }

        _lastProfilePage = _profileTabs.SelectedTab;
        if (_profileTabs.SelectedTab?.Tag is TrackerProfile profile)
        {
            _workspace.SelectedProfileId = profile.Id;
            SaveWorkspace();
        }
    }

    private void AddNewProfile()
    {
        if (_handlingAdd || IsDisposed)
        {
            return;
        }

        _handlingAdd = true;
        try
        {
            using var prompt = new NamePromptForm("New tracker", "New tracker");
            if (prompt.ShowDialog(this) != DialogResult.OK)
            {
                _profileTabs.SelectedTab = _lastProfilePage ?? _profileTabs.TabPages[0];
                return;
            }

            var profile = new TrackerProfile { Name = MakeUniqueName(prompt.EnteredName) };
            _workspace.Profiles.Add(profile);
            var page = AddProfilePage(profile);
            _profileTabs.SelectedTab = page;
            _lastProfilePage = page;
            _workspace.SelectedProfileId = profile.Id;
            SaveWorkspace();
        }
        finally
        {
            _handlingAdd = false;
        }
    }

    private void ProfileTabsOnMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        var page = FindProfilePageAt(e.Location);
        if (page is null)
        {
            return;
        }

        _profileTabs.SelectedTab = page;
        RenameSelectedProfile();
    }

    private void ProfileTabsOnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        var page = FindProfilePageAt(e.Location);
        if (page is null)
        {
            return;
        }

        _profileTabs.SelectedTab = page;
        _tabMenu.Show(_profileTabs, e.Location);
    }

    private TabPage? FindProfilePageAt(Point location)
    {
        for (var index = 0; index < _profileTabs.TabPages.Count - 1; index++)
        {
            if (_profileTabs.GetTabRect(index).Contains(location))
            {
                return _profileTabs.TabPages[index];
            }
        }

        return null;
    }

    private void RenameSelectedProfile()
    {
        if (_profileTabs.SelectedTab?.Tag is not TrackerProfile profile)
        {
            return;
        }

        using var prompt = new NamePromptForm("Rename tracker", profile.Name);
        if (prompt.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        profile.Name = MakeUniqueName(prompt.EnteredName, profile.Id);
        _profileTabs.SelectedTab.Text = profile.Name;
        SaveWorkspace();
    }

    private void DeleteSelectedProfile()
    {
        if (_profileTabs.SelectedTab is not { Tag: TrackerProfile profile } page)
        {
            return;
        }

        if (_workspace.Profiles.Count == 1)
        {
            MessageBox.Show(this, "At least one tracker tab must remain.", "Delete Tracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                this,
                $"Delete the tracker '{profile.Name}' and all of its history?",
                "Delete Tracker",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        if (page.Controls.OfType<TrackerView>().FirstOrDefault() is { } tracker)
        {
            tracker.StopForRemoval();
        }

        var index = _profileTabs.TabPages.IndexOf(page);
        _profileTabs.TabPages.Remove(page);
        page.Dispose();
        _workspace.Profiles.Remove(profile);
        var nextIndex = Math.Clamp(index - 1, 0, _profileTabs.TabPages.Count - 2);
        _profileTabs.SelectedIndex = nextIndex;
        SaveWorkspace();
    }

    private string MakeUniqueName(string requestedName, Guid? existingId = null)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? "Tracker" : requestedName.Trim();
        var candidate = baseName;
        var suffix = 2;
        while (_workspace.Profiles.Any(profile => profile.Id != existingId
            && profile.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix++}";
        }

        return candidate;
    }

    private void SaveWorkspace()
    {
        try
        {
            _workspace.Save(ApplicationDataPaths.WorkspacePath);
        }
        catch
        {
            // Active monitoring remains usable if settings cannot be persisted.
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closing)
        {
            _closing = true;
            foreach (var tracker in _profileTabs.TabPages.Cast<TabPage>()
                         .SelectMany(page => page.Controls.OfType<TrackerView>()))
            {
                tracker.PrepareForShutdown();
            }

            SaveWorkspace();
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tabMenu.Dispose();
            _applicationIcon?.Dispose();
            _iconCatalog.Dispose();
        }

        base.Dispose(disposing);
    }
}
