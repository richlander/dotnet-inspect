using System.Net;

namespace DotnetInspector;

/// <summary>
/// Factory for creating HttpClient instances with consistent configuration.
/// </summary>
public static class HttpClientFactory
{
    private const string UserAgent = "dotnet-inspect";

    /// <summary>
    /// Creates a new HttpClient with standard configuration including User-Agent header
    /// and automatic decompression for gzip/deflate/brotli responses.
    /// </summary>
    public static HttpClient Create(TimeSpan? timeout = null)
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
}
