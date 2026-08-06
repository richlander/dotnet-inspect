using System.Diagnostics;
using System.Globalization;
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
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Upper bound accepted from <c>--http-timeout</c> and
    /// <c>DOTNET_INSPECT_HTTP_TIMEOUT_IN_SECONDS</c>.
    /// <see cref="HttpClient.Timeout"/> rejects any value above <see cref="int.MaxValue"/>
    /// milliseconds, roughly 24.8 days, so accepting a value without a ceiling would let
    /// configuration crash the process instead of configuring it.
    /// </summary>
    private static readonly TimeSpan MaximumConfiguredTimeout = TimeSpan.FromHours(1);

    private static bool _offline;
    private static TimeSpan? _configuredTimeout;
    private static HttpClient? _shared;
    private static HttpClient? _sharedUntrustedFetch;
    private static HttpClient? _untrustedFetchOverride;
    private static IDisposable? _networkTrafficLoggingSubscription;
    private static Func<HttpMessageHandler, HttpMessageHandler>? _authenticationDecorator;

    /// <summary>
    /// Configure the factory before first use. Safe to call multiple times;
    /// the shared instance is created lazily on first access.
    /// </summary>
    /// <param name="offline">When true, every request throws <see cref="OfflineException"/>.</param>
    /// <param name="defaultTimeout">
    /// Default request timeout for <see cref="Shared"/>, normally the parsed <c>--http-timeout</c>
    /// value. When null, <c>DOTNET_INSPECT_HTTP_TIMEOUT_IN_SECONDS</c> is consulted instead, so
    /// the flag wins over the variable and the variable still works for callers that never parse
    /// a command line.
    /// </param>
    public static void Initialize(bool offline = false, TimeSpan? defaultTimeout = null)
    {
        _offline = offline;
        _configuredTimeout = defaultTimeout;
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
    /// <param name="contain">
    /// Applied to every composed line before it reaches the sink. Required, not
    /// defaulted: the logged URL carries the package id from argv, so a line
    /// terminator in it would forge an unindented stderr line, and a seam a caller
    /// can omit is one a caller will omit.
    /// </param>
    /// <param name="sink">
    /// Where to write the log. The default (<c>null</c>) binds <see cref="Console.Error"/>
    /// once, as a process-lifetime subscription kept in a static field. Pass an explicit
    /// sink to get a fresh, caller-owned subscription — dispose the returned handle to
    /// unsubscribe. The sink is captured here, not read at publish time, so logging never
    /// follows a later <see cref="Console.Error"/> swap (issue #705).
    /// </param>
    public static IDisposable EnableNetworkTrafficLogging(
        Func<string, string> contain, System.IO.TextWriter? sink = null)
    {
        ArgumentNullException.ThrowIfNull(contain);

        if (sink is not null)
            return NetworkTelemetry.Subscribe(new NetworkTrafficLogConsumer(sink, contain));

        return _networkTrafficLoggingSubscription ??=
#pragma warning disable RS0030 // An accounted stderr sink: NetworkTrafficLogConsumer applies `contain` to every line before writing it (issue #3319).
            NetworkTelemetry.Subscribe(new NetworkTrafficLogConsumer(Console.Error, contain));
#pragma warning restore RS0030
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
        client.Timeout = timeout ?? ResolveConfiguredTimeout();
        return client;
    }

    /// <summary>
    /// Resolves the default request timeout for <see cref="Shared"/>: the parsed
    /// <c>--http-timeout</c> value if one was supplied to <see cref="Initialize"/>, otherwise
    /// <c>DOTNET_INSPECT_HTTP_TIMEOUT_IN_SECONDS</c>, otherwise 30 seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A package search against a large authenticated feed can take longer than 30 seconds to
    /// answer, and the cap was previously unreachable from outside the process, so the command
    /// failed with no way for the operator to give it more time.
    /// </para>
    /// <para>
    /// An explicit per-call timeout argument still wins over both, so this only sets the default.
    /// </para>
    /// <para>
    /// This deliberately does not govern <see cref="SharedUntrustedFetch"/>. That client fetches
    /// URLs that originate in untrusted artifacts, so how long it will wait is part of its
    /// containment story, not a feed-performance knob.
    /// </para>
    /// </remarks>
    internal static TimeSpan ResolveConfiguredTimeout()
    {
        if (_configuredTimeout is TimeSpan fromCommandLine)
            return fromCommandLine;

        return TryParseTimeoutSeconds(
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_HTTP_TIMEOUT_IN_SECONDS"),
            out TimeSpan fromEnvironment)
            ? fromEnvironment
            : DefaultTimeout;
    }

    /// <summary>
    /// Parses a request timeout expressed as whole seconds, accepting [1, 3600].
    /// </summary>
    /// <remarks>
    /// Shared by <c>--http-timeout</c> and <c>DOTNET_INSPECT_HTTP_TIMEOUT_IN_SECONDS</c> so the
    /// two cannot drift apart on what they accept. Out-of-range values are rejected rather than
    /// clamped: clamping would turn a mistyped value into a silent one hour timeout, which is
    /// worse than the documented default. The flag reports the rejection and stops; the
    /// environment variable falls back to the default, because a stale variable in a shell
    /// profile should not make every command fail.
    /// </remarks>
    public static bool TryParseTimeoutSeconds(string? value, out TimeSpan timeout)
    {
        timeout = default;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
            return false;

        var requested = TimeSpan.FromSeconds(seconds);
        if (requested < TimeSpan.FromSeconds(1) || requested > MaximumConfiguredTimeout)
            return false;

        timeout = requested;
        return true;
    }

    /// <summary>
    /// Creates an HttpClient hardened against SSRF for fetching content from URLs that originate
    /// in untrusted artifacts (e.g. SourceLink URLs embedded in a PDB). Every TCP connection —
    /// including redirect hops — is validated to resolve to a public IP address, and automatic
    /// redirects are capped. Offline mode and DEBUG traffic logging are still honored.
    /// </summary>
    /// <remarks>
    /// The 30 second default here is fixed on purpose. It is not read from
    /// <c>DOTNET_INSPECT_HTTP_TIMEOUT_IN_SECONDS</c>, because the URLs this client visits come
    /// from untrusted artifacts rather than from a feed the operator chose.
    /// </remarks>
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
