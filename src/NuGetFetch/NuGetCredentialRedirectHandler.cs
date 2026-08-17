using System.Net;
using System.Net.Http.Headers;

namespace NuGetFetch;

internal sealed class NuGetCredentialRedirectHandler(
    HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    private const int MaximumRedirects = 5;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Uri credentialOrigin = request.RequestUri
            ?? throw new InvalidOperationException(
                "A NuGet request must have a request URI.");
        AuthenticationHeaderValue? authorization =
            request.Headers.Authorization;
        HttpRequestMessage current = request;
        HttpRequestMessage? redirectedRequest = null;

        try
        {
            for (int redirectCount = 0; ; redirectCount++)
            {
                HttpResponseMessage response =
                    await base.SendAsync(
                        current,
                        cancellationToken).ConfigureAwait(false);
                if (!IsRedirect(response.StatusCode))
                {
                    if (redirectedRequest is not null)
                    {
                        response.RequestMessage = request;
                    }

                    return response;
                }

                if (redirectCount == MaximumRedirects)
                {
                    response.Dispose();
                    throw new NuGetSourceResponseException(
                        "The package source response exceeded the redirect limit.");
                }

                Uri target;
                try
                {
                    target = ResolveRedirectTarget(
                        current.RequestUri!,
                        response);
                }
                finally
                {
                    response.Dispose();
                }

                redirectedRequest?.Dispose();
                redirectedRequest = CreateRedirectRequest(
                    request,
                    target,
                    SameOrigin(credentialOrigin, target)
                        ? authorization
                        : null);
                current = redirectedRequest;
            }
        }
        finally
        {
            redirectedRequest?.Dispose();
        }
    }

    private static HttpRequestMessage CreateRedirectRequest(
        HttpRequestMessage original,
        Uri target,
        AuthenticationHeaderValue? authorization)
    {
        var redirected = new HttpRequestMessage(
            HttpMethod.Get,
            target)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy,
        };
        foreach (KeyValuePair<string, IEnumerable<string>> header
            in original.Headers)
        {
            if (!header.Key.Equals(
                    "Authorization",
                    StringComparison.OrdinalIgnoreCase)
                && !header.Key.Equals(
                    "Host",
                    StringComparison.OrdinalIgnoreCase))
            {
                redirected.Headers.TryAddWithoutValidation(
                    header.Key,
                    header.Value);
            }
        }

        foreach (KeyValuePair<string, object?> option
            in original.Options)
        {
            redirected.Options.Set(
                new HttpRequestOptionsKey<object?>(option.Key),
                option.Value);
        }

        redirected.Headers.Authorization = authorization;
        return redirected;
    }

    private static Uri ResolveRedirectTarget(
        Uri current,
        HttpResponseMessage response)
    {
        Uri? location = response.Headers.Location;
        if (location is null)
        {
            throw new NuGetSourceResponseException(
                "The package source returned a redirect without a target.");
        }

        Uri target;
        try
        {
            target = location.IsAbsoluteUri
                ? location
                : new Uri(current, location);
        }
        catch (UriFormatException exception)
        {
            throw new NuGetSourceResponseException(
                "The package source returned an unusable redirect target.",
                exception);
        }

        if (target.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(target.Host))
        {
            throw new NuGetSourceResponseException(
                "The package source returned an unusable redirect target.");
        }

        return target;
    }

    private static bool SameOrigin(Uri source, Uri target)
    {
        try
        {
            return source.Scheme.Equals(
                    target.Scheme,
                    StringComparison.OrdinalIgnoreCase)
                && source.IdnHost.Equals(
                    target.IdnHost,
                    StringComparison.OrdinalIgnoreCase)
                && source.Port == target.Port;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
