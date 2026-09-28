namespace DotnetInspect.Web;

internal sealed class BrowserPublicEvidenceProxyHandler(
    HttpMessageHandler innerHandler)
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

        var proxyBaseUri = new Uri(
            candidate.GetLeftPart(UriPartial.Authority)
            + "/api/package-changes/");
        if (_proxyBaseUri is { } configured
            && configured != proxyBaseUri)
        {
            throw new InvalidOperationException(
                "The browser public-evidence proxy origin is already configured.");
        }

        _proxyBaseUri = proxyBaseUri;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Uri? rewritten = TryCreateProxyRequest(request);
        if (rewritten is null)
        {
            return await base.SendAsync(
                request,
                cancellationToken).ConfigureAwait(false);
        }

        using var proxyRequest = new HttpRequestMessage(
            HttpMethod.Get,
            rewritten)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };
        HttpResponseMessage response = await base.SendAsync(
            proxyRequest,
            cancellationToken).ConfigureAwait(false);
        response.RequestMessage = request;
        return response;
    }

    private Uri? TryCreateProxyRequest(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Get
            || request.RequestUri is not { } requestUri
            || requestUri.Scheme != Uri.UriSchemeHttps
            || requestUri.Port != 443
            || requestUri.UserInfo.Length != 0
            || requestUri.Fragment.Length != 0)
        {
            return null;
        }

        Uri proxyBaseUri;
        if (requestUri.Host.Equals(
                "api.nuget.org",
                StringComparison.OrdinalIgnoreCase)
            && requestUri.Query.Length == 0
            && (requestUri.AbsolutePath == "/v3/index.json"
                || requestUri.AbsolutePath.StartsWith(
                    "/v3/catalog0/",
                    StringComparison.Ordinal)))
        {
            proxyBaseUri = RequireConfiguredOrigin();
            return new Uri(
                proxyBaseUri,
                "nuget?path="
                + Uri.EscapeDataString(requestUri.AbsolutePath));
        }

        if (requestUri.Host.Equals(
                "api.github.com",
                StringComparison.OrdinalIgnoreCase)
            && requestUri.AbsolutePath == "/advisories"
            && requestUri.Query.Length > 1)
        {
            proxyBaseUri = RequireConfiguredOrigin();
            return new Uri(
                proxyBaseUri,
                "advisories" + requestUri.Query);
        }

        return null;
    }

    private Uri RequireConfiguredOrigin() =>
        _proxyBaseUri
        ?? throw new InvalidOperationException(
            "The browser public-evidence proxy origin was not configured.");
}
