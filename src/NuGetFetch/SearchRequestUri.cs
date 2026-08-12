namespace NuGetFetch;

/// <summary>
/// Composes a search request URI from a feed-declared endpoint and this
/// product's own search parameters.
/// </summary>
/// <remarks>
/// <para>
/// A search endpoint arrives in a feed's service index, so it may already carry
/// a query — a pre-signed endpoint such as
/// <c>https://feed.test/query?sig=…</c> is the common case. Appending
/// <c>"?q=…"</c> to that as text does not add a parameter: it extends the last
/// existing value, so the signature becomes <c>sig=…?q=term</c> and the search
/// term never reaches the server as <c>q</c> at all. The endpoint has to be
/// parsed, its existing query preserved, and the new parameters joined with
/// <c>&amp;</c>.
/// </para>
/// <para>
/// The existing query is carried through verbatim rather than re-encoded. Its
/// exact byte sequence is what a signature was computed over, so normalizing
/// escapes would be a correctness change disguised as tidiness. The new
/// parameters this product contributes are escaped as it writes them, which is
/// the half it owns.
/// </para>
/// <para>
/// Composition fails rather than guessing for an endpoint that is not an
/// absolute HTTP(S) URL; the caller turns that into a typed failure, which its
/// own adapter redacts. A fragment is dropped: it is never sent to the server,
/// and leaving it in front of an appended query would swallow it.
/// </para>
/// <para>
/// Gated by <c>SearchRequestUriTests</c> and, end to end, by
/// <c>NuGetSearchSourcesTests</c>' signed-endpoint assertions.
/// </para>
/// </remarks>
internal static class SearchRequestUri
{
    /// <summary>
    /// Composes <paramref name="endpoint"/> with <paramref name="parameters"/>,
    /// or returns false when the endpoint cannot be used.
    /// </summary>
    internal static bool TryCompose(
        string? endpoint,
        IReadOnlyList<(string Name, string Value)> parameters,
        out string url)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        url = string.Empty;

        if (string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? parsed)
            || !IsHttpScheme(parsed))
        {
            return false;
        }

        string appended = string.Join(
            '&',
            parameters.Select(static parameter =>
                $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}"));

        // GetLeftPart(Path) is the already-escaped scheme, authority, and path,
        // with the query and fragment removed, so the query can be rebuilt
        // deliberately instead of extended by accident.
        string root = parsed.GetLeftPart(UriPartial.Path);
        string existing = parsed.Query.TrimStart('?');
        string query = existing.Length == 0
            ? appended
            : appended.Length == 0
                ? existing
                : $"{existing}&{appended}";

        string composed = query.Length == 0 ? root : $"{root}?{query}";
        if (!Uri.TryCreate(composed, UriKind.Absolute, out Uri? result)
            || !IsHttpScheme(result))
        {
            return false;
        }

        url = composed;
        return true;
    }

    private static bool IsHttpScheme(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal)
        || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.Ordinal);
}
