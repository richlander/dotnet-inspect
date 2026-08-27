using System.Globalization;
using System.Text;

namespace SourceLinkFetch;

public static partial class SourceLinkProvenance
{
    /// <summary>
    /// Requires the text substituted for the key's wildcard to land in the component that
    /// actually selects content on the origin's host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Agreement on the origin tuple is not enough on its own, and neither is the matcher's
    /// two-probe check. That check compares request <em>text</em>, so it is satisfied by any
    /// varying component, including one the host does not read. When the varying component is
    /// inert, every document resolves to a distinct URL that fetches the <em>same</em> file, and
    /// the origin reported is genuinely where that file is served from — so both existing checks
    /// pass and the user is shown one file as the source of every document under a clean
    /// attribution.
    /// </para>
    /// <para>
    /// Measured against <c>dev.azure.com/dnceng-public/public</c>, repository
    /// <c>dotnet-public-wiki</c>, with <c>path=/README.md</c> fixed: <c>api-version</c> of
    /// <c>1.0</c>, <c>7.1</c>, <c>1.0-preview</c> and <c>5.0</c> all return the same content,
    /// SHA-256 <c>0129277c5fd5e35a…</c>. So <c>{"*": ".../items?api-version=*&amp;…&amp;path=/README.md"}</c>
    /// attributed cleanly while serving one file for every document. The allow list says each
    /// parameter is <em>understood</em>; it does not say each one <em>selects</em>, and this is
    /// the rule that says which ones do.
    /// </para>
    /// <para>
    /// <c>scopePath</c> is on the accept list because it was measured to select, not because the
    /// allow list already named it — that is the assumption this rule exists to stop making.
    /// Against the same repository, <c>scopePath=/README.md</c> returns the same 985 bytes and
    /// the same SHA-256 as <c>path=/README.md</c>, while <c>scopePath=/</c> returns a different
    /// 425-byte response. It names a collection rather than an item, and serves the file anyway.
    /// </para>
    /// <para>
    /// This runs per document rather than across documents, because an assembly with a single
    /// document offers nothing to compare and the defect is present there just the same.
    /// </para>
    /// <para>
    /// Offsets index the resolved URL's raw text, and the substitution site is reported by the
    /// matcher rather than searched for: the escaped remainder can also occur in the map's own
    /// literal text, so searching could find a lookalike and clear a substitution that landed
    /// somewhere inert. The substituted run cannot escape the component it lands in, because
    /// <c>EscapePathSegments</c> percent-encodes every character that would end one —
    /// <c>?</c>, <c>&amp;</c> and <c>#</c> — leaving only <c>/</c>, which is legal inside both a
    /// path and a query value.
    /// </para>
    /// <para>
    /// Gated by <c>SourceLinkProvenanceTests.ASubstitutionThatSelectsNoContent_IsNotAttributable</c>,
    /// whose accept rows are the shapes <c>Microsoft.SourceLink.GitHub</c> and
    /// <c>Microsoft.SourceLink.AzureRepos.Git</c> generate — <c>{sha}/*</c> in the path and
    /// <c>path=/*</c> in the query respectively.
    /// </para>
    /// </remarks>
    internal static bool TryCheckSubstitutionSelectsContent(
        string url,
        string host,
        int offset,
        int length,
        out string rejection)
    {
        rejection = "";

        // A wildcard-free key resolves one document to one fixed URL. Nothing varies with the
        // document, so there is no question of what the variation selects.
        if (offset < 0)
        {
            return true;
        }

        int end = offset + length;
        int authorityEnd = AuthorityEnd(url);
        int queryStart = url.IndexOf('?', StringComparison.Ordinal);
        int fragmentStart = url.IndexOf('#', StringComparison.Ordinal);
        int pathEnd = queryStart >= 0 ? queryStart : (fragmentStart >= 0 ? fragmentStart : url.Length);

        if (string.Equals(host, GitHubRawHost, StringComparison.Ordinal))
        {
            // This host serves the path, so the path is the only place a substitution can select
            // anything. The query is already refused outright for this host, which leaves the
            // authority: a map may spell the host itself with the wildcard, and a document that
            // happens to spell 'raw' would reach here with a path that is the same for every
            // document it does resolve.
            if (offset < authorityEnd || end > pathEnd)
            {
                rejection =
                    $"'{host}' selects content by path, and the document text is " +
                    "substituted outside it, so every document resolves to the same file";
                return false;
            }

            return true;
        }

        // Azure DevOps serves the file named by 'path' or 'scopePath'; every other parameter it
        // accepts describes the request rather than choosing the file. A substitution anywhere
        // else -- in the route, the repository segment, 'api-version', or a version selector that
        // the immutability rules already pin to one commit -- leaves the served file fixed.
        if (queryStart >= 0
            && TrySpanOfContentSelector(
                url,
                queryStart,
                out int valueStart,
                out int valueEnd,
                out bool requestAlwaysFails)
            && (requestAlwaysFails || (offset >= valueStart && end <= valueEnd)))
        {
            return true;
        }

        rejection =
            $"'{host}' selects content by the 'path' or 'scopePath' parameter, and the " +
            "document text is substituted outside it, so every document resolves to the same file";
        return false;
    }

