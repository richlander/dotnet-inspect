using System.Text;

namespace NuGetFetch;

/// <summary>
/// Identifies the transport family used by a package source.
/// </summary>
public enum PackageSourceKind
{
    NuGetV3,
    NuGetGallery,
    LocalFolder,
}

/// <summary>
/// Operations a package source client can perform.
/// </summary>
[Flags]
public enum PackageSourceCapabilities
{
    None = 0,
    Search = 1 << 0,
    VersionEnumeration = 1 << 1,
    PackagePayload = 1 << 2,
    SymbolPayload = 1 << 3,
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
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri
            || (endpoint.Scheme != Uri.UriSchemeHttp
                && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "A package source endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(endpoint));
        }

        var origin =
            $"{endpoint.Scheme.ToLowerInvariant()}://{endpoint.IdnHost.ToLowerInvariant()}:{endpoint.Port}";
        string absolutePath = endpoint.AbsolutePath;
        string path = NormalizeEscapes(
            absolutePath.EndsWith("/", StringComparison.Ordinal)
                ? absolutePath[..^1]
                : absolutePath);
        return new PackageSourceIdentity(
            $"{origin}{path}{NormalizeEscapes(endpoint.Query)}{NormalizeEscapes(endpoint.Fragment)}");
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
        if (serviceIndex.UserInfo.Length > 0)
        {
            throw new ArgumentException(
                "A portable package source endpoint cannot contain user information.",
                nameof(serviceIndex));
        }

        PackageSourceIdentity identity =
            PackageSourceIdentity.ForHttpEndpoint(serviceIndex);
        return new PackageSourceDescriptor(
            id,
            displayName,
            PackageSourceKind.NuGetV3,
            identity,
            serviceIndex,
            enabled);
    }
}

/// <summary>
/// Protocol-independent package source operations.
/// </summary>
public interface IPackageSourceClient
{
    PackageSourceIdentity Identity { get; }
    PackageSourceKind Kind { get; }
    PackageSourceCapabilities Capabilities { get; }

    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    Task<Stream> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);

    Task<Stream?> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Raised when a source client is asked to perform an operation it does not advertise.
/// </summary>
public sealed class PackageSourceCapabilityException(
    PackageSourceKind kind,
    PackageSourceCapabilities capability)
    : NotSupportedException(
        $"Package source kind '{kind}' does not support capability '{capability}'.")
{
    public PackageSourceKind Kind { get; } = kind;
    public PackageSourceCapabilities Capability { get; } = capability;
}

/// <summary>
/// Raised when a registered source kind has no runtime client implementation.
/// </summary>
public sealed class PackageSourceClientUnavailableException(
    PackageSourceKind kind)
    : NotSupportedException(
        $"Package source kind '{kind}' does not have a runtime client implementation.")
{
    public PackageSourceKind Kind { get; } = kind;
}

/// <summary>
/// Creates runtime clients without exposing transport construction to consumers.
/// </summary>
public static class PackageSourceClientFactory
{
    public static IPackageSourceClient Create(
        PackageSource source,
        HttpClient client,
        NuGetFetchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? endpoint)
            || endpoint.IsFile)
        {
            throw new PackageSourceClientUnavailableException(
                PackageSourceKind.LocalFolder);
        }

        PackageSourceDescriptor descriptor = PackageSourceDescriptor.NuGetV3(
            source.Name,
            source.Name,
            endpoint);
        return Create(
            descriptor,
            client,
            options,
            source.Credential);
    }

    public static IPackageSourceClient Create(
        PackageSourceDescriptor descriptor,
        HttpClient client,
        NuGetFetchOptions? options = null,
        PackageSourceCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(client);
        options ??= new NuGetFetchOptions();

        return descriptor.Kind switch
        {
            PackageSourceKind.NuGetV3 when descriptor.Endpoint is not null =>
                new NuGetV3PackageSourceClient(
                    descriptor,
                    client,
                    options,
                    credential),
            _ => throw new PackageSourceClientUnavailableException(
                descriptor.Kind),
        };
    }
}

internal sealed class NuGetV3PackageSourceClient : IPackageSourceClient
{
    private readonly PackageSourceDescriptor _descriptor;
    private readonly PackageSourceCredential? _credential;
    private readonly NuGetClient _nuget;
    private readonly SearchService? _search;

    public NuGetV3PackageSourceClient(
        PackageSourceDescriptor descriptor,
        HttpClient client,
        NuGetFetchOptions options,
        PackageSourceCredential? credential)
    {
        _descriptor = descriptor;
        _credential = credential;
        _nuget = new NuGetClient(client, options);
        if (descriptor.Identity == PackageSourceIdentity.NuGetOrg)
        {
            _search = new SearchService(
                client,
                NuGetClient.NuGetOrgSearchUrl,
                options);
        }
    }

    public PackageSourceIdentity Identity => _descriptor.Identity;
    public PackageSourceKind Kind => PackageSourceKind.NuGetV3;
    public PackageSourceCapabilities Capabilities =>
        PackageSourceCapabilities.VersionEnumeration
        | PackageSourceCapabilities.PackagePayload
        | (_search is null
            ? PackageSourceCapabilities.None
            : PackageSourceCapabilities.Search);

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (_search is null)
        {
            throw new PackageSourceCapabilityException(
                Kind,
                PackageSourceCapabilities.Search);
        }

        return await _search.SearchAsync(
            query,
            take,
            prerelease,
            auth: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default) =>
        await _nuget.GetVersionsAsync(
            packageId,
            _descriptor.Endpoint!.AbsoluteUri,
            _credential,
            cancellationToken).ConfigureAwait(false);

    public async Task<Stream> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default) =>
        await _nuget.DownloadAsync(
            packageId,
            version,
            _descriptor.Endpoint!.AbsoluteUri,
            _credential,
            cancellationToken).ConfigureAwait(false);

    public Task<Stream?> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default) =>
        throw new PackageSourceCapabilityException(
            Kind,
            PackageSourceCapabilities.SymbolPayload);

}
