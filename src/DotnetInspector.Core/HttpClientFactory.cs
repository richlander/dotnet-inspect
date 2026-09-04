using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DotnetInspector.Networking;

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
    private static readonly ConcurrentDictionary<string, Lazy<HttpClient>>
        _packageSourceClients = new(StringComparer.Ordinal);
    private static IDisposable? _networkTrafficLoggingSubscription;
    private static Func<HttpMessageHandler, HttpMessageHandler>? _authenticationDecorator;
    private static Func<string, HttpMessageHandler>?
        _packageSourceHandlerOverride;

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
        _packageSourceHandlerOverride = null;
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
    public static HttpClient CreateClient() =>
        CreateClient(includeAuthentication: true);

    /// <summary>
    /// Creates a standard client that honors offline and timeout policy without
    /// adopting package-source credentials.
    /// </summary>
    /// <remarks>
    /// The caller owns the returned client. Use this for fixed public endpoints
    /// such as the built-in NuGet Gallery transport, where an ambient credential
    /// retry must never cross the transport boundary.
    /// </remarks>
    public static HttpClient CreateCredentialFreeClient() =>
        CreateClient(includeAuthentication: false);

    /// <summary>
    /// Creates the owned handler chain for a standard credential-free client.
    /// Offline, telemetry, and counting policy are retained.
    /// </summary>
    /// <remarks>The caller owns and must dispose the returned handler.</remarks>
    public static HttpMessageHandler CreateCredentialFreeHandler()
    {
        HttpClientFactoryOptions options = _options;
        return CreateClientHandler(
            options,
            includeAuthentication: false);
    }

    /// <summary>
    /// Creates the owned credential-free handler chain for one explicitly
    /// configured package source.
    /// </summary>
    /// <remarks>
    /// The configured host and port may resolve to private addresses. Redirect
    /// handling remains disabled because the source client owns bounded
    /// redirect authorization. This desktop transport is unavailable in
    /// Browser/Wasm.
    /// </remarks>
    public static HttpMessageHandler
        CreateCredentialFreePackageSourceHandler(string sourceUrl)
    {
        if (OperatingSystem.IsBrowser())
        {
            throw new PlatformNotSupportedException(
                "Configured package-source handlers are unavailable in Browser/Wasm.");
        }

        if (_packageSourceHandlerOverride is not null)
            return _packageSourceHandlerOverride(sourceUrl);

        Uri source = ParsePackageSource(sourceUrl);
        string trustedHost = source.IdnHost;
        int trustedPort = source.Port;
        HttpMessageHandler handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            UseProxy = false,
            ConnectCallback = (context, cancellationToken) =>
                NetworkDestinationPolicy.ConnectAsync(
                    context,
                    trustedHost,
                    trustedPort,
                    cancellationToken),
        };

        if (_options.Offline)
            handler = new OfflineHandler(handler);

        if (InfoTracker.Enabled)
            handler = new CountingHandler(handler);

        handler = new NetworkTelemetryHandler(
            handler,
            NetworkClientKinds.Shared);
        return new UserAgentHandler(handler, UserAgent);
    }

    internal static void SetPackageSourceHandlerForTesting(
        Func<string, HttpMessageHandler>? factory) =>
        _packageSourceHandlerOverride = factory;

    private static HttpClient CreateClient(bool includeAuthentication)
    {
        HttpClientFactoryOptions options = _options;
        HttpMessageHandler handler = CreateClientHandler(
            options,
            includeAuthentication);
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        client.Timeout = options.DefaultTimeout;
        return client;
    }

    private static HttpMessageHandler CreateClientHandler(
        HttpClientFactoryOptions options,
        bool includeAuthentication)
    {
        HttpMessageHandler handler = CreateTransportHandler(
            OperatingSystem.IsBrowser(),
            includeAuthentication);

        if (options.Offline)
            handler = new OfflineHandler(handler);

        if (InfoTracker.Enabled)
            handler = new CountingHandler(handler);

        handler = new NetworkTelemetryHandler(handler, NetworkClientKinds.Shared);

        // Outermost, so each replayed attempt is observed by the telemetry and counting
        // handlers below it. A 401 followed by an authenticated retry really is two requests.
        if (includeAuthentication && _authenticationDecorator is not null)
        {
            handler = _authenticationDecorator(handler);
        }

        return handler;
    }

    internal static HttpClientHandler CreateTransportHandler(
        bool isBrowser,
        bool includeAuthentication)
    {
        var transport = new HttpClientHandler();
        if (isBrowser)
            return transport;

        transport.AutomaticDecompression = DecompressionMethods.All;

        if (!includeAuthentication)
        {
            transport.UseCookies = false;
            transport.UseDefaultCredentials = false;
            transport.PreAuthenticate = false;
            transport.AllowAutoRedirect = false;
        }

        return transport;
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
                NetworkDestinationPolicy.ConnectAsync(
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
        => await NetworkDestinationPolicy.ConnectAsync(
            context,
            trustedHost: null,
            trustedPort: null,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Returns true for destinations that are not globally reachable, including embedded
    /// non-public IPv4 destinations in IPv6 translation and transition addresses. Pinned by
    /// <c>HttpClientFactoryTests.UntrustedFetchAddressClassification_MatchesNonPublicContract</c>.
    /// </summary>
    private static bool IsNonPublic(IPAddress ip) =>
        NetworkDestinationPolicy.IsNonPublic(ip);
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

internal sealed class UserAgentHandler(
    HttpMessageHandler inner,
    string productName) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.UserAgent.Count == 0)
            request.Headers.UserAgent.ParseAdd(productName);
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
