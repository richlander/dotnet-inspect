namespace DotnetInspect.Cli;

/// <summary>
/// App-level alias that forwards to <see cref="DotnetInspector.Core.HttpClientFactory"/>.
/// Kept so CLI callers use the host-owned HTTP entry point.
/// </summary>
public static class HttpClientFactory
{
    public static HttpClient Shared => DotnetInspector.Core.HttpClientFactory.Shared;
}
