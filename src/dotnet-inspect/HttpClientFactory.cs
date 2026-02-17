namespace DotnetInspector;

/// <summary>
/// App-level alias that forwards to <see cref="DotnetInspector.Core.HttpClientFactory"/>.
/// Kept so existing <c>using DotnetInspector;</c> references continue to compile.
/// </summary>
public static class HttpClientFactory
{
    public static HttpClient Shared => Core.HttpClientFactory.Shared;
}
