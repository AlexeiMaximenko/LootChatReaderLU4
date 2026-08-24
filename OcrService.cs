using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Tesseract;

namespace LootChatReader;

internal sealed partial class OcrService : IDisposable
{
    private readonly TesseractEngine _engine;

    public OcrService(string dataDirectory)
    {
        var dataPath = Path.Combine(dataDirectory, "tessdata");
        var languageFile = Path.Combine(dataPath, "eng.traineddata");
        EmbeddedResourceFiles.EnsureExtracted(
            "LootChatReader.Resources.eng.traineddata",
            languageFile);

        _engine = new TesseractEngine(dataPath, "eng", EngineMode.LstmOnly);
        _engine.SetVariable("preserve_interword_spaces", "1");
        _engine.SetVariable("user_defined_dpi", "288");
    }

    public IReadOnlyList<DetectedEvent> ReadEvents(Bitmap screenshot)
    {
        var detected = new List<DetectedEvent>();
        var yellowLineBounds = OcrImagePreprocessor.FindLineBounds(
            screenshot,
            TextMask.Yellow,
            relaxed: true);
        foreach (var bounds in yellowLineBounds)
        {
            var line = new OcrLine(string.Empty, bounds, TextMask.Yellow);
            var candidates = ReadLineCandidates(screenshot, line, includeRelaxedYellowMask: true)
                .Select(ParseLine)
                .Where(item => item is not null)
                .Cast<DetectedEvent>()
                .ToArray();
            var best = ChooseBestCandidate(candidates);
            if (best is not null)
            {
                detected.Add(best);
            }
        }

        using (var whiteMask = OcrImagePreprocessor.CreateMask(screenshot, TextMask.White))
        {
            var coarseLines = ReadLines(whiteMask, TextMask.White)
                .Where(LooksLikeExperienceEvent)
                .ToArray();
            foreach (var line in coarseLines)
            {
                var candidates = ReadLineCandidates(screenshot, line, includeRelaxedYellowMask: false)
                    .Select(ParseLine)
                    .Where(item => item is not null)
                    .Cast<DetectedEvent>()
                    .ToArray();
                var best = ChooseBestCandidate(candidates);
                if (best is not null)
                {
                    detected.Add(best);
                }
            }
        }

        return MergeDuplicateDetections(detected)
            .OrderBy(item => item.Top)
            .ToArray();
    }

    private IEnumerable<OcrLine> ReadLineCandidates(
        Bitmap screenshot,
        OcrLine coarseLine,
        bool includeRelaxedYellowMask)
    {
        if (!string.IsNullOrWhiteSpace(coarseLine.Text))
        {
            yield return coarseLine;
        }

        using var crop = CropLine(screenshot, coarseLine.Bounds);
        using var enlarged = OcrImagePreprocessor.EnlargeOriginal(crop);
        var refinedText = ReadSingleLine(enlarged);
        if (!string.IsNullOrWhiteSpace(refinedText))
        {
            yield return coarseLine with { Text = refinedText };
        }

        // The original pixels preserve character shapes, while the binary mask
        // removes a moving/animated game background. Try both representations;
        // either one can be the clearer source depending on the current scene.
        using var strictMask = OcrImagePreprocessor.CreateMask(crop, coarseLine.TextMask);
        var strictText = ReadSingleLine(strictMask);
        if (!string.IsNullOrWhiteSpace(strictText))
        {
            yield return coarseLine with { Text = strictText };
        }

        if (!includeRelaxedYellowMask)
        {
            yield break;
        }

        using var relaxedMask = OcrImagePreprocessor.CreateMask(crop, TextMask.Yellow, relaxed: true);
        var relaxedText = ReadSingleLine(relaxedMask);
        if (!string.IsNullOrWhiteSpace(relaxedText))
        {
            yield return coarseLine with { Text = relaxedText };
        }
    }

    private static bool LooksLikeYellowEvent(OcrLine line)
    {
        if (TryExtractYellowItem(line.Text, out _, out _))
        {
            return true;
        }

        // Even when the coarse pass damages the verb, refine any yellow "You have"
        // line from the original pixels before deciding that it is not an event.
        var words = WordRegex().Matches(line.Text).Cast<Match>().ToArray();
        return HasYouHavePrefix(words, words.Length);
    }

