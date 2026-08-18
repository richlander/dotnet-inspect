using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DotnetInspector.Core;

/// <summary>
/// Process-wide configuration captured by clients when they are constructed.
/// </summary>
public sealed record HttpClientFactoryOptions
{
    public static TimeSpan BaselineTimeout { get; } = TimeSpan.FromSeconds(30);

    public bool Offline { get; init; }

    public TimeSpan DefaultTimeout { get; init; } = BaselineTimeout;
}

/// <summary>
/// Factory for creating HttpClient instances with consistent configuration.
/// Call <see cref="Initialize"/> once at startup to configure new clients.
/// </summary>
public static class HttpClientFactory
{
    private const string UserAgent = "dotnet-inspect";
    private static HttpClientFactoryOptions _options = new();
    private static HttpClient? _shared;
    private static HttpClient? _sharedUntrustedFetch;
    private static HttpClient? _untrustedFetchOverride;
    private static readonly IPAddress PcpAnycastV6 = IPAddress.Parse("2001:1::1");
    private static readonly IPAddress TurnAnycastV6 = IPAddress.Parse("2001:1::2");
    private static readonly IPAddress DnssdAnycastV6 = IPAddress.Parse("2001:1::3");
    private static readonly ConcurrentDictionary<string, Lazy<HttpClient>>
        _packageSourceClients = new(StringComparer.Ordinal);
    private static IDisposable? _networkTrafficLoggingSubscription;
    private static Func<HttpMessageHandler, HttpMessageHandler>? _authenticationDecorator;

    /// <summary>
    /// Configure the factory before first use. Safe to call multiple times;
    /// the shared instance is created lazily on first access.
    /// </summary>
    /// <param name="options">Configuration captured by clients constructed after this call.</param>
    /// <remarks>
    /// "Before first use" is a real precondition, not advice, and it covers both settings.
    /// Each is consumed when a client is constructed rather than per request:
    /// <see cref="HttpClientFactoryOptions.DefaultTimeout"/> becomes <see cref="HttpClient.Timeout"/>,
    /// and <see cref="HttpClientFactoryOptions.Offline"/> decides whether an offline handler joins
    /// the chain. A call made once <see cref="Shared"/> exists therefore governs later <see cref="CreateClient"/>
    /// calls and leaves that instance alone. <see cref="ResetSharedForTesting"/> is how the
    /// tests reconfigure; the CLI is unaffected because <c>Program.cs</c> calls this in
    /// top-level code before any command runs. Pinned by
    /// <c>HttpClientFactoryTests.Initialize_OnceSharedExists_GovernsOnlyLaterClients</c>.
    /// </remarks>
    public static void Initialize(HttpClientFactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public static bool IsOffline => _options.Offline;

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
        foreach (Lazy<HttpClient> client in _packageSourceClients.Values)
        {
            if (client.IsValueCreated)
                client.Value.Dispose();
        }
        _packageSourceClients.Clear();
        _networkTrafficLoggingSubscription?.Dispose();
        _networkTrafficLoggingSubscription = null;
    }

    /// <summary>
    /// Gets the shared HttpClient instance for the application.
    /// This instance should be used throughout the app lifetime and not disposed.
    /// </summary>
    public static HttpClient Shared => _shared ??= CreateClient();

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
    public static HttpClient CreateClient()
    {
        HttpClientFactoryOptions options = _options;
        HttpMessageHandler handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };

