namespace ILInspector.SourceLink;

/// <summary>Source URL presentation without changing provenance attribution.</summary>
public static class SourceLinkUrlPresentation
{
    /// <summary>Prefers a supported rendered view, retaining the original URL otherwise.</summary>
    public static string PreferRenderedUrl(string url)
    {
        if (SourceLinkProvenance.BrowseUrl(url) is { } browseUrl)
        {
            return browseUrl;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.IdnHost.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.IsDefaultPort
            && uri.UserInfo.Length == 0)
        {
            var parts = uri.AbsolutePath.TrimStart('/').Split('/', 5);
            if (parts.Length == 5
                && parts.All(static part => part.Length > 0)
                && parts[2] == "raw")
            {
                parts[2] = "blob";
                return new UriBuilder(uri) { Path = string.Join("/", parts) }.Uri.AbsoluteUri;
            }
        }

        return url;
    }
}
