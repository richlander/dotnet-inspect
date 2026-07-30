using System.Reflection.Metadata;
using System.Text;
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
public readonly record struct SourceLinkResolution(string Remainder, string Url, bool IsPrefixMatch);

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
///     A non-conformant entry — a key that breaks the wildcard rules, or a value that is not a
///     string — is rejected individually and recorded in <see cref="RejectedKeys"/>, rather than
///     invalidating the whole map. Rejecting the map would let one bad key deny source for every
///     other document, and keeping the entry would let it match and outrank valid, less specific
///     entries. The rejection is recorded here rather than being silently indistinguishable from
///     a key that simply did not match.
///   </item>
/// </list>
/// </remarks>
public class SourceLinkResolver
{
    // SourceLink GUID: CC110556-A091-4D38-9FEC-25AB9A351A6A
    private static readonly Guid SourceLinkGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

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
    /// Creates a resolver from a PDB metadata reader. Returns null when the PDB carries no
    /// SourceLink map at all; a map that is present but unusable yields a resolver whose
    /// <see cref="ParseError"/> or <see cref="RejectedKeys"/> says so.
    /// </summary>
    public static SourceLinkResolver? Create(MetadataReader pdbReader)
    {
        string? sourceLinkJson = ExtractSourceLinkJson(pdbReader);
        return sourceLinkJson is null ? null : Parse(sourceLinkJson);
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
                resolution = new SourceLinkResolution(
                    remainder, SubstituteUrl(entry, remainder), IsPrefixMatch: true);
                return true;
            }

            if (string.Equals(path, entry.PathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                resolution = new SourceLinkResolution(
                    string.Empty, entry.UrlPrefix, IsPrefixMatch: false);
                return true;
            }
        }

        return false;
    }

    private static string SubstituteUrl(Entry entry, string remainder)
    {
        // Rule 4: the wildcard may sit anywhere in the URL, so the substitution is
        // prefix + path + suffix rather than an append to the end.
        return entry.UrlSuffix is null
            ? entry.UrlPrefix
            : entry.UrlPrefix + EscapePathSegments(remainder) + entry.UrlSuffix;
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

        entry = new Entry(normalizedKey, isPrefix, url[..urlStar], urlSuffix);
        return true;
    }

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

    /// <summary>
    /// Extracts SourceLink JSON from a PDB metadata reader.
    /// </summary>
    internal static string? ExtractSourceLinkJson(MetadataReader reader)
    {
        foreach (CustomDebugInformationHandle handle in reader.CustomDebugInformation)
        {
            CustomDebugInformation info = reader.GetCustomDebugInformation(handle);
            Guid kind = reader.GetGuid(info.Kind);

            if (kind == SourceLinkGuid)
            {
                byte[] bytes = reader.GetBlobBytes(info.Value);
                return Encoding.UTF8.GetString(bytes);
            }
        }

        return null;
    }
}
