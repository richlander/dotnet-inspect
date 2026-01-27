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
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "dotnet-inspect");
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
}