        if (options.Offline)
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
        client.Timeout = options.DefaultTimeout;
        return client;
    }

    /// <summary>
    /// Creates an HttpClient hardened against SSRF for fetching content from URLs that originate
    /// in untrusted artifacts (e.g. SourceLink URLs embedded in a PDB). Every TCP connection —
    /// including redirect hops — is validated to resolve to a public IP address, and automatic
    /// redirects are capped. Offline mode and DEBUG traffic logging are still honored.
    /// </summary>
    /// <remarks>
    /// The 30 second default here is fixed on purpose. It does not follow
    /// <see cref="HttpClientFactoryOptions.DefaultTimeout"/>, because the URLs this client visits
    /// come from untrusted artifacts rather than from a feed the operator chose. Pinned by
    /// <c>HttpClientFactoryTests.CreateUntrustedFetchClient_DoesNotFollowTheConfiguredDefaultTimeout</c>.
    /// </remarks>
    public static HttpClient CreateUntrustedFetchClient()
    {
        HttpMessageHandler handler = CreateUntrustedSocketsHandler();

        if (_options.Offline)
            handler = new OfflineHandler(handler);

        if (InfoTracker.Enabled)
            handler = new CountingHandler(handler);

        handler = new NetworkTelemetryHandler(handler, NetworkClientKinds.UntrustedFetch);

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        client.Timeout = HttpClientFactoryOptions.BaselineTimeout;
        return client;
    }

    internal static SocketsHttpHandler CreateUntrustedSocketsHandler() =>
        new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseProxy = false,
            ConnectCallback = SsrfGuardedConnectAsync,
        };

    /// <summary>
    /// Gets the process-lifetime credential-capable client for one explicitly configured
    /// package-source origin.
    /// </summary>
    /// <remarks>
    /// Clients are shared by scheme, host, and port so package audits reuse DNS, TCP, and TLS
    /// state without extending the private-address exception to another origin. Do not dispose
    /// the returned client.
    /// </remarks>
    public static HttpClient GetPackageSourceClient(string sourceUrl)
    {
        Uri source = ParsePackageSource(sourceUrl);
        string originKey =
            $"{source.Scheme.ToLowerInvariant()}\n"
            + $"{source.IdnHost.ToLowerInvariant()}\n"
            + source.Port;
        var candidate = new Lazy<HttpClient>(
            () => CreatePackageSourceClient(source.AbsoluteUri),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return _packageSourceClients.GetOrAdd(originKey, candidate).Value;
    }

    /// <summary>
    /// Creates a credential-capable client for one explicitly configured package-source origin.
    /// Connections to that exact host and port may use private addresses; redirect and cross-origin
    /// connections must resolve entirely to public addresses.
    /// </summary>
    /// <remarks>The caller owns and must dispose the returned client.</remarks>
    public static HttpClient CreatePackageSourceClient(string sourceUrl)
    {
        Uri source = ParsePackageSource(sourceUrl);

        string trustedHost = source.IdnHost;
        int trustedPort = source.Port;
        HttpMessageHandler handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseProxy = false,
            ConnectCallback = (context, cancellationToken) =>
                ConnectWithTrustedOriginAsync(
                    context,
                    trustedHost,
                    trustedPort,
                    cancellationToken),
        };

        if (_options.Offline)
            handler = new OfflineHandler(handler);

        if (InfoTracker.Enabled)
            handler = new CountingHandler(handler);

        handler = new NetworkTelemetryHandler(handler, NetworkClientKinds.Shared);

        if (_authenticationDecorator is not null)
            handler = _authenticationDecorator(handler);

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        client.Timeout = _options.DefaultTimeout;
        return client;
    }

    private static Uri ParsePackageSource(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? source)
            || source.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "Package source must be an absolute HTTP or HTTPS URL.",
                nameof(sourceUrl));
        }

        return source;
    }

    private static async ValueTask<Stream> SsrfGuardedConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        => await ConnectWithTrustedOriginAsync(
            context,
            trustedHost: null,
            trustedPort: null,
            cancellationToken).ConfigureAwait(false);

    private static async ValueTask<Stream> ConnectWithTrustedOriginAsync(
        SocketsHttpConnectionContext context,
        string? trustedHost,
        int? trustedPort,
        CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0)
            throw new HttpRequestException($"Could not resolve host: {endpoint.Host}");

        bool isTrustedOrigin = trustedHost is not null
            && endpoint.Port == trustedPort
            && string.Equals(
                endpoint.Host,
                trustedHost,
                StringComparison.OrdinalIgnoreCase);
        if (!isTrustedOrigin)
        {
            foreach (var address in addresses)
            {
                if (IsNonPublic(address))
                    throw new HttpRequestException(
                        $"Blocked request to non-public address: {endpoint.Host} resolves to {address}");
            }
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

    /// <summary>
    /// Returns true for destinations that are not globally reachable, including embedded
    /// non-public IPv4 destinations in IPv6 translation and transition addresses. Pinned by
    /// <c>HttpClientFactoryTests.UntrustedFetchAddressClassification_MatchesNonPublicContract</c>.
    /// </summary>
    private static bool IsNonPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            return true;

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv4MappedToIPv6)
                return IsNonPublic(ip.MapToIPv4());

            if (ip.IsIPv6LinkLocal
                || ip.IsIPv6SiteLocal
                || ip.IsIPv6Multicast
                || ip.IsIPv6UniqueLocal)
            {
                return true;
            }

            byte[] address = ip.GetAddressBytes();

            // Deprecated IPv4-compatible addresses have an all-zero /96 prefix.
            if (HasPrefix(address, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 96))
                return true;

            // ISATAP can appear beneath other transition prefixes. Treat a private
            // embedded destination as non-public before a prefix-specific branch
            // can return a public verdict.
            if ((address[8] is 0x00 or 0x02)
                && address[9] == 0x00
                && address[10] == 0x5e
                && address[11] == 0xfe
                && IsNonPublic(new IPAddress(address.AsSpan(12, 4))))
            {
                return true;
            }

            // The globally reachable NAT64 prefix carries an IPv4 destination in its final
            // 32 bits. Apply the same policy to that destination rather than letting the
            // translation hide it.
            if (HasPrefix(
                    address,
                    [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0],
                    96))
            {
                return IsNonPublic(new IPAddress(address.AsSpan(12, 4)));
            }

            if (HasPrefix(address, [0x00, 0x64, 0xff, 0x9b, 0x00, 0x01], 48)
                || HasPrefix(address, [0x01, 0x00, 0, 0, 0, 0, 0, 0], 64)
                || HasPrefix(address, [0x01, 0x00, 0, 0, 0, 0, 0, 0x01], 64))
            {
                return true;
            }

            // 2001::/23 is reserved for IETF protocols. Only the registry entries explicitly
            // marked globally reachable are usable as ordinary public destinations.
            if (HasPrefix(address, [0x20, 0x01, 0x00], 23))
            {
                bool globallyReachable =
                    ip.Equals(PcpAnycastV6)
                    || ip.Equals(TurnAnycastV6)
                    || ip.Equals(DnssdAnycastV6)
                    || HasPrefix(address, [0x20, 0x01, 0x00, 0x03], 32)
                    || HasPrefix(address, [0x20, 0x01, 0x00, 0x04, 0x01, 0x12], 48)
                    || HasPrefix(address, [0x20, 0x01, 0x00, 0x20], 28)
                    || HasPrefix(address, [0x20, 0x01, 0x00, 0x30], 28);
                return !globallyReachable;
            }

            if (HasPrefix(address, [0x20, 0x01, 0x0d, 0xb8], 32)
                || HasPrefix(address, [0x3f, 0xff, 0x00], 20)
                || HasPrefix(address, [0x5f, 0x00], 16))
            {
                return true;
            }

            // 6to4 also embeds the routed IPv4 destination.
            if (HasPrefix(address, [0x20, 0x02], 16))
                return IsNonPublic(new IPAddress(address.AsSpan(2, 4)));

            // Public IPv6 unicast is allocated from 2000::/3. Explicit public
            // exceptions outside that range, such as NAT64, returned above.
            return !HasPrefix(address, [0x20], 3);
        }

        byte[] b = ip.GetAddressBytes();
        return b[0] switch
        {
            0 => true,                              // 0.0.0.0/8
            10 => true,                             // 10.0.0.0/8
            127 => true,                            // loopback
            169 when b[1] == 254 => true,           // link-local
            172 when b[1] >= 16 && b[1] <= 31 => true, // 172.16.0.0/12
            192 when b[1] == 0 && b[2] == 0
                && b[3] is not (9 or 10) => true,   // IETF protocol assignments
            192 when b[1] == 0 && b[2] == 2 => true, // TEST-NET-1
            192 when b[1] == 88 && b[2] == 99 => true, // deprecated 6to4 relay
            192 when b[1] == 168 => true,           // 192.168.0.0/16
            100 when b[1] >= 64 && b[1] <= 127 => true, // CGNAT 100.64.0.0/10
            198 when b[1] is 18 or 19 => true,      // benchmarking 198.18.0.0/15
            198 when b[1] == 51 && b[2] == 100 => true, // TEST-NET-2
            203 when b[1] == 0 && b[2] == 113 => true, // TEST-NET-3
            >= 224 => true,                         // multicast / reserved
            _ => false,
        };
    }

    private static bool HasPrefix(
        ReadOnlySpan<byte> address,
        ReadOnlySpan<byte> prefix,
        int prefixLength)
    {
        int wholeBytes = prefixLength / 8;
        if (!address[..wholeBytes].SequenceEqual(prefix[..wholeBytes]))
            return false;

        int remainingBits = prefixLength % 8;
        if (remainingBits == 0)
            return true;

        byte mask = (byte)(0xff << (8 - remainingBits));
        return (address[wholeBytes] & mask) == (prefix[wholeBytes] & mask);
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
