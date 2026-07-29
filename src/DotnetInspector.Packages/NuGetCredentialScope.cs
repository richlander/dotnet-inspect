// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;
using NuGetSource = NuGetFetch.PackageSource;

namespace DotnetInspector.Packages;

/// <summary>
/// Decides whether a source's credentials may travel to a given endpoint.
/// </summary>
/// <remarks>
/// V3 endpoints are not configured by the user: they are discovered by reading the feed's service
/// index and trusting the <c>@id</c> it returns. That value is controlled by whoever controls the
/// index, so a hostile, compromised, or merely misconfigured feed can name any URL it likes and
/// collect the credentials the user configured for the real feed.
///
/// Credentials are therefore scoped to the origin the user actually configured — the scheme, host,
/// and port of the source URL — and are withheld from anything else. The request itself still
/// goes out, so a genuinely cross-origin authenticated endpoint fails visibly with 401 rather
/// than silently handing over a token. See issue #3417.
/// </remarks>
public static class NuGetCredentialScope
{
    /// <summary>
    /// Returns true when both URLs are absolute and share a scheme, host, and port.
    /// Unparseable or relative URLs are never same-origin.
    /// </summary>
    public static bool IsSameOrigin(string? sourceUrl, string? endpointUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source)
            || !Uri.TryCreate(endpointUrl, UriKind.Absolute, out var endpoint))
        {
            return false;
        }

        return string.Equals(source.Scheme, endpoint.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(source.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase)
            && source.Port == endpoint.Port;
    }

    /// <summary>
    /// Returns <paramref name="source"/>'s credentials when <paramref name="endpointUrl"/> is on
    /// the source's own origin, and null otherwise. Sources without credentials return null either
    /// way, and are not reported — there is nothing to withhold.
    /// </summary>
    public static AuthenticationHeaderValue? AuthFor(
        NuGetSource source,
        string? endpointUrl,
        Action<string>? log = null)
    {
        AuthenticationHeaderValue? auth = source.GetAuthHeader();
        if (auth is null || IsSameOrigin(source.Url, endpointUrl))
        {
            return auth;
        }

        log?.Invoke(
            $"Withholding credentials for source '{source.Name}': discovered endpoint '{endpointUrl}' "
            + "is not on the source's origin.");
        return null;
    }
}
