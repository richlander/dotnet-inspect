using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>The configuration family that established one package authority.</summary>
public enum ConfiguredPackageAuthorityKind
{
    /// <summary>An absolute HTTP or HTTPS package source.</summary>
    Http,

    /// <summary>A canonical local-folder package source.</summary>
    LocalFolder,
}

/// <summary>
/// One package-owned configured authority and its source-result association.
/// </summary>
/// <remarks>
/// Object identity is the authority identity. Endpoint, producer, display, and
/// transport values are evidence about the authority rather than substitutes
/// for that identity.
/// </remarks>
public sealed class ConfiguredPackageAuthority
{
    private const string PersistentKeyNamespace = "authority-v1";

    internal ConfiguredPackageAuthority(PackageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        Key = ConfiguredPackageAuthorityKey.Create(source);
        Association = PackageSourceAssociation.Create();
        PersistentCacheKey = CreatePersistentCacheKey(source, Key);
    }

    /// <summary>Gets the selected configured source representation.</summary>
    public PackageSource Source { get; }

    /// <summary>Gets the classified authority family.</summary>
    public ConfiguredPackageAuthorityKind Kind => Key.Kind;

    /// <summary>Gets the source-result association minted for this authority.</summary>
    public PackageSourceAssociation Association { get; }

    /// <summary>
    /// Gets the versioned persistent cache key when one can be formed without
    /// credentials, or <see langword="null"/> for process-local authority.
    /// </summary>
    public string? PersistentCacheKey { get; }

    /// <summary>Gets the canonical local identity for a local authority.</summary>
    public LocalPackageSourceIdentity? LocalIdentity => Key.LocalIdentity;

    /// <summary>Gets the configured endpoint for an HTTP authority.</summary>
    public Uri? HttpEndpoint => Key.HttpEndpoint;

    internal ConfiguredPackageAuthorityKey Key { get; }

    private static string? CreatePersistentCacheKey(
        PackageSource source,
        ConfiguredPackageAuthorityKey key)
    {
        if (!key.TryGetPersistentValue(source, out string? stableIdentity))
            return null;

        byte[] digest = SHA256.HashData(
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true)
                .GetBytes(stableIdentity));
        return $"{PersistentKeyNamespace}-"
            + Convert.ToHexStringLower(digest.AsSpan(0, 16));
    }
}

/// <summary>
/// The package-owned runtime key used to decide whether aliases may collapse.
/// </summary>
internal sealed class ConfiguredPackageAuthorityKey :
    IEquatable<ConfiguredPackageAuthorityKey>
{
    private static readonly ConfiguredPackageAuthorityKey NuGetOrg =
        Create(PackageSource.NuGetOrg);

    private readonly string _value;

    private ConfiguredPackageAuthorityKey(
        string value,
        ConfiguredPackageAuthorityKind kind,
        LocalPackageSourceIdentity? localIdentity,
        Uri? httpEndpoint)
    {
        _value = value;
        Kind = kind;
        LocalIdentity = localIdentity;
        HttpEndpoint = httpEndpoint;
    }

    public ConfiguredPackageAuthorityKind Kind { get; }
    public LocalPackageSourceIdentity? LocalIdentity { get; }
    public Uri? HttpEndpoint { get; }

    public static ConfiguredPackageAuthorityKey Create(PackageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (TryCreate(source, out ConfiguredPackageAuthorityKey? key, out _))
            return key;

        throw new ArgumentException(
            "The package source endpoint is unusable.",
            nameof(source));
    }

    public static bool TryCreate(
        PackageSource source,
        [NotNullWhen(true)] out ConfiguredPackageAuthorityKey? key,
        [NotNullWhen(false)] out string? problem)
    {
        ArgumentNullException.ThrowIfNull(source);
        key = null;
        problem = null;
        if (LocalPackageSourceIdentity.IsLocalSource(source.Url))
        {
            try
            {
                LocalPackageSourceIdentity local =
                    LocalPackageSourceIdentity.CreateAbsolute(source.Url);
                key = new ConfiguredPackageAuthorityKey(
                    $"local\n{local.PersistentValue}",
                    ConfiguredPackageAuthorityKind.LocalFolder,
                    local,
                    httpEndpoint: null);
                return true;
            }
            catch (Exception exception) when (exception is
                ArgumentException
                or IOException
                or NotSupportedException)
            {
                problem = "The local package source path is unusable.";
                return false;
            }
        }

        int schemeEnd = source.Url.IndexOf(
            "://",
            StringComparison.Ordinal);
        if (schemeEnd <= 0
            || !Uri.TryCreate(
                source.Url,
                UriKind.Absolute,
                out Uri? endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || !NuGetHttpRequest.HasValidRawText(
                source.Url,
                allowNonAscii: true)
            || !NuGetSourceRequest.TryEndpointUrl(source.Url, out _)
            || !NuGetSourceRequest.CanProjectEndpoint(endpoint))
        {
            problem =
                "The package source service-index endpoint is unusable.";
            return false;
        }

        string host;
        try
        {
            host = endpoint.HostNameType == UriHostNameType.IPv6
                ? $"[{endpoint.IdnHost}]"
                : endpoint.IdnHost.ToLowerInvariant();
        }
        catch (UriFormatException)
        {
            problem =
                "The package source service-index endpoint has an unusable host.";
            return false;
        }

        int suffixStart = source.Url.IndexOfAny(
            ['/', '?', '#'],
            schemeEnd + 3);
        string suffix = suffixStart < 0
            ? string.Empty
            : source.Url[suffixStart..];
        int pathEnd = suffix.IndexOfAny(['?', '#']);
        if (pathEnd < 0)
            pathEnd = suffix.Length;
        string path = suffix[..pathEnd];
        if (path.EndsWith("/", StringComparison.Ordinal))
            path = path[..^1];
        string remainder = suffix[pathEnd..];
        string origin =
            $"{endpoint.Scheme.ToLowerInvariant()}://{host}:{endpoint.Port}";
        key = new ConfiguredPackageAuthorityKey(
            $"{origin}{NuGetCredentialScope.NormalizeEscapes(path)}"
            + NuGetCredentialScope.NormalizeEscapes(remainder),
            ConfiguredPackageAuthorityKind.Http,
            localIdentity: null,
            endpoint);
        return true;
    }

    public bool IsNuGetOrg => Equals(NuGetOrg);

    public bool Equals(ConfiguredPackageAuthorityKey? other) =>
        other is not null
        && string.Equals(_value, other._value, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is ConfiguredPackageAuthorityKey other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(_value);

    public override string ToString() =>
        nameof(ConfiguredPackageAuthorityKey);

    internal bool TryGetPersistentValue(
        PackageSource source,
        [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (source.Credential is not null
            || LocalIdentity is not { } local)
            return false;

        value = $"local:{local.PersistentValue}";
        return true;
    }
}
