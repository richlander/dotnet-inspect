using System.Text.Json;
using System.Text.RegularExpressions;

namespace SourceLinkFetch;

/// <summary>
/// The result of matching a PDB document path against a SourceLink map.
/// </summary>
/// <param name="Remainder">
/// The portion of the document path the matched key did not cover, separator-normalized and
/// unescaped. For an exact (wildcard-free) key this is empty. Callers that want a display path
/// use this; callers that want to fetch use <paramref name="Url"/>.
/// </param>
/// <param name="Url">
/// The source URL, with the key's wildcard substitution applied and each remainder segment
/// percent-encoded. Never null: an entry that carries no usable URL is rejected at parse time
/// rather than kept as an entry that matches and resolves to nothing.
/// </param>
/// <param name="IsPrefixMatch">
/// True when a wildcard key matched a prefix of the path, false when a wildcard-free key matched
/// it exactly. Both can produce an empty <paramref name="Remainder"/>, so a caller deriving a
/// display path cannot tell them apart without this.
/// </param>
/// <param name="SubstitutionOffset">
/// The index in <paramref name="Url"/> where the substituted document text begins, or -1 when
/// nothing was substituted. The substituted run is <see cref="SubstitutionLength"/> characters
/// long. This is reported rather than searched for, because the escaped remainder can also occur
/// in the map's own literal text, and a caller deciding whether the document chose the content
/// needs the site the substitution actually happened at, not one that looks like it.
/// </param>
/// <param name="SubstitutionLength">
/// The length of the substituted run in <paramref name="Url"/>, or 0 when nothing was
/// substituted.
/// </param>
public readonly record struct SourceLinkResolution(
    string Remainder,
    string Url,
    bool IsPrefixMatch,
    int SubstitutionOffset,
    int SubstitutionLength);

/// <summary>
/// Parses a SourceLink map and maps PDB document paths to source URLs.
/// </summary>
/// <remarks>
/// <para>
/// This is the single owner of the SourceLink document-map rule. It implements the mapping
/// behavior specified by the <c>documents</c> schema in the Source Link design document
/// (<c>dotnet/designs</c>, <c>accepted/2020/diagnostics/source-link.md</c>), whose stated rules
/// are:
/// </para>
/// <list type="number">
///   <item>a key carries at most one <c>*</c>, which is replaced by a relative path;</item>
///   <item>a key without <c>*</c> pairs with a URL without <c>*</c>;</item>
///   <item>a key's <c>*</c>, when present, must be its final character;</item>
///   <item>a URL's <c>*</c> may appear <em>anywhere</em> in the URL.</item>
/// </list>
/// <para>
/// Two further behaviors are specified in prose rather than in those four rules, and both are
/// load-bearing: entries resolve "in order from most specific to least specific" (implemented as
/// a descending sort on key-prefix length, so an exact key and a longer prefix both beat a
/// shorter prefix regardless of document order), and "original source file paths are compared
/// case-insensitively to documents".
/// </para>
/// <para>
/// Rule 4 is the one most easily missed, because the common GitHub map puts the URL's <c>*</c>
/// last. Azure DevOps maps do not: their documented shape is
/// <c>.../items?scopePath=/*&amp;versionDescriptor.version=&lt;commit&gt;</c>. A matcher that only
/// substitutes a trailing <c>*</c> emits that URL with a literal asterisk still in it.
/// </para>
/// <para>
/// Deviations from the reference consumer, both deliberate:
/// </para>
/// <list type="bullet">
///   <item>
///     Separators are normalized to <c>/</c> on both the key and the document path before
///     comparison, so a map authored with one separator still matches document names recorded
///     with the other.
///   </item>
///   <item>
///     A non-conformant entry — a key that breaks the wildcard rules, a value that is not a
///     string, or a value that names no origin source can be retrieved from — is rejected
///     individually and recorded in <see cref="RejectedKeys"/>, rather than
///     invalidating the whole map. Rejecting the map would let one bad key deny source for every
///     other document, and keeping the entry would let it match and outrank valid, less specific
///     entries. The rejection is recorded here rather than being silently indistinguishable from
///     a key that simply did not match.
///   </item>
/// </list>
/// </remarks>
public partial class SourceLinkResolver
{
    /// <summary>
    /// A single validated map entry, pre-split so that matching does no parsing.
    /// <paramref name="PathPrefix"/> is the key with its trailing <c>*</c> removed when
    /// <paramref name="IsPrefix"/>; otherwise it is the whole key and matching is equality.
    /// <paramref name="UrlSuffix"/> is null when the URL carries no wildcard, which is what
    /// distinguishes "substitute into this URL" from "this URL is the whole answer".
    /// </summary>
    private readonly record struct Entry(
        string PathPrefix,
        bool IsPrefix,
        string UrlPrefix,
        string? UrlSuffix);

