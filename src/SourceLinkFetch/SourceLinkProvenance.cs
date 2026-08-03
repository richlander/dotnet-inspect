using System.Globalization;
using System.Text;
using System.Reflection.Metadata;

namespace SourceLinkFetch;

/// <summary>
/// The origin that source content is actually fetched from, read off a resolved SourceLink URL.
/// </summary>
/// <remarks>
/// Construction is <c>internal</c>, and the components are get-only so that <c>with</c> cannot
/// replace one. An origin is artifact text — it is assembled from a URL that came out of a
/// downloaded package's PDB — and the rule that none of its components may carry a scalar that
/// can act on a sink is enforced at the one place origins are made,
/// <c>SourceLinkProvenance.TryEmitOrigin</c>. That rule is only worth anything if the type
/// cannot be built around it, which is what a public positional constructor would allow.
/// <c>default</c> remains constructible, as it does for every struct, and carries no text.
/// </remarks>
public readonly record struct SourceLinkOrigin
{
    internal SourceLinkOrigin(
        string host,
        string organization,
        string repository,
        string revision,
        string repositoryUrl)
    {
        Host = host;
        Organization = organization;
        Repository = repository;
        Revision = revision;
        RepositoryUrl = repositoryUrl;
    }

    /// <summary>The canonical, lower-cased host of the resolved URL.</summary>
    public string Host { get; }

    /// <summary>
    /// The owning account: the GitHub owner, or the Azure DevOps organization and project.
    /// </summary>
    public string Organization { get; }

    /// <summary>The repository name.</summary>
    public string Repository { get; }

    /// <summary>
    /// The commit, branch, or tag the content is served at. Two entries naming one repository at
    /// two revisions are two origins, because a revision is reachable in a repository without
    /// being part of it — the head of an unmerged pull request is served by the same host as the
    /// default branch.
    /// </summary>
    public string Revision { get; }

    /// <summary>A browsable URL for the repository.</summary>
    public string RepositoryUrl { get; }

    /// <summary>
    /// A stable identity for this exact origin, suitable as a cache key. It names the revision
    /// <em>and</em> the repository it was served from, because a commit hash alone is shared by
    /// every fork that contains that commit.
    /// </summary>
    /// <remarks>
    /// Length-prefixed rather than delimiter-joined. Azure DevOps repository names and Git ref
    /// names may both contain <c>/</c> and <c>@</c> (<c>git check-ref-format</c> accepts
    /// <c>branch@tip</c>), so a joined form is ambiguous: repository <c>repo@branch</c> at
    /// revision <c>tip</c> and repository <c>repo</c> at revision <c>branch@tip</c> would produce
    /// one string. This key selects a persistent source index, so a collision serves one
    /// repository's source for another's assembly.
    /// </remarks>
    public string Identity
    {
        get
        {
            var builder = new StringBuilder();
            foreach (string part in (string[])[Host, Organization, Repository, Revision])
                builder.Append(part.Length).Append(':').Append(part).Append('|');
            return builder.ToString();
        }
    }
}

/// <summary>
/// The outcome of establishing provenance. <see cref="Origin"/> is null when provenance could not
/// be established, and <see cref="Reason"/> always says why in that case, so that "no repository"
/// is reported as a decision rather than as absence.
/// </summary>
/// <remarks>
/// "Reported" here means reported to the caller. Every product call site currently takes
/// <c>Origin?.RepositoryUrl</c> and drops the reason, so a user sees no repository and no
/// explanation. Carrying the reason into output is tracked by
/// <see href="https://github.com/richlander/dotnet-inspect/issues/3590">#3590</see>. That the
/// reason is always populated is gated by
/// <c>SourceLinkProvenanceTests.EveryUnestablishedResult_CarriesAReason</c>.
/// </remarks>
public readonly record struct SourceLinkProvenanceResult(SourceLinkOrigin? Origin, string Reason)
{
    /// <summary>Whether an origin was established.</summary>
    public bool IsEstablished => Origin is not null;
}

/// <summary>
/// Establishes the origin that source content is fetched from.
/// </summary>
/// <remarks>
/// <para>
/// The invariant this implements is stated in <c>docs/design/untrusted-data-threat-model.md</c>:
/// reported provenance must describe the origin that source content is actually fetched from, for
/// every document the assembly resolves; when that cannot be established for all of them, report
/// no repository.
/// </para>
/// <para>
/// It is established on the <em>final resolved URL</em> — after wildcard substitution, after path
/// escaping, and after <see cref="Uri"/> canonicalization — and never on the mapping text. Every
/// weaker reading has a documented, reproduced bypass: reading the mapping text misses dot-segment
/// removal; reading the mapping prefix misses that the wildcard suffix comes from the equally
/// attacker-controlled PDB document path; and comparing only owner and repository misses that one
/// repository serves any revision reachable in it.
/// </para>
/// <para>
/// The whole SourceLink map comes from a PDB in a downloaded package. Every part of it is
/// untrusted: the keys, the URL templates, and the document names the keys are matched against.
/// </para>
/// </remarks>
public static class SourceLinkProvenance
{
    private const string GitHubRawHost = "raw.githubusercontent.com";
    private const string AzureDevOpsHost = "dev.azure.com";
    private const string VisualStudioHostSuffix = ".visualstudio.com";

