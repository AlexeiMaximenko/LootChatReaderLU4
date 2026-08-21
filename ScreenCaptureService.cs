using System.Drawing.Imaging;

namespace LootChatReader;

internal static class ScreenCaptureService
{
    public static Bitmap Capture(Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(region));
        }

        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }
}
