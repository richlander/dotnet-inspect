using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotnetInspector.Networking;
using NuGet.Versioning;

namespace NuGetFetch;

/// <summary>
/// Identifies the transport family used by a package source.
/// </summary>
public enum PackageSourceKind
{
    /// <summary>A standard NuGet v3 service-index source.</summary>
    NuGetV3,

    /// <summary>The built-in NuGet Gallery browser transport.</summary>
    NuGetGallery,

    /// <summary>A package source backed by a local directory.</summary>
    LocalFolder,
}

/// <summary>
/// Operations a package source client can perform.
/// </summary>
[Flags]
public enum PackageSourceCapabilities
{
    /// <summary>No package operations are supported.</summary>
    None = 0,

    /// <summary>Keyword or package-identity search.</summary>
    Search = 1 << 0,

    /// <summary>Version enumeration for a package ID.</summary>
    VersionEnumeration = 1 << 1,

    /// <summary>Exact package payload acquisition.</summary>
    PackagePayload = 1 << 2,

    /// <summary>Exact symbol-package payload acquisition.</summary>
    SymbolPayload = 1 << 3,

    /// <summary>Exact bounded package-manifest acquisition.</summary>
    Manifest = 1 << 4,
}

/// <summary>
/// Stable producer identity shared by every transport that represents one source.
/// </summary>
public sealed record PackageSourceIdentity
{
    private PackageSourceIdentity(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the canonical producer key.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the canonical NuGet.org producer identity.
    /// </summary>
    public static PackageSourceIdentity NuGetOrg { get; } =
        ForHttpEndpoint(new Uri("https://api.nuget.org/v3/index.json"));

    /// <summary>
    /// Creates an identity for an absolute HTTP or HTTPS source endpoint.
    /// </summary>
    public static PackageSourceIdentity ForHttpEndpoint(Uri endpoint)
    {
        PackageSourceIdentity identity = ForProducerEndpoint(endpoint);
        return new PackageSourceIdentity(
            $"{identity.Value}{NormalizeEscapes(endpoint.Query)}"
            + NormalizeEscapes(endpoint.Fragment));
    }

    internal static PackageSourceIdentity ForProducerEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri
            || (endpoint.Scheme != Uri.UriSchemeHttp
                && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "A package source endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(endpoint));
        }

        string idnHost;
        try
        {
            idnHost = endpoint.IdnHost;
        }
        catch (UriFormatException exception)
        {
            throw new ArgumentException(
                "A package source endpoint must have a usable host.",
                nameof(endpoint),
                exception);
        }

        string host = endpoint.HostNameType == UriHostNameType.IPv6
            ? $"[{idnHost}]"
            : idnHost.ToLowerInvariant();
        var origin =
            $"{endpoint.Scheme.ToLowerInvariant()}://{host}:{endpoint.Port}";
        string absolutePath = endpoint.AbsolutePath;
        string path = NormalizeEscapes(
            absolutePath.EndsWith("/", StringComparison.Ordinal)
                ? absolutePath[..^1]
                : absolutePath);
        return new PackageSourceIdentity($"{origin}{path}");
    }

    /// <inheritdoc/>
    public override string ToString() => Value;

    private static string NormalizeEscapes(string value)
    {
        if (!value.Contains('%', StringComparison.Ordinal))
            return value;

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '%'
                && i + 2 < value.Length
                && Uri.IsHexDigit(value[i + 1])
                && Uri.IsHexDigit(value[i + 2]))
            {
                builder.Append('%')
                    .Append(char.ToUpperInvariant(value[i + 1]))
                    .Append(char.ToUpperInvariant(value[i + 2]));
                i += 2;
            }
            else
            {
                builder.Append(value[i]);
            }
        }

        return builder.ToString();
    }

}

/// <summary>
/// Credential-free configuration for one registered package source.
/// </summary>
public sealed record PackageSourceDescriptor
{
    private PackageSourceDescriptor(
        string id,
        string displayName,
        PackageSourceKind kind,
        PackageSourceIdentity identity,
        Uri? endpoint,
        bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Id = id;
        DisplayName = displayName;
        Kind = kind;
        Identity = identity;
        Endpoint = endpoint;
        Enabled = enabled;
    }

