using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotnetInspector.Networking;
using InertText;
using NuGetFetch.Plugins;
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
    /// <summary>Gets the complete identity bound to this runtime client.</summary>
    PackageSourceResultIdentity Source { get; }

    /// <summary>Gets the operations implemented by this runtime client.</summary>
    PackageSourceCapabilities Capabilities { get; }

    /// <summary>Searches for packages.</summary>
    Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);

    /// <summary>Searches for packages whose IDs start with a prefix.</summary>
    Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(
        string prefix,
        int take = 100,
        bool prerelease = false,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);

    /// <summary>Gets the versions reported for a package ID.</summary>
    Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);

    /// <summary>Gets an exact bounded package manifest without acquiring the package archive.</summary>
    Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);

    /// <summary>Gets an exact package payload owned by the returned stream.</summary>
    Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);

    /// <summary>Gets an exact symbol-package payload when supported and available.</summary>
    Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);
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
public static partial class PackageSourceClientFactory
{
    private const string CanonicalNuGetOrgEndpoint =
        "https://api.nuget.org/v3/index.json";
    private static readonly object OwnerCapability = new();
    private static readonly PackageProducerIdentity CanonicalNuGetOrgProducer =
        CreateHttpProducerCore(
            NuGetSourceRequest.ProjectEndpoint(
                new Uri(CanonicalNuGetOrgEndpoint)));

    internal static PackageProducerIdentity NuGetOrgProducer =>
        CanonicalNuGetOrgProducer;

    internal static void RequireOwnerCapability(object? capability)
    {
        if (!ReferenceEquals(capability, OwnerCapability))
        {
            throw new InvalidOperationException(
                "NuGetFetch result construction requires its private owner capability.");
        }
    }

