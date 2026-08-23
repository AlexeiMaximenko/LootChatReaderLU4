using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LootChatReader;

internal enum TextMask
{
    Yellow,
    White
}

internal static class OcrImagePreprocessor
{
    public const int Scale = 4;

    public static Bitmap CreateMask(Bitmap source, TextMask textMask, bool relaxed = false)
    {
        using var normalized = Ensure24Bit(source);
        using var mask = BuildBinaryMask(normalized, textMask, relaxed);

        var scaled = new Bitmap(mask.Width * Scale, mask.Height * Scale, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(scaled);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(mask, new Rectangle(0, 0, scaled.Width, scaled.Height));
        return scaled;
    }

    public static Bitmap EnlargeOriginal(Bitmap source)
    {
        var scaled = new Bitmap(source.Width * Scale, source.Height * Scale, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(scaled);
        graphics.Clear(Color.Black);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, scaled.Width, scaled.Height));
        return scaled;
    }

    public static IReadOnlyList<Rectangle> FindLineBounds(
        Bitmap source,
        TextMask textMask,
        bool relaxed = false)
    {
        using var normalized = Ensure24Bit(source);
        var bounds = new Rectangle(0, 0, normalized.Width, normalized.Height);
        var data = normalized.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var activeRows = new bool[normalized.Height];

        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * normalized.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var y = 0; y < normalized.Height; y++)
            {
                var pixels = 0;
                for (var x = 0; x < normalized.Width; x++)
                {
                    var index = y * data.Stride + x * 3;
                    var blue = bytes[index];
                    var green = bytes[index + 1];
                    var red = bytes[index + 2];
                    var isText = textMask == TextMask.Yellow
                        ? IsYellow(red, green, blue, relaxed)
                        : IsWhite(red, green, blue);
                    if (isText && ++pixels >= 4)
                    {
                        activeRows[y] = true;
                        break;
                    }
                }
            }
        }
        finally
        {
            normalized.UnlockBits(data);
        }

        const int maximumGap = 2;
        var result = new List<Rectangle>();
        var start = -1;
        var lastActive = -1;
        for (var y = 0; y <= activeRows.Length; y++)
        {
            if (y == activeRows.Length)
            {
                if (start >= 0)
                {
                    AddLineBounds(result, normalized.Size, start, lastActive);
                }

                break;
            }

            if (activeRows[y])
            {
                start = start < 0 ? y : start;
                lastActive = y;
                continue;
            }

            if (start < 0 || y - lastActive <= maximumGap)
            {
                continue;
            }

            AddLineBounds(result, normalized.Size, start, lastActive);
            start = -1;
            lastActive = -1;
        }

        return result;
    }

    private static void AddLineBounds(ICollection<Rectangle> destination, Size imageSize, int top, int bottom)
    {
        if (bottom - top < 2)
        {
            return;
        }

        destination.Add(Rectangle.FromLTRB(
            0,
            Math.Max(0, top - 2),
            imageSize.Width,
            Math.Min(imageSize.Height, bottom + 3)));
    }

    private static Bitmap Ensure24Bit(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImageUnscaled(source, 0, 0);
        return result;
    }

    private static Bitmap BuildBinaryMask(Bitmap source, TextMask textMask, bool relaxed)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        var bounds = new Rectangle(0, 0, source.Width, source.Height);
        var sourceData = source.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var resultData = result.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        try
        {
            var sourceBytes = new byte[Math.Abs(sourceData.Stride) * source.Height];
            var resultBytes = new byte[Math.Abs(resultData.Stride) * result.Height];
            Marshal.Copy(sourceData.Scan0, sourceBytes, 0, sourceBytes.Length);
            Array.Fill(resultBytes, (byte)255);

            for (var y = 0; y < source.Height; y++)
            {
                for (var x = 0; x < source.Width; x++)
                {
                    var sourceIndex = y * sourceData.Stride + x * 3;
                    var resultIndex = y * resultData.Stride + x * 3;
                    var blue = sourceBytes[sourceIndex];
                    var green = sourceBytes[sourceIndex + 1];
                    var red = sourceBytes[sourceIndex + 2];

                    var isText = textMask == TextMask.Yellow
                        ? IsYellow(red, green, blue, relaxed)
                        : IsWhite(red, green, blue);

                    if (!isText)
                    {
                        continue;
                    }

                    resultBytes[resultIndex] = 0;
                    resultBytes[resultIndex + 1] = 0;
                    resultBytes[resultIndex + 2] = 0;
                }
            }

            Marshal.Copy(resultBytes, 0, resultData.Scan0, resultBytes.Length);
        }
        finally
        {
            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);
        }

        return result;
    }

    private static bool IsYellow(byte red, byte green, byte blue, bool relaxed)
    {
        if (relaxed)
        {
            // Recovers dim anti-aliased yellow pixels without accepting LU4's warm
            // beige normal chat text.
            return red >= 140
                && green >= 115
                && blue <= 175
                && red - blue >= 35
                && green - blue >= 25;
        }

        return red >= 165
            && green >= 135
            && blue <= 155
            && red - blue >= 55
            && green - blue >= 40;
    }

    private static bool IsWhite(byte red, byte green, byte blue)
    {
        // LU4's visually white text is actually warm beige:
        // the normal color is near RGB(217,205,183), highlighted is RGB(255,242,194).
        // Channel ratio checks reject white highlights from the game background.
        var redGreen = red - green;
        var greenBlue = green - blue;
        var warmBeige = red >= 175
            && green >= 160
            && blue >= 130
            && redGreen >= 5
            && redGreen <= 32
            && greenBlue >= 8
            && greenBlue <= 65;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var neutralGray = minimum >= 145 && maximum - minimum <= 24;
        return warmBeige || neutralGray;
    }
}
