using System.Text;
using InertText;

namespace DotnetInspector.Core;

/// <summary>
/// The one owner of what a URL may look like in a diagnostic.
/// </summary>
/// <remarks>
/// <para>
/// A request URL is not safe to print. Some feeds carry the credential in the
/// query (<c>?sig=…</c>, <c>?api-key=…</c>), some in the path (<c>/auth/{token}/</c>),
/// and some in the user-info component; a URL that arrived from a feed's own
/// service index can carry all three plus a bidirectional override that
/// reorders the line it lands in. Every one of those reaches a log, a failure
/// message, or a telemetry tag through the same shape: interpolating the URL.
/// </para>
/// <para>
/// The query is removed whole rather than filtered by parameter name. A feed
/// declares the name as well as the value, so a list of familiar secret-bearing
/// names — <c>sig</c>, <c>access_token</c>, <c>api-key</c> — only recognizes
/// the spellings it has already met: renaming one signed URL's parameter to
/// <c>x</c> republishes the same credential through every diagnostic. The name
/// is feed-controlled text in its own right, so keeping names while dropping
/// values would still carry a hostile scalar into the line. What a reader needs
/// from a diagnostic is which source and which resource, and those are the
/// scheme, host, and path.
/// </para>
/// <para>
/// The fragment goes for the same reason. It is never sent to the server, so it
/// is not diagnostic evidence at all, and it is feed-controlled text that a
/// service index can put anything into.
/// </para>
/// <para>
/// So the rule is one function rather than one habit. A caller that needs to
/// name a URL calls <see cref="ForDiagnostics(string?)"/> and prints what comes
/// back; the request itself still uses the original, unredacted URL. Callers
/// must not build their own redaction — a second implementation is free to
/// drift from this one, and the direction it drifts is a printed secret.
/// </para>
/// <para>
/// Gated by <c>UrlRedactionTests</c> and, on the package paths that consume it,
/// by <c>PackagePayloadAcquisitionTests</c>' log assertions.
/// </para>
/// </remarks>
public static class UrlRedaction
{
    /// <summary>
    /// What replaces a query that was present. It keeps "this resource was
    /// requested with a query" legible without carrying any of it.
    /// </summary>
    public const string QueryMarker = "REDACTED";

    /// <summary>
    /// What replaces text that names an authority but cannot be parsed as a
    /// URL.
    /// </summary>
    /// <remarks>
    /// Such text is the one shape whose components cannot be located, so
    /// nothing in it can be shown to be safe: <c>https://user:secret@bad[</c>
    /// carries a password in a position no parser will point at. Emitting a
    /// fixed marker loses the ability to tell two malformed values apart, which
    /// is the correct trade for a value that is malformed precisely because a
    /// feed or a caller produced something no URI grammar accepts.
    /// </remarks>
    public const string UnparsableMarker = "<unparsable-url>";

    /// <summary>
    /// Returns <paramref name="url"/> with credential-bearing components
    /// removed and non-graphic scalars encoded, ready to print.
    /// </summary>
    /// <remarks>
    /// The result is an <see cref="InertString"/> rather than a
    /// <see cref="string"/> so a caller cannot accidentally re-introduce the
    /// terminal-control hazard by concatenating the raw value instead; its
    /// <c>ToString</c> is the encoded text.
    /// </remarks>
    public static InertString ForDiagnostics(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return InertString.Empty;

        // The query is cut at the text level before anything parses it. A value
        // that is not a URL at all still gets the rule applied, and a value
        // .NET reinterprets — a rooted path parses as an absolute `file:` URI
        // on Unix, with the `?` escaped into the path — cannot smuggle its
        // query through as path text.
        (string locator, bool hadQuery) = SplitLocator(url);
        string rendered =
            Uri.TryCreate(locator, UriKind.Absolute, out Uri? absolute)
                && IsHttpScheme(absolute)
                ? RenderAuthorityAndPath(absolute)
                : TryRedactNetworkPath(locator, out string networkPath)
                    ? networkPath
                : NamesAnAuthority(locator)
                    ? UnparsableMarker
                    : RedactPath(locator);
        return new InertString(
            TextPolicy.Field,
            hadQuery ? $"{rendered}?{QueryMarker}" : rendered);
    }

