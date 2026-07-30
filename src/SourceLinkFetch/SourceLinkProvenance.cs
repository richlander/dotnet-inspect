using System.Text;
using System.Reflection.Metadata;

namespace SourceLinkFetch;

/// <summary>
/// The origin that source content is actually fetched from, read off a resolved SourceLink URL.
/// </summary>
/// <param name="Host">The canonical, lower-cased host of the resolved URL.</param>
/// <param name="Organization">
/// The owning account: the GitHub owner, or the Azure DevOps organization and project.
/// </param>
/// <param name="Repository">The repository name.</param>
/// <param name="Revision">
/// The commit, branch, or tag the content is served at. Two entries naming one repository at two
/// revisions are two origins, because a revision is reachable in a repository without being part
/// of it — the head of an unmerged pull request is served by the same host as the default branch.
/// </param>
/// <param name="RepositoryUrl">A browsable URL for the repository.</param>
public readonly record struct SourceLinkOrigin(
    string Host,
    string Organization,
    string Repository,
    string Revision,
    string RepositoryUrl)
{
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

            string? url = resolver.ResolveUrl(documentPath);
            if (url is null)
            {
                // The document is not fetched, so it makes no claim about where source comes from.
                continue;
            }

            resolvedCount++;

            if (!TryReadOrigin(url, out SourceLinkOrigin origin, out string rejection))
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
    /// fall back to showing the resolved URL itself.
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

        // AbsolutePath has had dot segments removed, so traversal has already been applied and the
        // segments below name where content is really served from.
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string host = uri.Host;

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

            origin = new SourceLinkOrigin(
                host,
                segments[0],
                segments[1],
                segments[2],
                $"https://github.com/{segments[0]}/{segments[1]}");
            rejection = "";
            return true;
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
    /// or the older <c>https://{account}.visualstudio.com/{project}/_apis/...</c> spelling.
    /// </summary>
    private static bool TryReadAzureDevOpsOrigin(
        Uri uri,
        string host,
        string[] segments,
        out SourceLinkOrigin origin,
        out string rejection)
    {
        origin = default;

        int apis = Array.IndexOf(segments, "_apis");
        if (apis < 0 ||
            segments.Length != apis + 5 ||
            !string.Equals(segments[apis + 1], "git", StringComparison.Ordinal) ||
            !string.Equals(segments[apis + 2], "repositories", StringComparison.Ordinal) ||
            !string.Equals(segments[apis + 4], "items", StringComparison.Ordinal))
        {
            rejection = $"'{host}' path '{uri.AbsolutePath}' is not a Git items path";
            return false;
        }

        string organization = string.Join('/', segments[..apis]);
        if (organization.Length == 0)
        {
            rejection = $"'{host}' path '{uri.AbsolutePath}' names no organization";
            return false;
        }

        string repository = segments[apis + 3];

        // Every parameter must be one this reader has reasoned about. Azure's Items API takes
        // several that change which content is returned, and an unrecognized one cannot be assumed
        // inert: the reported origin would then describe less than the URL selects. This is an
        // allow list rather than a deny list because the API grows and we do not.
        if (!TryCheckKnownQueryParameters(uri.Query, out string unknown))
        {
            rejection =
                $"'{host}' URL carries the unrecognized query parameter '{unknown}', which may " +
                "select content the reported origin does not describe";
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

        origin = new SourceLinkOrigin(
            host,
            organization,
            repository,
            revision,
            $"https://{host}/{organization}/_git/{repository}");
        rejection = "";
        return true;
    }

    /// <summary>
    /// A full Git object name: 40 hex characters for SHA-1, 64 for SHA-256. Abbreviations are
    /// deliberately not accepted — an abbreviation is a prefix, so two of them can name one
    /// revision while comparing unequal, and one of them can become ambiguous as a repository
    /// grows.
    /// </summary>
    private static bool IsCommitHash(string value)
    {
        if (value.Length is not (40 or 64))
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
    private static readonly string[] KnownAzureQueryParameters =
    [
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

    private static bool TryCheckKnownQueryParameters(string query, out string unknown)
    {
        ReadOnlySpan<char> pairs = query.AsSpan().TrimStart('?');

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
                unknown = name.ToString();
                return false;
            }
        }

        unknown = "";
        return true;
    }

    /// <summary>
    /// Reads exactly one value for <paramref name="name"/>. A repeated parameter is rejected
    /// rather than resolved, for the same reason a duplicated JSON key is: two readers of one
    /// query string can disagree about which value wins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parameter names are matched case-insensitively but accepted only in the exact spelling
    /// <paramref name="name"/> gives. Whether a host treats <c>VERSION</c> as <c>version</c> is
    /// not stated by the URL, so <c>?VERSION=a&amp;version=b</c> has two readings and neither is
    /// established. Matching case-sensitively would silently pick <c>b</c> while a
    /// case-insensitive server may serve <c>a</c>.
    /// </para>
    /// <para>
    /// A repeat is refused even when the values are equal. Equal values do not make one reading:
    /// ASP.NET, which Azure DevOps is built on, <em>joins</em> repeats with a comma, so
    /// <c>?version=aaaa&amp;version=aaaa</c> selects the ref named <c>aaaa,aaaa</c> — an
    /// attacker-controlled ref distinct from <c>aaaa</c>.
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

        foreach (Range range in pairs.Split('&'))
        {
            ReadOnlySpan<char> pair = pairs[range];
            int equals = pair.IndexOf('=');
            if (equals < 0 || !pair[..equals].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!pair[..equals].SequenceEqual(name))
            {
                rejection =
                    $"spells the '{name}' parameter as '{pair[..equals]}', and whether the host " +
                    "matches parameter names case-insensitively is not stated by the URL";
                return null;
            }

            if (found is not null)
            {
                rejection =
                    $"repeats the '{name}' parameter, and readers of one query string disagree " +
                    "about whether a repeat wins, loses, or joins";
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

        if (found is null || found.Length == 0)
        {
            // Absent, so nothing is rejected. An empty rejection means "not present"; a non-empty
            // one means "present and unusable", and callers reading more than one selector need
            // to tell those apart.
            rejection = "";
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