    /// <summary>
    /// Gets the source registry identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the user-facing source name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the transport family.
    /// </summary>
    public PackageSourceKind Kind { get; }

    /// <summary>
    /// Gets the producer identity shared across transports.
    /// </summary>
    public PackageSourceIdentity Identity { get; }

    /// <summary>
    /// Gets the transport endpoint, when the source kind requires one.
    /// </summary>
    public Uri? Endpoint { get; }

    /// <summary>
    /// Gets whether the source is enabled in portable configuration.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets the built-in Gallery descriptor. Its producer identity is NuGet.org,
    /// but it has no configurable v3 endpoint.
    /// </summary>
    public static PackageSourceDescriptor NuGetGallery { get; } =
        new(
            "nuget-gallery",
            "NuGet Gallery",
            PackageSourceKind.NuGetGallery,
            PackageSourceIdentity.NuGetOrg,
            endpoint: null,
            enabled: true);

    /// <summary>
    /// Creates a standard NuGet v3 source descriptor.
    /// </summary>
    public static PackageSourceDescriptor NuGetV3(
        string id,
        string displayName,
        Uri serviceIndex,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(serviceIndex);
        if (!Uri.TryCreate(
                serviceIndex.OriginalString,
                UriKind.Absolute,
                out Uri? portableEndpoint))
        {
            throw new ArgumentException(
                "A portable package source endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(serviceIndex));
        }

        if (portableEndpoint.UserInfo.Length > 0)
        {
            throw new ArgumentException(
                "A portable package source endpoint cannot contain user information.",
                nameof(serviceIndex));
        }

        if (portableEndpoint.Query.Length > 0
            || portableEndpoint.Fragment.Length > 0)
        {
            throw new ArgumentException(
                "A portable package source endpoint cannot contain a query or fragment.",
                nameof(serviceIndex));
        }

        PackageSourceIdentity identity =
            PackageSourceIdentity.ForHttpEndpoint(portableEndpoint);
        return new PackageSourceDescriptor(
            id,
            displayName,
            PackageSourceKind.NuGetV3,
            identity,
            portableEndpoint,
            enabled);
    }
}

/// <summary>
/// Protocol-independent package source operations.
/// </summary>
public interface IPackageSourceClient : IDisposable
{
    /// <summary>Gets the producer represented by this transport.</summary>
    PackageSourceIdentity Identity { get; }

    /// <summary>Gets the transport family.</summary>
    PackageSourceKind Kind { get; }

    /// <summary>Gets the operations implemented by this runtime client.</summary>
    PackageSourceCapabilities Capabilities { get; }

    /// <summary>Searches for packages.</summary>
    Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default);

    /// <summary>Searches for packages whose IDs start with a prefix.</summary>
    Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(
        string prefix,
        int take = 100,
        bool prerelease = false,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the versions reported for a package ID.</summary>
    Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets an exact bounded package manifest without acquiring the package archive.</summary>
    Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);

    /// <summary>Gets an exact package payload owned by the returned stream.</summary>
    Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);

    /// <summary>Gets an exact symbol-package payload when supported and available.</summary>
    Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Raised when a registered source kind has no runtime client implementation.
/// </summary>
public sealed class PackageSourceClientUnavailableException(
    PackageSourceKind kind)
    : NotSupportedException(
        $"Package source kind '{kind}' does not have a runtime client implementation.")
{
    /// <summary>Gets the source kind without a runtime implementation.</summary>
    public PackageSourceKind Kind { get; } = kind;
}

