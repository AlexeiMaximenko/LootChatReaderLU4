using System.Text.Json;

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

    public Rectangle CaptureRegion => new(CaptureX, CaptureY, CaptureWidth, CaptureHeight);

    public bool HasCaptureRegion => CaptureWidth >= 80
        && CaptureHeight >= 30
        && ReferenceWindowWidth > 0
        && ReferenceWindowHeight > 0
        && TargetProcessName.Length > 0;

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