    /// <summary>
    /// Adapts the existing desktop source model to a typed runtime client.
    /// </summary>
    public static IPackageSourceClient Create(
        PackageSource source,
        PackageSourceAssociation association,
        NuGetFetchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(association);
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? endpoint)
            || endpoint.IsFile)
        {
            throw new PackageSourceClientUnavailableException(
                PackageSourceKind.LocalFolder);
        }

        return new NuGetV3PackageSourceClient(
            CreateResultFactory(
                endpoint,
                association,
                PackageSourceKind.NuGetV3),
            endpoint,
            CreateOwnedTransport(endpoint),
            options ?? new NuGetFetchOptions(),
            source.Credential);
    }

    /// <summary>
    /// Adapts the existing desktop source model to a typed runtime client with
    /// source-scoped plugin authentication.
    /// </summary>
    public static IPackageSourceClient CreateWithPluginAuthentication(
        PackageSource source,
        PackageSourceAssociation association,
        PluginAuthenticationContext authenticationContext,
        NuGetFetchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(association);
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? endpoint)
            || endpoint.IsFile)
        {
            throw new PackageSourceClientUnavailableException(
                PackageSourceKind.LocalFolder);
        }
        RequireAuthenticationAssociation(
            association,
            authenticationContext,
            endpoint);

        return new NuGetV3PackageSourceClient(
            CreateResultFactory(
                endpoint,
                association,
                PackageSourceKind.NuGetV3),
            endpoint,
            CreateOwnedTransport(
                endpoint,
                authenticationContext: authenticationContext),
            options ?? new NuGetFetchOptions(),
            source.Credential);
    }

    /// <summary>
    /// Creates a runtime client from credential-free configuration and optional
    /// ephemeral credentials.
    /// </summary>
    public static IPackageSourceClient Create(
        PackageSourceDescriptor descriptor,
        PackageSourceAssociation association,
        NuGetFetchOptions? options = null,
        PackageSourceCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(association);
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
                    CreateResultFactory(descriptor, association),
                    descriptor.Endpoint,
                    CreateOwnedTransport(descriptor.Endpoint),
                    options,
                    credential),
            _ => throw new PackageSourceClientUnavailableException(
                descriptor.Kind),
        };
    }

    /// <summary>
    /// Creates a V3 runtime client with source-scoped plugin authentication.
    /// </summary>
    public static IPackageSourceClient CreateWithPluginAuthentication(
        PackageSourceDescriptor descriptor,
        PackageSourceAssociation association,
        PluginAuthenticationContext authenticationContext,
        NuGetFetchOptions? options = null,
        PackageSourceCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(association);
        RequireAuthenticationAssociation(
            association,
            authenticationContext,
            descriptor.Endpoint);
        options ??= new NuGetFetchOptions();
        if (descriptor.Kind == PackageSourceKind.NuGetGallery)
        {
            throw new InvalidOperationException(
                "The NuGet Gallery source cannot use plugin authentication.");
        }

        return descriptor.Kind switch
        {
            PackageSourceKind.NuGetV3 when descriptor.Endpoint is not null =>
                new NuGetV3PackageSourceClient(
                    CreateResultFactory(descriptor, association),
                    descriptor.Endpoint,
                    CreateOwnedTransport(
                        descriptor.Endpoint,
                        authenticationContext:
                            authenticationContext),
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
        PackageSourceAssociation association,
        NuGetFetchOptions? options = null) =>
        new NuGetGalleryPackageSourceClient(
            CreateResultFactory(
                CanonicalNuGetOrgProducer,
                association,
                PackageSourceKind.NuGetGallery),
            CreateGalleryTransport(),
            options ?? new NuGetFetchOptions());

    /// <summary>
    /// Creates the built-in Gallery client over a caller-created,
    /// credential-free transport owned by the returned client.
    /// </summary>
    public static IPackageSourceClient CreateGallery(
        PackageSourceAssociation association,
        HttpMessageHandler ownedCredentialFreeTransport,
        NuGetFetchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(ownedCredentialFreeTransport);
        return new NuGetGalleryPackageSourceClient(
            CreateResultFactory(
                CanonicalNuGetOrgProducer,
                association,
                PackageSourceKind.NuGetGallery),
            CreateGalleryTransport(
                ownedCredentialFreeTransport,
                OperatingSystem.IsBrowser()),
            options ?? new NuGetFetchOptions());
    }

    internal static IPackageSourceClient Create(
        PackageSource source,
        PackageSourceAssociation association,
        HttpMessageHandler transport,
        NuGetFetchOptions? options = null,
        PluginAuthenticationContext? authenticationContext = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(transport);
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? endpoint)
            || endpoint.IsFile)
        {
            throw new PackageSourceClientUnavailableException(
                PackageSourceKind.LocalFolder);
        }

        if (authenticationContext is not null)
        {
            RequireAuthenticationAssociation(
                association,
                authenticationContext,
                endpoint);
        }

        return new NuGetV3PackageSourceClient(
            CreateResultFactory(
                endpoint,
                association,
                PackageSourceKind.NuGetV3),
            endpoint,
            CreateOwnedTransport(
                endpoint,
                transport,
                authenticationContext),
            options ?? new NuGetFetchOptions(),
            source.Credential);
    }

    internal static IPackageSourceClient Create(
        PackageSourceDescriptor descriptor,
        PackageSourceAssociation association,
        HttpMessageHandler transport,
        NuGetFetchOptions? options = null,
        PackageSourceCredential? credential = null,
        PluginAuthenticationContext? authenticationContext = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(association);
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

        if (authenticationContext is not null)
        {
            RequireAuthenticationAssociation(
                association,
                authenticationContext,
                descriptor.Endpoint);
        }

        return new NuGetV3PackageSourceClient(
            CreateResultFactory(descriptor, association),
            descriptor.Endpoint,
            CreateOwnedTransport(
                descriptor.Endpoint!,
                transport,
                authenticationContext),
            options,
            credential);
    }

    /// <summary>
    /// Registers a supported custom client against one owner-bound result
    /// factory.
    /// </summary>
    public static IPackageSourceClient CreateCustom(
        PackageSourceDescriptor descriptor,
        PackageSourceAssociation association,
        Func<PackageSourceResultFactory, IPackageSourceClient> createClient)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(createClient);

        PackageSourceResultFactory resultFactory =
            CreateResultFactory(descriptor, association);
        IPackageSourceClient client = createClient(resultFactory)
            ?? throw new InvalidOperationException(
                "The custom package source callback returned no client.");
        try
        {
            if (!ReferenceEquals(client.Source, resultFactory.Source))
            {
                throw new InvalidOperationException(
                    "The custom package source client did not expose the bound source identity.");
            }
        }
        catch (Exception validationFailure)
        {
            try
            {
                client.Dispose();
            }
            catch (Exception disposalFailure)
            {
                throw new AggregateException(
                    validationFailure,
                    disposalFailure);
            }

            ExceptionDispatchInfo.Capture(validationFailure).Throw();
            throw;
        }

        return new CustomPackageSourceClientAdapter(
            resultFactory,
            client);
    }

    private static HttpClient CreateOwnedTransport(
        Uri source,
        HttpMessageHandler? transport = null,
        PluginAuthenticationContext? authenticationContext = null)
    {
        bool isBrowser = OperatingSystem.IsBrowser();
        if (isBrowser && authenticationContext is not null)
        {
            throw new PlatformNotSupportedException(
                "NuGet credential-provider plugins are not supported in Browser/Wasm.");
        }

        HttpMessageHandler handler = transport
            ?? CreateV3TransportHandler(source, isBrowser);
        if (authenticationContext is not null)
        {
            handler = authenticationContext.Bind(handler);
        }

        if (!isBrowser)
        {
            handler = new NuGetCredentialRedirectHandler(handler);
        }

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private static void RequireAuthenticationAssociation(
        PackageSourceAssociation association,
        PluginAuthenticationContext authenticationContext,
        Uri? endpoint)
    {
        ArgumentNullException.ThrowIfNull(authenticationContext);
        if (authenticationContext.IsRetired)
        {
            throw new InvalidOperationException(
                "The plugin authentication context has retired.");
        }

        if (!authenticationContext.IsBoundTo(association))
        {
            throw new InvalidOperationException(
                "The plugin authentication context belongs to another package source association.");
        }

        if (endpoint is not null
            && !authenticationContext.IsResourceInScope(endpoint))
        {
            throw new InvalidOperationException(
                "The V3 source endpoint is outside the plugin authentication context's resource scope.");
        }
    }

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

    private static PackageSourceResultFactory CreateResultFactory(
        PackageSourceDescriptor descriptor,
        PackageSourceAssociation association)
    {
        return descriptor.Kind switch
        {
            PackageSourceKind.NuGetGallery
                when descriptor.Endpoint is null
                    && descriptor.Identity
                        == PackageSourceIdentity.NuGetOrg =>
                CreateResultFactory(
                    CanonicalNuGetOrgProducer,
                    association,
                    PackageSourceKind.NuGetGallery),
            PackageSourceKind.NuGetV3
                when descriptor.Endpoint is not null =>
                CreateResultFactory(
                    descriptor.Endpoint,
                    association,
                    PackageSourceKind.NuGetV3),
            _ => throw new PackageSourceClientUnavailableException(
                descriptor.Kind),
        };
    }

    private static PackageSourceResultFactory CreateResultFactory(
        Uri endpoint,
        PackageSourceAssociation association,
        PackageSourceKind transportKind) =>
        CreateResultFactory(
            CreateHttpProducer(
                NuGetSourceRequest.ProjectEndpoint(endpoint)),
            association,
            transportKind);

    private static PackageSourceResultFactory CreateResultFactory(
        PackageProducerIdentity producer,
        PackageSourceAssociation association,
        PackageSourceKind transportKind)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(association);
        var source = new PackageSourceResultIdentity(
            OwnerCapability,
            producer,
            association,
            transportKind);
        return new PackageSourceResultFactory(
            OwnerCapability,
            source);
    }

    private static PackageProducerIdentity CreateHttpProducer(
        NuGetSourceRequest.EndpointProjection endpoint)
    {
        PackageProducerIdentity producer =
            CreateHttpProducerCore(endpoint);
        return producer == CanonicalNuGetOrgProducer
            ? CanonicalNuGetOrgProducer
            : producer;
    }

    private static PackageProducerIdentity CreateHttpProducerCore(
        NuGetSourceRequest.EndpointProjection endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        string canonicalPath = CanonicalizeIdentityPath(
            endpoint.EscapedPath);
        InertString safePath =
            UrlRedaction.ForPathComponent(canonicalPath);
        byte[] host = endpoint.HostKind switch
        {
            NuGetSourceRequest.EndpointHostKind.Dns =>
                Encoding.UTF8.GetBytes(endpoint.DnsHost),
            NuGetSourceRequest.EndpointHostKind.IPv4
                or NuGetSourceRequest.EndpointHostKind.IPv6 =>
                endpoint.AddressBytes.ToArray(),
            _ => throw new InvalidOperationException(
                "Unknown normalized package-source host kind."),
        };
        string hostTag = endpoint.HostKind switch
        {
            NuGetSourceRequest.EndpointHostKind.Dns => "dns",
            NuGetSourceRequest.EndpointHostKind.IPv4 => "ipv4",
            NuGetSourceRequest.EndpointHostKind.IPv6 => "ipv6",
            _ => throw new InvalidOperationException(
                "Unknown normalized package-source host kind."),
        };
        byte[][] fields =
        [
            Encoding.UTF8.GetBytes(endpoint.Scheme),
            Encoding.ASCII.GetBytes(hostTag),
            host,
            Encoding.UTF8.GetBytes(endpoint.Zone),
            Encoding.UTF8.GetBytes(
                endpoint.Port.ToString(CultureInfo.InvariantCulture)),
            Encoding.UTF8.GetBytes(safePath.ToString()),
        ];
        int framedLength = fields.Sum(field => 4 + field.Length);
        var framed = new byte[framedLength];
        int offset = 0;
        foreach (byte[] field in fields)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                framed.AsSpan(offset, 4),
                checked((uint)field.Length));
            offset += 4;
            field.CopyTo(framed, offset);
            offset += field.Length;
        }

        string key = "nfs-http-1."
            + Convert.ToBase64String(framed)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        string displayHost = endpoint.HostKind switch
        {
            NuGetSourceRequest.EndpointHostKind.Dns =>
                endpoint.DnsHost,
            NuGetSourceRequest.EndpointHostKind.IPv4 =>
                new IPAddress(endpoint.AddressBytes).ToString(),
            NuGetSourceRequest.EndpointHostKind.IPv6 =>
                $"[{new IPAddress(endpoint.AddressBytes)}"
                + (endpoint.Zone.Length == 0
                    ? "]"
                    : $"%25{endpoint.Zone}]"),
            _ => throw new InvalidOperationException(
                "Unknown normalized package-source host kind."),
        };
        var display = new InertString(
            TextPolicy.Field,
            $"{endpoint.Scheme}://{displayHost}:{endpoint.Port}"
            + safePath.ToString());
        return new PackageProducerIdentity(
            OwnerCapability,
            key,
            display);
    }

    private static string CanonicalizeIdentityPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = new StringBuilder(path.Length);
        for (int i = 0; i < path.Length; i++)
        {
            char character = path[i];
            if (character == '%'
                && i + 2 < path.Length
                && IsAsciiHex(path[i + 1])
                && IsAsciiHex(path[i + 2]))
            {
                normalized.Append('%');
                normalized.Append(ToUpperAsciiHex(path[i + 1]));
                normalized.Append(ToUpperAsciiHex(path[i + 2]));
                i += 2;
            }
            else
            {
                normalized.Append(character);
            }
        }

        if (normalized.Length > 0
            && normalized[^1] == '/')
        {
            normalized.Length--;
        }

        return normalized.ToString();
    }

    private static bool IsAsciiHex(char value) =>
        value is >= '0' and <= '9'
            or >= 'A' and <= 'F'
            or >= 'a' and <= 'f';

    private static char ToUpperAsciiHex(char value) =>
        value is >= 'a' and <= 'f'
            ? (char)(value - ('a' - 'A'))
            : value;
}

