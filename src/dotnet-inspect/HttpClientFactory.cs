namespace DotnetInspector;

/// <summary>
/// Factory for creating HttpClient instances with consistent configuration.
/// </summary>
public static class HttpClientFactory
{
    private const string UserAgent = "dotnet-inspect";

    /// <summary>
    /// Creates a new HttpClient with standard configuration including User-Agent header.
    /// </summary>
    public static HttpClient Create(TimeSpan? timeout = null)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        client.Timeout = timeout ?? TimeSpan.FromSeconds(30);
        return client;
    }
}
