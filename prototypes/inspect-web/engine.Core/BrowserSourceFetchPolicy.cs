using DotnetInspector.Services;

namespace InspectWeb.Engine;

internal sealed class BrowserSourceFetchPolicy : ISourceFetchPolicy
{
    static readonly HttpRequestOptionsKey<IDictionary<string, object>>
        BrowserFetchOptions =
            new("WebAssemblyFetchOptions");

    static readonly HashSet<string> ExactHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "raw.githubusercontent.com",
            "dev.azure.com",
            "gitlab.com",
            "api.bitbucket.org",
            "bitbucket.org",
        };

    public static BrowserSourceFetchPolicy Instance { get; } = new();

    public bool FinalResponseUriIsReliable => true;

    public bool IsRequestAllowed(Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        return requestUri.Scheme == Uri.UriSchemeHttps
            && (ExactHosts.Contains(requestUri.IdnHost)
                || requestUri.IdnHost.EndsWith(
                    ".visualstudio.com",
                    StringComparison.OrdinalIgnoreCase));
    }

    public void ConfigureRequest(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = new Dictionary<string, object>(
            StringComparer.Ordinal)
        {
            ["credentials"] = "omit",
            ["redirect"] = "error",
        };
        request.Options.Set(BrowserFetchOptions, options);
    }
}
