namespace DotnetInspector.Inspectors;

/// <summary>
/// Fetches source files from URLs with in-memory caching.
/// </summary>
public class SourceFetcher
{
    private readonly Dictionary<string, string> _cache = new();
    private readonly HttpClient _httpClient;

    public SourceFetcher()
    {
        _httpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Fetches source content from a URL, with caching.
    /// Returns null if the fetch fails.
    /// </summary>
    public async Task<string?> FetchSourceAsync(string url)
    {
        if (_cache.TryGetValue(url, out string? cached))
        {
            return cached;
        }

        try
        {
            string content = await _httpClient.GetStringAsync(url);
            _cache[url] = content;
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
        var regionLines = new List<string>();
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
        var result = new List<string>();
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