    /// <summary>
    /// Determines the single origin every resolvable document is fetched from, or reports why no
    /// such origin exists.
    /// </summary>
    /// <param name="resolver">The owner of the SourceLink mapping rule.</param>
    /// <param name="documentPaths">
    /// The document names recorded in the PDB. Provenance is a claim about what this assembly's
    /// source resolves to, so it is established over the documents the assembly actually declares
    /// and not over the map in the abstract: an entry no document matches is never fetched.
    /// </param>
    public static SourceLinkProvenanceResult Determine(
        SourceLinkResolver resolver,
        IEnumerable<string> documentPaths)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(documentPaths);

        if (resolver.ParseError is not null)
        {
            return new SourceLinkProvenanceResult(null, $"the SourceLink map did not parse: {resolver.ParseError}");
        }

        SourceLinkOrigin? agreed = null;
        int resolvedCount = 0;

        foreach (string documentPath in documentPaths)
        {
            if (documentPath is null)
            {
                continue;
            }

            if (!resolver.TryResolve(documentPath, out SourceLinkResolution resolution))
            {
                // The document is not fetched, so it makes no claim about where source comes from.
                continue;
            }

            string url = resolution.Url;
            resolvedCount++;

            if (!TryReadOrigin(url, out SourceLinkOrigin origin, out string rejection))
            {
                return new SourceLinkProvenanceResult(
                    null,
                    $"a resolved source URL has no attributable origin: {rejection}");
            }

            if (!TryCheckSubstitutionSelectsContent(
                    url, origin.Host, resolution.SubstitutionOffset, resolution.SubstitutionLength, out rejection))
            {
                return new SourceLinkProvenanceResult(
                    null,
                    $"a resolved source URL has no attributable origin: {rejection}");
            }

            if (agreed is null)
            {
                agreed = origin;
            }
            else if (agreed.Value != origin)
            {
                return new SourceLinkProvenanceResult(
                    null,
                    "documents resolve to more than one origin, so no single repository describes this assembly's source");
            }
        }

        if (resolvedCount == 0)
        {
            return new SourceLinkProvenanceResult(null, "no document resolves to a source URL");
        }