/// <summary>
/// Creates runtime clients without exposing transport construction to consumers.
/// </summary>
public static class PackageSourceClientFactory
{
    /// <summary>
    /// Adapts the existing desktop source model to a typed runtime client.
    /// </summary>
    public static IPackageSourceClient Create(
        PackageSource source,
        NuGetFetchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? endpoint)
            || endpoint.IsFile)
        {
            throw new PackageSourceClientUnavailableException(
                PackageSourceKind.LocalFolder);
        }

        return new NuGetV3PackageSourceClient(
            PackageSourceIdentity.ForProducerEndpoint(endpoint),
            endpoint,
            CreateOwnedTransport(endpoint),
            options ?? new NuGetFetchOptions(),
            source.Credential);
    }

    /// <summary>
    /// Adapts the existing desktop source model to a typed runtime client
    /// using a caller-owned transport.
    /// </summary>
    /// <remarks>
    /// The returned source client does not dispose <paramref name="client"/>.
    /// The caller must keep it alive until every payload stream returned by
    /// the source client has been disposed.
    /// </remarks>
    internal static IPackageSourceClient Create(
        PackageSource source,
        HttpClient client,
        NuGetFetchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(client);
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? endpoint)
            || endpoint.IsFile)
        {
            throw new PackageSourceClientUnavailableException(
                PackageSourceKind.LocalFolder);
        }

        HttpClient transport = OperatingSystem.IsBrowser()
            ? client
            : CreateBorrowedDesktopTransport(client);
        return new NuGetV3PackageSourceClient(
            PackageSourceIdentity.ForProducerEndpoint(endpoint),
            endpoint,
            transport,
            options ?? new NuGetFetchOptions(),
            source.Credential,
            ownsClient: !OperatingSystem.IsBrowser());
    }

    internal static Uri NormalizeServiceIndexEndpoint(Uri endpoint)
    {
        string original = endpoint.OriginalString;
        int query = original.IndexOf('?');
        int fragment = original.IndexOf('#');
        int suffixStart = query < 0
            ? fragment
            : fragment < 0
                ? query
                : Math.Min(query, fragment);
        string path = suffixStart < 0
            ? original
            : original[..suffixStart];
        string suffix = suffixStart < 0
            ? ""
            : original[suffixStart..];

        string pathWithoutOptionalSlash =
            path.EndsWith("/", StringComparison.Ordinal)
                ? path[..^1]
                : path;
        if (endpoint.AbsolutePath.TrimEnd('/').EndsWith(
                "index.json",
                StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(
                $"{pathWithoutOptionalSlash}{suffix}",
                UriKind.Absolute);
        }

        if (endpoint.AbsolutePath is not ("" or "/"))
            return endpoint;

        return new Uri(
            $"{pathWithoutOptionalSlash}/v3/index.json{suffix}",
            UriKind.Absolute);
    }

    /// <summary>
    /// Creates a runtime client from credential-free configuration and optional
    /// ephemeral credentials.
    /// </summary>
    public static IPackageSourceClient Create(
        PackageSourceDescriptor descriptor,
        NuGetFetchOptions? options = null,
        PackageSourceCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        options ??= new NuGetFetchOptions();
        if (descriptor.Kind == PackageSourceKind.NuGetGallery)
        {
            if (credential is not null)
            {
                throw new ArgumentException(
                    "The built-in NuGet Gallery source does not accept credentials.",
                    nameof(credential));
            }

            throw new InvalidOperationException(
                "The NuGet Gallery source requires its isolated transport. Use CreateGallery instead.");
        }

        return descriptor.Kind switch
        {
            PackageSourceKind.NuGetV3 when descriptor.Endpoint is not null =>
                new NuGetV3PackageSourceClient(
                    descriptor,
                    CreateOwnedTransport(descriptor.Endpoint),
                    options,
                    credential),
            _ => throw new PackageSourceClientUnavailableException(
                descriptor.Kind),
        };
    }

    internal static HttpMessageHandler CreateV3TransportHandler(
        Uri source,
        bool isBrowser)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (isBrowser)
            return CreateCredentialFreeTransportHandler(isBrowser: true);

        string trustedHost;
        try
        {
            trustedHost = source.IdnHost;
        }
        catch (UriFormatException exception)
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.",
                exception);
        }

        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            Credentials = null,
            PreAuthenticate = false,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = (context, cancellationToken) =>
                NetworkDestinationPolicy.ConnectAsync(
                    context,
                    trustedHost,
                    source.Port,
                    cancellationToken),
        };
    }

    /// <summary>
    /// Creates the built-in Gallery client with an isolated, credential-free
    /// transport owned by the returned client.
    /// </summary>
    public static IPackageSourceClient CreateGallery(
        NuGetFetchOptions? options = null) =>
        new NuGetGalleryPackageSourceClient(
            CreateGalleryTransport(),
            options ?? new NuGetFetchOptions());

    /// <summary>
    /// Creates the built-in Gallery client over a caller-created,
    /// credential-free transport owned by the returned client.
    /// </summary>
    public static IPackageSourceClient CreateGallery(
        HttpMessageHandler ownedCredentialFreeTransport,
        NuGetFetchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(ownedCredentialFreeTransport);
        return new NuGetGalleryPackageSourceClient(
            CreateGalleryTransport(
                ownedCredentialFreeTransport,
                OperatingSystem.IsBrowser()),
            options ?? new NuGetFetchOptions());
    }

    /// <summary>
    /// Creates the built-in Gallery client over a caller-owned transport.
    /// </summary>
    /// <remarks>
    /// The returned source client does not dispose <paramref name="client"/>.
    /// </remarks>
    public static IPackageSourceClient CreateGallery(
        HttpClient client,
        NuGetFetchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new NuGetGalleryPackageSourceClient(
            client,
            options ?? new NuGetFetchOptions(),
            ownsClient: false);
    }

    internal static IPackageSourceClient Create(
        PackageSource source,
        HttpMessageHandler transport,
        NuGetFetchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transport);
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? endpoint)
            || endpoint.IsFile)
        {
            throw new PackageSourceClientUnavailableException(
                PackageSourceKind.LocalFolder);
        }

        return new NuGetV3PackageSourceClient(
            PackageSourceIdentity.ForProducerEndpoint(endpoint),
            endpoint,
            CreateOwnedTransport(endpoint, transport),
            options ?? new NuGetFetchOptions(),
            source.Credential);
    }

    internal static IPackageSourceClient Create(
        PackageSourceDescriptor descriptor,
        HttpMessageHandler transport,
        NuGetFetchOptions? options = null,
        PackageSourceCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(transport);
        options ??= new NuGetFetchOptions();
        if (descriptor.Kind == PackageSourceKind.NuGetGallery)
        {
            if (credential is not null)
            {
                throw new ArgumentException(
                    "The built-in NuGet Gallery source does not accept credentials.",
                    nameof(credential));
            }

            throw new InvalidOperationException(
                "The NuGet Gallery source requires its isolated transport. Use CreateGallery instead.");
        }

        if (descriptor.Kind != PackageSourceKind.NuGetV3
            || descriptor.Endpoint is null)
        {
            throw new PackageSourceClientUnavailableException(
                descriptor.Kind);
        }

        return new NuGetV3PackageSourceClient(
            descriptor,
            CreateOwnedTransport(descriptor.Endpoint!, transport),
            options,
            credential);
    }

    private static HttpClient CreateOwnedTransport(
        Uri source,
        HttpMessageHandler? transport = null)
    {
        bool isBrowser = OperatingSystem.IsBrowser();
        HttpMessageHandler handler = transport
            ?? CreateV3TransportHandler(source, isBrowser);
        if (!isBrowser)
        {
            handler = new NuGetCredentialRedirectHandler(handler);
        }

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private static HttpClient CreateBorrowedDesktopTransport(
        HttpClient client) =>
        new(
            new NuGetCredentialRedirectHandler(
                new BorrowedHttpClientHandler(client)),
            disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

    private static HttpClient CreateGalleryTransport()
    {
        bool isBrowser = OperatingSystem.IsBrowser();
        HttpMessageHandler handler = CreateGalleryTransportHandler(isBrowser);
        return CreateGalleryTransport(handler, isBrowser);
    }

    internal static HttpClient CreateGalleryTransport(
        HttpMessageHandler handler,
        bool isBrowser)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!isBrowser)
        {
            handler = new NuGetCredentialRedirectHandler(handler);
        }

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    internal static HttpClientHandler CreateGalleryTransportHandler(
        bool isBrowser)
    {
        HttpClientHandler handler =
            CreateCredentialFreeTransportHandler(isBrowser);
        if (!isBrowser)
        {
            handler.AutomaticDecompression =
                System.Net.DecompressionMethods.All;
        }

        return handler;
    }

    internal static HttpClientHandler CreateCredentialFreeTransportHandler(
        bool isBrowser)
    {
        var handler = new HttpClientHandler();
        if (isBrowser)
        {
            return handler;
        }

        handler.UseCookies = false;
        handler.UseDefaultCredentials = false;
        handler.PreAuthenticate = false;
        handler.AllowAutoRedirect = false;
        return handler;
    }
}