    /// <summary>
    /// Whether a substitution may resolve: refuses one that would fetch a fixed file instead of
    /// the document it represents, on a host whose content selector this reader knows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the <em>resolution</em> half of issue #3599, and it is deliberately a weaker
    /// predicate than the attribution half above rather than the same one applied twice. Refusing
    /// to resolve wherever attribution refuses was the obvious reading of the issue and is wrong:
    /// measured on the six shapes pinned by
    /// <c>SourceLinkMapConformanceTests.OnlyAnEntryThatCannotSelectContent_IsRefusedResolution</c>,
    /// attribution refuses five and only two of those fetch the wrong file.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     A wildcard in the path with an inert query alongside it
    ///     (<c>.../{sha}/*?foo=bar</c>) is unattributable because this host ignores the query,
    ///     yet the path still selects, so it fetches exactly the right file.
    ///   </item>
    ///   <item>
    ///     A branch-based GitHub map (<c>.../o/r/main/*</c>) is unattributable because the
    ///     revision/path boundary is not determinable, and fetches correctly regardless.
    ///   </item>
    ///   <item>
    ///     A self-hosted server is unattributable because it is not a recognized host — and
    ///     refusing it would stop this tool resolving source for every SourceLink deployment
    ///     outside the two hosts whose grammar is written down here.
    ///   </item>
    /// </list>
    /// <para>
    /// So an unrecognized host keeps today's behavior: unknown grammar means no claim in either
    /// direction, and the substitution resolves. At parse time, a known-host entry whose ordinary
    /// substitution demonstrably misses the selector lands in
    /// <see cref="SourceLinkResolver.RejectedKeys"/>. At resolution time, a concrete empty or
    /// blank substitution that changes the host's selector binding yields to a valid less-specific
    /// entry. Both remedies enforce the same rule: wrong content is worse than no content.
    /// </para>
    /// <para>
    /// The direction of the call is worth naming. <see cref="Determine"/> asks the resolver what
    /// a map resolves to, and here the resolver asks this class what a host reads. That is not a
    /// cycle: this method is a pure question about a host's URL grammar, holds no state, and
    /// reaches nothing on the resolver. The grammars live here because that is where the hosts
    /// are already written down, and duplicating them into the matcher is what issue #3599
    /// explicitly ruled out.
    /// </para>
    /// </remarks>
    internal static bool CanSelectContent(string url, int offset, int length, out string rejection)
    {
        rejection = "";

        if (offset < 0)
        {
            return true;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return true;
        }

        string host = CanonicalHost(uri);
        if (!IsRecognizedSourceHost(host))
        {
            // An unrecognized host's grammar is unknown, so nothing here can say whether the
            // substitution selects. Silence is the answer that preserves a working deployment.
            return true;
        }

        return TryCheckSubstitutionSelectsContent(url, host, offset, length, out rejection);
    }

