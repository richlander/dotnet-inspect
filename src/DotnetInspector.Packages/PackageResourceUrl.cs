namespace DotnetInspector.Packages;

/// <summary>
/// Builds package resource URLs from a feed-declared base address and
/// product-validated path segments.
/// </summary>
/// <remarks>
/// <para>
/// A flat-container base address is feed metadata: it arrives in a service
/// index the source controls, so it is untrusted input in a URL-shaped
/// position. String concatenation onto such a value is not URL construction —
/// it appends to whatever the base ends with. A base carrying a query, such as
/// a pre-signed <c>https://feed.test/flat?sig=…</c>, silently puts the package
/// path inside the query string and requests the container root; a relative or
/// non-HTTP <c>@id</c> throws from the request layer rather than failing that
/// one source.
/// </para>
/// <para>
/// This owner parses the base, requires an absolute HTTP or HTTPS URL with no
/// embedded credentials, appends escaped segments to the <em>path</em>, and
/// re-attaches the base's query unchanged so a signature survives. A base it
/// cannot use yields <see langword="null"/>, which the acquisition path treats
/// as this source failing to serve the coordinate — the next authorized source
/// is tried.
/// </para>
/// <para>
/// Gated by <c>PackageResourceUrlTests</c>.
/// </para>
/// </remarks>
public static class PackageResourceUrl
{
    /// <summary>
    /// Returns the absolute URL for <paramref name="segments"/> beneath
    /// <paramref name="baseAddress"/>, or <see langword="null"/> when the base
    /// address is not a usable absolute HTTP(S) resource URL.
    /// </summary>
    /// <remarks>
    /// Each segment is escaped as one URI path component, so a segment can
    /// never introduce a new path, query, or fragment. The base's own query is
    /// preserved; its fragment is dropped, because a fragment is never sent to
    /// the server and carrying one forward would only make the composed URL
    /// disagree with the request.
    /// </remarks>
    public static string? Combine(string? baseAddress, params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        Uri? baseUri = ParseUsableBaseAddress(baseAddress);
        if (baseUri is null || segments.Length == 0)
        {
            return null;
        }

        foreach (string segment in segments)
        {
            // EscapeDataString leaves "." / ".." unchanged, and Uri then
            // applies dot-segment removal — so a bare ".." segment would climb
            // out of the feed-declared base path (and keep a pre-signed query).
            if (string.IsNullOrEmpty(segment)
                || segment is "." or ".."
                || segment.Contains('\\', StringComparison.Ordinal))
            {
                return null;
            }
        }

        // GetLeftPart(Path) is the already-escaped scheme, authority, and path
        // with the query and fragment removed, so the query can be re-attached
        // deliberately instead of being appended to by accident.
        string root = baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        string path = string.Join(
            '/',
            segments.Select(Uri.EscapeDataString));
        string pathOnly = $"{root}/{path}";
        string composed = $"{pathOnly}{baseUri.Query}";
        return Uri.TryCreate(composed, UriKind.Absolute, out Uri? result)
            && IsHttpScheme(result)
            // Re-parse must not climb above the composed path via residual
            // dot-segment behavior after escaping.
            && string.Equals(
                result.GetLeftPart(UriPartial.Path),
                pathOnly,
                StringComparison.Ordinal)
            ? result.AbsoluteUri
            : null;
    }

    internal static bool IsUsableBaseAddress(string? baseAddress) =>
        ParseUsableBaseAddress(baseAddress) is not null;

    static Uri? ParseUsableBaseAddress(string? baseAddress)
    {
        if (string.IsNullOrWhiteSpace(baseAddress)
            || !Uri.TryCreate(
                baseAddress,
                UriKind.Absolute,
                out Uri? baseUri)
            || !IsHttpScheme(baseUri)
            || !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            return null;
        }

        return baseUri;
    }

    static bool IsHttpScheme(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal)
        || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.Ordinal);
}