internal sealed class CustomPackageSourceClientAdapter
    : IPackageSourceClient
{
    private readonly PackageSourceResultFactory _results;
    private readonly IPackageSourceClient _client;
    private int _disposeState;

    internal CustomPackageSourceClientAdapter(
        PackageSourceResultFactory results,
        IPackageSourceClient client)
    {
        _results = results;
        _client = client;
    }

    public PackageSourceResultIdentity Source => _results.Source;

    public PackageSourceCapabilities Capabilities =>
        _client.Capabilities;

    public async Task<PackageSourceOperationResult<PackageSearchResult>>
        SearchAsync(
            string query,
            int take = 20,
            bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        PackageSourceOperationResult<PackageSearchResult> outcome =
            await _client.SearchAsync(
                query,
                take,
                prerelease,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        _results.ValidateSearchOutcome(outcome);
        return outcome;
    }

    public async Task<PackageSourceOperationResult<PackageSearchResult>>
        SearchByPrefixAsync(
            string prefix,
            int take = 100,
            bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        PackageSourceOperationResult<PackageSearchResult> outcome =
            await _client.SearchByPrefixAsync(
                prefix,
                take,
                prerelease,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        _results.ValidateSearchOutcome(outcome);
        return outcome;
    }

    public async Task<PackageSourceOperationResult<PackageVersionResult>>
        GetVersionsAsync(
            string packageId,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        PackageSourceOperationResult<PackageVersionResult> outcome =
            await _client.GetVersionsAsync(
                packageId,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        _results.ValidateVersionsOutcome(outcome);
        return outcome;
    }

    public async Task<PackageSourceOperationResult<PackageSourceManifest>>
        GetManifestAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        PackageSourceOperationResult<PackageSourceManifest> outcome =
            await _client.GetManifestAsync(
                packageId,
                version,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        _results.ValidateManifestOutcome(outcome, coordinate);
        return outcome;
    }

    public async Task<PackageSourceOperationResult<PackageSourcePayload>>
        GetPackageAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        PackageSourceOperationResult<PackageSourcePayload> outcome =
            await _client.GetPackageAsync(
                packageId,
                version,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        return await ValidatePayloadOutcomeAsync(
            outcome,
            coordinate,
            symbols: false).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageSourcePayload>>
        TryGetSymbolsAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        PackageSourceOperationResult<PackageSourcePayload> outcome =
            await _client.TryGetSymbolsAsync(
                packageId,
                version,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        return await ValidatePayloadOutcomeAsync(
            outcome,
            coordinate,
            symbols: true).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
            _client.Dispose();
    }

    private async Task<PackageSourceOperationResult<PackageSourcePayload>>
        ValidatePayloadOutcomeAsync(
            PackageSourceOperationResult<PackageSourcePayload> outcome,
            PackageSourceCoordinate coordinate,
            bool symbols)
    {
        try
        {
            if (symbols)
                _results.ValidateSymbolsOutcome(outcome, coordinate);
            else
                _results.ValidatePackageOutcome(outcome, coordinate);
            return outcome;
        }
        catch (Exception validationFailure)
        {
            if (outcome?.Value is not { } payload)
            {
                ExceptionDispatchInfo.Capture(validationFailure).Throw();
                throw;
            }

            try
            {
                await payload.Content.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception disposalFailure)
            {
                throw new AggregateException(
                    validationFailure,
                    disposalFailure);
            }

            ExceptionDispatchInfo.Capture(validationFailure).Throw();
            throw;
        }
    }
}

internal sealed class NuGetV3PackageSourceClient : IPackageSourceClient
{
    private readonly PackageSourceResultFactory _results;
    private readonly Uri _endpoint;
    private readonly PackageSourceCredential? _credential;
    private readonly HttpClient _client;
    private readonly NuGetV3PackageResourceClient _packageResources;
    private readonly NuGetFetchOptions _options;
    private readonly TimeSpan _clientTimeout;

    internal NuGetV3PackageSourceClient(
        PackageSourceResultFactory results,
        Uri endpoint,
        HttpClient client,
        NuGetFetchOptions options,
        PackageSourceCredential? credential)
    {
        _results = results;
        _endpoint = endpoint;
        _credential = credential;
        _client = client;
        _options = NuGetFetchOptions.Validate(options);
        _clientTimeout = client.Timeout;
        _packageResources = new NuGetV3PackageResourceClient(client);
    }

    public PackageSourceResultIdentity Source => _results.Source;
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
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        using NuGetOperationDeadline operation =
            CreateOperation(
                cancellationToken,
                operationContext);
        return await PackageSourceOperation.CaptureSearchAsync(
            _results,
            async () =>
            {
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
                            _results,
                            results,
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
            cancellationToken,
            operationContext: operationContext,
            operationDeadline: operation).ConfigureAwait(false);
    }

    public Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(
        string prefix,
        int take = 100,
        bool prerelease = false,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        UnsupportedSearch(
            cancellationToken,
            operationContext);

    public async Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageCoordinateValidation.ValidatePackageId(
            packageId,
            nameof(packageId));
        using NuGetOperationDeadline operation =
            CreateOperation(
                cancellationToken,
                operationContext);
        return await PackageSourceOperation.CaptureVersionsAsync(
            _results,
            async () =>
            {
                IReadOnlyList<string> versions =
                    await _packageResources.GetVersionsAsync(
                        packageId,
                        NuGetSourceRequest.EndpointUrl(_endpoint),
                        _credential,
                        _options,
                        operation,
                        useNuGetOrgShortcut: false).ConfigureAwait(false);
                return PackageSourceProjection.ProjectVersions(
                    _results,
                    packageId,
                    versions,
                    PackageDiscoveryContract.CompleteVersionEnumeration,
                    PackageListingState.Unknown,
                    hasAuthoritativeListingState: false,
                    operation);
            },
            cancellationToken,
            operationContext: operationContext,
            operationDeadline: operation).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return await PackageSourceOperation.CapturePackageAsync(
            _results,
            coordinate,
            async () =>
            {
                NuGetOperationDeadline operation =
                    CreateOperation(
                        cancellationToken,
                        operationContext);
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
                            useNuGetOrgShortcut: false).ConfigureAwait(false);
                    return _results.Payload(
                        coordinate,
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
            operationContext).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return await PackageSourceOperation.CaptureManifestAsync(
            _results,
            coordinate,
            async () =>
            {
                using NuGetOperationDeadline operation =
                    CreateOperation(
                        cancellationToken,
                        operationContext);
                return _results.Manifest(
                    coordinate,
                    await _packageResources.GetManifestAsync(
                        coordinate.PackageId,
                        coordinate.Version,
                        NuGetSourceRequest.EndpointUrl(_endpoint),
                        _credential,
                        _options,
                        operation,
                        useNuGetOrgShortcut: false).ConfigureAwait(false));
            },
            cancellationToken,
            operationContext).ConfigureAwait(false);
    }

    public Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return UnsupportedSymbols(
            coordinate,
            cancellationToken,
            operationContext);
    }

    public void Dispose()
    {
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

    private NuGetOperationDeadline CreateOperation(
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext) =>
        operationContext is null
            ? new NuGetOperationDeadline(
                _options,
                _clientTimeout,
                cancellationToken,
                Source)
            : operationContext.CreateDeadline(
                _clientTimeout,
                cancellationToken,
                Source);

    private Task<PackageSourceOperationResult<PackageSearchResult>>
        UnsupportedSearch(
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext)
    {
        if (operationContext is null)
        {
            return Task.FromResult(
                _results.FailedSearch(
                    PackageSourceFailureKind.Unsupported));
        }

        return PackageSourceOperation.CaptureSearchAsync(
            _results,
            () =>
            {
                using NuGetOperationDeadline operation =
                    CreateOperation(cancellationToken, operationContext);
                operation.ThrowIfExpired();
                return Task.FromException<PackageSearchResult>(
                    new NuGetSourceCapabilityUnavailableException());
            },
            cancellationToken,
            operationContext);
    }

    private Task<PackageSourceOperationResult<PackageSourcePayload>>
        UnsupportedSymbols(
            PackageSourceCoordinate coordinate,
            CancellationToken cancellationToken,
            NuGetOperationContext? operationContext)
    {
        if (operationContext is null)
        {
            return Task.FromResult(
                _results.FailedSymbols(
                    coordinate,
                    PackageSourceFailureKind.Unsupported));
        }

        return PackageSourceOperation.CaptureSymbolsAsync(
            _results,
            coordinate,
            () =>
            {
                using NuGetOperationDeadline operation =
                    CreateOperation(cancellationToken, operationContext);
                operation.ThrowIfExpired();
                return Task.FromException<PackageSourcePayload>(
                    new NuGetSourceCapabilityUnavailableException());
            },
            cancellationToken,
            operationContext);
    }
}

internal static partial class PackageCoordinateValidation
{
    public static bool IsValidPackageId(string? packageId) =>
        packageId is { Length: > 0 and <= 100 }
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
