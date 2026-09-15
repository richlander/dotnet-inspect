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
                        HttpRequestMessage? responseRequest =
                            response.RequestMessage;
                        response.RequestMessage = request;
                        if (!ReferenceEquals(
                                responseRequest,
                                redirectedRequest))
                        {
                            responseRequest?.Dispose();
                        }
                    }

                    return response;
                }

                if (redirectCount == MaximumRedirects)
                {
                    response.Dispose();
                    throw new NuGetRedirectLimitExceededException();
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
                    HttpRequestMessage? responseRequest =
                        response.RequestMessage;
                    response.Dispose();
                    if (!ReferenceEquals(
                            responseRequest,
                            current))
                    {
                        responseRequest?.Dispose();
                    }
                }

                HttpRequestMessage? previousRedirect =
                    redirectedRequest;
                HttpRequestMessage nextRedirect = CreateRedirectRequest(
                    current,
                    target,
                    SameOrigin(credentialOrigin, target)
                        ? authorization
                        : null);
                redirectedRequest = nextRedirect;
                current = nextRedirect;
                previousRedirect?.Dispose();
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
        if (!response.Headers.NonValidated.TryGetValues(
                "Location",
                out HeaderStringValues locationValues))
        {
            throw new NuGetSourceResponseException(
                "The package source returned a redirect without a target.");
        }

        if (locationValues.Count != 1)
        {
            throw new NuGetSourceResponseException(
                "The package source returned an unusable redirect target.");
        }

        string rawLocation = locationValues.ToString();
        if (string.IsNullOrEmpty(rawLocation)
            || !NuGetHttpRequest.HasValidRawText(
                rawLocation,
                allowNonAscii: true))
        {
            throw new NuGetSourceResponseException(
                "The package source returned an unusable redirect target.");
        }

        Uri target;
        try
        {
            if (!Uri.TryCreate(
                    rawLocation,
                    UriKind.RelativeOrAbsolute,
                    out Uri? parsedLocation)
                || parsedLocation is null)
            {
                throw new UriFormatException();
            }

            Uri location = parsedLocation;
            target = location.IsAbsoluteUri
                ? location
                : new Uri(current, location);
            _ = target.IdnHost;
        }
        catch (UriFormatException exception)
        {
            throw new NuGetSourceResponseException(
                "The package source returned an unusable redirect target.",
                exception);
        }

        if (target.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(target.Host)
            || !string.IsNullOrEmpty(target.UserInfo))
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
        statusCode is HttpStatusCode.MultipleChoices
            or HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
