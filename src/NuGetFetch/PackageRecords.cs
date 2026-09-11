namespace NuGetFetch;

public record PackageIdentity(string Id, string? Version);

/// <summary>
/// A NuGet package source.
/// </summary>
/// <remarks>
/// Construction is the check: <see cref="Url"/> rejects a value that cannot be
/// used as a source and canonicalizes local paths. Holding a
/// <c>PackageSource</c> is itself evidence that the value passed source
/// classification. Validating only at resolution points would be containment
/// applied by calling a function — something a new path can forget and a
/// reviewer cannot see in the type.
/// </remarks>
public record PackageSource(string Name, string Url, PackageSourceCredential? Credential = null)
{
    private const string NuGetOrgServiceIndexUrl =
        "https://api.nuget.org/v3/index.json";

    private readonly string _url = ValidatedUrl(Url);

    /// <summary>
    /// The source endpoint or canonical local path. Assigning an unusable
    /// source throws.
    /// </summary>
    /// <exception cref="UnsupportedSourceException">The URL cannot be used as a source.</exception>
    public string Url
    {
        get => _url;
        init => _url = ValidatedUrl(value);
    }

    private static string ValidatedUrl(string url)
    {
        UnsupportedSourceException.ThrowIfUnsupported(url);
        return LocalPackageSourceIdentity.IsLocalSource(url)
            ? LocalPackageSourceIdentity.Create(
                url,
                Directory.GetCurrentDirectory()).CanonicalPath
            : url;
    }

    public static PackageSource NuGetOrg { get; } =
        new("nuget.org", NuGetOrgServiceIndexUrl);

    public bool IsNuGetOrg => IsNuGetOrgServiceIndex(Url);

    internal static bool IsNuGetOrgServiceIndex(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || !uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals(
                "api.nuget.org",
                StringComparison.OrdinalIgnoreCase)
            || uri.Port != 443
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        int pathStart = url.IndexOfAny(
            ['/', '?', '#'],
            schemeEnd + 3);
        string suffix = pathStart < 0
            ? string.Empty
            : url[pathStart..];
        return suffix is "/v3/index.json" or "/v3/index.json/";
    }

    public string? GetFlatContainerUrl() =>
        IsNuGetOrg ? NuGetClient.NuGetOrgFlatContainer.TrimEnd('/') : null;

    public System.Net.Http.Headers.AuthenticationHeaderValue? GetAuthHeader()
    {
        if (Credential is null)
        {
            return null;
        }

        string encoded = Convert.ToBase64String(
            System.Text.Encoding.ASCII.GetBytes($"{Credential.Username}:{Credential.Password}"));
        return new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
    }
}

/// <summary>
/// Canonical starting states for package-source resolution.
/// </summary>
public static class PackageSources
{
    /// <summary>
    /// The lowest-precedence source layer used by ambient configuration discovery.
    /// </summary>
    public static IReadOnlyList<PackageSource> Default { get; } =
        Array.AsReadOnly([PackageSource.NuGetOrg]);

    /// <summary>
    /// An empty source layer used for explicitly selected configuration.
    /// </summary>
    public static IReadOnlyList<PackageSource> Empty { get; } =
        Array.Empty<PackageSource>();
}

public record PackageSourceCredential(string Username, string Password)
{
    public override string ToString() => $"PackageSourceCredential {{ Username = {Username}, Password = *** }}";
}

public record ExtractionResult(
    string Path,
    string Id,
    string Version,
    bool FromCache);

public record PackageDll(string Path, string? Tfm);