    private readonly Entry[] _entries;

    /// <summary>
    /// The document keys exactly as authored, in document order, before separator normalization
    /// and before the trailing wildcard is split off. Callers that ask a question about the map's
    /// text -- such as whether its paths were normalized by a deterministic build -- must see what
    /// was written, not what matching rewrote it into.
    /// </summary>
    public IReadOnlyList<string> DocumentKeys { get; }

    /// <summary>
    /// Keys that were dropped because the entry does not conform to the SourceLink rules, either
    /// in the key's wildcard placement or because the value was not a string.
    /// </summary>
    /// <remarks>
    /// This makes a rejected key <em>available</em> to a caller, not visible to a user: no
    /// command reports it today, so a map whose every entry is rejected is currently
    /// indistinguishable in output from a healthy one. Tracked by
    /// <see href="https://github.com/richlander/dotnet-inspect/issues/3590">#3590</see>. What is
    /// gated here is the narrower claim that a rejected entry does not participate in matching
    /// and does not shadow a valid one — see
    /// <c>SourceLinkMapConformanceTests.ARejectedKey_IsReportedAndDoesNotDenyTheRestOfTheMap</c>
    /// and <c>AnEntryWhoseValueIsNotAString_IsRejectedRatherThanMatchingNothing</c>.
    /// </remarks>
    public IReadOnlyList<string> RejectedKeys { get; }

    /// <summary>
    /// Why the map as a whole could not be read, or null when it was read. A map that fails here
    /// resolves nothing at all: a map with more than one valid reading (for example a duplicated
    /// <c>documents</c> key) must not bind one of them.
    /// </summary>
    public string? ParseError { get; }

    /// <summary>True when no entry is available to match against.</summary>
    public bool IsEmpty => _entries.Length == 0;

    /// <summary>An empty map, which resolves nothing.</summary>
    public static SourceLinkResolver Empty { get; } = new([], [], [], parseError: null);

    private SourceLinkResolver(
        Entry[] entries,
        string[] documentKeys,
        IReadOnlyList<string> rejectedKeys,
        string? parseError)
    {
        _entries = entries;
        DocumentKeys = documentKeys;
        RejectedKeys = rejectedKeys;
        ParseError = parseError;
    }

    internal SourceLinkResolver(Dictionary<string, string> documentMappings)
    {
        // Widened to a nullable value type because a map read from JSON may carry a non-string
        // value. That is a malformed entry, and the entry parser rejects it; this overload's
        // callers supply strings, so none of them can produce one.
        Dictionary<string, string?> mappings = new(documentMappings.Count);
        foreach (var (key, url) in documentMappings)
            mappings[key] = url;

        _entries = Build(mappings, out var rejected);
        DocumentKeys = [.. mappings.Keys];
        RejectedKeys = rejected;
        ParseError = null;
    }

    /// <summary>
    /// Parses a SourceLink map. Never throws: an unreadable map yields a resolver that resolves
    /// nothing and reports why through <see cref="ParseError"/>.
    /// </summary>
    public static SourceLinkResolver Parse(string? sourceLinkJson)
    {
        // Only absence is Empty. A payload that is present and says nothing -- blank, truncated,
        // or blanked out -- falls through to the parser, which rejects it and reports why. Widening
        // this test to IsNullOrWhiteSpace would make such a map indistinguishable from an assembly
        // that ships no SourceLink at all.
        if (sourceLinkJson is null)
            return Empty;

        Dictionary<string, string?> mappings;
        try
        {
            mappings = ReadDocuments(sourceLinkJson);
        }
        catch (JsonException e)
        {
            // A map with more than one valid reading, or no valid reading, resolves nothing.
            return new SourceLinkResolver([], [], [], e.Message);
        }

        var entries = Build(mappings, out var rejected);
        return new SourceLinkResolver(
            entries, [.. mappings.Keys], rejected, parseError: null);
    }

