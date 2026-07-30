namespace NuGetFetch;

public record PackageIdentity(string Id, string? Version);

public record PackageSource(string Name, string Url, PackageSourceCredential? Credential = null)
{
    public static PackageSource NuGetOrg { get; } = new("nuget.org", "https://api.nuget.org/v3/index.json");

    public bool IsNuGetOrg =>
        Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri)
        && (uri.Host.Equals("api.nuget.org", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".nuget.org", StringComparison.OrdinalIgnoreCase));

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
