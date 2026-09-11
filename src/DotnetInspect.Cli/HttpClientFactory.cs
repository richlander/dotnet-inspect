namespace DotnetInspect.Cli;

/// <summary>
/// App-level alias that forwards to <see cref="DotnetInspector.Networking.HttpClientFactory"/>.
/// Kept so CLI callers use the host-owned HTTP entry point.
/// </summary>
public static class HttpClientFactory
{
    public static HttpClient Shared => DotnetInspector.Networking.HttpClientFactory.Shared;
}