    private static bool LooksLikeExperienceEvent(OcrLine line)
    {
        return line.Text.Contains("acquired", StringComparison.OrdinalIgnoreCase)
            || (line.Text.Contains("XP", StringComparison.OrdinalIgnoreCase)
                && line.Text.Contains("SP", StringComparison.OrdinalIgnoreCase)
                && HasYouHavePrefix(
                    WordRegex().Matches(line.Text).Cast<Match>().ToArray(),
                    WordRegex().Matches(line.Text).Count));
    }

    internal IReadOnlyList<string> ReadDiagnosticLines(Bitmap screenshot)
    {
        var result = new List<string>();
        foreach (var textMask in new[] { TextMask.Yellow, TextMask.White })
        {
            using var mask = OcrImagePreprocessor.CreateMask(screenshot, textMask);
            var lines = ReadLines(mask, textMask).ToArray();
            result.AddRange(lines.Select(line => $"{textMask}\t{line.Top}\t{line.Text}"));

            foreach (var line in lines.Where(line =>
                         line.Text.Contains("acquired", StringComparison.OrdinalIgnoreCase)
                         || line.Text.Contains("obtained", StringComparison.OrdinalIgnoreCase)
                         || line.Text.Contains("earned", StringComparison.OrdinalIgnoreCase)))
            {
                using var crop = CropLine(screenshot, line.Bounds);
                using var enlarged = OcrImagePreprocessor.EnlargeOriginal(crop);
                result.Add($"TargetOriginal\t{line.Top}\t{ReadSingleLine(enlarged)}");

                using var strictMask = OcrImagePreprocessor.CreateMask(crop, TextMask.White);
                result.Add($"TargetMask\t{line.Top}\t{ReadSingleLine(strictMask)}");
            }
        }

        return result;
    }