    /// <summary>
    /// The DNS name a URL's authority denotes, in the ASCII form a request will actually use,
    /// with the root label's trailing dot removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three adversarial reviews found three spellings of one defect here: a host that names a
    /// recognized server, compares unequal to it, and is served by it anyway. First
    /// <see cref="Uri.Host"/> preserves the root label's dot, so
    /// <c>raw.githubusercontent.com.</c> was read as a host this code had never heard of. Then
    /// <see cref="Uri.Host"/> also preserves Unicode full stops, so <c>raw.githubusercontent.com</c>
    /// followed by U+3002, U+FF0E, or U+FF61 did the same. Measured: all four spellings return
    /// byte-identical content from GitHub, because the request is sent using the IDN form.
    /// </para>
    /// <para>
    /// <see cref="Uri.IdnHost"/> is therefore the right reading — it is the name that goes on the
    /// wire, with Unicode label separators folded to <c>'.'</c> and non-ASCII labels punycoded —
    /// and the trailing dot it still carries is trimmed after. It throws for an authority that has
    /// no IDN form, which is an authority no request can be sent to either, so the plain host is a
    /// safe fallback there rather than a reason to refuse.
    /// </para>
    /// <para>
    /// This is applied at the single point both readers derive a host from a URL, so that
    /// attribution and resolution cannot disagree about which host a URL names. That disagreement
    /// is the shape of defect issue #3391 fixed once already, and a host spelling is exactly the
    /// kind of thing two readers drift apart on.
    /// </para>
    /// </remarks>
    private static string CanonicalHost(Uri uri)
    {
        string host;
        try
        {
            host = uri.IdnHost;
        }
        catch (UriFormatException)
        {
            // What an authority with no IDN form raises -- a scalar IDNA forbids outright, such
            // as U+2066. It derives from FormatException, not ArgumentException. Such a host
            // cannot be requested at all, so reading it literally concedes nothing, and the
            // caller's inertness rule refuses it on its own terms rather than by crashing here.
            host = uri.Host;
        }
        catch (ArgumentException)
        {
            host = uri.Host;
        }

        return host.TrimEnd('.');
    }

    /// <summary>
    /// Whether this reader knows the URL grammar of a host, and can therefore say what a
    /// substitution placed in one of its components does.
    /// </summary>
    /// <remarks>
    /// This names the same set as the allow list in <see cref="TryReadOrigin"/>, and is
    /// deliberately the only other reader of it, so a host admitted there gains a content
    /// selector here in the same change rather than silently resolving unchecked. Both go through
    /// <see cref="CanonicalHost"/>, so neither can be evaded by a spelling the other accepts.
    /// </remarks>
    internal static bool IsRecognizedSourceHost(string host) =>
        string.Equals(host, GitHubRawHost, StringComparison.Ordinal)
        || string.Equals(host, AzureDevOpsHost, StringComparison.Ordinal)
        || host.EndsWith(VisualStudioHostSuffix, StringComparison.Ordinal);

