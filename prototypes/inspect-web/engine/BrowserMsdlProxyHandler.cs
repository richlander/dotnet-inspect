namespace InspectWeb.Engine;

internal sealed class BrowserMsdlProxyHandler(HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    private Uri? _proxyBaseUri;

    internal void Configure(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? candidate)
            || (candidate.Scheme != Uri.UriSchemeHttps
                && candidate.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || candidate.AbsolutePath != "/"
            || !string.IsNullOrEmpty(candidate.Query)
            || !string.IsNullOrEmpty(candidate.Fragment))
        {
            throw new ArgumentException(
                "The browser origin must be an absolute HTTP origin.",
                nameof(origin));
        }

        var proxyBaseUri =
            new Uri(candidate.GetLeftPart(UriPartial.Authority) + "/api/msdl/");
        if (_proxyBaseUri is { } configured
            && configured != proxyBaseUri)
        {
            throw new InvalidOperationException(
                "The browser MSDL proxy origin is already configured.");
        }

        _proxyBaseUri = proxyBaseUri;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (TryGetMsdlSegments(request.RequestUri, out string? pdbFileName, out string? symbolKey))
        {
            Uri proxyBaseUri =
                _proxyBaseUri
                ?? throw new InvalidOperationException(
                    "The browser MSDL proxy origin was not configured.");
            request.RequestUri =
                new Uri(proxyBaseUri, $"{pdbFileName}/{symbolKey}");
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool TryGetMsdlSegments(
        Uri? requestUri,
        out string? pdbFileName,
        out string? symbolKey)
    {
        pdbFileName = null;
        symbolKey = null;
        if (requestUri is null
            || requestUri.Scheme != Uri.UriSchemeHttps
            || !requestUri.Host.Equals(
                "msdl.microsoft.com",
                StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(requestUri.Query)
            || !string.IsNullOrEmpty(requestUri.Fragment))
        {
            return false;
        }

        string[] segments =
            requestUri.AbsolutePath.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 5
            || segments[0] != "download"
            || segments[1] != "symbols"
            || segments[2] != segments[4])
        {
            return false;
        }

        pdbFileName = segments[2];
        symbolKey = segments[3];
        return true;
    }
}
