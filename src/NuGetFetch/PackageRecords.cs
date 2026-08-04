namespace NuGetFetch;

public record PackageIdentity(string Id, string? Version);

/// <summary>
/// A NuGet package source.
/// </summary>
/// <remarks>
/// Construction is the check: <see cref="Url"/> rejects a URL that cannot be used as a source,
/// so holding a <c>PackageSource</c> is itself the evidence that its URL passed. Validating at
/// the points that resolve sources instead would be containment applied by calling a function —
/// something a new path can forget, and something no reviewer can see the absence of, because
/// <c>string</c> is the type of both a checked and an unchecked URL.
/// </remarks>
public record PackageSource(string Name, string Url, PackageSourceCredential? Credential = null)
{
    private const string NuGetOrgServiceIndexUrl =
        "https://api.nuget.org/v3/index.json";

    private readonly string _url = ValidatedUrl(Url);

    /// <summary>
    /// The source URL. Assigning one that cannot be used as a NuGet source throws.
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
        return url;
    }

    public static PackageSource NuGetOrg { get; } =
        new("nuget.org", NuGetOrgServiceIndexUrl);

    public bool IsNuGetOrg => IsNuGetOrgServiceIndex(Url);

    internal static bool IsNuGetOrgServiceIndex(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && uri.Host.Equals("api.nuget.org", StringComparison.OrdinalIgnoreCase)
        && uri.Port == 443
        && uri.AbsolutePath.TrimEnd('/').Equals(
            "/v3/index.json",
            StringComparison.Ordinal)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.UserInfo);

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
