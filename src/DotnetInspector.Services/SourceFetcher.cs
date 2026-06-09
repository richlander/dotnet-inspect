using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Fetches source files from URLs with persistent disk caching and in-memory caching.
/// </summary>
public class SourceFetcher(HttpClient httpClient)
{
    private readonly Dictionary<string, string> _memoryCache = new();
    private readonly HttpClient _httpClient = httpClient;

    /// <summary>
    /// Fetches source content from a URL, with caching.
    /// Checks in-memory cache first, then disk cache, then fetches from network.
    /// Returns null if the fetch fails.
    /// </summary>
    public async Task<string?> FetchSourceAsync(string url)
    {
        // URLs originate in untrusted artifacts (SourceLink data in a PDB). Restrict to absolute
        // http/https so attacker-supplied mappings cannot reach file:// or other schemes. The
        // injected HttpClient is additionally SSRF-hardened against private/internal IPs.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        // Check in-memory cache first
        if (_memoryCache.TryGetValue(url, out string? cached))
        {
            return cached;
        }

        // Check persistent disk cache
        var diskCached = PackageCacheService.TryGetCachedSource(url);
        if (diskCached != null)
        {
            _memoryCache[url] = diskCached;
            return diskCached;
        }

        // Fetch from network with retry
        try
        {
            string? content = await HttpRetryHelper.GetStringWithRetryAsync(_httpClient, url).ConfigureAwait(false);
            if (content == null)
                return null;
            _memoryCache[url] = content;
            PackageCacheService.CacheSource(url, content);
            return content;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts a named region from source content.
    /// Returns the content between #region Name and #endregion markers.
    /// </summary>
    public static string? ExtractRegion(string content, string regionName)
    {
        var lines = content.Split('\n');
        List<string> regionLines = [];
        bool inRegion = false;
        int regionDepth = 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            
            if (trimmed.StartsWith("#region", StringComparison.Ordinal))
            {
                if (inRegion)
                {
                    // Nested region
                    regionDepth++;
                    regionLines.Add(line);
                }
                else
                {
                    // Check if this is the region we want
                    var name = trimmed.Length > 7 ? trimmed[7..].Trim() : "";
                    if (name.Equals(regionName, StringComparison.OrdinalIgnoreCase))
                    {
                        inRegion = true;
                        regionDepth = 0;
                    }
                }
            }
            else if (trimmed.StartsWith("#endregion", StringComparison.Ordinal))
            {
                if (inRegion)
                {
                    if (regionDepth > 0)
                    {
                        regionDepth--;
                        regionLines.Add(line);
                    }
                    else
                    {
                        // End of our region
                        break;
                    }
                }
            }
            else if (inRegion)
            {
                regionLines.Add(line);
            }
        }

        if (regionLines.Count == 0)
            return null;

        // Trim common leading whitespace
        return TrimCommonIndentation(regionLines);
    }

    private static string TrimCommonIndentation(List<string> lines)
    {
        // Find minimum indentation (ignoring empty lines)
        int minIndent = int.MaxValue;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            int indent = line.TakeWhile(char.IsWhiteSpace).Count();
            minIndent = Math.Min(minIndent, indent);
        }

        if (minIndent == int.MaxValue || minIndent == 0)
            return string.Join('\n', lines).TrimEnd();

        // Remove common indentation
        List<string> result = [];
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                result.Add("");
            else
                result.Add(line[minIndent..]);
        }

        return string.Join('\n', result).TrimEnd();
    }
}
