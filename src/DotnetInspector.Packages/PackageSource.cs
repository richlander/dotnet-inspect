using System.Net.Http.Headers;
using System.Text;

namespace DotnetInspector.Packages;

public sealed class PackageSource
{
    private const string NuGetOrgV3Index = "https://api.nuget.org/v3/index.json";
    private const string NuGetOrgFlatContainer = "https://api.nuget.org/v3-flatcontainer";

    public PackageSource(string name, string url, string? username = null, string? password = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Name = name;
        Url = url;
        Username = username;
        Password = password;
    }

    public string Name { get; }

    public string Url { get; }

    public string? Username { get; }

    public string? Password { get; }

    public bool IsNuGetOrg => IsNuGetOrgUrl(Url);

    public static PackageSource NuGetOrg { get; } = new("nuget.org", NuGetOrgV3Index);

    public AuthenticationHeaderValue? GetAuthHeader()
    {
        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
            return null;

        string token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    public string? GetFlatContainerUrl()
    {
        if (IsNuGetOrg)
            return NuGetOrgFlatContainer;

        return Url.TrimEnd('/').EndsWith("v3-flatcontainer", StringComparison.OrdinalIgnoreCase)
            ? Url.TrimEnd('/')
            : null;
    }

    internal NuGet.Configuration.PackageSource ToNuGetPackageSource()
    {
        var source = new NuGet.Configuration.PackageSource(Url, Name, isEnabled: true);

        if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
        {
            source.Credentials = new NuGet.Configuration.PackageSourceCredential(
                Name,
                Username,
                Password,
                isPasswordClearText: true,
                validAuthenticationTypesText: null);
        }

        return source;
    }

    internal static PackageSource FromNuGetPackageSource(NuGet.Configuration.PackageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? username = null;
        string? password = null;
        if (source.Credentials?.IsValid() == true)
        {
            username = source.Credentials.Username;
            password = source.Credentials.Password;
        }

        return new PackageSource(source.Name, source.Source, username, password);
    }

    private static bool IsNuGetOrgUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return uri.Host.Equals("api.nuget.org", StringComparison.OrdinalIgnoreCase)
            && (uri.AbsolutePath.Equals("/v3/index.json", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.StartsWith("/v3-flatcontainer", StringComparison.OrdinalIgnoreCase));
    }
}
