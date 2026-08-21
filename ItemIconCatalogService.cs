using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LootChatReader;

internal sealed partial class ItemIconCatalogService : IDisposable
{
    private const string CatalogUrl =
        "https://mw2.wiki/lu4-b-w-c/search/result?Search%5Bquery%5D=&Search%5Bsearch_type%5D=0&per_page=100&page={0}";
    private static readonly Uri SiteBaseUri = new("https://mw2.wiki/");

    private readonly HttpClient _httpClient;
    private readonly string _indexPath;
    private readonly string _iconsDirectory;
    private readonly object _catalogLock = new();
    private readonly ConcurrentDictionary<string, Task<string?>> _iconDownloads =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, ItemIconEntry> _itemsByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ItemIconEntry> _itemsByNormalizedName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ItemIconEntry> _itemsByTierlessNormalizedName = new(StringComparer.OrdinalIgnoreCase);

    public ItemIconCatalogService(string dataDirectory)
    {
        var cacheDirectory = Path.Combine(dataDirectory, "cache");
        _iconsDirectory = Path.Combine(cacheDirectory, "icons");
        _indexPath = Path.Combine(cacheDirectory, "item-icons.json");
        Directory.CreateDirectory(_iconsDirectory);
        EmbeddedResourceFiles.EnsureExtracted(
            "LootChatReader.Resources.item-icons.json",
            _indexPath);

        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("LU4LootChatReader", "1.0"));