    /// <summary>
    /// Refuses an origin whose own text carries a scalar that can act on whatever displays it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reported origin is artifact text: it is assembled from a URL that came out of a
    /// downloaded package's PDB, and <c>SourceLinkInspector</c> renders <c>RepositoryUrl</c>. So a
    /// hostile map can aim a terminal escape or a bidi override at the reader unless something
    /// stops it.
    /// </para>
    /// <para>
    /// Most components are inert by accident rather than by rule. <see cref="Uri.AbsolutePath"/>
    /// leaves a percent-escape escaped, so <c>%1b</c> stays the three characters <c>%</c>,
    /// <c>1</c>, <c>b</c>, and a raw <c>U+202E</c> in a path segment comes back as
    /// <c>%E2%80%AE</c>. <see cref="Uri.Host"/> does not do that: a raw <c>U+2066</c> in a
    /// <c>*.visualstudio.com</c> label survives into <see cref="Uri.Host"/> unchanged, is
    /// accepted by the suffix rule, and reaches the reported URL as a live bidi control. That
    /// was a real bypass, found in round 17, and it is why this is a rule applied wherever an
    /// origin is produced rather than a property each reader is trusted to preserve. It is
    /// invoked from <see cref="TryEmitOrigin"/>, which is the only place an origin becomes
    /// visible to a caller — round 17's re-review established that placing it in
    /// <see cref="Determine"/> instead left <see cref="BrowseUrl"/>, a rendered product path,
    /// reading origins the rule had never seen.
    /// </para>
    /// <para>
    /// This refuses rather than encodes, because that is the strategy the threat model settles
    /// on: an encoder has to be right about the whole set of dangerous forms and is silent when
    /// it is wrong, and no legitimate origin needs a control character in its name. Encoding
    /// belongs on the presentation path, where text that must be shown is made inert; here the
    /// text does not have to be shown at all.
    /// </para>
    /// <para>
    /// Refused by Unicode general category rather than by a list of characters. A list written
    /// against terminal escapes misses <c>Cf</c>, which is where every Trojan Source code point
    /// (CVE-2021-42574) lives — including <c>U+2066</c>, the one that got through.
    /// </para>
    /// <para>
    /// The rejection names the component and the code point, never the value. Reproducing the
    /// text in a message that may itself be displayed would reintroduce the problem on the
    /// diagnostic channel, and for a homoglyph the code point says strictly more than the
    /// character does.
    /// </para>
    /// </remarks>
    private static bool TryCheckOriginTextIsInert(SourceLinkOrigin origin, out string rejection)
    {
        // Every component, not only the rendered URL: a caller may report any of them, and the
        // identity used as a cache key is built from them too.
        (string Name, string Value)[] components =
        [
            ("host", origin.Host),
            ("organization", origin.Organization),
            ("repository", origin.Repository),
            ("revision", origin.Revision),
            ("repository URL", origin.RepositoryUrl),
        ];

        foreach ((string name, string value) in components)
        {
            if (!TryCheckTextIsInert(value, name, out rejection))
            {
                return false;
            }
        }

        rejection = "";
        return true;
    }

    /// <summary>
    /// Whether one component's text carries nothing that can act on whatever displays it.
    /// </summary>
    /// <remarks>
    /// Split out so that a component can be judged before any canonicalization <em>this code</em>
    /// applies rewrites it. <see cref="CanonicalHost"/> applies IDNA mapping, which deletes a soft
    /// hyphen and a zero-width space outright — so a host checked only after canonicalization
    /// would be sanitized rather than refused, and
    /// <c>docs/design/untrusted-data-threat-model.md</c> settles on reject-don't-sanitize.
    /// <see cref="Uri"/>'s own authority normalization still runs first and is deliberately not
    /// pinned; see <c>ALiveFormatCharacterInAHostLabel_IsNotAttributable</c>.
    /// </remarks>
    private static bool TryCheckTextIsInert(string value, string name, out string rejection)
    {
        if (value is not null)
        {
            int index = 0;

            foreach (Rune scalar in value.EnumerateRunes())
            {
                if (IsNonGraphic(scalar))
                {
                    rejection =
                        $"the {name} carries U+{scalar.Value:X4} " +
                        $"({Rune.GetUnicodeCategory(scalar)}) at index {index}, which can act on " +
                        "whatever displays it";
                    return false;
                }

                index += scalar.Utf16SequenceLength;
            }
        }

        rejection = "";
        return true;
    }