    /// <summary>
    /// Applies the SourceLink map to a document path, returning the source URL or null when no
    /// entry matches.
    /// </summary>
    public string? ResolveUrl(string filePath)
        => TryResolve(filePath, out var resolution) ? resolution.Url : null;

    /// <summary>
    /// Matches a document path against the map. Returns false when no entry matches, which is
    /// the ordinary case for a document the map does not cover.
    /// </summary>
    public bool TryResolve(string filePath, out SourceLinkResolution resolution)
    {
        resolution = default;

        if (string.IsNullOrEmpty(filePath))
            return false;

        // A wildcard in the document path itself is not a path; it would let one document claim
        // a mapping meant for a whole subtree. The reference consumer refuses it, and so does this.
        if (filePath.Contains('*', StringComparison.Ordinal))
            return false;

        string path = NormalizeSeparators(filePath);

        foreach (var entry in _entries)
        {
            if (entry.IsPrefix)
            {
                if (!path.StartsWith(entry.PathPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string remainder = path[entry.PathPrefix.Length..];
                string substituted = SubstituteUrl(entry, remainder, out int offset, out int length);
                resolution = new SourceLinkResolution(
                    remainder, substituted, IsPrefixMatch: true, offset, length);
                return true;
            }

            if (string.Equals(path, entry.PathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                resolution = new SourceLinkResolution(
                    string.Empty, entry.UrlPrefix, IsPrefixMatch: false, -1, 0);
                return true;
            }
        }

        return false;
    }

    private static string SubstituteUrl(Entry entry, string remainder, out int offset, out int length)
    {
        // Rule 4: the wildcard may sit anywhere in the URL, so the substitution is
        // prefix + path + suffix rather than an append to the end.
        if (entry.UrlSuffix is null)
        {
            offset = -1;
            length = 0;
            return entry.UrlPrefix;
        }

        string escaped = EscapePathSegments(remainder);
        offset = entry.UrlPrefix.Length;
        length = escaped.Length;
        return entry.UrlPrefix + escaped + entry.UrlSuffix;
    }

    /// <summary>
    /// Percent-encodes each segment of the substituted path while leaving separators intact, so
    /// that a document name containing a space, a <c>#</c>, or a <c>?</c> cannot change the shape
    /// of the URL it is spliced into.
    /// </summary>
    private static string EscapePathSegments(string path)
        => string.Join('/', path.Split('/', '\\').Select(Uri.EscapeDataString));

    private static string NormalizeSeparators(string path) => path.Replace('\\', '/');

    private static Entry[] Build(Dictionary<string, string?> mappings, out IReadOnlyList<string> rejectedKeys)
    {
        List<Entry> entries = new(mappings.Count);
        List<string> rejected = [];

        foreach (var (key, url) in mappings)
        {
            if (TryParseEntry(key, url, out var entry))
                entries.Add(entry);
            else
                rejected.Add(key);
        }

        // "Resolved in order from most specific to least specific." Length alone does not order
        // the map: an exact key and the prefix key derived from it have the same length once the
        // trailing wildcard is stripped, and List<T>.Sort is unstable, so a length-only comparison
        // decides that pair by JSON enumeration order. That is the same document-order dependence
        // this type exists to remove, so the comparison is a total order instead.
        entries.Sort(static (left, right) =>
        {
            // A longer prefix is more specific, so it is checked first.
            int byLength = right.PathPrefix.Length.CompareTo(left.PathPrefix.Length);
            if (byLength != 0)
                return byLength;

            // "Absolute paths will be checked before a wildcard path with a matching base": an
            // exact key names one document, a prefix key names a subtree, so the exact key is the
            // more specific of the two.
            int byExactness = left.IsPrefix.CompareTo(right.IsPrefix);
            if (byExactness != 0)
                return byExactness;

            // Nothing below here can change which entry a path matches -- two distinct keys of
            // equal length and equal kind cannot both match one path unless they differ only by
            // case or by separator, in which case they are the same rule spelled twice. These
            // comparisons exist so that the resulting order is total, and therefore independent
            // of how the map was enumerated, rather than merely usually right.
            //
            // Of the two, only the URL comparison is observable through resolution, and only on
            // the separator shape: keys differing by separator normalize to one PathPrefix, so
            // byPrefix ties and byUrlPrefix decides. Keys differing by case do not tie on
            // PathPrefix, so that row never reaches byUrlPrefix --
            // AMapWhoseKeysTieOnLengthAndKind_ResolvesTheSameWhicheverOrderTheyAreWrittenIn gates
            // byUrlPrefix on the separator row alone, and its case row gates that byPrefix leaves
            // the order total rather than gating byPrefix itself.
            //
            // byPrefix orders entries that resolve identically, so no assertion on resolved output
            // can reach it: erasing it leaves the whole suite green, because byUrlPrefix then
            // decides and both write orders still agree. It is here for a total order and is
            // deliberately recorded as ungated rather than credited to that test.
            int byPrefix = string.CompareOrdinal(left.PathPrefix, right.PathPrefix);
            if (byPrefix != 0)
                return byPrefix;

            int byUrlPrefix = string.CompareOrdinal(left.UrlPrefix, right.UrlPrefix);
            return byUrlPrefix != 0 ? byUrlPrefix : string.CompareOrdinal(left.UrlSuffix, right.UrlSuffix);
        });

        rejectedKeys = rejected;
        return [.. entries];
    }

    private static bool TryParseEntry(string key, string? url, out Entry entry)
    {
        entry = default;

        if (string.IsNullOrEmpty(key))
            return false;

        string normalizedKey = NormalizeSeparators(key);

        int keyStar = normalizedKey.IndexOf('*', StringComparison.Ordinal);
        bool isPrefix;
        if (keyStar < 0)
        {
            isPrefix = false;
        }
        else if (keyStar == normalizedKey.Length - 1)
        {
            // Rule 3: the wildcard is the final character, so the key is a prefix and matching is
            // a prefix test. A prefix test cannot backtrack, which is what keeps a hostile key
            // carrying many wildcards from costing exponential time.
            isPrefix = true;
            normalizedKey = normalizedKey[..keyStar];
        }
        else
        {
            // A non-final wildcard breaks rule 3, and a second wildcard leaves the first one
            // non-final, so this single test rejects both violations.
            return false;
        }

        if (url is null)
        {
            // A non-string JSON value is a malformed entry, not a mapping to nothing. Letting it
            // into the map made it match: it resolved documents to no URL at all, and -- being
            // ordered by specificity like any other entry -- a malformed 'C:/src/*' outranked a
            // valid 'C:/*' and silently swallowed the URL that would otherwise have resolved.
            // That is a failure wearing the shape of an empty success. Rejecting the entry keeps
            // it out of matching and puts the key in RejectedKeys, where it is visible.
            return false;
        }

        int urlStar = url.IndexOf('*', StringComparison.Ordinal);

        if (urlStar < 0)
        {
            // Rule 2, in the direction the reference consumer states but does not enforce: "if
            // the file path contains a * the URL must contain a *". A wildcard key paired with a
            // constant URL maps every document in a subtree to one file, so the tool would show
            // one file's content as the source of all of them. Wrong content is worse than no
            // content, so the entry is rejected and reported rather than honoured.
            if (isPrefix)
                return false;

            if (!ProducesAFetchableRequest(url, urlSuffix: null))
                return false;

            entry = new Entry(normalizedKey, isPrefix, url, UrlSuffix: null);
            return true;
        }

        // Rule 2: a wildcard URL requires a wildcard key, or there is nothing to substitute.
        if (!isPrefix)
            return false;

        string urlSuffix = url[(urlStar + 1)..];

        // Rule 1: "one and only one".
        if (urlSuffix.Contains('*', StringComparison.Ordinal))
            return false;

        if (!ProducesAFetchableRequest(url[..urlStar], urlSuffix))
            return false;

        entry = new Entry(normalizedKey, isPrefix, url[..urlStar], urlSuffix);
        return true;
    }

    /// <summary>
    /// Whether an entry can produce a request that actually retrieves the document it matched:
    /// an absolute <c>http</c> or <c>https</c> URL, whose origin does not depend on the document,
    /// and whose substitution reaches the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the same defect round 7 fixed for a non-string value, in the shapes a string can
    /// take. A value that is not a URL at all — <c>"*"</c>, <c>"/nope/*"</c>,
    /// <c>"//evil.test/*"</c> — satisfies every wildcard rule, so it entered the map, matched, and
    /// resolved documents to a string no consumer can fetch. Ordered by specificity like any
    /// other entry, such a value outranked a valid less-specific entry and silently swallowed the
    /// URL that would otherwise have resolved. The specification calls the value a URL "where the
    /// source file can be retrieved via http or https", so an entry that cannot produce one is
    /// non-conformant and is rejected individually into
    /// <see cref="SourceLinkResolver.RejectedKeys"/> like any other non-conformant entry.
    /// </para>
    /// <para>
    /// Checking the URL text before the wildcard is not enough, and the claim that it was is the
    /// mistake this method exists to correct. <c>https://example.test:*/fixed.cs</c> has the
    /// fixed part <c>https://example.test:</c>, which <see cref="Uri.TryCreate(string, UriKind,
    /// out Uri)"/> accepts as absolute — and then every document resolves to
    /// <c>https://example.test:&lt;path&gt;/fixed.cs</c>, which is not a URL at all. The wildcard
    /// is not confined to the part after the origin, so the origin is not fixed until the
    /// substitution is done.
    /// </para>
    /// <para>
    /// So the pattern is checked by substituting two different probes and comparing the results.
    /// Three properties have to hold, and each has its own way of failing:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     Both must be absolute <c>http</c>/<c>https</c> URLs. A wildcard in the port
    ///     (<c>https://h.test:*/x</c>) or in the scheme (<c>htt*ps://h.test/x</c>) fails here.
    ///   </item>
    ///   <item>
    ///     Both must have the same authority, or the document path chooses the origin. A wildcard
    ///     in the host (<c>https://*.test/x</c>) or in the user information
    ///     (<c>https://user:*@h.test/x</c>) fails here.
    ///   </item>
    ///   <item>
    ///     They must differ in the part actually sent to the server. A wildcard confined to the
    ///     fragment (<c>https://h.test/README.md#*</c>) is never transmitted, and one erased by
    ///     dot-segment removal (<c>https://h.test/a/*/../fixed.cs</c>, or the same traversal
    ///     written inside a query value as <c>?path=/*/../fixed.cs</c>) is normalized away — both
    ///     stop the document from choosing the request at all. That is the same harm rule 2
    ///     refuses for a wildcard key paired with a constant URL, so it is refused on the same
    ///     terms: wrong content is worse than no content.
    ///   </item>
    /// </list>
    /// <para>
    /// What this establishes is that the document changes the request. It does not establish that
    /// the server <em>uses</em> the change, and no host-agnostic rule can, because the same URL
    /// shape means opposite things on the two hosts SourceLink maps actually name. Measured: a
    /// wildcard confined to the query is the documented Azure Repos shape, where <c>path=</c>
    /// selects the file (<c>path=/README.md</c> → 200, a different path → 404); the identical
    /// shape aimed at <c>raw.githubusercontent.com</c> serves one file for every document, because
    /// that host ignores the query entirely (<c>?document=A.cs</c>, <c>?document=B.cs</c> and no
    /// query all return the same bytes). Refusing the shape <em>here</em> would break every
    /// Azure Repos assembly, which is the bug this matcher was collapsed to fix. Deciding it
    /// needs a per-host content selector, which belongs with the host grammars in
    /// <c>SourceLinkProvenance</c> rather than in this host-agnostic matcher, and is tracked by
    /// issue #3599.
    /// </para>
    /// <para>
    /// That selector now exists on the provenance side, for both hosts this reader knows:
    /// <c>raw.githubusercontent.com</c> refuses a query outright, and every host requires the
    /// substituted document text to land in the component that actually selects content. So a map
    /// may still point every document at one file, but it can no longer report an origin while
    /// doing so. An earlier version of this note said
    /// provenance stayed correct in that case, because the origin it reports is genuinely where
    /// the content is served from. That is true of the origin and beside the point for the user,
    /// who was shown one file as the source of every document under a clean attribution — review
    /// found it exactly that way, once per host, in consecutive rounds. What is left to
    /// #3599 is the <em>resolution</em> half: such a map still fetches and displays that one
    /// file, now unattributed.
    /// </para>
    /// <para>
    /// Restricting the scheme rather than only requiring an absolute URI is deliberate, and is
    /// the same scope decision the host allow list makes: <c>file:///tmp/*</c> is a well-formed
    /// absolute URI that this product will never fetch, because
    /// <c>HttpClientFactory.IsAllowedFetchScheme</c> — the guard on every untrusted-source fetch —
    /// admits <c>http</c> and <c>https</c> only. Admitting it into the map would reintroduce
    /// exactly the shadowing this refuses, one layer later and out of sight.
    /// </para>
    /// <para>
    /// Gated by
    /// <c>SourceLinkMapConformanceTests.AnEntryWhoseUrlNamesNoFetchableOrigin_IsRejectedRatherThanShadowingAValidEntry</c>,
    /// whose accept rows are the shapes <c>Microsoft.SourceLink.GitHub</c> and
    /// <c>Microsoft.SourceLink.AzureRepos.Git</c> actually generate, and by
    /// <c>EachRefusedUrlShape_IsRefusedForItsOwnReasonAndNotAnIncidentalOne</c>, which pins each
    /// property against a close twin that differs only in the cause, rather than against the
    /// refusal alone.
    /// </para>
    /// </remarks>
    private static bool ProducesAFetchableRequest(string urlPrefix, string? urlSuffix)
    {
        if (urlSuffix is null)
            return TryReadFetchable(urlPrefix, out _);

        // Two probes, not one: a single substitution says whether some URL is produced, but not
        // whether the document had any bearing on which one.
        string first = urlPrefix + "a" + urlSuffix;
        string second = urlPrefix + "b" + urlSuffix;

        // Both readings must hold. A URL that survives one and not the other has two readings,
        // and which one applies is the receiving server's choice rather than anything the map
        // states.
        return SubstitutionSurvives(first, second)
            && SubstitutionSurvives(DecodeSeparators(first), DecodeSeparators(second));
    }

    /// <summary>
    /// Whether two probe substitutions still name one origin and two different requests.
    /// </summary>
    private static bool SubstitutionSurvives(string firstUrl, string secondUrl)
    {
        if (!TryReadFetchable(firstUrl, out Uri? first) ||
            !TryReadFetchable(secondUrl, out Uri? second))
        {
            return false;
        }

        if (!string.Equals(
                first!.GetLeftPart(UriPartial.Authority),
                second!.GetLeftPart(UriPartial.Authority),
                StringComparison.Ordinal))
        {
            return false;
        }

        // UriPartial.Query is everything the request line carries -- scheme, authority, path and
        // query -- and so excludes exactly the fragment, which is never transmitted. Uri resolves
        // dot segments in the path but not in the query, so the query is resolved here: otherwise
        // the one refusal rule 3 states is enforced only where Uri happened to do the work, and
        // the same erasure written inside a query value walks straight through.
        return !string.Equals(
            RequestKey(first),
            RequestKey(second),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The request line, with dot segments resolved in the query as well as the path.
    /// </summary>
    private static string RequestKey(Uri url)
        => url.GetLeftPart(UriPartial.Path) + "?" + ResolveDotSegmentsInQuery(url.Query);

    /// <summary>
    /// Removes <c>.</c> and <c>..</c> segments from each query value, where <see cref="Uri"/>
    /// leaves them alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Azure Repos carries the document in <c>path=</c>, so a traversal written into a query value
    /// erases the substitution exactly the way one written into the path does, and rule 3 refuses
    /// it for the same reason. <see cref="Uri"/> normalizes only <see cref="Uri.AbsolutePath"/>,
    /// so without this the query form is accepted and the path form is not, which is an
    /// inconsistency in the rule rather than a decision about it.
    /// </para>
    /// <para>
    /// Each parameter's value is resolved on its own, so a <c>..</c> can never escape into the
    /// parameter names around it. Treating the query as one <c>/</c>-separated run instead was the
    /// first attempt, and it failed in both directions: a <c>..</c> could pop the parameter names,
    /// falsely refusing a URL whose document still chose the request, and protecting those names
    /// with a fixed floor then left <c>?path=*/../README.md</c> accepted, because the substitution
    /// lands in the same run as the names it was protecting. Parsing the query removes the choice
    /// -- the parameter name is outside the poppable text structurally rather than by index.
    /// </para>
    /// </remarks>
    private static string ResolveDotSegmentsInQuery(string query)
    {
        if (!query.Contains("..", StringComparison.Ordinal))
        {
            return query;
        }

        string[] parameters = query.Split('&');
        for (int i = 0; i < parameters.Length; i++)
        {
            int equals = parameters[i].IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                continue;
            }

            parameters[i] = string.Concat(
                parameters[i].AsSpan(0, equals + 1),
                ResolveDotSegments(parameters[i][(equals + 1)..]));
        }

        return string.Join('&', parameters);
    }

    /// <summary>
    /// Removes <c>.</c> and <c>..</c> segments from one <c>/</c>-separated value.
    /// </summary>
    private static string ResolveDotSegments(string value)
    {
        if (!value.Contains("..", StringComparison.Ordinal))
        {
            return value;
        }

        string[] segments = value.Split('/');
        var kept = new List<string>(segments.Length);
        foreach (string segment in segments)
        {
            if (segment == "." && kept.Count > 0)
            {
                continue;
            }

            if (segment == ".." && kept.Count > 0)
            {
                kept.RemoveAt(kept.Count - 1);
                continue;
            }

            kept.Add(segment);
        }

        return string.Join('/', kept);
    }

    /// <summary>
    /// The reading a server reaches when it percent-decodes before resolving the path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Uri"/> preserves <c>%2f</c> and <c>%5c</c> verbatim and never treats them as
    /// separators, so dot-segment removal does not see them and the two probes come out looking
    /// different. A server that decodes first sees something else entirely. Measured against the
    /// host most SourceLink maps name, at a real commit:
    /// </para>
    /// <code>
    /// .../&lt;sha&gt;/README.md                 200
    /// .../&lt;sha&gt;/src/../README.md           200
    /// .../&lt;sha&gt;/src%2f..%2fREADME.md       200   &lt;- decoded, then traversed
    /// .../&lt;sha&gt;/src%5c..%5cREADME.md       404
    /// </code>
    /// <para>
    /// So <c>https://host/*%2f..%2ffixed.cs</c> passes the undecoded reading — the probes differ
    /// as text — while <c>raw.githubusercontent.com</c> serves one file's content as the source of
    /// every document under the key. That is the harm rule 2 refuses for a wildcard key paired
    /// with a constant URL, reached by hiding the separator from the client's parser instead.
    /// </para>
    /// <para>
    /// Both separators are decoded even though only one is live at that host: which of the two a
    /// server honours is a property of the server, and the reader's own provenance layer already
    /// refuses both for the same reason. Decoding only affects entries that carry an encoded
    /// separator, and a substitution that still reaches the server after decoding is still
    /// accepted, so a map using <c>%2f</c> harmlessly — inside a query value, say — is unaffected.
    /// </para>
    /// </remarks>
    private static string DecodeSeparators(string url) =>
        EncodedSeparatorRegex().Replace(url, "/");

    [GeneratedRegex("%2f|%5c", RegexOptions.IgnoreCase)]
    private static partial Regex EncodedSeparatorRegex();

    private static bool TryReadFetchable(string url, out Uri? uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>
    /// Reads the <c>documents</c> object, rejecting duplicate property names. A duplicated key
    /// has more than one valid reading, and binding either one silently picks an origin the
    /// assembly did not unambiguously declare.
    /// </summary>
    /// <exception cref="JsonException">
    /// The map is malformed or contains a duplicate property name.
    /// </exception>
    private static Dictionary<string, string?> ReadDocuments(string sourceLinkJson)
    {
        Dictionary<string, string?> mappings = [];

        using var document = JsonDocument.Parse(
            sourceLinkJson,
            new JsonDocumentOptions { AllowDuplicateProperties = false });

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException(
                $"The SourceLink map's root is {document.RootElement.ValueKind}, not an object.");
        }

        if (!document.RootElement.TryGetProperty("documents", out var documents))
        {
            // A map carrying only other properties declares no documents. That is an empty map,
            // not a malformed one: the format reserves unknown root properties for extensibility.
            return mappings;
        }

        if (documents.ValueKind != JsonValueKind.Object)
        {
            // A 'documents' value that is not an object has no reading at all. Returning an empty
            // map here would turn a malformed, attacker-supplied input into success-shaped
            // emptiness indistinguishable from an assembly that ships no SourceLink.
            throw new JsonException(
                $"The SourceLink map's 'documents' value is {documents.ValueKind}, not an object.");
        }

        foreach (var property in documents.EnumerateObject())
        {
            // A non-string value is a malformed entry. It is carried as null so that the key
            // reaches the entry parser and is rejected there, visibly, alongside every other
            // non-conformant key -- rather than being dropped here, where the map would look as
            // though the key had never been written.
            mappings[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : null;
        }

        return mappings;
    }

}
