using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LootChatReader;

/// <summary>
/// Estimates the vertical movement of the whole chat using colored text pixels,
/// including combat lines that are intentionally not returned by OCR. This lets
/// identical loot rows be distinguished when an old row scrolls out and an
/// identical new row takes its place.
/// </summary>
internal sealed class ChatFrameMotionDetector
{
    private const int MaximumShift = 96;
    private const int MinimumMovement = 4;
    private const int MinimumIntersection = 36;
    private const double MinimumScore = 0.45;
    private const double MinimumImprovementOverStationary = 0.06;

    private byte[]? _previousMask;
    private int _width;
    private int _height;

    public double LastConfidence { get; private set; }

    /// <returns>
    /// Current text position minus its previous position. Negative means normal
    /// chat advancement (content moved up); positive means the viewport moved up.
    /// </returns>
    public int Observe(Bitmap frame)
    {
        var currentMask = BuildTextMask(frame);
        if (_previousMask is null || _width != frame.Width || _height != frame.Height)
        {
            _previousMask = currentMask;
            _width = frame.Width;
            _height = frame.Height;
            LastConfidence = 0;
            return 0;
        }

        var stationary = ScoreShift(_previousMask, currentMask, _width, _height, 0);
        var best = stationary;
        var bestShift = 0;
        for (var shift = -MaximumShift; shift <= MaximumShift; shift++)
        {
            if (shift == 0)
            {
                continue;
            }

            var score = ScoreShift(_previousMask, currentMask, _width, _height, shift);
            if (score.Score > best.Score + 0.001
                || (Math.Abs(score.Score - best.Score) <= 0.001
                    && Math.Abs(shift) < Math.Abs(bestShift)))
            {
                best = score;
                bestShift = shift;
            }
        }

        _previousMask = currentMask;
        LastConfidence = best.Score;
        if (Math.Abs(bestShift) < MinimumMovement
            || best.Intersection < MinimumIntersection
            || best.Score < MinimumScore
            || best.Score < stationary.Score + MinimumImprovementOverStationary)
        {
            return 0;
        }

        return bestShift;
    }

    public void Reset()
    {
        _previousMask = null;
        _width = 0;
        _height = 0;
        LastConfidence = 0;
    }

    private static MotionScore ScoreShift(
        byte[] previous,
        byte[] current,
        int width,
        int height,
        int shift)
    {
        var previousStart = Math.Max(0, -shift);
        var previousEnd = Math.Min(height, height - shift);
        var previousCount = 0;
        var currentCount = 0;
        var intersection = 0;
        for (var previousY = previousStart; previousY < previousEnd; previousY++)
        {
            var currentY = previousY + shift;
            var previousOffset = previousY * width;
            var currentOffset = currentY * width;
            for (var x = 4; x < width - 4; x += 2)
            {
                var wasText = previous[previousOffset + x] != 0;
                var isText = current[currentOffset + x] != 0;
                if (wasText)
                {
                    previousCount++;
                }
                if (isText)
                {
                    currentCount++;
                }
                if (wasText && isText)
                {
                    intersection++;
                }
            }
        }

        var total = previousCount + currentCount;
        return new MotionScore(total == 0 ? 0 : 2D * intersection / total, intersection);
    }

    private static byte[] BuildTextMask(Bitmap source)
    {
        using var normalized = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(normalized))
        {
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        var rectangle = new Rectangle(0, 0, normalized.Width, normalized.Height);
        var data = normalized.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * normalized.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var mask = new byte[normalized.Width * normalized.Height];
            for (var y = 3; y < normalized.Height - 3; y++)
            {
                for (var x = 3; x < normalized.Width - 3; x++)
                {
                    var index = y * data.Stride + x * 3;
                    var blue = bytes[index];
                    var green = bytes[index + 1];
                    var red = bytes[index + 2];
                    if (LooksLikeChatText(red, green, blue))
                    {
                        mask[y * normalized.Width + x] = 1;
                    }
                }
            }
            return mask;
        }
        finally
        {
            normalized.UnlockBits(data);
        }
    }

    private static bool LooksLikeChatText(byte red, byte green, byte blue)
    {
        var warmWhite = red >= 165
            && green >= 145
            && blue >= 115
            && red - green is >= 3 and <= 38
            && green - blue is >= 6 and <= 75;
        var yellow = red >= 135
            && green >= 105
            && blue <= 180
            && red - blue >= 32
            && green - blue >= 20;
        var greenText = green >= 125
            && green - red >= 20
            && green - blue >= 12;
        var magenta = red >= 135
            && blue >= 95
            && red - green >= 28
            && blue - green >= 18;
        return warmWhite || yellow || greenText || magenta;
    }

    private readonly record struct MotionScore(double Score, int Intersection);
}
