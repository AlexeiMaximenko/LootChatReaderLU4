using System.Text.Json;
using System.Text.Json.Serialization;

namespace LootChatReader;

internal sealed class WorkspaceState
{
    public int SchemaVersion { get; set; } = 1;
    public Guid SelectedProfileId { get; set; }
    public List<TrackerProfile> Profiles { get; set; } = [];

    public static WorkspaceState Load(string path, string legacySettingsPath)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(path));
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch
        {
            // A damaged workspace must not prevent the application from starting.
        }

        var legacySettings = AppSettings.Load(legacySettingsPath);
        var first = new TrackerProfile
        {
            Name = "Main",
            Settings = legacySettings
        };
        return new WorkspaceState
        {
            SelectedProfileId = first.Id,
            Profiles = [first]
        };
    }

    public void Save(string path)
    {
        Normalize();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        File.Move(temporaryPath, path, true);
    }

    private void Normalize()
    {
        Profiles ??= [];
        foreach (var profile in Profiles)
        {
            profile.Normalize();
        }

        if (Profiles.Count == 0)
        {
            Profiles.Add(new TrackerProfile { Name = "Main" });
        }

        if (Profiles.All(profile => profile.Id != SelectedProfileId))
        {
            SelectedProfileId = Profiles[0].Id;
        }
    }
}

internal sealed class TrackerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Main";
    public AppSettings Settings { get; set; } = new();
    public List<TrackingHistory> Histories { get; set; } = [];

    public void Normalize()
    {
        if (Id == Guid.Empty)
        {
            Id = Guid.NewGuid();
        }

        Name = string.IsNullOrWhiteSpace(Name) ? "Tracker" : Name.Trim();
        Settings ??= new AppSettings();
        Histories ??= [];
    }
}

internal sealed class TrackingHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public long ElapsedTicks { get; set; }
    public long Adena { get; set; }
    public long Xp { get; set; }
    public long Sp { get; set; }
    public List<HistoryItem> Items { get; set; } = [];
    public List<HistoryItem> QuestItems { get; set; } = [];
    public List<HistoryLogEntry> Logs { get; set; } = [];

    [JsonIgnore]
    public TimeSpan Elapsed => TimeSpan.FromTicks(Math.Max(0, ElapsedTicks));

    [JsonIgnore]
    public string DisplayName => $"{StartedAt:dd.MM.yyyy HH:mm:ss} - {EndedAt:dd.MM.yyyy HH:mm:ss}";
}

internal sealed class HistoryItem
{
    public string Name { get; set; } = string.Empty;
    public long Total { get; set; }
}

internal sealed class HistoryLogEntry
{
    public DateTime Time { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string RawText { get; set; } = string.Empty;
    public string SummaryName { get; set; } = string.Empty;
}