internal sealed class NuGetV3PackageSourceClient : IPackageSourceClient
{
    private readonly PackageSourceIdentity _identity;
    private readonly Uri _endpoint;
    private readonly PackageSourceCredential? _credential;
    private readonly HttpClient _client;
    private readonly NuGetV3PackageResourceClient _packageResources;
    private readonly NuGetFetchOptions _options;
    private readonly TimeSpan _clientTimeout;
    private readonly bool _ownsClient;

    public NuGetV3PackageSourceClient(
        PackageSourceDescriptor descriptor,
        HttpClient client,
        NuGetFetchOptions options,
        PackageSourceCredential? credential,
        bool ownsClient = true)
        : this(
            descriptor.Identity,
            descriptor.Endpoint!,
            client,
            options,
            credential,
            ownsClient)
    {
    }

    public NuGetV3PackageSourceClient(
        PackageSourceIdentity identity,
        Uri endpoint,
        HttpClient client,
        NuGetFetchOptions options,
        PackageSourceCredential? credential,
        bool ownsClient = true)
    {
        _identity = identity;
        _endpoint =
            PackageSourceClientFactory.NormalizeServiceIndexEndpoint(endpoint);
        _credential = credential;
        _client = client;
        _options = NuGetFetchOptions.Validate(options);
        _clientTimeout = client.Timeout;
        _packageResources = new NuGetV3PackageResourceClient(client);
        _ownsClient = ownsClient;
    }

