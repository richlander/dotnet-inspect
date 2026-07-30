using SLF = SourceLinkFetch;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Pins the provenance invariant stated in <c>docs/design/untrusted-data-threat-model.md</c>:
/// reported provenance must describe the origin that source content is actually fetched from, for
/// every document the assembly resolves; when that cannot be established for all of them, report
/// no repository.
/// </summary>
/// <remarks>
/// <para>
/// The threat model records four ways weaker formulations of this invariant failed, each found by
/// attacking a previous formulation, and requires them as a regression floor. They are the four
/// <c>TheThreatModelsCase</c> tests below. Passing them is not evidence that the invariant holds —
/// that is what the invariant tests around them are for — but failing any of them is proof that it
/// does not.
/// </para>
/// <para>
/// Every input here is attacker-controlled in practice: the map and the document names both come
/// from a PDB in a downloaded package.
/// </para>
/// </remarks>
public class SourceLinkProvenanceTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static SLF.SourceLinkProvenanceResult Determine(string map, params string[] documents)
        => SLF.SourceLinkProvenance.Determine(SLF.SourceLinkResolver.Parse(map), documents);

    /// <summary>
    /// Canonical GitHub SourceLink reports its repository.
    /// </summary>
    /// <remarks>
    /// Open item 2 in the threat model: the replaced reader preconditioned on the value containing
    /// <c>github.com</c>, which <c>raw.githubusercontent.com</c> does not contain, so every
    /// GitHub-hosted assembly reported no repository at all. This is the plain case that reader
    /// could not do.
    /// </remarks>
    [Fact]
    public void CanonicalGitHubSourceLink_ReportsItsRepository()
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com/dotnet/runtime/{{{Sha}}}/*"}}""",
            "/_/src/System/String.cs",
            "/_/src/System/Int32.cs");

        Assert.True(result.IsEstablished, result.Reason);
        Assert.Equal("https://github.com/dotnet/runtime", result.Origin!.Value.RepositoryUrl);
        Assert.Equal(Sha, result.Origin!.Value.Revision);
    }

    /// <summary>
    /// A URL that merely mentions the GitHub raw host somewhere inside it is not GitHub's.
    /// </summary>
    /// <remarks>
    /// Issue #3408. The replaced reader matched an unanchored regular expression against the raw
    /// URL text, so a URL served by <c>evil.example</c> that carries the GitHub host in a query
    /// parameter was reported as <c>https://github.com/dotnet/runtime</c>. Provenance is read off
    /// the parsed host, which is not a substring search.
    /// </remarks>
    [Fact]
    public void AUrlThatMerelyMentionsTheGitHubRawHost_IsNotAttributedToGitHub()
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://evil.example/?u=https://raw.githubusercontent.com/dotnet/runtime/{{{Sha}}}/*"}}""",
            "/_/src/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("evil.example", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The threat model's first case: agreement on owner and repository ignores the revision, and
    /// one repository serves any revision reachable in it — including the head of an unmerged pull
    /// request. Two entries on one repository at two revisions are two origins.
    /// </summary>
    [Fact]
    public void TheThreatModelsCase_OfOneRepositoryAtTwoRevisions_ReportsNoRepository()
    {
        var result = Determine(
            $$$"""
            {"documents":{
              "/_/src/*":"https://raw.githubusercontent.com/dotnet/runtime/{{{Sha}}}/src/*",
              "/_/gen/*":"https://raw.githubusercontent.com/dotnet/runtime/{{{OtherSha}}}/gen/*"}}
            """,
            "/_/src/A.cs",
            "/_/gen/B.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("more than one origin", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The threat model's second case: <see cref="Uri"/> applies RFC 3986 dot-segment removal, so
    /// a mapping value containing <c>../</c> is fetched from the traversed-to path while a regular
    /// expression over the raw string reports the literal one. The reported origin must be the one
    /// content is served from.
    /// </summary>
    [Fact]
    public void TheThreatModelsCase_OfDotSegmentsInTheMapping_ReportsWhereContentIsReallyServedFrom()
    {
        const string map =
            """{"documents":{"/_/*":"https://raw.githubusercontent.com/dotnet/runtime/x/../../attacker/evil/main/*"}}""";

        var result = Determine(map, "/_/A.cs");

        Assert.True(result.IsEstablished, result.Reason);
        Assert.Equal("https://github.com/dotnet/attacker", result.Origin!.Value.RepositoryUrl);
        Assert.NotEqual("https://github.com/dotnet/runtime", result.Origin!.Value.RepositoryUrl);
    }

    /// <summary>
    /// The threat model's third case: even a clean mapping is not enough, because the wildcard
    /// suffix comes from the PDB document path, which is equally attacker-controlled, and the
    /// per-segment escaping leaves <c>..</c> intact. A benign-looking map plus a traversing
    /// document name resolves outside the repository the map names.
    /// </summary>
    [Fact]
    public void TheThreatModelsCase_OfATraversingDocumentName_ReportsNoRepository()
    {
        string map =
            $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com/dotnet/runtime/{{{Sha}}}/*"}}""";

        var result = Determine(
            map,
            "/_/src/A.cs",
            "/_/../../../attacker/evil/main/Program.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("more than one origin", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The threat model's fourth case: <see cref="Uri"/> preserves percent-encoded separators
    /// verbatim, so <c>..%2f</c> and <c>..%5c</c> survive canonicalization. A canonicalize-then-
    /// check step passes while a server that percent-decodes before resolving dot segments still
    /// traverses out, so encoded separators and encoded dot segments are rejected outright.
    /// </summary>
    [Theory]
    [InlineData("..%2f..%2f..%2fattacker")]
    [InlineData("..%5c..%5c..%5cattacker")]
    [InlineData("%2e%2e/%2e%2e/attacker")]
    [InlineData("..%2F..%2Fattacker")]
    public void TheThreatModelsCase_OfEncodedSeparators_ReportsNoRepository(string traversal)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com/dotnet/runtime/{{{Sha}}}/{{{traversal}}}/*"}}""",
            "/_/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("canonicalization does not resolve", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host that merely appears before the real host is user information, not the origin.
    /// <c>https://raw.githubusercontent.com@evil.example/...</c> parses with host
    /// <c>evil.example</c>, so reading the host text rather than parsing it attributes an
    /// attacker's content to GitHub.
    /// </summary>
    [Fact]
    public void AHostGivenAsUserInformation_IsNotTheOrigin()
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com@evil.example/dotnet/runtime/{{{Sha}}}/*"}}""",
            "/_/A.cs");

        Assert.False(result.IsEstablished);
    }

    /// <summary>
    /// The host allow list already rejects a spoofed authority, because <see cref="Uri"/> takes
    /// the authority after the last <c>@</c>. What the user-info rejection decides on its own is
    /// this case: a credential presented to a host that <em>is</em> on the allow list. The
    /// response then depends on the identity presented rather than on the public path the URL
    /// names, so the public repository does not establish the bytes fetched.
    /// </summary>
    [Fact]
    public void ACredentialPresentedToAnAllowedHost_IsNotAttributedToThePublicRepository()
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://token@raw.githubusercontent.com/dotnet/runtime/{{{Sha}}}/*"}}""",
            "/_/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("user information", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://raw.githubusercontent.com/dotnet/runtime/aaaa/*", "not https")]
    [InlineData("https://example.invalid/dotnet/runtime/aaaa/*", "not a recognized source host")]
    [InlineData("https://raw.githubusercontent.com/dotnet/*", "names no owner")]
    public void AUrlWithNoAttributableOrigin_ReportsNoRepositoryAndSaysWhy(string template, string expectedReason)
    {
        var result = Determine($$$"""{"documents":{"/_/*":"{{{template}}}"}}""", "/_/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains(expectedReason, result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// An entry no document matches is never fetched, so it makes no claim about where source
    /// comes from. Provenance is established over the documents the assembly declares.
    /// </summary>
    [Fact]
    public void AnEntryNoDocumentMatches_DoesNotDenyProvenance()
    {
        var result = Determine(
            $$$"""
            {"documents":{
              "/_/*":"https://raw.githubusercontent.com/dotnet/runtime/{{{Sha}}}/*",
              "/other/*":"https://evil.example/{{{Sha}}}/*"}}
            """,
            "/_/src/A.cs");

        Assert.True(result.IsEstablished, result.Reason);
        Assert.Equal("https://github.com/dotnet/runtime", result.Origin!.Value.RepositoryUrl);
    }

    /// <summary>
    /// A PDB whose documents resolve to nothing reports no repository, and says so rather than
    /// returning a null that reads like "not asked".
    /// </summary>
    [Theory]
    [InlineData("""{"documents":{"/other/*":"https://raw.githubusercontent.com/o/r/aaaa/*"}}""", "no document resolves")]
    [InlineData("""{"documents":[]}""", "did not parse")]
    public void AMapThatResolvesNothing_ReportsNoRepositoryAndSaysWhy(string map, string expectedReason)
    {
        var result = Determine(map, "/_/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains(expectedReason, result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Azure DevOps substitutes into the middle of a URL and carries the revision in the query
    /// string, so its origin cannot be read off the path alone.
    /// </summary>
    [Fact]
    public void AnAzureDevOpsMap_ReportsItsOrganizationProjectAndRepository()
    {
        var result = Determine(
            """
            {"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?api-version=1.0&versionType=commit&version=deadbeef&path=/*"}}
            """,
            "/_/src/A.cs",
            "/_/src/B.cs");

        Assert.True(result.IsEstablished, result.Reason);
        Assert.Equal("https://dev.azure.com/contoso/widgets/_git/core", result.Origin!.Value.RepositoryUrl);
        Assert.Equal("deadbeef", result.Origin!.Value.Revision);
    }

    /// <summary>
    /// Two Azure DevOps entries on one repository at two versions are two origins, for the same
    /// reason two GitHub revisions are.
    /// </summary>
    [Fact]
    public void AnAzureDevOpsMapAtTwoVersions_ReportsNoRepository()
    {
        var result = Determine(
            """
            {"documents":{
              "/_/src/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?version=deadbeef&path=/*",
              "/_/gen/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?version=feedface&path=/*"}}
            """,
            "/_/src/A.cs",
            "/_/gen/B.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("more than one origin", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The cache identity names the repository as well as the revision. A commit hash alone is
    /// shared by every fork containing that commit, so keying an index on it would serve one
    /// repository's index for another repository's assembly.
    /// </summary>
    [Fact]
    public void TheCacheIdentity_DistinguishesForksSharingARevision()
    {
        string Identity(string owner) => Determine(
            $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com/{{{owner}}}/runtime/{{{Sha}}}/*"}}""",
            "/_/A.cs").Origin!.Value.Identity;

        Assert.NotEqual(Identity("dotnet"), Identity("attacker"));
    }

    /// <summary>
    /// Whenever provenance is not established there is a stated reason, so "no repository" is
    /// always reported as a decision rather than as absence.
    /// </summary>
    [Fact]
    public void EveryUnestablishedResult_CarriesAReason()
    {
        string[] maps =
        [
            """{"documents":[]}""",
            """{"documents":{}}""",
            """{"documents":{"/_/*":"https://evil.example/*"}}""",
            $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com/dotnet/runtime/{{{Sha}}}/..%2f*"}}""",
        ];

        foreach (string map in maps)
        {
            var result = Determine(map, "/_/A.cs");
            Assert.False(result.IsEstablished);
            Assert.NotEmpty(result.Reason);
        }
    }

    /// <summary>
    /// Pins that provenance has one owner, so an unanchored URL match cannot reappear elsewhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defect this closes existed in four places at once — two regular expressions over raw
    /// URL text, a substring precondition that never matched the canonical host, and a substring
    /// test over the whole map that let any package claim a Microsoft origin — and nothing failed,
    /// because nothing was watching for a second reader. This watches, by set equality, so a new
    /// reader fails here rather than being quietly tolerated.
    /// </para>
    /// <para>
    /// Three entries are legitimate and stay. <c>LocalRepoSourceAcquisition</c> maps an
    /// already-attributed URL onto a local git object, <c>SourceLinkUrls</c> classifies a URL as
    /// content-addressed for caching, and <c>GitHubUrlResolver</c> builds a raw URL from a
    /// github.com one. All three parse rather than match text, and none of them decides where
    /// source came from. Comment text is ignored: naming the host in prose is not reading it.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnlyTheProvenanceOwner_AndTwoNonAttributingReaders_NameTheGitHubRawHost()
    {
        string src = Path.Combine(FindRepoRoot(), "src");

        var readers = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains(".Tests", StringComparison.Ordinal))
            .Where(static file => File.ReadLines(file).Any(NamesTheHostInCode))
            .Select(file => Path.GetRelativePath(src, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "DotnetInspector.Services/GitHubUrlResolver.cs",
                "DotnetInspector.Services/LocalRepoSourceAcquisition.cs",
                "SourceLinkFetch/SourceLinkProvenance.cs",
                "dotnet-inspect/Services/SourceLinkUrls.cs",
            ],
            readers);
    }

    private static bool NamesTheHostInCode(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
        {
            return false;
        }

        return trimmed.Contains("raw.githubusercontent.com", StringComparison.Ordinal)
            || trimmed.Contains(@"raw\.githubusercontent\.com", StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "dotnet-inspect.slnx")))
            dir = dir.Parent;

        Assert.True(dir != null, "Could not locate the repository root (dotnet-inspect.slnx).");
        return dir!.FullName;
    }
}
