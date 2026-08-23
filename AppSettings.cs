using System.Text.Json;
using System.Text.Json.Serialization;

namespace LootChatReader;

internal sealed class AppSettings
{
    public int CaptureX { get; set; }
    public int CaptureY { get; set; }
    public int CaptureWidth { get; set; }
    public int CaptureHeight { get; set; }
    public int ReferenceWindowWidth { get; set; }
    public int ReferenceWindowHeight { get; set; }
    public string TargetProcessName { get; set; } = string.Empty;
    public string TargetWindowTitle { get; set; } = string.Empty;
    public string TargetWindowClass { get; set; } = string.Empty;
    public OverlayPlacement OverlayPlacement { get; set; } = OverlayPlacement.Off;
    public bool ShowItemsOverlay { get; set; }
    public bool ShowQuestItemsOverlay { get; set; }
    public int ItemsOverlayX { get; set; }
    public int ItemsOverlayY { get; set; }
    public int ItemsOverlayWidth { get; set; } = 320;
    public int ItemsOverlayHeight { get; set; } = 250;
    public bool ItemsOverlayRegionSet { get; set; }
    public int QuestItemsOverlayX { get; set; }
    public int QuestItemsOverlayY { get; set; }
    public int QuestItemsOverlayWidth { get; set; } = 320;
    public int QuestItemsOverlayHeight { get; set; } = 250;
    public bool QuestItemsOverlayRegionSet { get; set; }

    [JsonIgnore]
    public Rectangle CaptureRegion => new(CaptureX, CaptureY, CaptureWidth, CaptureHeight);

    [JsonIgnore]
    public Rectangle ItemsOverlayRegion => new(
        ItemsOverlayX,
        ItemsOverlayY,
        ItemsOverlayWidth,
        ItemsOverlayHeight);

    [JsonIgnore]
    public Rectangle QuestItemsOverlayRegion => new(
        QuestItemsOverlayX,
        QuestItemsOverlayY,
        QuestItemsOverlayWidth,
        QuestItemsOverlayHeight);

    [JsonIgnore]
    public bool HasCaptureRegion => CaptureWidth >= 80
        && CaptureHeight >= 30
        && ReferenceWindowWidth > 0
        && ReferenceWindowHeight > 0
        && TargetProcessName.Length > 0
        && TargetWindowTitle.Length > 0;

    public void SetCaptureTarget(WindowDescriptor window, Rectangle relativeRegion)
    {
        CaptureX = relativeRegion.X;
        CaptureY = relativeRegion.Y;
        CaptureWidth = relativeRegion.Width;
        CaptureHeight = relativeRegion.Height;
        ReferenceWindowWidth = window.Bounds.Width;
        ReferenceWindowHeight = window.Bounds.Height;
        TargetProcessName = window.ProcessName;
        TargetWindowTitle = window.Title;
        TargetWindowClass = window.ClassName;
    }

    public void SetItemsOverlayRegion(Rectangle relativeRegion)
    {
        ItemsOverlayX = relativeRegion.X;
        ItemsOverlayY = relativeRegion.Y;
        ItemsOverlayWidth = relativeRegion.Width;
        ItemsOverlayHeight = relativeRegion.Height;
        ItemsOverlayRegionSet = true;
    }

    public void SetQuestItemsOverlayRegion(Rectangle relativeRegion)
    {
        QuestItemsOverlayX = relativeRegion.X;
        QuestItemsOverlayY = relativeRegion.Y;
        QuestItemsOverlayWidth = relativeRegion.Width;
        QuestItemsOverlayHeight = relativeRegion.Height;
        QuestItemsOverlayRegionSet = true;
    }

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt or unavailable settings must not prevent startup.
        }

        return new AppSettings();
    }

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch
        {
            // The selected area remains active until this application run ends.
        }
    }
}

internal enum OverlayPlacement
{
    Off,
    Left,
    Top,
    Right,
    Bottom
}
