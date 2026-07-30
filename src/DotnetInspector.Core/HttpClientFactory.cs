using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DotnetInspector.Core;

/// <summary>
/// Factory for creating HttpClient instances with consistent configuration.
/// Call <see cref="Initialize"/> once at startup to configure offline mode.
/// </summary>
public static class HttpClientFactory
{
    private const string UserAgent = "dotnet-inspect";
    private static bool _offline;
    private static HttpClient? _shared;
    private static HttpClient? _sharedUntrustedFetch;
    private static HttpClient? _untrustedFetchOverride;
    private static IDisposable? _networkTrafficLoggingSubscription;
    private static Func<HttpMessageHandler, HttpMessageHandler>? _authenticationDecorator;

    /// <summary>
    /// Configure the factory before first use. Safe to call multiple times;
    /// the shared instance is created lazily on first access.
    /// </summary>
    public static void Initialize(bool offline = false)
    {
        _offline = offline;
    }

    public static bool IsOffline => _offline;

    /// <summary>
    /// Installs a decorator around the outermost handler of shared clients, so that a source
    /// answering 401 can have credentials supplied and its request replayed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This layer knows nothing about NuGet credential plugins on purpose: it sits below
    /// NuGetFetch and cannot reference it. The composition root installs
    /// <c>NuGetFetch.Plugins.PluginAuthenticationHandler</c> through this seam.
    /// </para>
    /// <para>
    /// The decorator is captured when a client is constructed, so it must be set before first
    /// use of <see cref="Shared"/>. It is deliberately not applied to
    /// <see cref="SharedUntrustedFetch"/>: that client fetches URLs that originate in untrusted
    /// artifacts, and feed credentials have no business being offered to them.
    /// </para>
    /// </remarks>
    public static void SetAuthenticationDecorator(Func<HttpMessageHandler, HttpMessageHandler>? decorator) =>
        _authenticationDecorator = decorator;

    /// <summary>
    /// Enables logging for managed HTTP request observations. Requests are still
    /// allowed to proceed; use offline mode to block network access.
    /// </summary>
    /// <param name="sink">
    /// Where to write the log. The default (<c>null</c>) binds <see cref="Console.Error"/>
    /// once, as a process-lifetime subscription kept in a static field. Pass an explicit
    /// sink to get a fresh, caller-owned subscription — dispose the returned handle to
    /// unsubscribe. The sink is captured here, not read at publish time, so logging never
    /// follows a later <see cref="Console.Error"/> swap (issue #705).
    /// </param>
    public static IDisposable EnableNetworkTrafficLogging(System.IO.TextWriter? sink = null)
    {
        if (sink is not null)
            return NetworkTelemetry.Subscribe(new NetworkTrafficLogConsumer(sink));

        return _networkTrafficLoggingSubscription ??=
            NetworkTelemetry.Subscribe(new NetworkTrafficLogConsumer(Console.Error));
    }

    /// <summary>
    /// Resets the shared instance so the next access creates a fresh one.
    /// Test-only: allows toggling offline mode between tests.
    /// </summary>
    internal static void ResetSharedForTesting()
    {
        _shared = null;
        _sharedUntrustedFetch = null;
        _untrustedFetchOverride = null;
        _networkTrafficLoggingSubscription?.Dispose();
        _networkTrafficLoggingSubscription = null;
    }

    /// <summary>
    /// Gets the shared HttpClient instance for the application.
    /// This instance should be used throughout the app lifetime and not disposed.
    /// </summary>
    public static HttpClient Shared => _shared ??= CreateNew();

    /// <summary>
    /// Shared, process-lifetime SSRF-hardened client for fetching content from URLs that originate
    /// in untrusted artifacts (SourceLink URLs embedded in a PDB, etc.). Use this — not <see cref="Shared"/> —
    /// for any URL that came from inspected package/PDB data. Do not dispose.
    /// </summary>
    public static HttpClient SharedUntrustedFetch => _untrustedFetchOverride ?? (_sharedUntrustedFetch ??= CreateUntrustedFetchClient());

    /// <summary>
    /// Test-only: substitutes the transport used by untrusted-source fetches so acquisition
    /// paths (including failure) can be exercised without network access. This replaces only
    /// the transport; callers still run the real <c>SourceFetcher</c>, so scheme restriction,
    /// caching, and status handling stay under test. Pass null to restore the real client.
    /// </summary>
    internal static void SetUntrustedFetchForTesting(HttpClient? client) => _untrustedFetchOverride = client;