    /// <summary>
    /// Returns <paramref name="uri"/> with credential-bearing components
    /// removed and non-graphic scalars encoded, or null when it is null.
    /// </summary>
    public static InertString? ForDiagnostics(Uri? uri)
    {
        if (uri == null)
            return null;

        if (!uri.IsAbsoluteUri || !IsHttpScheme(uri))
            return ForDiagnostics(uri.ToString());

        string rendered = RenderAuthorityAndPath(uri);
        return new InertString(
            TextPolicy.Field,
            HasQueryContent(uri.Query) ? $"{rendered}?{QueryMarker}" : rendered);
    }

    /// <summary>
    /// Describes a failed request by its redacted URL, for a log line that must
    /// not carry an exception message.
    /// </summary>
    /// <remarks>
    /// A transport exception's message frequently embeds the request URI —
    /// <c>HttpRequestException</c> and <c>NotSupportedException</c> both do —
    /// so printing it re-opens the channel the redaction closed. The exception
    /// type is a category, not text the remote controls, so it stays.
    /// </remarks>
    public static InertString DescribeRequestFailure(string? url, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return InertString.Format(
            TextPolicy.Field,
            $"{ForDiagnostics(url)} failed with {error.GetType().Name}");
    }

    private static bool HasQueryContent(string query) =>
        query.Length > 0 && query.TrimStart('?').Length > 0;