    public PackageSourceIdentity Identity => _identity;
    public PackageSourceKind Kind => PackageSourceKind.NuGetV3;
    internal TimeSpan TransportTimeout => _client.Timeout;
    public PackageSourceCapabilities Capabilities =>
        PackageSourceCapabilities.Search
        | PackageSourceCapabilities.VersionEnumeration
        | PackageSourceCapabilities.Manifest
        | PackageSourceCapabilities.PackagePayload;

    public async Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default) =>
        await PackageSourceOperation.CaptureAsync(
            Identity,
            Kind,
            PackageSourceCapabilities.Search,
            async () =>
            {
                using var operation = new NuGetOperationDeadline(
                    _options,
                    _clientTimeout,
                    cancellationToken);
                IReadOnlyList<string> endpoints =
                    await NuGetV3SearchResourceDiscovery
                        .GetSearchEndpointsAsync(
                            _client,
                            _endpoint,
                            _credential,
                            _options,
                            operation)
                        .ConfigureAwait(false);
                if (endpoints.Count == 0)
                {
                    throw new NuGetSourceCapabilityUnavailableException();
                }

                Exception? lastFailure = null;
                foreach (string endpoint in endpoints)
                {
                    try
                    {
                        var search = new SearchService(
                            _client,
                            endpoint,
                            _options,
                            retryTransientRequests: true);
                        IReadOnlyList<SearchResult> results =
                            await search.SearchAsync(
                                    query,
                                    take,
                                    prerelease,
                                    NuGetSourceRequest
                                        .AuthenticationForEndpoint(
                                            NuGetSourceRequest.EndpointUrl(
                                                _endpoint),
                                            endpoint,
                                            _credential),
                                    operation)
                                .ConfigureAwait(false);
                        return PackageSourceProjection.ProjectSearch(
                            results,
                            Identity,
                            operation);
                    }
                    catch (Exception exception)
                        when (CanFailOverSearchEndpoint(exception))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        operation.ThrowIfExpired();
                        lastFailure = exception;
                    }
                }

                throw lastFailure switch
                {
                    InvalidOperationException invalidResponse
                        when invalidResponse
                            is not NuGetSourceResponseException =>
                        new NuGetSourceResponseException(
                            "The package source search response did not satisfy the search contract.",
                            invalidResponse),
                    OperationCanceledException canceled =>
                        new IOException(
                            "The package source search request was canceled by the transport.",
                            canceled),
                    not null => lastFailure,
                    _ => new NuGetSourceResponseException(
                        "The package source did not provide a usable search endpoint."),
                };
            },
            cancellationToken).ConfigureAwait(false);

    public Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(
        string prefix,
        int take = 100,
        bool prerelease = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            PackageSourceOperation.Unsupported<PackageSearchResult>(
                Identity,
                Kind,
                PackageSourceCapabilities.Search));

    public async Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        PackageCoordinateValidation.ValidatePackageId(
            packageId,
            nameof(packageId));
        return await PackageSourceOperation.CaptureAsync(
            Identity,
            Kind,
            PackageSourceCapabilities.VersionEnumeration,
            async () =>
            {
                using var operation = new NuGetOperationDeadline(
                    _options,
                    _clientTimeout,
                    cancellationToken);
                IReadOnlyList<string> versions =
                    await _packageResources.GetVersionsAsync(
                        packageId,
                        NuGetSourceRequest.EndpointUrl(_endpoint),
                        _credential,
                        _options,
                        operation,
                        useNuGetOrgShortcut: false,
                        retryTransientRequests: true).ConfigureAwait(false);
                return PackageSourceProjection.ProjectVersions(
                    packageId,
                    versions,
                    Identity,
                    PackageDiscoveryContract.CompleteVersionEnumeration,
                    PackageListingState.Unknown,
                    hasAuthoritativeListingState: false,
                    operation);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return await PackageSourceOperation.CaptureAsync(
            Identity,
            Kind,
            PackageSourceCapabilities.PackagePayload,
            async () =>
            {
                var operation = new NuGetOperationDeadline(
                    _options,
                    _clientTimeout,
                    cancellationToken);
                try
                {
                    (Stream content, long? advertisedLength) =
                        await _packageResources.GetPackageAsync(
                            coordinate.PackageId,
                            coordinate.Version,
                            NuGetSourceRequest.EndpointUrl(_endpoint),
                            _credential,
                            _options,
                            operation,
                            useNuGetOrgShortcut: false,
                            retryTransientRequests: true).ConfigureAwait(false);
                    return new PackageSourcePayload(
                        coordinate,
                        Identity,
                        Kind,
                        PackageSourcePayloadKind.Package,
                        content,
                        advertisedLength);
                }
                catch
                {
                    operation.Dispose();
                    throw;
                }
            },
            cancellationToken,
            coordinate).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return await PackageSourceOperation.CaptureAsync(
            Identity,
            Kind,
            PackageSourceCapabilities.Manifest,
            async () =>
            {
                using var operation = new NuGetOperationDeadline(
                    _options,
                    _clientTimeout,
                    cancellationToken);
                return new PackageSourceManifest(
                    coordinate,
                    Identity,
                    Kind,
                    await _packageResources.GetManifestAsync(
                        coordinate.PackageId,
                        coordinate.Version,
                        NuGetSourceRequest.EndpointUrl(_endpoint),
                        _credential,
                        _options,
                        operation,
                        useNuGetOrgShortcut: false,
                        retryTransientRequests: true).ConfigureAwait(false));
            },
            cancellationToken,
            coordinate).ConfigureAwait(false);
    }

    public Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return Task.FromResult(
            PackageSourceOperation.Unsupported<PackageSourcePayload>(
                Identity,
                Kind,
                PackageSourceCapabilities.SymbolPayload,
                coordinate));
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }

    private static bool CanFailOverSearchEndpoint(Exception exception) =>
        (exception is
                HttpRequestException
                or JsonException
                or InvalidOperationException
                or OperationCanceledException
                or IOException
                or TimeoutException)
        && exception is not HttpRequestException
        {
            StatusCode:
                System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden,
        };
}

internal static partial class PackageCoordinateValidation
{
    public static bool IsValidPackageId(string? packageId) =>
        packageId is
            { Length: > 0 and <= PackageSourceCoordinate.MaxPackageIdLength }
        && PackageIdPattern().IsMatch(packageId);

    public static bool IsValidPackageVersion(string? version) =>
        version is not null
        && version.AsSpan().Trim().Length == version.Length
        && NuGetVersion.TryParse(version, out _);

    public static void ValidatePackageId(
        string? packageId,
        string parameterName)
    {
        if (!IsValidPackageId(packageId))
        {
            throw new ArgumentException(
                "A package ID must use the NuGet package ID grammar.",
                parameterName);
        }
    }

    public static string NormalizeVersion(
        string? version,
        string parameterName)
    {
        if (!IsValidPackageVersion(version)
            || !NuGetVersion.TryParse(version, out NuGetVersion? parsed))
        {
            throw new ArgumentException(
                "A package version must be a valid NuGet version without surrounding whitespace.",
                parameterName);
        }

        return parsed.ToNormalizedString().ToLowerInvariant();
    }

    [GeneratedRegex(
        @"^\w+(?:[.-]\w+)*\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();
}
