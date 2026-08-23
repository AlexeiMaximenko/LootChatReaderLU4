using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LootChatReader;

internal sealed partial class ItemIconCatalogService : IDisposable
{
    private readonly object _catalogLock = new();

    private Dictionary<string, ItemIconEntry> _itemsByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ItemIconEntry> _itemsByNormalizedName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ItemIconEntry> _itemsByTierlessNormalizedName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ItemIconEntry> _itemsByRaritylessNormalizedName = new(StringComparer.OrdinalIgnoreCase);

    public ItemIconCatalogService(string dataDirectory)
    {
        _ = dataDirectory;
        LoadEmbeddedCatalog();
    }

    public int Count
    {
        get
        {
            lock (_catalogLock)
            {
                return _itemsByName.Count;
            }
        }
    }

    public ItemIconMatch? Resolve(string recognizedName)
    {
        if (string.IsNullOrWhiteSpace(recognizedName))
        {
            return null;
        }

        Dictionary<string, ItemIconEntry> byName;
        Dictionary<string, ItemIconEntry> byNormalizedName;
        Dictionary<string, ItemIconEntry> byTierlessNormalizedName;
        Dictionary<string, ItemIconEntry> byRaritylessNormalizedName;
        lock (_catalogLock)
        {
            byName = _itemsByName;
            byNormalizedName = _itemsByNormalizedName;
            byTierlessNormalizedName = _itemsByTierlessNormalizedName;
            byRaritylessNormalizedName = _itemsByRaritylessNormalizedName;
        }

        var normalized = NormalizeName(recognizedName);
        if (normalized.Length < 2)
        {
            // Prevent punctuation-only OCR artifacts such as "-" from matching
            // technical catalog entries named "_" or "__".
            return null;
        }

        if (byName.TryGetValue(recognizedName, out var exact))
        {
            return new ItemIconMatch(exact, false);
        }

        if (byNormalizedName.TryGetValue(normalized, out exact))
        {
            return new ItemIconMatch(exact, false);
        }

        // Tesseract frequently reads the compact LU4 font's "m" as "rn"
        // (for example Stem -> Stern). Treat that ligature as an exact OCR variant.
        var joinedLetters = normalized.Replace("rn", "m", StringComparison.Ordinal);
        if (joinedLetters != normalized && byNormalizedName.TryGetValue(joinedLetters, out exact))
        {
            return new ItemIconMatch(exact, true);
        }

        // Some LU4 chat messages omit the catalog's grade suffix, for example
        // "Ashen Reagent Cache" vs "Ashen Reagent Cache Tier D". Only resolve
        // such a name when the tierless catalog key is unambiguous.
        if (byTierlessNormalizedName.TryGetValue(normalized, out exact)
            || (joinedLetters != normalized
                && byTierlessNormalizedName.TryGetValue(joinedLetters, out exact)))
        {
            return new ItemIconMatch(exact, true);
        }

        if (byRaritylessNormalizedName.TryGetValue(normalized, out exact)
            || (joinedLetters != normalized
                && byRaritylessNormalizedName.TryGetValue(joinedLetters, out exact)))
        {
            return new ItemIconMatch(exact, true);
        }

        ItemIconEntry? best = null;
        var bestDistance = int.MaxValue;
        var tied = false;
        var allowedDistance = normalized.Length switch
        {
            <= 3 => 0,
            <= 6 => 1,
            <= 12 => 2,
            _ => 3
        };

        if (allowedDistance == 0)
        {
            return null;
        }

        foreach (var candidate in byNormalizedName)
        {
            if (Math.Abs(candidate.Key.Length - normalized.Length) > allowedDistance)
            {
                continue;
            }

            var distance = Math.Min(
                LevenshteinDistance(normalized, candidate.Key, allowedDistance),
                LevenshteinDistance(joinedLetters, candidate.Key, allowedDistance));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate.Value;
                tied = false;
            }
            else if (distance == bestDistance)
            {
                tied = true;
            }
        }

