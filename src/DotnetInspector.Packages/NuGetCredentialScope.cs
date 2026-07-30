// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;
using System.Text;
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
    /// <remarks>
    /// Hosts are compared as <see cref="Uri.IdnHost"/> rather than <see cref="Uri.Host"/>, which
    /// does not canonicalize Unicode and punycode spellings against each other: a feed configured
    /// as <c>https://bücher.example</c> whose index advertises <c>https://xn--bcher-kva.example</c>
    /// is the same DNS name, and comparing <c>Host</c> would withhold credentials from the user's
    /// own feed. IdnHost narrows nothing — distinct DNS names still compare distinct.
    /// </remarks>
    public static bool IsSameOrigin(string? sourceUrl, string? endpointUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source)
            || !Uri.TryCreate(endpointUrl, UriKind.Absolute, out var endpoint))
        {
            return false;
        }

        return string.Equals(source.Scheme, endpoint.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(source.IdnHost, endpoint.IdnHost, StringComparison.OrdinalIgnoreCase)
            && source.Port == endpoint.Port;
    }

    /// <summary>
    /// Returns true when both URLs name the same endpoint: the same origin, plus the same escaped
    /// path and query. Unparseable URLs fall back to an ordinal comparison.
    /// </summary>
    /// <remarks>
    /// Origin is compared with <see cref="IsSameOrigin"/>, which folds case because scheme and
    /// host are case-insensitive by definition. Path and query are compared ordinally: HTTP paths
    /// are case-sensitive, so <c>/FeedA</c> and <c>/feeda</c> may be different feeds, and treating
    /// them as one would let a caller adopt the wrong feed's credentials.
    ///
    /// Two normalizations are applied, both for equivalences the URI grammar itself defines:
    /// percent-escape hex digits are case-insensitive per RFC 3986 (<c>%2f</c> and <c>%2F</c> are
    /// the same octet), and .NET's <see cref="Uri"/> preserves whichever the caller wrote, so
    /// comparing raw would withhold credentials from a feed the user did configure. A trailing
    /// slash is likewise ignored: it is the commonest way a hand-typed source URL differs from
    /// its configured spelling, and the alternative failure — authentication silently not
    /// working — is the one this whole change exists to prevent.
    ///
    /// Trailing-slash tolerance is a candidacy test, not an authorization decision. It can make a
    /// URL match more than one configured entry, and entries that differ only by a trailing slash
    /// may carry different credentials, so callers that adopt credentials from a match must
    /// require the match to be unambiguous. <see cref="NuGetSourceResolver"/> does that by
    /// preferring an exact spelling and accepting a slash-tolerant match only when exactly one
    /// configured source matches.
    /// </remarks>
    public static bool IsSameEndpoint(string? a, string? b)
    {
        if (!Uri.TryCreate(a, UriKind.Absolute, out var x)
            || !Uri.TryCreate(b, UriKind.Absolute, out var y))
        {
            return string.Equals(a, b, StringComparison.Ordinal);
        }

        return IsSameOrigin(a, b)
            && string.Equals(
                NormalizeEscapes(x.AbsolutePath.TrimEnd('/')),
                NormalizeEscapes(y.AbsolutePath.TrimEnd('/')),
                StringComparison.Ordinal)
            && string.Equals(
                NormalizeEscapes(x.Query), NormalizeEscapes(y.Query), StringComparison.Ordinal);
    }

    /// <summary>
    /// Upper-cases the hex digits of percent-escapes, which RFC 3986 defines as case-insensitive,
    /// leaving every other character byte-for-byte intact.
    /// </summary>
    private static string NormalizeEscapes(string value)
    {
        if (!value.Contains('%', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '%'
                && i + 2 < value.Length
                && Uri.IsHexDigit(value[i + 1])
                && Uri.IsHexDigit(value[i + 2]))
            {
                builder.Append('%')
                    .Append(char.ToUpperInvariant(value[i + 1]))
                    .Append(char.ToUpperInvariant(value[i + 2]));
                i += 2;
            }
            else
            {
                builder.Append(value[i]);
            }
        }

        return builder.ToString();
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