    /// <summary>
    /// Whether <paramref name="url"/> is an absolute http/https URL. Untrusted-source fetches restrict
    /// themselves to these schemes so attacker-supplied SourceLink data cannot reach file://, etc.
    /// </summary>
    public static bool IsAllowedFetchScheme(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>
    /// Creates a new HttpClient with standard configuration including User-Agent header
    /// and automatic decompression for gzip/deflate/brotli responses.
    /// In offline mode, all requests will throw <see cref="OfflineException"/>.
    /// When traffic logging is enabled (DEBUG startup), requests log their traffic kind and URL to stderr.
    /// </summary>
    public static HttpClient CreateNew(TimeSpan? timeout = null)
    {
        HttpMessageHandler handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };

        if (_offline)
            handler = new OfflineHandler(handler);

        if (InfoTracker.Enabled)
            handler = new CountingHandler(handler);

        handler = new NetworkTelemetryHandler(handler, NetworkClientKinds.Shared);

        // Outermost, so each replayed attempt is observed by the telemetry and counting
        // handlers below it. A 401 followed by an authenticated retry really is two requests.
        if (_authenticationDecorator is not null)
        {
            handler = _authenticationDecorator(handler);
        }

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        client.Timeout = timeout ?? TimeSpan.FromSeconds(30);
        return client;
    }

    /// <summary>
    /// Creates an HttpClient hardened against SSRF for fetching content from URLs that originate
    /// in untrusted artifacts (e.g. SourceLink URLs embedded in a PDB). Every TCP connection —
    /// including redirect hops — is validated to resolve to a public IP address, and automatic
    /// redirects are capped. Offline mode and DEBUG traffic logging are still honored.
    /// </summary>
    public static HttpClient CreateUntrustedFetchClient(TimeSpan? timeout = null)
    {
        HttpMessageHandler handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectCallback = SsrfGuardedConnectAsync,
        };

        if (_offline)
            handler = new OfflineHandler(handler);

        if (InfoTracker.Enabled)
            handler = new CountingHandler(handler);

        handler = new NetworkTelemetryHandler(handler, NetworkClientKinds.UntrustedFetch);

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        client.Timeout = timeout ?? TimeSpan.FromSeconds(30);
        return client;
    }

    private static async ValueTask<Stream> SsrfGuardedConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0)
            throw new HttpRequestException($"Could not resolve host: {endpoint.Host}");

        foreach (var address in addresses)
        {
            if (IsNonPublic(address))
                throw new HttpRequestException(
                    $"Blocked request to non-public address: {endpoint.Host} resolves to {address}");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Returns true for loopback, link-local, private, CGNAT, multicast, and unspecified addresses.</summary>
    private static bool IsNonPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            return true;

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || ip.IsIPv6UniqueLocal)
                return true;
            if (ip.IsIPv4MappedToIPv6)
                return IsNonPublic(ip.MapToIPv4());
            return false;
        }

        byte[] b = ip.GetAddressBytes();
        return b[0] switch
        {
            0 => true,                              // 0.0.0.0/8
            10 => true,                             // 10.0.0.0/8
            127 => true,                            // loopback
            169 when b[1] == 254 => true,           // link-local
            172 when b[1] >= 16 && b[1] <= 31 => true, // 172.16.0.0/12
            192 when b[1] == 168 => true,           // 192.168.0.0/16
            100 when b[1] >= 64 && b[1] <= 127 => true, // CGNAT 100.64.0.0/10
            >= 224 => true,                         // multicast / reserved
            _ => false,
        };
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

internal sealed class NetworkTelemetryHandler(HttpMessageHandler inner, string clientKind) : DelegatingHandler(inner)
{
    private readonly string _clientKind = clientKind;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        bool allowed = NetworkTelemetry.RecordRequestStarting(
            request,
            _clientKind);
        if (!allowed)
        {
            throw new NetworkPolicyException(
                $"Network traffic '{NetworkTelemetry.CurrentTrafficKind.ToTelemetryName()}' requires explicit capability authorization. Attempted: {request.Method} {request.RequestUri}");
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// A handler that counts HTTP requests for <see cref="InfoTracker"/>.
/// </summary>
internal sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        InfoTracker.RecordHttpRequest();
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Thrown when a network request is attempted in offline mode.
/// </summary>
public sealed class OfflineException(string message) : InvalidOperationException(message);

/// <summary>
/// Thrown when a request reaches the HTTP seam without the capability required
/// by its current <see cref="NetworkTrafficKind"/>.
/// </summary>
public sealed class NetworkPolicyException(string message) : InvalidOperationException(message);