        return best is not null && bestDistance <= allowedDistance && !tied
            ? new ItemIconMatch(best, true)
            : null;
    }

    public Task<Bitmap?> LoadIconAsync(ItemIconEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.IconPath.EndsWith("/none.png", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<Bitmap?>(null);
        }

        var bitmap = LoadEmbeddedIcon(entry.IconPath);
        if (bitmap is null)
        {
            // Some icon paths published by the wiki currently return HTTP 404.
            // Keep every catalog item usable offline with the embedded generic box.
            bitmap = LoadEmbeddedIcon("/i64/etc_jewel_box_i00.png");
        }

        return Task.FromResult(bitmap);
    }

    private static Bitmap? LoadEmbeddedIcon(string iconPath)
    {
        try
        {
            var fileName = Path.GetFileName(new Uri(new Uri("https://mw2.wiki/"), iconPath).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                $"LootChatReader.Resources.ItemIcons.{fileName}");
            if (stream is null)
            {
                return null;
            }

            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch
        {
            return null;
        }
    }

    private void LoadEmbeddedCatalog()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                "LootChatReader.Resources.item-icons.json");
            var entries = stream is null
                ? null
                : JsonSerializer.Deserialize<ItemIconEntry[]>(stream);
            if (entries is { Length: > 0 })
            {
                ReplaceCatalog(entries);
            }
        }
        catch
        {
            // A damaged embedded catalog is treated as unavailable.
        }
    }

    private void ReplaceCatalog(IEnumerable<ItemIconEntry> entries)
    {
        var entryList = entries.ToArray();
        var byName = new Dictionary<string, ItemIconEntry>(StringComparer.OrdinalIgnoreCase);
        var byNormalizedName = new Dictionary<string, ItemIconEntry>(StringComparer.OrdinalIgnoreCase);
        var tierlessCandidates = new Dictionary<string, ItemIconEntry>(StringComparer.OrdinalIgnoreCase);
        var ambiguousTierlessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entryList)
        {
            byName.TryAdd(entry.Name, entry);
            byNormalizedName.TryAdd(NormalizeName(entry.Name), entry);

            var tierlessName = TierSuffixRegex().Replace(entry.Name, string.Empty).Trim();
            if (tierlessName.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tierlessKey = NormalizeName(tierlessName);
            if (ambiguousTierlessNames.Contains(tierlessKey))
            {
                continue;
            }

            if (tierlessCandidates.TryGetValue(tierlessKey, out var existing)
                && existing.Id != entry.Id)
            {
                tierlessCandidates.Remove(tierlessKey);
                ambiguousTierlessNames.Add(tierlessKey);
                continue;
            }

            tierlessCandidates[tierlessKey] = entry;
        }

        var raritylessCandidates = entryList
            .Select(entry => new
            {
                Entry = entry,
                BaseName = RaritySuffixRegex().Replace(entry.Name, string.Empty).Trim()
            })
            .Where(candidate => !candidate.BaseName.Equals(candidate.Entry.Name, StringComparison.OrdinalIgnoreCase))
            .GroupBy(candidate => NormalizeName(candidate.BaseName), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(candidate => candidate.Entry.IconPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var first = group.First();
                    return first.Entry with { Name = first.BaseName };
                },
                StringComparer.OrdinalIgnoreCase);

        lock (_catalogLock)
        {
            _itemsByName = byName;
            _itemsByNormalizedName = byNormalizedName;
            _itemsByTierlessNormalizedName = tierlessCandidates;
            _itemsByRaritylessNormalizedName = raritylessCandidates;
        }
    }

    private static string NormalizeName(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }

    private static int LevenshteinDistance(string left, string right, int stopAfter)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            var rowMinimum = current[0];
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
                rowMinimum = Math.Min(rowMinimum, current[j]);
            }

            if (rowMinimum > stopAfter)
            {
                return stopAfter + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    public void Dispose()
    {
    }

    [GeneratedRegex(@"\s+Tier\s+[A-Za-z0-9]+$", RegexOptions.IgnoreCase)]
    private static partial Regex TierSuffixRegex();

    [GeneratedRegex(@"\s+(?:Common|Uncommon|Rare)$", RegexOptions.IgnoreCase)]
    private static partial Regex RaritySuffixRegex();
}

internal sealed record ItemIconEntry(int Id, string Name, string IconPath, string ItemPath);

internal sealed record ItemIconMatch(ItemIconEntry Entry, bool IsFuzzyMatch);
