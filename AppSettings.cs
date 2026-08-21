using System.Text.Json;

namespace LootChatReader;

internal sealed class AppSettings
{
    public int CaptureX { get; set; }
    public int CaptureY { get; set; }
    public int CaptureWidth { get; set; }
    public int CaptureHeight { get; set; }

    public Rectangle CaptureRegion => new(CaptureX, CaptureY, CaptureWidth, CaptureHeight);

    public bool HasCaptureRegion => CaptureWidth >= 80 && CaptureHeight >= 30;

    public void SetCaptureRegion(Rectangle region)
    {
        CaptureX = region.X;
        CaptureY = region.Y;
        CaptureWidth = region.Width;
        CaptureHeight = region.Height;
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