    /// <summary>
    /// Reports whether <paramref name="scalar"/> falls in a category that can act on a sink.
    /// </summary>
    /// <remarks>
    /// <c>Cc</c> reaches terminal control sequences, <c>Cf</c> reaches the bidi algorithm and
    /// carries the Trojan Source set, <c>Cs</c> breaks UTF-8 conversion, and <c>Zl</c>/<c>Zp</c>
    /// reach line-oriented consumers. This is what <c>InertText.TextPolicy.Field</c> will own
    /// once it lands (#3563); it is spelled out here because this assembly cannot depend on an
    /// unmerged library, and it is defined by category rather than by a list so that the two
    /// cannot drift in what they mean.
    /// </remarks>
    private static bool IsNonGraphic(Rune scalar)
        => Rune.GetUnicodeCategory(scalar) switch
        {
            UnicodeCategory.Control => true,
            UnicodeCategory.Format => true,
            UnicodeCategory.Surrogate => true,
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => true,
            _ => false,
        };

    /// <summary>
    /// Returns the index just past the URL's authority in its raw text, so that offsets into the
    /// unparsed string can be classified without depending on canonicalization.
    /// </summary>
    private static int AuthorityEnd(string url)
    {
        int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return 0;
        }

        int authorityStart = schemeEnd + 3;
        int authorityEnd = url.IndexOfAny(['/', '?', '#'], authorityStart);
        return authorityEnd < 0 ? url.Length : authorityEnd;
    }

    /// <summary>
    /// Finds the span of the parameter value Azure DevOps actually selects content with, or
    /// reports that both selectors are valued and the host refuses the request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first occurrence of each name binds; an <em>empty</em> value is not a selection, and
    /// the host falls through to <c>scopePath</c> rather than serving the root listing. Reading
    /// only the first name that appears — which is what this did — refused
    /// <c>path&amp;scopePath=/*</c>, a map whose wildcard the host genuinely reads. Over-refusal
    /// is a real defect in this predicate, so that mattered; found in review.
    /// </para>
    /// <para>
    /// Measured against <c>dev.azure.com/dnceng-public/public</c>, repository
    /// <c>dotnet-public-wiki</c> at commit <c>af56d96fdbd7c26e9fc94336b6f50dcc6ceff484</c>, where
    /// the requested file is 985 bytes, the repository root listing is 425, and a missing file is
    /// 404. Nine shapes, and the model above accounts for all nine:
    /// </para>
    /// <code>
    /// path&amp;scopePath=/README.md                 200  985   empty path falls through
    /// path=&amp;scopePath=/README.md                200  985   an explicit '=' is the same
    /// path&amp;scopePath=/nope.txt                  404        and it is really selecting
    /// path&amp;scopePath=/README.md&amp;path=/nope.txt  200  985   first occurrence, still empty
    /// scopePath&amp;path=/README.md                 200  985   symmetric: empty scopePath yields
    /// path&amp;path=/README.md                      200  425   no scopePath, so root listing
    /// path&amp;scopePath&amp;path=/README.md             200  425   both empty, so root listing
    /// scopePath=/README.md                     200  985
    /// path=/README.md&amp;scopePath=/nope.txt       400        both selecting is an error
    /// </code>
    /// <para>
    /// The last row is why a valued pair of both is left to resolve: it fetches nothing at all,
    /// visibly, rather than serving one wrong file under every document's name, which is the
    /// defect this predicate exists to stop.
    /// </para>
    /// </remarks>
    private static bool TrySpanOfContentSelector(
        string url,
        int queryStart,
        out int valueStart,
        out int valueEnd,
        out bool requestAlwaysFails)
    {
        bool pathSelects =
            TrySpanOfQueryValue(url, queryStart, "path", out int pathStart, out int pathEnd)
            && ValueSelects(url, pathStart, pathEnd);
        bool scopePathSelects =
            TrySpanOfQueryValue(
                url,
                queryStart,
                "scopePath",
                out int scopePathStart,
                out int scopePathEnd)
            && ValueSelects(url, scopePathStart, scopePathEnd);

        requestAlwaysFails = pathSelects && scopePathSelects;

        if (pathSelects)
        {
            valueStart = pathStart;
            valueEnd = pathEnd;
            return true;
        }

        if (scopePathSelects)
        {
            valueStart = scopePathStart;
            valueEnd = scopePathEnd;
            return true;
        }

        valueStart = 0;
        valueEnd = 0;
        return false;
    }

    /// <summary>
    /// Whether a selector value names anything, as the host judges it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Blank is not a selection, and blank is decided after decoding rather than on the raw text:
    /// measured, <c>path=%20</c>, <c>path=+</c>, <c>path=%09</c>, <c>path=%0a</c>, <c>path=%0d</c>
    /// and <c>path=%C2%A0</c> all fall through to <c>scopePath</c> exactly as an absent value
    /// does, and <c>path=%20</c> alone answers with the repository root listing. Comparing raw
    /// lengths made this reader treat those as selections and refuse maps the host resolves,
    /// which is over-refusal — a real defect in this predicate. Found in review.
    /// </para>
    /// <para>
    /// The host does not <em>trim</em>, so this is emptiness and not normalization:
    /// <c>path=%20/README.md</c> answers 404 rather than serving the file.
    /// </para>
    /// </remarks>
    private static bool ValueSelects(string url, int valueStart, int valueEnd)
    {
        if (valueEnd <= valueStart)
        {
            return false;
        }

        string raw = url.Substring(valueStart, valueEnd - valueStart);

        // '+' is a space in a form-encoded value, which is how these hosts read a query.
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(raw.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            // Undecodable text is not blank, and refusing to call it blank keeps the caller from
            // falling through to a selector the host will not reach.
            return true;
        }

        return !string.IsNullOrWhiteSpace(decoded);
    }

    /// <summary>
    /// Finds the span of a named query parameter's value in the URL's raw text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The span is measured in the raw string rather than a parsed collection because it is
    /// compared against a substitution offset into that same string.
    /// </para>
    /// <para>
    /// The first occurrence wins, which is what these hosts serve, so the span is the one the
    /// host reads even when the caller has not separately refused a repeat. The attribution
    /// reader does refuse one; the content-selector reader does not, and relies on this.
    /// </para>
    /// <para>
    /// Names are compared decoded, because the host decodes them: measured against a live Azure
    /// DevOps endpoint, <c>%70ath=/README.md</c> returns that file, and
    /// <c>%70ath=/README.md&amp;path=/nope.txt</c> returns it too rather than 404. Comparing the
    /// raw text would leave the first pair invisible here while the host serves it — an
    /// adversarial review found exactly that, turning <c>%70ath=/fixed.cs&amp;path=/*</c> into a
    /// map whose wildcard this reader sees, the host never reads, and every document resolves
    /// through to one file.
    /// </para>
    /// </remarks>
    /// <param name="queryStart">The index of the <c>?</c> that begins the query.</param>
    private static bool TrySpanOfQueryValue(
        string url, int queryStart, string name, out int valueStart, out int valueEnd)
    {
        valueStart = valueEnd = -1;

        // A fragment ends the query and is never sent, so it bounds the search. The first '#'
        // after the query wins: a later one is inside the fragment, not a second delimiter.
        // IndexOf(char, int) is ordinal by definition; the StringComparison overload does not
        // exist on the net10.0 fallback target this also has to build against.
        int fragmentStart = url.IndexOf('#', queryStart);
        int limit = fragmentStart < 0 ? url.Length : fragmentStart;

        int i = queryStart + 1;
        while (i < limit)
        {
            int amp = url.IndexOf('&', i);
            int pairEnd = amp < 0 || amp > limit ? limit : amp;
            int eq = url.IndexOf('=', i);
            bool hasValue = eq >= 0 && eq < pairEnd;

            if (DecodedNameMatches(url.AsSpan(i, (hasValue ? eq : pairEnd) - i), name))
            {
                // A pair written without '=' still binds the name: measured, Azure DevOps reads
                // "&path&path=/*" as a path of "" and serves the repository root listing for
                // every document, ignoring the second pair entirely. Skipping the valueless pair
                // as if it were not there left the wildcard in the occurrence the host never
                // reads -- issue #3599 again, found in review. Its value is the empty span, which
                // no substitution can land inside, so the entry is refused.
                valueStart = hasValue ? eq + 1 : pairEnd;
                valueEnd = pairEnd;
                return true;
            }

            if (amp < 0 || amp >= limit)
            {
                break;
            }

            i = amp + 1;
        }

        return false;
    }

    /// <summary>
    /// Whether a raw query parameter name denotes <paramref name="name"/> once the percent-escapes
    /// the host decodes have been applied and the array brackets its model binder deletes have
    /// been removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decoding happens during the comparison rather than into a buffer so that a name cannot be
    /// read one way here and another way by the caller. An escape that is not two hex digits is
    /// left as the literal <c>'%'</c> it is, which is how these hosts read it.
    /// </para>
    /// <para>
    /// An empty <c>[]</c> group is deleted wherever it appears — not only at the ends — in a
    /// single left-to-right pass. The host does not rescan what a deletion brings together, so
    /// <c>p[[]]ath</c> collapses to <c>p[]ath</c> and stays unbound while <c>p[]ath</c> binds.
    /// Both directions are measured; see
    /// <c>AnArraySuffixedContentSelector_BindsTheSameParameter</c>.
    /// </para>
    /// <para>
    /// Case folds over ASCII only. <see cref="char.ToUpperInvariant(char)"/> maps U+017F LATIN
    /// SMALL LETTER LONG S to <c>'S'</c>, which made this reader see <c>ſcopePath</c> as
    /// <c>scopePath</c> while the host ignores it entirely — so a map could put the wildcard in a
    /// parameter only this reader believes exists. Found in review and measured: <c>ſcopePath</c>
    /// alone answers with the repository root listing, and <c>ſcopePath=/A.cs</c> beside
    /// <c>scopePath=/README.md</c> serves README for every document.
    /// </para>
    /// </remarks>
    private static bool DecodedNameMatches(ReadOnlySpan<char> raw, string name)
    {
        int matched = 0;
        int i = 0;

        while (i < raw.Length)
        {
            if (!TryDecodeAt(raw, i, out char c, out int next))
            {
                return false;
            }

            if (c == '['
                && TryDecodeAt(raw, next, out char close, out int afterClose)
                && close == ']')
            {
                i = afterClose;
                continue;
            }

            if (matched == name.Length || AsciiLower(c) != AsciiLower(name[matched]))
            {
                return false;
            }

            matched++;
            i = next;
        }

        return matched == name.Length;
    }

    /// <summary>
    /// Lowercases an ASCII letter and leaves every other scalar alone, so that no non-ASCII
    /// scalar can be folded onto one of these parameter names.
    /// </summary>
    private static char AsciiLower(char c) =>
        (uint)(c - 'A') <= 'Z' - 'A' ? (char)(c | 0x20) : c;

    /// <summary>
    /// Reads the character at <paramref name="i"/> as the host decodes it, reporting where the
    /// text it was spelled with ends.
    /// </summary>
    private static bool TryDecodeAt(ReadOnlySpan<char> raw, int i, out char c, out int next)
    {
        if (i >= raw.Length)
        {
            c = default;
            next = i;
            return false;
        }

        if (raw[i] == '%'
            && i + 2 < raw.Length
            && TryReadHexDigit(raw[i + 1], out int high)
            && TryReadHexDigit(raw[i + 2], out int low))
        {
            c = (char)((high << 4) | low);
            next = i + 3;
            return true;
        }

        c = raw[i];
        next = i + 1;
        return true;
    }

    private static bool TryReadHexDigit(char c, out int value)
    {
        value = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };

        return value >= 0;
    }
}