    private IEnumerable<OcrLine> ReadLines(Bitmap mask, TextMask textMask)
    {
        using var stream = new MemoryStream();
        mask.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        using var pix = Pix.LoadFromMemory(stream.ToArray());
        using var page = _engine.Process(pix, PageSegMode.SingleBlock);
        using var iterator = page.GetIterator();

        iterator.Begin();
        do
        {
            var text = iterator.GetText(PageIteratorLevel.TextLine);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var sourceBounds = Rectangle.Empty;
            if (iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var bounds))
            {
                sourceBounds = Rectangle.FromLTRB(
                    bounds.X1 / OcrImagePreprocessor.Scale,
                    bounds.Y1 / OcrImagePreprocessor.Scale,
                    (int)Math.Ceiling((double)bounds.X2 / OcrImagePreprocessor.Scale),
                    (int)Math.Ceiling((double)bounds.Y2 / OcrImagePreprocessor.Scale));
            }

            yield return new OcrLine(NormalizeText(text), sourceBounds, textMask);
        }
        while (iterator.Next(PageIteratorLevel.TextLine));
    }

    private string ReadSingleLine(Bitmap image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        using var pix = Pix.LoadFromMemory(stream.ToArray());
        using var page = _engine.Process(pix, PageSegMode.SingleLine);
        return NormalizeText(page.GetText());
    }

    private static Bitmap CropLine(Bitmap screenshot, Rectangle bounds)
    {
        var padded = Rectangle.FromLTRB(
            0,
            Math.Max(0, bounds.Top - 2),
            screenshot.Width,
            Math.Min(screenshot.Height, bounds.Bottom + 2));
        return screenshot.Clone(padded, screenshot.PixelFormat);
    }

    private static DetectedEvent? ParseLine(OcrLine line)
    {
        if (line.TextMask == TextMask.Yellow)
        {
            if (!TryExtractYellowItem(line.Text, out var verb, out var extractedValue))
            {
                return null;
            }

            var value = CleanValue(extractedValue);
            var (summaryName, quantity) = ParseItemValue(value);
            var kind = verb.Equals("earned", StringComparison.OrdinalIgnoreCase)
                ? DetectedEventKind.QuestItem
                : DetectedEventKind.Drop;
            var adena = summaryName.Equals("adena", StringComparison.OrdinalIgnoreCase)
                ? quantity
                : 0;
            if (adena > 0)
            {
                kind = DetectedEventKind.Drop;
            }
            return value.Length == 0
                ? null
                : new DetectedEvent(kind, value, line.Text, line.Top, summaryName, quantity, 0, 0, adena);
        }

        if (!TryExtractExperience(line.Text, out var xpText, out var spText))
        {
            return null;
        }

        var xp = NormalizeNumber(xpText);
        var sp = NormalizeNumber(spText);
        if (!long.TryParse(xp, out var xpValue) || !long.TryParse(sp, out var spValue))
        {
            return null;
        }

        return new DetectedEvent(
            DetectedEventKind.Experience,
            $"{xpValue} XP, {spValue} SP",
            line.Text,
            line.Top,
            string.Empty,
            0,
            xpValue,
            spValue,
            0);
    }

    internal static DetectedEvent? ParseDiagnosticText(string text, TextMask textMask)
    {
        return ParseLine(new OcrLine(NormalizeText(text), Rectangle.Empty, textMask));
    }

    private static string NormalizeText(string value)
    {
        value = value.Replace('’', '\'').Replace('‘', '\'').Replace('`', '\'');
        return WhitespaceRegex().Replace(value, " ").Trim();
    }

    private static string CleanValue(string value)
    {
        return value.Trim().TrimEnd('.', ',', ':', ';').Trim();
    }

    private static (string Name, long Quantity) ParseItemValue(string value)
    {
        var match = ItemQuantityRegex().Match(value);
        if (!match.Success)
        {
            return (value, 1);
        }

        var quantityText = match.Groups[1].Value.Replace(",", string.Empty).Replace(" ", string.Empty);
        if (!long.TryParse(quantityText, out var quantity) || quantity <= 0)
        {
            return (value, 1);
        }

        return (match.Groups[2].Value.Trim(), quantity);
    }

    private static string NormalizeNumber(string value)
    {
        return value
            .Replace(",", string.Empty)
            .Replace(" ", string.Empty)
            .Replace('O', '0')
            .Replace('o', '0')
            .Replace('I', '1')
            .Replace('l', '1')
            .Replace('|', '1');
    }

    private static bool TryExtractExperience(string text, out string xp, out string sp)
    {
        var exact = ExperienceRegex().Match(text);
        if (exact.Success)
        {
            xp = exact.Groups[1].Value;
            sp = exact.Groups[2].Value;
            return true;
        }

        // A single damaged letter in "acquired" used to discard an otherwise
        // perfectly readable XP/SP line. Require the recognizable message prefix
        // and values, but accept the verb with up to two OCR substitutions.
        var words = WordRegex().Matches(text).Cast<Match>().ToArray();
        var acquiredIndex = Array.FindIndex(words, word =>
            BoundedLevenshtein(word.Value, "acquired", 2) <= 2);
        var values = ExperienceValuesRegex().Match(text);
        if (acquiredIndex >= 0
            && HasYouHavePrefix(words, acquiredIndex)
            && values.Success)
        {
            xp = values.Groups[1].Value;
            sp = values.Groups[2].Value;
            return true;
        }

        xp = string.Empty;
        sp = string.Empty;
        return false;
    }

    private static bool TryExtractYellowItem(string text, out string verb, out string value)
    {
        var exact = YellowItemRegex().Match(text);
        if (exact.Success)
        {
            verb = exact.Groups[1].Value;
            value = exact.Groups[2].Value;
            return CleanValue(value).Length > 0;
        }

        var words = WordRegex().Matches(text).Cast<Match>().ToArray();
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index].Value;
            if (word.Length < 5)
            {
                continue;
            }

            var obtainedDistance = BoundedLevenshtein(word, "obtained", 2);
            var earnedDistance = BoundedLevenshtein(word, "earned", 2);
            var bestDistance = Math.Min(obtainedDistance, earnedDistance);
            if (bestDistance > 2 || !HasYouHavePrefix(words, index))
            {
                continue;
            }

            verb = obtainedDistance <= earnedDistance ? "obtained" : "earned";
            value = text[(words[index].Index + words[index].Length)..];
            return CleanValue(value).Length > 0;
        }

        verb = string.Empty;
        value = string.Empty;
        return false;
    }

    private static bool HasYouHavePrefix(IReadOnlyList<Match> words, int verbIndex)
    {
        // Ownership is encoded by the first two words. Do not scan for "You
        // have" later in the sentence: a party member's nickname at the start
        // must make the whole row ineligible.
        return verbIndex >= 2
            && words.Count >= 2
            && BoundedLevenshtein(words[0].Value, "you", 1) <= 1
            && BoundedLevenshtein(words[1].Value, "have", 1) <= 1;
    }

    private static int BoundedLevenshtein(string first, string second, int maximum)
    {
        first = first.ToLowerInvariant();
        second = second.ToLowerInvariant();
        if (Math.Abs(first.Length - second.Length) > maximum)
        {
            return maximum + 1;
        }

        var previous = Enumerable.Range(0, second.Length + 1).ToArray();
        var current = new int[second.Length + 1];
        for (var firstIndex = 1; firstIndex <= first.Length; firstIndex++)
        {
            current[0] = firstIndex;
            var rowMinimum = current[0];
            for (var secondIndex = 1; secondIndex <= second.Length; secondIndex++)
            {
                var substitution = previous[secondIndex - 1]
                    + (first[firstIndex - 1] == second[secondIndex - 1] ? 0 : 1);
                current[secondIndex] = Math.Min(
                    Math.Min(previous[secondIndex] + 1, current[secondIndex - 1] + 1),
                    substitution);
                rowMinimum = Math.Min(rowMinimum, current[secondIndex]);
            }

            if (rowMinimum > maximum)
            {
                return maximum + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[second.Length];
    }

    private static DetectedEvent? ChooseBestCandidate(IReadOnlyList<DetectedEvent> candidates)
    {
        return candidates
            .OrderByDescending(ScoreCandidate)
            .FirstOrDefault();
    }

    private static int ScoreCandidate(DetectedEvent candidate)
    {
        var score = candidate.RawText.StartsWith("You have", StringComparison.OrdinalIgnoreCase) ? 40 : 0;
        if (candidate.RawText.Contains("obtained", StringComparison.OrdinalIgnoreCase)
            || candidate.RawText.Contains("earned", StringComparison.OrdinalIgnoreCase)
            || candidate.RawText.Contains("acquired", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        score += Math.Min(candidate.Value.Length, 50);
        score -= candidate.Value.Count(character =>
            !char.IsLetterOrDigit(character)
            && !char.IsWhiteSpace(character)
            && character is not '\'' and not '-' and not '(' and not ')' and not ':') * 4;
        return score;
    }

    private static IReadOnlyList<DetectedEvent> MergeDuplicateDetections(IReadOnlyList<DetectedEvent> detected)
    {
        var result = new List<DetectedEvent>();
        foreach (var candidate in detected.OrderBy(item => item.Top))
        {
            var duplicateIndex = result.FindIndex(existing =>
                existing.Kind == candidate.Kind
                && Math.Abs(existing.Top - candidate.Top) <= 5);
            if (duplicateIndex < 0)
            {
                result.Add(candidate);
                continue;
            }

            if (ScoreCandidate(candidate) > ScoreCandidate(result[duplicateIndex]))
            {
                result[duplicateIndex] = candidate;
            }
        }

        return result;
    }

    public void Dispose()
    {
        _engine.Dispose();
    }

    private sealed record OcrLine(string Text, Rectangle Bounds, TextMask TextMask)
    {
        public int Top => Bounds.Top;
    }

    [GeneratedRegex(@"^[^A-Za-z0-9]*You\s+have\s+(obtained|earned)\s+(.+?)(?:\.|$)", RegexOptions.IgnoreCase)]
    private static partial Regex YellowItemRegex();

    [GeneratedRegex(@"^[^A-Za-z0-9]*You\s+have\s+acquired\s+([0-9OIl|, ]+)\s*XP\s+and\s+([0-9OIl|, ]+)\s*SP", RegexOptions.IgnoreCase)]
    private static partial Regex ExperienceRegex();

    [GeneratedRegex(@"([0-9OIl|, ]+)\s*XP\s+(?:and|ancl|ond)?\s*([0-9OIl|, ]+)\s*SP", RegexOptions.IgnoreCase)]
    private static partial Regex ExperienceValuesRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(\d[\d, ]*)\s+(.+)$")]
    private static partial Regex ItemQuantityRegex();

    [GeneratedRegex(@"[A-Za-z]+")]
    private static partial Regex WordRegex();
}