        LoadCachedCatalog();
    }

    public bool ShouldRefresh(TimeSpan maximumAge)
    {
        try
        {
            return !File.Exists(_indexPath)
                || DateTime.UtcNow - File.GetLastWriteTimeUtc(_indexPath) >= maximumAge;
        }
        catch
        {
            return true;
        }
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

    public async Task<int> SyncAsync(
        IProgress<IconCatalogProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var collected = new Dictionary<string, ItemIconEntry>(StringComparer.OrdinalIgnoreCase);
        var firstHtml = await DownloadCatalogPageAsync(1, cancellationToken);
        var totalItems = ReadTotalItemCount(firstHtml);
        var totalPages = totalItems > 0 ? (int)Math.Ceiling(totalItems / 100d) : 0;

        var firstPageRows = AddPageEntries(firstHtml, collected);
        progress?.Report(new IconCatalogProgress(1, totalPages, collected.Count));

        var maximumPage = totalPages > 0 ? totalPages : firstPageRows < 100 ? 1 : 500;
        for (var page = 2; page <= maximumPage; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var html = await DownloadCatalogPageAsync(page, cancellationToken);
            var pageCount = AddPageEntries(html, collected);
            progress?.Report(new IconCatalogProgress(page, totalPages, collected.Count));

            if (totalItems == 0 && pageCount < 100)
            {
                break;
            }

            await Task.Delay(100, cancellationToken);
        }

        if (collected.Count == 0)
        {
            throw new InvalidOperationException("The item catalog returned no usable entries.");
        }

        var entries = collected.Values.OrderBy(item => item.Name).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
        await File.WriteAllTextAsync(
            _indexPath,
            JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        ReplaceCatalog(entries);
        return entries.Length;
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
        lock (_catalogLock)
        {
            byName = _itemsByName;
            byNormalizedName = _itemsByNormalizedName;
            byTierlessNormalizedName = _itemsByTierlessNormalizedName;
        }

        if (byName.TryGetValue(recognizedName, out var exact))
        {
            return new ItemIconMatch(exact, false);
        }

        var normalized = NormalizeName(recognizedName);
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

    public async Task<Bitmap?> LoadIconAsync(ItemIconEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.IconPath.EndsWith("/none.png", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var localPath = await _iconDownloads.GetOrAdd(
            entry.IconPath,
            path => DownloadIconAsync(path, cancellationToken));
        if (localPath is null || !File.Exists(localPath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(localPath, cancellationToken);
        using var stream = new MemoryStream(bytes);
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private async Task<string> DownloadCatalogPageAsync(int page, CancellationToken cancellationToken)
    {
        var url = string.Format(CatalogUrl, page);
        return await _httpClient.GetStringAsync(url, cancellationToken);
    }

    private static int AddPageEntries(string html, IDictionary<string, ItemIconEntry> destination)
    {
        var parsed = 0;
        foreach (Match anchorMatch in ItemAnchorRegex().Matches(html))
        {
            var body = anchorMatch.Groups[3].Value;
            var iconMatch = IconRegex().Match(body);
            var nameMatch = ItemNameRegex().Match(body);
            if (!iconMatch.Success || !nameMatch.Success)
            {
                continue;
            }

            var name = WebUtility.HtmlDecode(StripTagsRegex().Replace(nameMatch.Groups[1].Value, string.Empty)).Trim();
            name = WhitespaceRegex().Replace(name, " ");
            if (name.Length == 0)
            {
                continue;
            }

            if (!int.TryParse(anchorMatch.Groups[2].Value, out var id))
            {
                continue;
            }

            var entry = new ItemIconEntry(
                id,
                name,
                WebUtility.HtmlDecode(iconMatch.Groups[1].Value),
                WebUtility.HtmlDecode(anchorMatch.Groups[1].Value));

            parsed++;

            if (!destination.ContainsKey(name))
            {
                destination[name] = entry;
            }
        }

        return parsed;
    }

    private static int ReadTotalItemCount(string html)
    {
        var plainText = StripTagsRegex().Replace(WebUtility.HtmlDecode(html), " ");
        plainText = WhitespaceRegex().Replace(plainText, " ");
        var match = TotalItemsRegex().Match(plainText);
        return match.Success && int.TryParse(match.Groups[1].Value, out var count) ? count : 0;
    }

    private async Task<string?> DownloadIconAsync(string iconPath, CancellationToken cancellationToken)
    {
        try
        {
            var fileName = Path.GetFileName(new Uri(SiteBaseUri, iconPath).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var localPath = Path.Combine(_iconsDirectory, fileName);
            if (File.Exists(localPath))
            {
                return localPath;
            }

            var bytes = await _httpClient.GetByteArrayAsync(new Uri(SiteBaseUri, iconPath), cancellationToken);
            await File.WriteAllBytesAsync(localPath, bytes, cancellationToken);
            return localPath;
        }
        catch
        {
            return null;
        }
    }

    private void LoadCachedCatalog()
    {
        try
        {
            if (!File.Exists(_indexPath))
            {
                return;
            }

            var entries = JsonSerializer.Deserialize<ItemIconEntry[]>(File.ReadAllText(_indexPath));
            if (entries is { Length: > 0 })
            {
                ReplaceCatalog(entries);
            }
        }
        catch
        {
            // A damaged cache is ignored and can be rebuilt from the UI.
        }
    }

    private void ReplaceCatalog(IEnumerable<ItemIconEntry> entries)
    {
        var byName = new Dictionary<string, ItemIconEntry>(StringComparer.OrdinalIgnoreCase);
        var byNormalizedName = new Dictionary<string, ItemIconEntry>(StringComparer.OrdinalIgnoreCase);
        var tierlessCandidates = new Dictionary<string, ItemIconEntry>(StringComparer.OrdinalIgnoreCase);
        var ambiguousTierlessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
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

        lock (_catalogLock)
        {
            _itemsByName = byName;
            _itemsByNormalizedName = byNormalizedName;
            _itemsByTierlessNormalizedName = tierlessCandidates;
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
        _httpClient.Dispose();
    }

    [GeneratedRegex("<a\\s+[^>]*class=[\"'][^\"']*\\bitem-name\\b[^\"']*[\"'][^>]*href=[\"']([^\"']*/lu4-b-w-c/item/(\\d+)[^\"']*)[\"'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ItemAnchorRegex();

    [GeneratedRegex("<img\\s+[^>]*src=[\"']([^\"']*/i64/[^\"']+\\.png)[\"']", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex IconRegex();

    [GeneratedRegex("<span\\s+class=[\"']item-name__content[\"']>(.*?)(?:<span\\s+class=[\"']item-grade[\"']>|</span>)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ItemNameRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex StripTagsRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("(?:Предмет|Item)\\s*(\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TotalItemsRegex();

    [GeneratedRegex(@"\s+Tier\s+[A-Za-z0-9]+$", RegexOptions.IgnoreCase)]
    private static partial Regex TierSuffixRegex();
}

internal sealed record ItemIconEntry(int Id, string Name, string IconPath, string ItemPath);

internal sealed record ItemIconMatch(ItemIconEntry Entry, bool IsFuzzyMatch);

internal sealed record IconCatalogProgress(int Page, int TotalPages, int ItemCount);
