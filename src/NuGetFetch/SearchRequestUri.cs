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
/// Existing parameters not owned by this product are carried through verbatim
/// rather than re-encoded. Their exact byte sequence may be covered by a
/// signature, so normalizing escapes would be a correctness change disguised
/// as tidiness. A same-named existing parameter is removed before the
/// product-owned value is appended; otherwise a server could honor the
/// feed-supplied value instead. New parameters are escaped as they are written.
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

        int fragmentDelimiter = endpoint.IndexOf('#', StringComparison.Ordinal);
        string withoutFragment = fragmentDelimiter < 0
            ? endpoint
            : endpoint[..fragmentDelimiter];
        int queryDelimiter = withoutFragment.IndexOf(
            '?',
            StringComparison.Ordinal);
        string root = queryDelimiter < 0
            ? withoutFragment
            : withoutFragment[..queryDelimiter];
        int authorityStart = root.IndexOf("://", StringComparison.Ordinal) + 3;
        if (root.IndexOf('/', authorityStart) < 0)
        {
            root += "/";
        }

        string existing = queryDelimiter < 0
            ? ""
            : withoutFragment[(queryDelimiter + 1)..];
        if (existing.Length > 0 && parameters.Count > 0)
        {
            var ownedNames = new HashSet<string>(
                parameters.Select(static parameter => parameter.Name),
                StringComparer.OrdinalIgnoreCase);
            existing = string.Join(
                '&',
                existing.Split('&').Where(
                    pair => !ownedNames.Contains(DecodeParameterName(pair))));
        }

        string query = existing.Length == 0
            ? appended
            : appended.Length == 0
                ? existing
                : $"{existing}&{appended}";

        string composed = query.Length == 0 ? root : $"{root}?{query}";
        if (!NuGetHttpRequest.TryCreatePreservingPathAndQuery(
                composed,
                out _))
        {
            return false;
        }

        url = composed;
        return true;
    }

    private static bool IsHttpScheme(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal)
        || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.Ordinal);

    private static string DecodeParameterName(string pair)
    {
        int separator = pair.IndexOf('=', StringComparison.Ordinal);
        string name = separator < 0 ? pair : pair[..separator];
        return Uri.UnescapeDataString(name.Replace("+", " ", StringComparison.Ordinal));
    }
}
