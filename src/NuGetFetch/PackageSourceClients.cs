using System.Text;
using System.Text.RegularExpressions;
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
public interface IPackageSourceClient
{
    /// <summary>Gets the producer represented by this transport.</summary>
    PackageSourceIdentity Identity { get; }

    /// <summary>Gets the transport family.</summary>
    PackageSourceKind Kind { get; }

    /// <summary>Gets the operations implemented by this runtime client.</summary>
    PackageSourceCapabilities Capabilities { get; }

    /// <summary>Searches for packages.</summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the versions reported for a package ID.</summary>
    Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets an exact package payload owned by the returned stream.</summary>
    Task<Stream> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);

    /// <summary>Gets an exact symbol-package payload when supported and available.</summary>
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
    /// <summary>Gets the source kind that rejected the operation.</summary>
    public PackageSourceKind Kind { get; } = kind;

    /// <summary>Gets the unsupported operation.</summary>
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

        return new NuGetV3PackageSourceClient(
            PackageSourceIdentity.ForHttpEndpoint(endpoint),
            endpoint,
            client,
            options ?? new NuGetFetchOptions(),
            source.Credential);
    }

    /// <summary>
    /// Creates a runtime client from credential-free configuration and optional
    /// ephemeral credentials.
    /// </summary>
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
    private readonly PackageSourceIdentity _identity;
    private readonly Uri _endpoint;
    private readonly PackageSourceCredential? _credential;
    private readonly NuGetClient _nuget;

    public NuGetV3PackageSourceClient(
        PackageSourceDescriptor descriptor,
        HttpClient client,
        NuGetFetchOptions options,
        PackageSourceCredential? credential)
        : this(
            descriptor.Identity,
            descriptor.Endpoint!,
            client,
            options,
            credential)
    {
    }

    public NuGetV3PackageSourceClient(
        PackageSourceIdentity identity,
        Uri endpoint,
        HttpClient client,
        NuGetFetchOptions options,
        PackageSourceCredential? credential)
    {
        _identity = identity;
        _endpoint = endpoint;
        _credential = credential;
        _nuget = new NuGetClient(client, options);
    }

    public PackageSourceIdentity Identity => _identity;
    public PackageSourceKind Kind => PackageSourceKind.NuGetV3;
    public PackageSourceCapabilities Capabilities =>
        PackageSourceCapabilities.VersionEnumeration
        | PackageSourceCapabilities.PackagePayload;

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default) =>
        throw new PackageSourceCapabilityException(
            Kind,
            PackageSourceCapabilities.Search);

    public async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        PackageCoordinateValidation.ValidatePackageId(
            packageId,
            nameof(packageId));
        return await _nuget.GetVersionsAsync(
            packageId,
            _endpoint.AbsoluteUri,
            _credential,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Stream> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        PackageCoordinateValidation.ValidatePackageId(
            packageId,
            nameof(packageId));
        string normalizedVersion =
            PackageCoordinateValidation.NormalizeVersion(
                version,
                nameof(version));
        return await _nuget.DownloadAsync(
            packageId,
            normalizedVersion,
            _endpoint.AbsoluteUri,
            _credential,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<Stream?> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default) =>
        throw new PackageSourceCapabilityException(
            Kind,
            PackageSourceCapabilities.SymbolPayload);

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
