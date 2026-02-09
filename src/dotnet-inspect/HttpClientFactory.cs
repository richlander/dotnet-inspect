using System.Net;

namespace DotnetInspector;

/// <summary>
/// Factory for creating HttpClient instances with consistent configuration.
/// </summary>
public static class HttpClientFactory
{
    private const string UserAgent = "dotnet-inspect";
    private static readonly HttpClient SharedInstance = CreateNew();

    /// <summary>
    /// Gets the shared HttpClient instance for the application.
    /// This instance should be used throughout the app lifetime and not disposed.
    /// </summary>
    public static HttpClient Shared => SharedInstance;

    /// <summary>
    /// Creates a new HttpClient with standard configuration including User-Agent header
    /// and automatic decompression for gzip/deflate/brotli responses.
    /// Only use this if you need a separate instance with different settings.
    /// </summary>
    public static HttpClient CreateNew(TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        client.Timeout = timeout ?? TimeSpan.FromSeconds(30);
        return client;
    }

    /// <summary>
    /// Creates a new HttpClient. Deprecated - use Shared instead.
    /// </summary>
    [Obsolete("Use HttpClientFactory.Shared instead of creating new instances")]
    public static HttpClient Create(TimeSpan? timeout = null) => CreateNew(timeout);
}