    private static bool IsHttpScheme(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal)
        || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.Ordinal);

    /// <summary>
    /// True when text names an authority — a scheme, or a user-info separator
    /// ahead of any path separator — and so must not be echoed when it fails to
    /// parse.
    /// </summary>
    /// <remarks>
    /// A relative path is the value this deliberately does not catch: it has no
    /// authority to hide a credential in, and it is a genuinely useful
    /// diagnostic. Anything shaped like a locator is a different matter — the
    /// components a redaction removes live in the part that failed to parse, so
    /// there is nothing left to reason about and the whole value goes.
    /// </remarks>
    private static bool NamesAnAuthority(string locator)
    {
        if (locator.Contains("://", StringComparison.Ordinal))
            return true;

        int authorityEnd = locator.IndexOfAny(['/', '\\']);
        int userInfo = locator.IndexOf('@', StringComparison.Ordinal);
        if (userInfo >= 0 && (authorityEnd < 0 || userInfo < authorityEnd))
            return true;

        // A scheme prefix: one ASCII letter, then letters, digits, and "+-.",
        // terminated by ':'. A Windows drive letter is deliberately excluded by
        // requiring more than one character before the colon.
        int colon = locator.IndexOf(':', StringComparison.Ordinal);
        if (colon < 2 || !char.IsAsciiLetter(locator[0]))
            return false;

        for (int index = 1; index < colon; index++)
        {
            char character = locator[index];
            if (!char.IsAsciiLetterOrDigit(character)
                && character is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryRedactNetworkPath(
        string locator,
        out string redacted)
    {
        int start = 0;
        while (start < locator.Length
            && locator[start] is ' ' or '\t' or '\r' or '\n')
        {
            start++;
        }

        if (start + 1 >= locator.Length
            || !IsPathSeparator(locator[start])
            || !IsPathSeparator(locator[start + 1]))
        {
            redacted = "";
            return false;
        }

        int authorityStart = start + 2;
        int authorityEnd =
            locator.IndexOfAny(['/', '\\'], authorityStart);
        if (authorityEnd < 0)
            authorityEnd = locator.Length;

        int authorityLength = authorityEnd - authorityStart;
        // `///user:SECRET@host/path` is a network path with an empty
        // authority; the credential-shaped text then sits in the path and
        // would survive RedactPath. Fail closed — nothing in an empty
        // authority form can be shown to be safe.
        if (authorityLength == 0)
        {
            redacted = UnparsableMarker;
            return true;
        }

        int userInfoEnd =
            locator.LastIndexOf('@', authorityEnd - 1, authorityLength);
        if (userInfoEnd >= authorityStart)
        {
            locator = string.Concat(
                locator.AsSpan(0, authorityStart),
                locator.AsSpan(userInfoEnd + 1));
        }

        redacted = RedactPath(locator);
        return true;
    }

    private static bool IsPathSeparator(char value) => value is '/' or '\\';

    /// <summary>
    /// Splits text at the first <c>?</c> or <c>#</c>, reporting whether a
    /// non-empty query was present. The fragment is dropped outright: it is
    /// never sent to the server, so it is not evidence, and it is text a feed
    /// controls.
    /// </summary>
    private static (string Locator, bool HadQuery) SplitLocator(string url)
    {
        int cut = url.IndexOfAny(['?', '#']);
        if (cut < 0)
            return (url, false);

        if (url[cut] != '?')
            return (url[..cut], false);

        int fragment = url.IndexOf('#', cut + 1);
        int queryLength =
            (fragment < 0 ? url.Length : fragment) - (cut + 1);
        return (url[..cut], queryLength > 0);
    }

    /// <summary>
    /// Renders an absolute HTTP(S) URL's scheme, host, port, and path — the
    /// parts that say which source and which resource — with the user-info
    /// component removed and the known path-token rule applied.
    /// </summary>
    /// <remarks>
    /// The result is later carried in an <see cref="InertString"/>. That
    /// matters here: <see cref="Uri"/> normalization percent-encodes C0
    /// controls, which makes it look as though non-graphic text were already
    /// handled, but it passes Cf straight through — so a bidi override in a
    /// source URL would survive into a failure message and reorder it.
    /// </remarks>
    private static string RenderAuthorityAndPath(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            UserName = "",
            Password = "",
            Fragment = "",
            Query = "",
        };

        builder.Path = RedactPath(builder.Path);
        try
        {
            return builder.Uri.ToString();
        }
        catch (UriFormatException)
        {
            // HttpClient can carry an effective URI whose malformed host
            // cannot be reconstructed. UriBuilder still exposes the already
            // separated components, so render those after redaction rather
            // than reparsing the original text or replacing the request
            // failure with a diagnostic failure.
            return builder.ToString();
        }
    }

    // Some feeds carry the credential in the path rather than the query. MyGet publishes
    // service index URLs shaped like https://host/F/<feed>/auth/<token>/api/v3/index.json,
    // so the segment following an "auth" segment is a secret. Only that segment is removed:
    // the rest of the path is the feed's identity (an Azure DevOps organization, project and
    // feed all live in the path) and is exactly what a reader needs to tell sources apart.
    private static string RedactPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.Contains("auth", StringComparison.OrdinalIgnoreCase))
            return path;

        var builder = new StringBuilder(path.Length);
        int segmentStart = 0;
        bool previousWasAuth = false;
        bool changed = false;
        for (int index = 0; index <= path.Length; index++)
        {
            if (index < path.Length && !IsPathSeparator(path[index]))
                continue;

            ReadOnlySpan<char> segment =
                path.AsSpan(segmentStart, index - segmentStart);
            bool redact = segment.Length > 0 && previousWasAuth;
            builder.Append(redact ? "REDACTED" : segment);
            if (index < path.Length)
                builder.Append(path[index]);

            changed |= redact;
            // Empty segments do not consume the pending auth state. A path
            // shaped like /auth//SECRET must still redact SECRET; clearing on
            // the empty segment would leak the token.
            if (segment.Length > 0)
            {
                previousWasAuth =
                    segment.Equals("auth", StringComparison.OrdinalIgnoreCase);
            }

            segmentStart = index + 1;
        }

        return changed ? builder.ToString() : path;
    }
}