        return new SourceLinkProvenanceResult(agreed, "");
    }

    /// <summary>
    /// Determines provenance directly from a PDB, over the documents that PDB declares.
    /// </summary>
    /// <returns>
    /// A result whose <see cref="SourceLinkProvenanceResult.Origin"/> is null, with a reason, when
    /// the PDB carries no SourceLink map or no attributable single origin.
    /// </returns>
    public static SourceLinkProvenanceResult Determine(MetadataReader pdbReader)
    {
        ArgumentNullException.ThrowIfNull(pdbReader);

        SourceLinkResolver? resolver = SourceLinkResolver.Create(pdbReader);
        return resolver is null
            ? new SourceLinkProvenanceResult(null, "the PDB carries no SourceLink map")
            : Determine(resolver, EnumerateDocumentNames(pdbReader));
    }

    private static IEnumerable<string> EnumerateDocumentNames(MetadataReader pdbReader)
    {
        foreach (DocumentHandle handle in pdbReader.Documents)
        {
            yield return pdbReader.GetString(pdbReader.GetDocument(handle).Name);
        }
    }

    /// <summary>
    /// Converts a resolved GitHub raw-content URL into a browsable URL for the same content.
    /// </summary>
    /// <returns>
    /// Null when the URL has no attributable GitHub origin. A URL that traverses out of the
    /// repository it appears to name must not be dressed up as a github.com link, and callers
    /// fall back to showing the resolved URL itself. This is the origin reader's rule in the
    /// form a user is most likely to trust and click, so it is the origin reader that decides
    /// it — the link is built from the attributed origin, never from the URL's own text. Gated
    /// by
    /// <c>SourceLinkProvenanceTests.ABrowseLink_IsOnlyOfferedForAnAttributableGitHubOrigin</c>.
    /// </returns>
    public static string? BrowseUrl(string? resolvedUrl)
    {
        if (resolvedUrl is null || !TryReadOrigin(resolvedUrl, out SourceLinkOrigin origin, out _))
        {
            return null;
        }

        if (!string.Equals(origin.Host, GitHubRawHost, StringComparison.Ordinal))
        {
            return null;
        }

        // TryReadOrigin already established that this parses and has at least four segments.
        string[] segments = new Uri(resolvedUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string path = string.Join('/', segments[3..]);
        return $"https://github.com/{origin.Organization}/{origin.Repository}/blob/{origin.Revision}/{path}";
    }

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
            && (TrySpanOfQueryValue(url, queryStart, "path", out int valueStart, out int valueEnd)
                || TrySpanOfQueryValue(url, queryStart, "scopePath", out valueStart, out valueEnd))
            && offset >= valueStart
            && end <= valueEnd)
        {
            return true;
        }

        rejection =
            $"'{host}' selects content by the 'path' or 'scopePath' parameter, and the " +
            "document text is substituted outside it, so every document resolves to the same file";
        return false;
    }

    /// <summary>
    /// Whether an entry may resolve at all: refuses one that would fetch a single file for every
    /// document it matches, on a host whose content selector this reader knows.
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
    /// direction, and the entry resolves. Only a host this reader can speak for, whose selector
    /// the substitution demonstrably misses, is refused — and it is refused as a non-conformant
    /// entry, so it lands in <see cref="SourceLinkResolver.RejectedKeys"/> and stops shadowing a
    /// valid less-specific entry rather than merely failing to resolve. That is the same remedy
    /// this matcher already applies to a wildcard key paired with a constant URL, and for the
    /// identical stated reason: wrong content is worse than no content.
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
    /// downloaded package's PDB, and <c>AssemblyInspector</c> renders <c>RepositoryUrl</c>. So a
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
    /// Split out so that a component can be judged <em>as written</em>, before any
    /// canonicalization rewrites it. <see cref="CanonicalHost"/> applies IDNA mapping, which
    /// deletes a soft hyphen and a zero-width space outright — so a host checked only after
    /// canonicalization would be sanitized rather than refused, and
    /// <c>docs/design/untrusted-data-threat-model.md</c> settles on reject-don't-sanitize.
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
    /// the host decodes have been applied.
    /// </summary>
    /// <remarks>
    /// Decoding happens during the comparison rather than into a buffer so that a name cannot be
    /// read one way here and another way by the caller. An escape that is not two hex digits is
    /// left as the literal <c>'%'</c> it is, which is how these hosts read it.
    /// </remarks>
    private static bool DecodedNameMatches(ReadOnlySpan<char> raw, string name)
    {
        int matched = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '%'
                && i + 2 < raw.Length
                && TryReadHexDigit(raw[i + 1], out int high)
                && TryReadHexDigit(raw[i + 2], out int low))
            {
                c = (char)((high << 4) | low);
                i += 2;
            }

            // Case-insensitively for the same reason the parameter reader is: whether the host
            // folds case is not stated by the URL, so a name that differs only in case has to be
            // seen here and refused by the rule that owns it, not missed.
            if (matched == name.Length
                || char.ToUpperInvariant(c) != char.ToUpperInvariant(name[matched]))
            {
                return false;
            }

            matched++;
        }

        return matched == name.Length;
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

    /// <summary>
    /// Reads the origin off one final resolved URL, or says why the URL is not attributable.
    /// </summary>
    internal static bool TryReadOrigin(string url, out SourceLinkOrigin origin, out string rejection)
    {
        origin = default;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            rejection = "it is not an absolute URI";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            rejection = $"its scheme is '{uri.Scheme}', not https";
            return false;
        }

        if (!uri.IsDefaultPort)
        {
            // The origin is (scheme, host, port), but the reader identifies a host by name alone,
            // so 'raw.githubusercontent.com:444' would be attributed to GitHub and would share the
            // persistent cache identity of port 443 while a different service answers. The two
            // hosts this reader knows are named by the SourceLink generators without a port, so
            // nothing that is generated is refused here. An on-prem host that does carry a port
            // has to arrive with its own URL grammar, exactly as the host allow list requires.
            rejection = $"it names port {uri.Port}, which is a different origin from '{uri.Host}'";
            return false;
        }

        if (uri.UserInfo.Length != 0)
        {
            // Two separate reasons, and only the second is load-bearing.
            //
            // "https://raw.githubusercontent.com@evil.example/..." parses with Host 'evil.example'
            // and UserInfo 'raw.githubusercontent.com'. The host allow list below already rejects
            // that, because Uri takes the authority after the last '@' -- user info can never
            // redirect the fetch past the host check.
            //
            // What user info does change is what the host serves. A credential makes the response
            // depend on the identity presented rather than on the public path the URL names, so
            // "https://token@raw.githubusercontent.com/dotnet/runtime/{sha}/a.cs" may return bytes
            // that github.com/dotnet/runtime does not show for that revision. Reported provenance
            // has to describe where the content actually comes from, and an authenticated response
            // is not established by the public identity in the URL. Hence: not attributable.
            rejection = "it carries user information before the host";
            return false;
        }

        if (ContainsEncodedSeparatorOrDotSegment(url, out string encoded))
        {
            // Uri preserves these verbatim through canonicalization, so a canonicalize-then-check
            // step passes while a server that percent-decodes before resolving dot segments still
            // traverses out of the path this URL appears to name.
            rejection = $"it contains the encoded sequence '{encoded}', which canonicalization does not resolve";
            return false;
        }

        // The host is judged as written, before CanonicalHost applies IDNA mapping. That mapping
        // deletes a soft hyphen and a zero-width space rather than preserving them, so a check
        // made only on the canonical form would silently sanitize a hostile host into a clean one
        // and attribute it -- the opposite of the reject-don't-sanitize rule the threat model
        // settles on, and a regression of the round-17 refusal this restates.
        if (!TryCheckTextIsInert(uri.Host, "host", out rejection))
        {
            return false;
        }

        // AbsolutePath has had dot segments removed, so traversal has already been applied and the
        // segments below name where content is really served from.
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string host = CanonicalHost(uri);

        if (string.Equals(host, GitHubRawHost, StringComparison.Ordinal))
        {
            // /{owner}/{repo}/{ref}/{path...}
            if (segments.Length < 4)
            {
                rejection = $"'{host}' path '{uri.AbsolutePath}' names no owner, repository, and revision";
                return false;
            }

            // The ref and the path are separated by a '/' that is also legal inside a ref: this
            // host serves branch names, and a branch may contain '/'. So
            // ".../owner/repo/feature/auth/File.cs" reads equally well as ref "feature" with path
            // "auth/File.cs" or as ref "feature/auth" with path "File.cs", and nothing in the URL
            // says which. Taking segment 2 makes two different branches report one revision, which
            // is a false provenance claim and a colliding cache identity.
            //
            // A commit hash cannot contain '/', so requiring one makes the boundary determinable.
            // Anything else is not attributable rather than guessed -- "when that cannot be
            // established, report no repository". This costs abbreviated hashes and branch-based
            // maps; the SDK emits a full commit hash, and a moving ref would not identify a
            // revision anyway.
            if (!IsCommitHash(segments[2]))
            {
                rejection =
                    $"'{host}' revision '{segments[2]}' is not a commit hash, so the boundary " +
                    "between the revision and the file path is not determinable";
                return false;
            }

            // This host serves the path and ignores the query completely. Measured against
            // raw.githubusercontent.com: no query, '?ignored=A.cs', '?ignored=B.cs' and
            // '?path=/other.cs' all return the same 33400 bytes with the same hash. So a query
            // here cannot select content -- it can only carry a substitution the server will
            // never see, which is how '{"*": ".../fixed.cs?ignored=*"}' resolves every document
            // to one file: each document produces a textually distinct URL, so the resolver's
            // two-probe check is satisfied, while all of them fetch fixed.cs and agree on an
            // origin that is genuinely where fixed.cs is served from.
            //
            // This is the per-host content selector the two-probe check defers to this layer
            // (issue #3599). It is decidable here and not there: the same shape aimed at Azure
            // Repos is the documented generated form, where 'path=' really does select the file,
            // so a host-agnostic matcher cannot refuse it. Nothing generated carries a query
            // either -- Microsoft.SourceLink.GitHub builds '{contentUrl}/{owner}/{repo}/{sha}/*'
            // by pure path concatenation (UriUtilities.Combine) and never appends one.
            if (uri.Query.Length > 0)
            {
                rejection =
                    $"'{host}' ignores the query, so '{uri.Query}' cannot select content and a " +
                    "substitution placed there would resolve every document to one file";
                return false;
            }

            return TryEmitOrigin(
                new SourceLinkOrigin(
                    host,
                    segments[0],
                    segments[1],
                    segments[2],
                    $"https://github.com/{segments[0]}/{segments[1]}"),
                out origin,
                out rejection);
        }

        if (string.Equals(host, AzureDevOpsHost, StringComparison.Ordinal) ||
            host.EndsWith(VisualStudioHostSuffix, StringComparison.Ordinal))
        {
            return TryReadAzureDevOpsOrigin(uri, host, segments, out origin, out rejection);
        }

        // The allow list is the set of hosts whose URL grammar this reader knows, not a
        // trust boundary. SourceLink's generators also emit '*.vsts.me' and Azure DevOps Server
        // URLs on arbitrary hosts and ports; both are deliberately outside it, and both report no
        // repository rather than a guessed one.
        //
        // Admitting a host is a security decision needing its own evidence: who operates the
        // domain, and — for an on-prem server — where the virtual directory ends, which the URL
        // does not state. "No repository" is the invariant's defined answer when an origin cannot
        // be established, so refusing here is conservative rather than wrong. Widening this is a
        // separate change.
        rejection = $"host '{host}' is not a recognized source host";
        return false;
    }

    /// <summary>
    /// Reads an Azure DevOps Git items URL:
    /// <c>https://dev.azure.com/{org}/{project}/_apis/git/repositories/{repo}/items?version={rev}</c>,
    /// or the older <c>https://{account}.visualstudio.com/{project}/_apis/...</c> spelling, where
    /// the account is the host label and an optional <c>DefaultCollection</c> may precede the
    /// project.
    /// </summary>
    /// <remarks>
    /// The segments before <c>_apis</c> are read by position rather than joined, because their
    /// count is what says which host route the URL names. Joining an arbitrary count reported an
    /// organization that was assembled rather than read: a project-less
    /// <c>dev.azure.com/{org}/_apis/...</c> was attributed to <c>{org}</c>, and
    /// <c>dev.azure.com/a/b/c/_apis/...</c> to the organization <c>a/b/c</c> and the repository
    /// page <c>https://dev.azure.com/a/b/c/_git/{repo}</c>, which is not a page.
    /// <para>
    /// Reading positionally refuses no shape a supported generator produces.
    /// <c>AzureDevOpsUrlParser.TryParseHostedHttp</c> builds the project path as
    /// <c>{account}/{project}</c> off <c>dev.azure.com</c> and as <c>{project}</c> off a
    /// <c>*.visualstudio.com</c> host — it drops the team and trims <c>DefaultCollection</c> — and
    /// <c>GetSourceLinkUrl</c> appends <c>_apis/git/repositories/{repo}/items</c> to exactly that.
    /// </para>
    /// <para>
    /// It also refuses nothing the host serves. Measured against
    /// <c>dev.azure.com/dnceng-public/public</c>: the generator shape returns 200, while a
    /// project-less path, a wrong project, and a wrong organization each redirect to a sign-in
    /// page on another host, and an extra segment returns 404. The
    /// <c>*.visualstudio.com</c> spelling returns byte-identical content with and without
    /// <c>DefaultCollection</c>, which is why the collection is dropped from the identity rather
    /// than made part of it. The route shapes are gated by
    /// <c>SourceLinkProvenanceTests.AnAzureUrlWhoseSegmentsBeforeApisAreNotTheHostsRoute_IsNotAttributable</c>,
    /// which asserts establishment only; that dropping <c>DefaultCollection</c> yields the
    /// <em>same</em> origin as the project alone — the identity claim, which an establishment
    /// gate cannot see — is gated by
    /// <c>SourceLinkProvenanceTests.TheLegacyCollectionSpelling_NamesTheSameOriginAsTheProjectAlone</c>.
    /// </para>
    /// </remarks>
    private static bool TryReadAzureDevOpsOrigin(
        Uri uri,
        string host,
        string[] segments,
        out SourceLinkOrigin origin,
        out string rejection)
    {
        origin = default;

        // The account lives in the host label on the legacy spelling and in the path on
        // dev.azure.com, so the route names one fewer path segment there.
        bool accountInHost = host.EndsWith(VisualStudioHostSuffix, StringComparison.Ordinal);
        int route = accountInHost ? 1 : 2;

        if (accountInHost &&
            segments.Length > 0 &&
            string.Equals(segments[0], "DefaultCollection", StringComparison.OrdinalIgnoreCase))
        {
            segments = segments[1..];
        }

        int apis = route;
        if (segments.Length != apis + 5 ||
            !string.Equals(segments[apis], "_apis", StringComparison.Ordinal) ||
            !string.Equals(segments[apis + 1], "git", StringComparison.Ordinal) ||
            !string.Equals(segments[apis + 2], "repositories", StringComparison.Ordinal) ||
            !string.Equals(segments[apis + 4], "items", StringComparison.Ordinal))
        {
            rejection = $"'{host}' path '{uri.AbsolutePath}' is not a Git items path";
            return false;
        }

        string organization = string.Join('/', segments[..apis]);
        if (Array.Exists(segments[..apis], static segment => segment.Length == 0))
        {
            rejection = $"'{host}' path '{uri.AbsolutePath}' names no organization";
            return false;
        }

        // On dev.azure.com, a leading 'e' is the enterprise discovery prefix rather than an
        // organization, so reading the route positionally would report the organization 'e' for a
        // URL that names none. Measured: '/e/dnceng-public/_apis/git/repositories/{guid}/items'
        // returns 404 where the same request without the prefix returns 200, so the shape serves
        // nothing to describe. The reference generator refuses it for the same reason
        // (AzureDevOpsUrlParser rejects parts[0] == "e"), so no generator shape is lost.
        if (!accountInHost && string.Equals(segments[0], "e", StringComparison.OrdinalIgnoreCase))
        {
            rejection =
                $"'{host}' path '{uri.AbsolutePath}' begins with the enterprise discovery prefix " +
                "'e', which names no organization";
            return false;
        }

        string repository = segments[apis + 3];

        // Every parameter must be one this reader has reasoned about. Azure's Items API takes
        // several that change which content is returned, and an unrecognized one cannot be assumed
        // inert: the reported origin would then describe less than the URL selects. This is an
        // allow list rather than a deny list because the API grows and we do not.
        if (!TryCheckQueryParameters(uri.Query, host, out rejection))
        {
            return false;
        }

        // Azure's Items API accepts the revision two ways: the flat 'version' parameter and the
        // 'versionDescriptor.version' member, and the descriptor takes precedence when both are
        // present. Reading only 'version' therefore reports the losing selector -- a URL carrying
        // both reports one revision while fetching the other. Confirmed against the live API:
        // 'version=A&versionDescriptor.version=B' returns B's commitId.
        //
        // Both spellings are read. A descriptor-only URL is attributable to the descriptor; a URL
        // carrying both is attributable only when they agree, because agreeing selectors have one
        // reading and disagreeing ones are the bug this exists to catch.
        if (!TryReadAgreedSelector(uri.Query, host, "version", out string? revision, out rejection))
        {
            return false;
        }

        if (revision is null)
        {
            rejection = $"'{host}' URL names no 'version' or 'versionDescriptor.version'";
            return false;
        }

        // 'version' alone does not say what it selects. Azure reads it against 'versionType',
        // which defaults to 'branch', so a branch and a tag of one name are two different contents
        // behind one spelling -- and behind one cache identity. Measured against a live
        // repository: 'main' as a branch returned 200 and as a tag returned 404.
        //
        // Only an immutable selector is attributable, so 'versionType' must say 'commit' and the
        // version must be a commit hash. This is what SourceLink itself generates -- both
        // Microsoft.SourceLink.AzureRepos.Git and .AzureDevOpsServer.Git emit
        // '?api-version=1.0&versionType=commit&version={sha}&path=/*' -- so requiring it does not
        // refuse a map any supported generator produces.
        if (!TryReadAgreedSelector(uri.Query, host, "versionType", out string? versionType, out rejection))
        {
            return false;
        }

        if (!string.Equals(versionType, "commit", StringComparison.OrdinalIgnoreCase))
        {
            rejection =
                $"'{host}' URL selects revision '{revision}' as " +
                $"'{versionType ?? "branch"}', which is a moving ref rather than a commit";
            return false;
        }

        if (!IsCommitHash(revision))
        {
            rejection =
                $"'{host}' URL selects revision '{revision}' as a commit, but it is not a " +
                "commit hash";
            return false;
        }

        // 'versionOptions' moves the selection off the named commit: 'previousChange' and
        // 'firstParent' both serve a different commit's content under the same 'version'.
        if (!TryReadAgreedSelector(uri.Query, host, "versionOptions", out string? versionOptions, out rejection))
        {
            return false;
        }

        if (versionOptions is not null &&
            !string.Equals(versionOptions, "none", StringComparison.OrdinalIgnoreCase))
        {
            rejection =
                $"'{host}' URL sets 'versionOptions' to '{versionOptions}', which selects a " +
                "commit other than the one named";
            return false;
        }

        return TryEmitOrigin(
            new SourceLinkOrigin(
                host,
                organization,
                repository,
                revision,
                $"https://{host}/{organization}/_git/{repository}"),
            out origin,
            out rejection);
    }

    /// <summary>
    /// The single point at which a <see cref="SourceLinkOrigin"/> becomes visible to a caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inertness rule is enforced here rather than in <see cref="Determine"/> so that it is a
    /// property of the value and not of one code path. <see cref="BrowseUrl"/> reads an origin
    /// without going through <see cref="Determine"/>, and its result is rendered as
    /// <c>GitHubBrowseUrl</c>; the cache identity is built from the same components. A rule that
    /// only one consumer applies is a rule the next consumer will not inherit. For the same
    /// reason <see cref="SourceLinkOrigin"/>'s constructor is <c>internal</c> and its components
    /// are get-only: a public positional constructor would let a caller build an origin around
    /// this method, so the claim that this is the only place an origin is produced would be
    /// false.
    /// </para>
    /// <para>
    /// Gated by
    /// <c>SourceLinkProvenanceTests.NoOriginIsEverProducedCarryingAScalarThatCanActOnASink</c>,
    /// which reads the origin at this seam rather than through a renderer, because every renderer
    /// this reader has today happens to percent-escape the components it prints — so a test aimed
    /// at rendered text would pass whether or not this check exists.
    /// </para>
    /// </remarks>
    private static bool TryEmitOrigin(
        SourceLinkOrigin candidate,
        out SourceLinkOrigin origin,
        out string rejection)
    {
        if (!TryCheckOriginTextIsInert(candidate, out rejection))
        {
            origin = default;
            return false;
        }

        origin = candidate;
        return true;
    }

    /// <summary>
    /// A full SHA-1 Git object name. Abbreviations are deliberately not accepted — an
    /// abbreviation is a prefix, so two of them can name one revision while comparing unequal,
    /// and one of them can become ambiguous as a repository grows.
    /// </summary>
    /// <remarks>
    /// A 64-character SHA-256 object name is refused rather than accepted, because whether a
    /// hex string is an object name at all is a property of the host's object format, not of
    /// the string. Every host this reader knows — GitHub and Azure DevOps — stores SHA-1
    /// repositories only, and Git permits a branch named with 64 hex characters
    /// (<c>git branch</c> creates one), so on those hosts a 64-hex revision cannot be a commit
    /// and can only be a moving ref. Admitting the SHA-256 length needs the same kind of
    /// evidence as admitting a host: that the host serves SHA-256 repositories, and that it
    /// excludes refs spelled like one. Gated by
    /// <c>SourceLinkProvenanceTests.ASixtyFourHexRevisionOnASha1Host_IsNotACommit</c>.
    /// </remarks>
    private static bool IsCommitHash(string value)
    {
        if (value.Length is not 40)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads one Azure version selector, which the Items API accepts in a flat spelling
    /// (<c>version</c>) and a descriptor spelling (<c>versionDescriptor.version</c>).
    /// </summary>
    /// <remarks>
    /// The descriptor takes precedence at the host, so reading only the flat spelling reports the
    /// losing selector — a URL carrying both would report one revision while fetching the other.
    /// Confirmed against the live API: <c>version=A&amp;versionDescriptor.version=B</c> returns
    /// B's <c>commitId</c>. Both are read; a descriptor-only URL is attributable to the
    /// descriptor; a URL carrying both is attributable only when they agree, because agreeing
    /// selectors have one reading and disagreeing ones are the bug this exists to catch.
    /// </remarks>
    private static bool TryReadAgreedSelector(
        string query,
        string host,
        string name,
        out string? value,
        out string rejection)
    {
        value = null;

        string? flat = ReadSingleQueryValue(query, name, out string flatRejection);
        if (flat is null && flatRejection.Length != 0)
        {
            rejection = $"'{host}' URL {flatRejection}";
            return false;
        }

        string descriptorName = $"versionDescriptor.{name}";
        string? descriptor = ReadSingleQueryValue(query, descriptorName, out string descriptorRejection);
        if (descriptor is null && descriptorRejection.Length != 0)
        {
            rejection = $"'{host}' URL {descriptorRejection}";
            return false;
        }

        if (flat is not null && descriptor is not null &&
            !string.Equals(flat, descriptor, StringComparison.Ordinal))
        {
            rejection =
                $"'{host}' URL gives '{name}' as '{flat}' and '{descriptorName}' as " +
                $"'{descriptor}', and the descriptor is the one the host honours";
            return false;
        }

        value = descriptor ?? flat;
        rejection = "";
        return true;
    }

    /// <summary>
    /// Every query parameter Azure's Items API takes that this reader has reasoned about. An
    /// unrecognized parameter is refused rather than ignored: the API grows and we do not, so an
    /// unknown name may select content the reported origin does not describe.
    /// </summary>
    private static readonly string[] KnownAzureQueryParameters =    [
        "api-version",
        "path",
        "scopePath",
        "version",
        "versionType",
        "versionOptions",
        "versionDescriptor.version",
        "versionDescriptor.versionType",
        "versionDescriptor.versionOptions",
    ];

    /// <summary>
    /// Checks that every query parameter is one this reader has reasoned about, that no parameter
    /// is given twice, and that the two content selectors are not given together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A repeat is refused because Azure serves the <em>first</em> occurrence, so a later one
    /// selects nothing while looking like it selects. Measured against
    /// <c>dev.azure.com/dnceng-public/public</c>: <c>path=/README.md&amp;path=/nope.txt</c>
    /// returns README, and <c>path=/.gitignore&amp;path=/README.md</c> returns 404 for the
    /// first path. A map may therefore write <c>path=/fixed.cs&amp;path=/*</c> — every document
    /// substitutes into an occurrence the host ignores, and every one of them fetches
    /// <c>fixed.cs</c>. The revision selectors were already read one at a time; the content
    /// selectors are the ones that were not, and this covers every name uniformly.
    /// </para>
    /// <para>
    /// Names are compared case-insensitively because the host binds them that way: measured,
    /// <c>PATH=/README.md</c> alone returns README, and
    /// <c>PATH=/README.md&amp;path=/nope.txt</c> returns README while
    /// <c>path=/nope.txt&amp;PATH=/README.md</c> returns 404 — one parameter, first occurrence
    /// wins, whatever the spelling. A repeat in two spellings keeps the more specific reason
    /// that <c>ReadSingleQueryValue</c> gives for a single mis-spelled name.
    /// </para>
    /// <para>
    /// A repeat is refused even when the two values are equal. Equal values do not make one
    /// reading, and the host is the evidence: measured, <c>version=aaaa&amp;version=aaaa</c>
    /// returns 400 "Ambiguous values for version", so a repeated selector fetches nothing at all.
    /// An earlier note reasoned instead from <c>HttpUtility.ParseQueryString</c>, which joins
    /// repeats with a comma, and concluded the host would select the ref <c>aaaa,aaaa</c>; that
    /// is a client decoder's behaviour, not the host's, and the live API neither joins nor
    /// serves.
    /// </para>
    /// <para>
    /// <c>path</c> and <c>scopePath</c> are refused together because a request carrying both has
    /// no single reading. Measured against <c>dev.azure.com/dnceng-public/public</c>:
    /// <c>path=/README.md</c> alone returns the file, <c>scopePath=/README.md</c> alone returns
    /// the <em>same bytes</em>, <c>scopePath=/</c> returns a JSON collection, and the two together
    /// return 400 "Cannot specify an item \"path\" as well as \"scopePath\"". Each selects content
    /// on its own and nothing states which governs, so this is the repeated-parameter rule
    /// applied to two spellings of one role: were the host ever to start preferring one, every
    /// document would resolve through the selector that does <em>not</em> carry the wildcard and
    /// they would all fetch the same content while attributing cleanly — the defect a repeated
    /// <c>path</c> already produced.
    /// </para>
    /// <para>
    /// The rule is ambiguity, not fetchability. "A URL the host will not serve is not
    /// attributable" would be a stronger claim and is not made, because it cannot be enforced:
    /// <c>api-version</c> is allow-listed and unvalidated, and measured, <c>api-version=bogus</c>
    /// and <c>api-version=99.0</c> each return 400 while <c>1.0</c>, <c>7.1</c>,
    /// <c>1.0-preview</c>, an empty value and no value at all return the same 985 bytes. Pinning
    /// that parameter to a list of versions would refuse whatever version ships next, and it
    /// would buy nothing: a request that fails serves no content, so nothing is misattributed and
    /// the failure stays visible. Raised in review, where the over-broad wording above was read —
    /// correctly — as promising a check this does not perform.
    /// </para>
    /// <para>
    /// Gated by
    /// <c>SourceLinkProvenanceTests.ARepeatedContentSelector_IsNotAttributable</c> and
    /// <c>SourceLinkProvenanceTests.AnAzureUrlGivingBothContentSelectors_IsNotAttributable</c>.
    /// </para>
    /// </remarks>
    private static bool TryCheckQueryParameters(string query, string host, out string rejection)
    {
        ReadOnlySpan<char> pairs = query.AsSpan().TrimStart('?');
        var seen = new List<string>();

        foreach (Range range in pairs.Split('&'))
        {
            ReadOnlySpan<char> pair = pairs[range];
            if (pair.IsEmpty)
            {
                continue;
            }

            int equals = pair.IndexOf('=');
            ReadOnlySpan<char> name = equals < 0 ? pair : pair[..equals];

            bool known = false;
            foreach (string candidate in KnownAzureQueryParameters)
            {
                // Case-insensitively, so that a parameter differing only in case is recognized
                // here and refused by the reader that owns its spelling, rather than slipping
                // past as an unknown name with a different message.
                if (name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                rejection =
                    $"'{host}' URL carries the unrecognized query parameter '{name}', which may " +
                    "select content the reported origin does not describe";
                return false;
            }

            foreach (string earlier in seen)
            {
                if (!name.Equals(earlier, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                rejection = name.SequenceEqual(earlier)
                    ? $"'{host}' URL repeats the '{earlier}' parameter, and the host serves the " +
                      "first occurrence, so a later one selects nothing while appearing to"
                    : $"'{host}' URL spells the '{earlier}' parameter also as '{name}', and " +
                      "whether the host matches parameter names case-insensitively is not " +
                      "stated by the URL";
                return false;
            }

            seen.Add(name.ToString());
        }

        // 'path' and 'scopePath' are each a content selector on their own, and the host refuses
        // the pair outright rather than preferring one. A URL carrying both selects nothing, so
        // there is no content for the reported origin to describe.
        bool hasPath = seen.Exists(static name =>
            string.Equals(name, "path", StringComparison.OrdinalIgnoreCase));
        bool hasScopePath = seen.Exists(static name =>
            string.Equals(name, "scopePath", StringComparison.OrdinalIgnoreCase));

        if (hasPath && hasScopePath)
        {
            rejection =
                $"'{host}' URL gives both 'path' and 'scopePath', which the host refuses rather " +
                "than resolving, so the URL selects no content the reported origin could describe";
            return false;
        }

        rejection = "";
        return true;
    }

    /// <summary>
    /// Reads exactly one value for <paramref name="name"/>, which the caller has already
    /// established is present at most once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parameter names are matched case-insensitively but accepted only in the exact spelling
    /// <paramref name="name"/> gives. Whether a host treats <c>VERSION</c> as <c>version</c> is
    /// not stated by the URL, so <c>?VERSION=a&amp;version=b</c> has two readings and neither is
    /// established. Matching case-sensitively would silently pick <c>b</c> while a
    /// case-insensitive server may serve <c>a</c>. A repeat in two spellings is caught earlier;
    /// this branch is what refuses a single occurrence spelled <c>Version=a</c>.
    /// </para>
    /// <para>
    /// The repeat rule itself has one owner, <c>TryCheckQueryParameters</c>, which runs before
    /// any call to this and applies it to every parameter rather than to the selectors alone.
    /// This once carried a second copy of it; the copy became unreachable, and a second copy of a
    /// rule that can no longer fire is a gate that reports coverage it does not have.
    /// </para>
    /// <para>
    /// A literal <c>+</c> in a value is refused. A form decoder reads it as a space and a percent
    /// decoder reads it as a plus, so <c>version=a%2Bb&amp;versionDescriptor.version=a+b</c> is
    /// two agreeing selectors to one reader and two disagreeing ones to another. <c>%2B</c> is
    /// unambiguous and stays accepted; only the bare character is refused.
    /// </para>
    /// </remarks>
    private static string? ReadSingleQueryValue(string query, string name, out string rejection)
    {
        ReadOnlySpan<char> pairs = query.AsSpan().TrimStart('?');
        string? found = null;
        bool present = false;

        foreach (Range range in pairs.Split('&'))
        {
            ReadOnlySpan<char> pair = pairs[range];
            int equals = pair.IndexOf('=');
            ReadOnlySpan<char> spelling = equals < 0 ? pair : pair[..equals];
            if (!spelling.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!spelling.SequenceEqual(name))
            {
                rejection =
                    $"spells the '{name}' parameter as '{spelling}', and whether the host " +
                    "matches parameter names case-insensitively is not stated by the URL";
                return null;
            }

            present = true;

            if (equals < 0)
            {
                rejection =
                    $"names the '{name}' parameter with no value at all, which selects nothing " +
                    "and which a host may read as the parameter's default";
                return null;
            }

            ReadOnlySpan<char> rawValue = pair[(equals + 1)..];
            if (rawValue.Contains('+'))
            {
                rejection =
                    $"gives the '{name}' parameter a value containing a literal '+', which a " +
                    "form decoder reads as a space and a percent decoder reads as a plus";
                return null;
            }

            found = Uri.UnescapeDataString(rawValue.ToString());
        }

        if (!present)
        {
            // Absent, so nothing is rejected. An empty rejection means "not present"; a non-empty
            // one means "present and unusable", and callers reading more than one selector need
            // to tell those apart.
            rejection = "";
            return null;
        }

        if (found!.Length == 0)
        {
            // Present and empty is not absent. Reporting it as absent skipped the agreement check
            // between a selector's flat and descriptor spellings entirely, so
            // 'versionType=commit&versionDescriptor.versionType=' was read as an unopposed
            // 'commit' while the host, which honours the descriptor, reads an empty selector as
            // its default of 'branch'.
            rejection =
                $"gives the '{name}' parameter an empty value, which selects nothing and which " +
                "a host may read as the parameter's default";
            return null;
        }

        rejection = "";
        return found;
    }

    /// <summary>
    /// Detects percent-encoded path separators and percent-encoded dot segments, which survive
    /// <see cref="Uri"/> canonicalization unchanged.
    /// </summary>
    private static bool ContainsEncodedSeparatorOrDotSegment(string url, out string encoded)
    {
        for (int i = 0; i + 2 < url.Length; i++)
        {
            if (url[i] != '%')
            {
                continue;
            }

            ReadOnlySpan<char> pair = url.AsSpan(i + 1, 2);
            if (pair.Equals("2f", StringComparison.OrdinalIgnoreCase) ||
                pair.Equals("5c", StringComparison.OrdinalIgnoreCase) ||
                pair.Equals("2e", StringComparison.OrdinalIgnoreCase))
            {
                encoded = url.Substring(i, 3);
                return true;
            }
        }

        encoded = "";
        return false;
    }
}
