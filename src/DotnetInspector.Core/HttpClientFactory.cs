using System.Diagnostics;
using System.Net;

namespace DotnetInspector.Core;

/// <summary>
/// Factory for creating HttpClient instances with consistent configuration.
/// Call <see cref="Initialize"/> once at startup to configure offline mode.
/// </summary>
public static class HttpClientFactory
{
    private const string UserAgent = "dotnet-inspect";
    private static bool _offline;
    private static bool _denyNetwork;
    private static HttpClient? _shared;

    /// <summary>
    /// Configure the factory before first use. Safe to call multiple times;
    /// the shared instance is created lazily on first access.
    /// </summary>
    public static void Initialize(bool offline = false)
    {
        _offline = offline;
    }

    /// <summary>
    /// Denies all network access through managed HttpClient instances.
    /// Any HTTP request will throw <see cref="NetworkGuardException"/>.
    /// Use this to assert that a code path is fully offline.
    /// </summary>
    public static void DenyNetwork() => _denyNetwork = true;

    /// <summary>
    /// Re-allows network access after a prior <see cref="DenyNetwork"/> call.
    /// </summary>
    public static void AllowNetwork() => _denyNetwork = false;

    /// <summary>
    /// Whether network access is currently denied.
    /// </summary>
    internal static bool IsNetworkDenied => _denyNetwork;

    /// <summary>
    /// Resets the shared instance so the next access creates a fresh one.
    /// Test-only: allows toggling offline mode between tests.
    /// </summary>
    internal static void ResetSharedForTesting() => _shared = null;

    /// <summary>
    /// Gets the shared HttpClient instance for the application.
    /// This instance should be used throughout the app lifetime and not disposed.
    /// </summary>
    public static HttpClient Shared => _shared ??= CreateNew();

    /// <summary>
    /// Creates a new HttpClient with standard configuration including User-Agent header
    /// and automatic decompression for gzip/deflate/brotli responses.
    /// In offline mode, all requests will throw <see cref="OfflineException"/>.
    /// When network is denied, all requests will throw <see cref="NetworkGuardException"/>.
    /// </summary>
    public static HttpClient CreateNew(TimeSpan? timeout = null)
    {
        HttpMessageHandler handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };

        if (_offline)
            handler = new OfflineHandler(handler);

        handler = new NetworkGuardHandler(handler);

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        client.Timeout = timeout ?? TimeSpan.FromSeconds(30);
        return client;
    }
}

/// <summary>
/// A handler that rejects all HTTP requests when the tool is running in offline mode.
/// </summary>
internal sealed class OfflineHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw new OfflineException(
            $"Network access is disabled (--offline mode). Attempted: {request.Method} {request.RequestUri}");
    }
}

/// <summary>
/// A handler that rejects all HTTP requests when network access has been denied
/// via <see cref="HttpClientFactory.DenyNetwork"/>.
/// Unlike <see cref="OfflineHandler"/>, this is a runtime toggle — arm it to assert
/// that a code path makes no network calls, then disarm with <see cref="HttpClientFactory.AllowNetwork"/>.
/// DEBUG-only: the check is compiled out in Release builds.
/// </summary>
internal sealed class NetworkGuardHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
#if DEBUG
        if (HttpClientFactory.IsNetworkDenied)
        {
            var message = $"Network guard violation: {request.Method} {request.RequestUri}";
            Debug.Fail(message);
            throw new NetworkGuardException(message);
        }
#endif
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Thrown when a network request is attempted in offline mode.
/// </summary>
public sealed class OfflineException(string message) : InvalidOperationException(message);

/// <summary>
/// Thrown when a network request is attempted while network access is denied.
/// </summary>
public sealed class NetworkGuardException(string message) : InvalidOperationException(message);
