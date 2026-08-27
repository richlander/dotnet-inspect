using System.Globalization;
using System.Text;

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
/// explanation for a provenance-attribution failure. Map parse errors and rejected entries use
/// the separate SourceLink map-inspection path; this reason remains available to programmatic
/// provenance callers. That the reason is always populated is gated by
/// <c>SourceLinkProvenanceTests.EveryUnestablishedResult_CarriesAReason</c>.
/// </remarks>
public readonly record struct SourceLinkProvenanceResult(SourceLinkOrigin? Origin, string Reason)
{
    /// <summary>Whether an origin was established.</summary>
    public bool IsEstablished => Origin is not null;
}

public enum SourceLinkFetchOriginStatus
{
    Unattributed,
    Preserved,
    Changed,
}

/// <summary>
/// The relationship between the SourceLink URL requested and the URL that supplied the response.
/// </summary>
public readonly record struct SourceLinkFetchOriginResult(
    SourceLinkFetchOriginStatus Status,
    string Reason)
{
    public bool IsAllowed => Status is not SourceLinkFetchOriginStatus.Changed;
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
public static partial class SourceLinkProvenance
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
    /// Checks that an attributed SourceLink request did not redirect to another repository,
    /// revision, or unattributable destination.
    /// </summary>
    /// <remarks>
    /// URLs outside the known provenance grammars remain fetchable: they carry no reported
    /// repository claim, and checksum verification remains the content-authenticity boundary.
    /// Once the requested URL is attributable, however, the final response URL must produce the
    /// same complete origin tuple. Gated by
    /// <c>SourceLinkProvenanceTests.FetchOrigin_AttributedResponseMustPreserveTheCompleteOrigin</c>,
    /// <c>...FetchOrigin_AzureSignInRedirectIsNotTheAttributedRepository</c>, and
    /// <c>...FetchOrigin_UnknownSourceLinkHostCarriesNoOriginClaim</c>.
    /// </remarks>
    public static SourceLinkFetchOriginResult ValidateFetchOrigin(
        string requestedUrl,
        string finalUrl)
    {
        ArgumentNullException.ThrowIfNull(requestedUrl);
        ArgumentNullException.ThrowIfNull(finalUrl);

        if (!TryReadOrigin(requestedUrl, out SourceLinkOrigin requestedOrigin, out _))
        {
            return new SourceLinkFetchOriginResult(
                SourceLinkFetchOriginStatus.Unattributed,
                "");
        }

        if (!TryReadOrigin(finalUrl, out SourceLinkOrigin finalOrigin, out _))
        {
            return new SourceLinkFetchOriginResult(
                SourceLinkFetchOriginStatus.Changed,
                "the response URL has no attributable origin");
        }

        if (requestedOrigin != finalOrigin)
        {
            return new SourceLinkFetchOriginResult(
                SourceLinkFetchOriginStatus.Changed,
                "the response URL names a different source origin");
        }

        return new SourceLinkFetchOriginResult(
            SourceLinkFetchOriginStatus.Preserved,
            "");
    }

    /// <summary>
    /// Reports whether a resolved source URL names content at an immutable commit origin.
    /// </summary>
    /// <remarks>
    /// This is the cache-policy view of the provenance grammar. A URL is immutable only when the
    /// same host-specific reader that attributes its repository and revision establishes a full
    /// commit selector. Unknown hosts, moving refs, ambiguous selectors, and malformed URLs remain
    /// mutable. Gated by <c>SourceLinkUrlsTests</c>.
    /// </remarks>
    public static bool IsImmutableContentUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return TryReadOrigin(url, out _, out _);
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
}
