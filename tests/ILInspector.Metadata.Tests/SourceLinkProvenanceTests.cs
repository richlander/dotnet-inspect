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
        // Canonicalization pops 'dotnet/runtime/<sha>' and lands on 'attacker/evil/<sha>'. The
        // revision after traversal is a commit hash, so the origin is established and can be
        // compared against what the raw mapping text says.
        string map =
            $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com/dotnet/runtime/{{{Sha}}}/../../../attacker/evil/{{{Sha}}}/*"}}""";

        var result = Determine(map, "/_/A.cs");

        Assert.True(result.IsEstablished, result.Reason);
        Assert.Equal("https://github.com/attacker/evil", result.Origin!.Value.RepositoryUrl);
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

        // The traversal lands on a commit hash, so both documents yield an established origin and
        // the disagreement between them is what has to be caught.
        var result = Determine(
            map,
            "/_/src/A.cs",
            $"/_/../../../attacker/evil/{Sha}/Program.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("more than one origin", result.Reason, StringComparison.Ordinal);

        // The same vector landing on a branch name is caught one step earlier, by the revision
        // boundary rule, rather than reported.
        var onABranch = Determine(map, "/_/src/A.cs", "/_/../../../attacker/evil/main/Program.cs");

        Assert.False(onABranch.IsEstablished);
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
            $$$"""
            {"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?api-version=1.0&versionType=commit&version={{{Sha}}}&path=/*"}}
            """,
            "/_/src/A.cs",
            "/_/src/B.cs");

        Assert.True(result.IsEstablished, result.Reason);
        Assert.Equal("https://dev.azure.com/contoso/widgets/_git/core", result.Origin!.Value.RepositoryUrl);
        Assert.Equal(Sha, result.Origin!.Value.Revision);
    }

    /// <summary>
    /// Two Azure DevOps entries on one repository at two versions are two origins, for the same
    /// reason two GitHub revisions are.
    /// </summary>
    [Fact]
    public void AnAzureDevOpsMapAtTwoVersions_ReportsNoRepository()
    {
        var result = Determine(
            $$$"""
            {"documents":{
              "/_/src/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?versionType=commit&version={{{Sha}}}&path=/*",
              "/_/gen/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?versionType=commit&version={{{OtherSha}}}&path=/*"}}
            """,
            "/_/src/A.cs",
            "/_/gen/B.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("more than one origin", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>raw.githubusercontent.com</c> serves branch names, and a branch may contain <c>/</c>, so
    /// <c>.../owner/repo/feature/auth/File.cs</c> reads equally well as revision <c>feature</c>
    /// with path <c>auth/File.cs</c> or as revision <c>feature/auth</c> with path <c>File.cs</c>.
    /// Nothing in the URL says which. Reading the third segment as the revision made two different
    /// branches report one revision — a false provenance claim, and a colliding cache identity
    /// that would serve one branch's source for the other.
    /// </summary>
    [Theory]
    [InlineData("feature/auth")]
    [InlineData("feature/login")]
    [InlineData("main")]
    [InlineData("aaaaaaa")]
    public void ARevisionThatIsNotACommitHash_IsNotAttributable(string reference)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com/owner/repo/{{{reference}}}/*"}}""",
            "/_/File.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("is not a commit hash", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two branches that previously collided must not produce one identity.
    /// </summary>
    [Fact]
    public void TwoBranchesSharingAFirstSegment_DoNotShareOneIdentity()
    {
        string Map(string reference) =>
            $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com/owner/repo/{{{reference}}}/*"}}""";

        var auth = Determine(Map("feature/auth"), "/_/File.cs");
        var login = Determine(Map("feature/login"), "/_/File.cs");

        Assert.NotEqual(
            auth.Origin?.Identity ?? "auth-unestablished",
            login.Origin?.Identity ?? "login-unestablished");
    }

    /// <summary>
    /// Whether a host matches query parameter names case-insensitively is not stated by the URL,
    /// so <c>?VERSION=evil&amp;version=legit</c> has two readings. Matching case-sensitively picks
    /// <c>legit</c> and reports it, while a case-insensitive host may serve <c>evil</c> — the
    /// reported origin would then not be where content is fetched from.
    /// </summary>
    [Theory]
    [InlineData("VERSION=evil&version=legit")]
    [InlineData("version=legit&VERSION=evil")]
    [InlineData("Version=legit")]
    public void AVersionParameterSpelledInAnotherCase_IsNotAttributable(string query)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?{{{query}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("case-insensitively is not stated", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A repeated parameter is refused even when its values are equal, and for every parameter
    /// rather than the revision selectors alone. Equal values do not make one reading, and the
    /// host is the evidence: measured against <c>dev.azure.com/dnceng-public/public</c>,
    /// <c>version=aaaa&amp;version=aaaa</c> returns 400 "Ambiguous values for version", so a
    /// repeated selector fetches nothing at all.
    /// </summary>
    /// <remarks>
    /// An earlier version of this comment reasoned from <c>HttpUtility.ParseQueryString</c>,
    /// which joins repeats with a comma, and claimed the host would therefore select the ref
    /// <c>aaaa,aaaa</c>. That is a client decoder's behaviour, not the host's; measurement shows
    /// Azure rejects the request instead. The refusal is right, the stated mechanism was not.
    /// </remarks>
    [Theory]
    [InlineData("version=aaaa&version=aaaa")]
    [InlineData("versionDescriptor.version=aaaa&versionDescriptor.version=aaaa")]
    public void ARepeatedVersionParameter_IsNotAttributableEvenWhenItsValuesAgree(string query)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?{{{query}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("repeats the", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A repeated content selector resolves every document to one file while the origin still
    /// reads cleanly, which is why the repeat rule cannot stop at the revision selectors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Azure serves the <em>first</em> occurrence of <c>path</c>, so
    /// <c>path=/fixed.cs&amp;path=/*</c> puts the substitution where the host will not look:
    /// every document produces a distinct URL — enough for the resolver's two-probe check, which
    /// only sees text — and every one of them fetches <c>fixed.cs</c>. Measured against
    /// <c>dev.azure.com/dnceng-public/public</c> at commit
    /// <c>af56d96fdbd7c26e9fc94336b6f50dcc6ceff484</c>:
    /// <c>path=/README.md&amp;path=/nope.txt</c> returns README with 200, and
    /// <c>path=/.gitignore&amp;path=/README.md</c> returns 404 for the first path rather than
    /// README for the second.
    /// </para>
    /// <para>
    /// The spelling rows are the same defect with the second occurrence cased differently, which
    /// the host binds to the same parameter: measured, <c>PATH=/README.md&amp;path=/nope.txt</c>
    /// returns README and <c>path=/nope.txt&amp;PATH=/README.md</c> returns 404. They carry the
    /// case reason rather than the repeat reason, so a fix that only compared spellings
    /// ordinally would fail the rows below rather than pass them for the wrong reason.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("path=/fixed.cs&path=/*", "repeats the")]
    [InlineData("path=/*&path=/fixed.cs", "repeats the")]
    // The wildcard has to sit in one of the repeats: with none, the entry is refused a layer
    // earlier for pairing a wildcard key with a constant URL, which is not this rule.
    [InlineData("scopePath=/*&scopePath=/other", "repeats the")]
    [InlineData("api-version=1.0&api-version=7.1&path=/*", "repeats the")]
    [InlineData("PATH=/fixed.cs&path=/*", "case-insensitively is not stated")]
    [InlineData("path=/*&PATH=/fixed.cs", "case-insensitively is not stated")]
    public void ARepeatedContentSelector_IsNotAttributable(string query, string reason)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?versionType=commit&version={{{Sha}}}&{{{query}}}"}}""",
            "/_/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains(reason, result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same URL without the repeat is attributable, so the rows above are refused for the
    /// repeat and not for something incidental to how they are written.
    /// </summary>
    [Theory]
    [InlineData("path=/*")]
    [InlineData("scopePath=/*")]
    [InlineData("api-version=7.1&path=/*")]
    public void ASingleContentSelector_IsAttributable(string query)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?versionType=commit&version={{{Sha}}}&{{{query}}}"}}""",
            "/_/A.cs");

        Assert.True(result.IsEstablished, result.Reason);
    }

    /// <summary>
    /// <c>path</c> asks for one item and <c>scopePath</c> for a collection, and the host refuses
    /// to be asked for both, so a URL carrying both selects no content for the reported origin to
    /// describe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured against <c>dev.azure.com/dnceng-public/public</c> at commit
    /// <c>af56d96fdbd7c26e9fc94336b6f50dcc6ceff484</c>: together they return 400, <c>Cannot
    /// specify an item "path" as well as "scopePath"</c>. Each alone serves — <c>path=/README.md</c>
    /// and <c>scopePath=/README.md</c> return the same bytes, and <c>scopePath=/</c> returns a
    /// collection — which is why the rows in
    /// <c>ASingleContentSelector_IsAttributable</c> keep each of them on its own rather than
    /// refusing <c>scopePath</c> outright.
    /// </para>
    /// <para>
    /// Raised in review against a row this suite had just added asserting the opposite. Both
    /// parameters were individually allow-listed and their combination was never considered, so
    /// the allow list said "each of these is inert" where the host says the pair is an error.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("scopePath=/src&path=/*")]
    [InlineData("path=/*&scopePath=/src")]
    // The exclusion is on the parameter, not on its spelling: the host binds names
    // case-insensitively, so a cased spelling is the same pair.
    [InlineData("SCOPEPATH=/src&path=/*")]
    public void AnAzureUrlGivingBothContentSelectors_IsNotAttributable(string query)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?versionType=commit&version={{{Sha}}}&{{{query}}}"}}""",
            "/_/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("both 'path' and 'scopePath'", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A literal <c>+</c> has two decodings — a space to a form decoder, a plus to a percent
    /// decoder — so a value carrying one is not attributable. Without this, the two selectors in
    /// <c>version=a%2Bb&amp;versionDescriptor.version=a+b</c> agree under percent decoding and
    /// disagree under form decoding, and the descriptor is the one the host honours: we would
    /// report <c>a+b</c> while Azure serves <c>a b</c>.
    /// </summary>
    /// <remarks>
    /// The last row is what keeps this from being satisfied by refusing everything. <c>%2B</c> is
    /// unambiguous — both decoders read it as a plus — so it must get past this rule and be
    /// refused further on, for not being a commit hash. A fix that refused any plus-bearing value,
    /// encoded or not, would stop at the wrong reason and fail that row.
    /// </remarks>
    [Theory]
    [InlineData("version=a%2Bb&versionDescriptor.version=a+b", "literal '+'")]
    [InlineData("version=a+b", "literal '+'")]
    [InlineData("versionDescriptor.version=a+b", "literal '+'")]
    [InlineData("version=a%2Bb&versionDescriptor.version=a%2Bb", "is not a commit hash")]
    public void AVersionValueWhoseDecodingIsParserDependent_IsNotAttributable(
        string query, string reason)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?versionType=commit&{{{query}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains(reason, result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>version</c> alone does not say what it selects. Azure reads it against
    /// <c>versionType</c>, which defaults to <c>branch</c>, so a branch and a tag of one name are
    /// two different contents behind one spelling — and, before this, behind one cache identity.
    /// Measured against a live repository: <c>main</c> as a branch returned 200 and as a tag 404.
    /// Only an immutable selector is attributable.
    /// </summary>
    /// <remarks>
    /// Requiring <c>versionType=commit</c> refuses no map a supported generator produces. Both
    /// <c>Microsoft.SourceLink.AzureRepos.Git</c> and <c>Microsoft.SourceLink.AzureDevOpsServer.Git</c>
    /// emit <c>?api-version=1.0&amp;versionType=commit&amp;version={sha}&amp;path=/*</c>, which the
    /// last row pins as the accept case.
    /// </remarks>
    [Theory]
    [InlineData("versionType=branch&version=main", false)]
    [InlineData("versionType=tag&version=main", false)]
    [InlineData("version=main", false)]
    [InlineData("versionType=commit&version=main", false)]
    // A ref may be named with hex characters, so a branch can be named after a real commit hash.
    // Requiring the hash alone would report that hash while Azure served the branch's content,
    // because versionType=branch makes the branch win.
    [InlineData("versionType=branch&version=$SHA", false)]
    [InlineData("versionDescriptor.versionType=tag&version=$SHA", false)]
    [InlineData("versionType=commit&versionDescriptor.versionType=branch&version=$SHA", false)]
    [InlineData("api-version=1.0&versionType=commit&version=$SHA", true)]
    public void AnAzureRevisionThatIsNotAnImmutableCommit_IsNotAttributable(
        string query, bool established)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?{{{query.Replace("$SHA", Sha, StringComparison.Ordinal)}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.Equal(established, result.IsEstablished);
    }

    /// <summary>
    /// <c>versionOptions</c> moves the selection off the named commit: <c>previousChange</c> and
    /// <c>firstParent</c> each serve a different commit's content under an unchanged
    /// <c>version</c>, so the reported revision would not be the one fetched.
    /// </summary>
    [Theory]
    [InlineData("versionOptions=previousChange", false)]
    [InlineData("versionOptions=firstParent", false)]
    [InlineData("versionDescriptor.versionOptions=previousChange", false)]
    [InlineData("versionOptions=none", true)]
    public void AnAzureVersionOptionThatMovesOffTheNamedCommit_IsNotAttributable(
        string query, bool established)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?versionType=commit&version={{{Sha}}}&{{{query}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.Equal(established, result.IsEstablished);
    }

    /// <summary>
    /// The Azure path must end at the <c>items</c> endpoint. Matching only
    /// <c>/_apis/git/repositories/{repo}</c> attributed endpoints that ignore <c>version</c>
    /// entirely — the repository-metadata endpoint returned byte-identical content for every
    /// revision supplied, so any revision could be reported for content that has none.
    /// </summary>
    [Theory]
    [InlineData("/contoso/widgets/_apis/git/repositories/core", false)]
    [InlineData("/contoso/widgets/_apis/git/repositories/core/pullRequests/1", false)]
    [InlineData("/contoso/widgets/_apis/git/repositories/core/items/extra", false)]
    [InlineData("/contoso/widgets/_apis/git/repositories/core/items", true)]
    public void AnAzureUrlThatIsNotTheItemsEndpoint_IsNotAttributable(string path, bool established)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com{{{path}}}?versionType=commit&version={{{Sha}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.Equal(established, result.IsEstablished);
    }

    /// <summary>
    /// The segments before <c>_apis</c> are the host's route, so their count is part of the
    /// grammar rather than free text to join into an organization name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Joining whatever preceded <c>_apis</c> reported an organization that was assembled rather
    /// than read. A project-less <c>dev.azure.com/{org}/_apis/...</c> was attributed to
    /// <c>{org}</c> at a commit, and <c>dev.azure.com/a/b/c/_apis/...</c> to the organization
    /// <c>a/b/c</c> with the repository page <c>https://dev.azure.com/a/b/c/_git/{repo}</c>,
    /// which is not a page. Raised in review with a live request.
    /// </para>
    /// <para>
    /// Measured against <c>dev.azure.com/dnceng-public/public</c> at commit
    /// <c>af56d96fdbd7c26e9fc94336b6f50dcc6ceff484</c>, so the rows refuse nothing the host
    /// serves: the two-segment shape returns 200, the project-less shape redirects to a sign-in
    /// page on <c>spsprodcus4.vssps.visualstudio.com</c>, a wrong project and a wrong
    /// organization each redirect the same way — the route really is keyed on both — and an extra
    /// segment returns 404.
    /// </para>
    /// <para>
    /// They also refuse nothing a generator emits, which
    /// <c>EveryUrlTheReferenceGeneratorEmits_IsAttributable</c> checks directly against the
    /// reference's own asserted output. <c>AzureDevOpsUrlParser.TryParseHostedHttp</c> builds the
    /// project path as <c>{account}/{project}</c> off <c>dev.azure.com</c> and as
    /// <c>{project}</c> off a <c>*.visualstudio.com</c> host, dropping the team and trimming
    /// <c>DefaultCollection</c>, and <c>GetSourceLinkUrl</c> appends
    /// <c>_apis/git/repositories/{repo}/items</c> to exactly that.
    /// </para>
    /// </remarks>
    [Theory]
    // The shape both hosted generators emit.
    [InlineData("https://dev.azure.com/contoso/widgets", true)]
    // Project-less: a real Items route, but one the host answers with a sign-in page.
    [InlineData("https://dev.azure.com/contoso", false)]
    // More than the route names; the surplus was being folded into the organization.
    [InlineData("https://dev.azure.com/contoso/widgets/extra", false)]
    [InlineData("https://dev.azure.com", false)]
    // The account is the host label on the legacy spelling, so the route is one segment shorter.
    [InlineData("https://contoso.visualstudio.com/widgets", true)]
    [InlineData("https://contoso.visualstudio.com/widgets/extra", false)]
    [InlineData("https://contoso.visualstudio.com", false)]
    // DefaultCollection is an alias the host resolves to the same content — measured
    // byte-identical — so it is trimmed rather than counted as the extra segment it looks like.
    // The generator trims it too (VisualStudioHost_DefaultCollection), so nothing generated
    // carries it; this accepts a spelling an older generator could have emitted.
    [InlineData("https://contoso.visualstudio.com/DefaultCollection/widgets", true)]
    [InlineData("https://contoso.visualstudio.com/DefaultCollection", false)]
    // '/e/' is Azure's enterprise discovery page, which the generator refuses to emit at all.
    // Without the check it satisfies the segment count and reports the organization 'e'.
    [InlineData("https://dev.azure.com/e/contoso", false)]
    [InlineData("https://dev.azure.com/E/contoso", false)]
    public void AnAzureUrlWhoseSegmentsBeforeApisAreNotTheHostsRoute_IsNotAttributable(
        string prefix,
        bool established)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"{{{prefix}}}/_apis/git/repositories/core/items?api-version=1.0&versionType=commit&version={{{Sha}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.Equal(established, result.IsEstablished);
    }

        /// <summary>
    /// <c>DefaultCollection</c> is an alias for the same content, measured byte-identical against
    /// <c>dnceng-public.visualstudio.com/public</c> with and without it, so the two spellings name
    /// one origin and must not produce two cache identities.
    /// </summary>
    [Fact]
    public void TheLegacyCollectionSpelling_NamesTheSameOriginAsTheProjectAlone()
    {
        const string Url =
            "/_apis/git/repositories/core/items?api-version=1.0&versionType=commit&version=" +
            Sha + "&path=/*";

        var with = Determine(
            $$$"""{"documents":{"/_/*":"https://contoso.visualstudio.com/DefaultCollection/widgets{{{Url}}}"}}""",
            "/_/A.cs");
        var without = Determine(
            $$$"""{"documents":{"/_/*":"https://contoso.visualstudio.com/widgets{{{Url}}}"}}""",
            "/_/A.cs");

        Assert.True(with.IsEstablished, with.Reason);
        Assert.True(without.IsEstablished, without.Reason);
        Assert.Equal(without.Origin!.Value.Identity, with.Origin!.Value.Identity);
    }

    /// <summary>
    /// Every content URL the reference generator emits, on a host this reader admits, is
    /// attributable. This is the direction the negative rows cannot check: they say a wrong shape
    /// is refused, not that the right one still passes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rows are the exact URLs asserted by <c>dotnet/sourcelink</c> at <c>b989174</c>, with
    /// its placeholder account, project, repository and revision kept:
    /// <c>src/SourceLink.AzureRepos.Git.UnitTests/GetSourceLinkUrlTests.cs</c>
    /// (<c>RepoOnly</c>, <c>Project</c>, <c>Project_Team</c>,
    /// <c>VisualStudioHost_DefaultCollection</c>,
    /// <c>DevAzureCom_RepositoryName_WithDotGit_IsPreservedInOutput</c>) and
    /// <c>src/SourceLink.GitHub/GetSourceLinkUrl.cs</c>, whose <c>BuildSourceLinkUrl</c> composes
    /// <c>{contentUrl}/{owner}/{repo}/{revision}/*</c>.
    /// </para>
    /// <para>
    /// Taking them from the generator's own assertions rather than restating the grammar is the
    /// point: a shape this reader refuses that the generator emits is a false negative on a real
    /// assembly, and no amount of reasoning about the grammar detects one. Two of these rows are
    /// what say the team and the collection never reach a content URL, so refusing both spellings
    /// costs nothing.
    /// </para>
    /// </remarks>
    [Theory]
    // Project supplied; the account is a path segment.
    [InlineData("https://dev.azure.com/account/project/_apis/git/repositories/repo/items", "account/project", "repo")]
    // No project in the repository URL: the generator uses the repository name as the project.
    [InlineData("https://dev.azure.com/account/repo/_apis/git/repositories/repo/items", "account/repo", "repo")]
    // A team in the repository URL is dropped rather than emitted.
    [InlineData("https://dev.azure.com/account/project/_apis/git/repositories/repo/items", "account/project", "repo")]
    // A '.git' suffix is part of the repository name and is preserved.
    [InlineData("https://dev.azure.com/org/project/_apis/git/repositories/repo.git/items", "org/project", "repo.git")]
    // The legacy spelling: the account is the host label, so only the project precedes '_apis'.
    [InlineData("https://account.visualstudio.com/project/_apis/git/repositories/repo/items", "project", "repo")]
    // 'DefaultCollection' is trimmed by the generator, leaving the repository name as the project.
    [InlineData("https://account.visualstudio.com/repo/_apis/git/repositories/repo/items", "repo", "repo")]
    public void EveryUrlTheReferenceGeneratorEmits_IsAttributable(
        string url,
        string organization,
        string repository)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"{{{url}}}?api-version=1.0&versionType=commit&version={{{Sha}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.True(result.IsEstablished, result.Reason);
        Assert.Equal(organization, result.Origin!.Value.Organization);
        Assert.Equal(repository, result.Origin!.Value.Repository);
        Assert.Equal(Sha, result.Origin!.Value.Revision);
    }

    /// <summary>
    /// The one shape the reference generator can emit that this reader refuses: it preserves the
    /// scheme of the repository URL, so an <c>http</c> remote yields an <c>http</c> content URL
    /// (<c>RepoOnly</c> asserts one), and provenance read off cleartext is attributable to
    /// anyone on the path.
    /// </summary>
    /// <remarks>
    /// A deliberate divergence, recorded here rather than left as a silent gap. It costs nothing
    /// on the two hosts this reader admits — both serve https, and neither is reachable over
    /// cleartext — and <c>DotnetInspector.Core.HttpClientFactory.IsAllowedFetchScheme</c> would
    /// refuse to fetch such a URL regardless, so attributing it would report an origin no content
    /// can come from.
    /// </remarks>
    [Fact]
    public void AnHttpUrlTheGeneratorCouldEmit_IsNotAttributable()
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"http://dev.azure.com/account/project/_apis/git/repositories/repo/items?api-version=1.0&versionType=commit&version={{{Sha}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("not https", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The GitHub generator composes <c>{contentUrl}/{owner}/{repo}/{revision}/*</c>
    /// (<c>src/SourceLink.GitHub/GetSourceLinkUrl.cs</c> in <c>dotnet/sourcelink</c> at
    /// <c>b989174</c>), which is the shape this reader reads three segments off.
    /// </summary>
    [Fact]
    public void TheUrlTheGitHubGeneratorEmits_IsAttributable()
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com/owner/repo/{{{Sha}}}/*"}}""",
            "/_/A.cs");

        Assert.True(result.IsEstablished, result.Reason);
        Assert.Equal("owner", result.Origin!.Value.Organization);
        Assert.Equal("repo", result.Origin!.Value.Repository);
        Assert.Equal(Sha, result.Origin!.Value.Revision);
    }

    /// <summary>
    /// Query parameters are allow-listed. Azure's Items API takes several that change which
    /// content is returned, and it grows while this reader does not, so a name nobody has reasoned
    /// about cannot be assumed inert — it may select content the reported origin does not
    /// describe.
    /// </summary>
    [Theory]
    [InlineData("futureSelector=evil", false)]
    [InlineData("resolveLfs=true", false)]
    // 'scopePath' is deliberately absent: this theory's URL already carries 'path', and the two
    // together are refused as a pair by AnAzureUrlGivingBothContentSelectors_IsNotAttributable.
    // That it is a known parameter is proved by ASingleContentSelector_IsAttributable.
    [InlineData("api-version=7.1", true)]
    [InlineData("versionOptions=none", true)]
    public void AnAzureQueryParameterNobodyHasReasonedAbout_IsNotAttributable(
        string extra, bool established)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?versionType=commit&version={{{Sha}}}&path=/*&{{{extra}}}"}}""",
            "/_/A.cs");

        Assert.Equal(established, result.IsEstablished);
    }

    /// <summary>
    /// The host allow list is the set of hosts whose URL grammar this reader knows, not a trust
    /// boundary, and it is deliberately narrower than what SourceLink's generators emit. Both
    /// rows here are produced by official generators and both report no repository.
    /// </summary>
    /// <remarks>
    /// This is a scope boundary recorded as a decision, not an oversight. Admitting a host needs
    /// its own evidence — who operates the domain, and for an on-prem server where the virtual
    /// directory ends, which the URL does not state. "No repository" is what the invariant
    /// prescribes when an origin cannot be established, so refusing is conservative rather than
    /// wrong. This test exists so that widening the list is a visible choice.
    ///
    /// The on-prem row deliberately carries no port. An on-prem server usually does, but a port
    /// is refused earlier by the origin rule below, so a row carrying one would pass without
    /// ever reaching the host allow list this test is about.
    /// </remarks>
    [Theory]
    [InlineData("account.vsts.me/project")]
    [InlineData("contoso.com/tfs/collection/project")]
    public void AHostWhoseUrlGrammarIsNotKnown_ReportsNoRepositoryRatherThanAGuess(string prefix)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://{{{prefix}}}/_apis/git/repositories/core/items?api-version=1.0&versionType=commit&version={{{Sha}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("not a recognized source host", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// An origin is (scheme, host, port); the reader identifies a host by name alone. A port
    /// other than the scheme's default names a different service on the same machine, so
    /// attributing it reports a repository whose content it does not serve, and — because the
    /// identity is built from the host name — hands it the persistent cache identity of the real
    /// one. Both allow-listed hosts are affected, so both are covered.
    /// </summary>
    /// <remarks>
    /// The default-port rows are the non-vacuity half. An explicit <c>:443</c> is the same origin
    /// as no port at all and stays accepted, so the refusal has to come from the port differing
    /// rather than from any port being written down.
    /// </remarks>
    [Theory]
    [InlineData("", true)]
    [InlineData(":443", true)]
    [InlineData(":444", false)]
    [InlineData(":8443", false)]
    public void APortOtherThanTheSchemeDefault_IsADifferentOrigin(string port, bool established)
    {
        var github = Determine(
            $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com{{{port}}}/dotnet/runtime/{{{Sha}}}/*"}}""",
            "/_/A.cs");
        var azure = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com{{{port}}}/contoso/widgets/_apis/git/repositories/core/items?api-version=1.0&versionType=commit&version={{{Sha}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.Equal(established, github.IsEstablished);
        Assert.Equal(established, azure.IsEstablished);
    }

    /// <summary>
    /// An encoded separator is refused even inside Azure's repository segment, where a reviewer
    /// read it as a legitimate "repository folder". Azure DevOps has no repository folders and
    /// forbids <c>/</c> in a repository name, so no such repository exists to be refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generator does pass the sequence through — pointing a build at a remote of
    /// <c>.../_git/parent%2Frepo</c> emits
    /// <c>.../repositories/parent%2Frepo/items?...</c> verbatim — so the map shape is real even
    /// though the repository it names cannot be.
    /// </para>
    /// <para>
    /// Accepting it would also undercut the rule that the path must end at <c>items</c>. That
    /// rule is decided by splitting the path, and <c>%2F</c> survives canonicalization unchanged,
    /// so our split and the server's need not agree on where the repository segment ends or on
    /// which endpoint is addressed. The two rules have to hold together.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEncodedSeparatorInTheAzureRepositorySegment_IsNotAttributable()
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/org/project/_apis/git/repositories/parent%2Frepo/items?api-version=1.0&versionType=commit&version={{{Sha}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.False(result.IsEstablished);
        Assert.Contains("%2F", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A parameter that is present but empty is not absent. Reporting it as absent skipped the
    /// agreement check between a selector's flat and descriptor spellings entirely, so
    /// <c>versionType=commit&amp;versionDescriptor.versionType=</c> read as an unopposed
    /// <c>commit</c> while the host, which honours the descriptor, reads an empty selector as its
    /// default of <c>branch</c> — serving a <em>branch</em> named after the reported commit hash,
    /// which an attacker can point anywhere.
    /// </summary>
    /// <remarks>
    /// This is the same mistake the blank-map rule exists to prevent, one level down: only
    /// genuine absence may be treated as absence. A valueless parameter with no <c>=</c> at all
    /// is present too.
    /// </remarks>
    [Theory]
    [InlineData("versionType=commit&versionDescriptor.versionType=&version=$SHA", "empty value")]
    [InlineData("versionType=commit&versionDescriptor.versionType&version=$SHA", "no value at all")]
    [InlineData("versionType=commit&version=$SHA&versionDescriptor.version=", "empty value")]
    [InlineData("versionType=commit&version=$SHA&versionOptions=", "empty value")]
    [InlineData("versionType=commit&version=&version=$SHA", "repeats")]
    [InlineData("versionType=&version=$SHA", "empty value")]
    public void AnAzureSelectorThatIsPresentButEmpty_IsNotTreatedAsAbsent(string query, string reason)
    {
        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?{{{query.Replace("$SHA", Sha, StringComparison.Ordinal)}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.False(result.IsEstablished);

        // Asserting only that the URL is refused would let this pass for the wrong reason: with
        // the empty-value rule deleted, an empty selector reads as the value "", which every
        // downstream rule refuses anyway. The reason is what distinguishes "refused because the
        // parameter is present and says nothing" from "refused by a later rule".
        Assert.Contains(reason, result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a hex string is an object name is a property of the host's object format, not of
    /// the string. GitHub and Azure DevOps store SHA-1 repositories only, and Git will create a
    /// branch named with 64 hex characters, so on those hosts a 64-character revision cannot be
    /// a commit — it can only be a moving ref, whose head an attacker with push access moves
    /// while the reported revision and the persistent cache identity stay put. Accepting the
    /// SHA-256 length "for when it ships" would attribute that ref today.
    /// </summary>
    /// <remarks>
    /// The 40-character rows are the non-vacuity half: the refusal has to come from the length
    /// being wrong for the host, not from hex revisions having stopped resolving.
    /// </remarks>
    [Theory]
    [InlineData(40, true)]
    [InlineData(64, false)]
    public void ASixtyFourHexRevisionOnASha1Host_IsNotACommit(int length, bool established)
    {
        string revision = new('a', length);

        var github = Determine(
            $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com/dotnet/runtime/{{{revision}}}/*"}}""",
            "/_/A.cs");
        var azure = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?api-version=1.0&versionType=commit&version={{{revision}}}&path=/*"}}""",
            "/_/A.cs");

        Assert.Equal(established, github.IsEstablished);
        Assert.Equal(established, azure.IsEstablished);
    }

    /// <summary>
    /// <c>BrowseUrl</c> dresses a resolved raw-content URL up as a <c>github.com</c> link, so it
    /// makes the same claim the origin reader does — that the content came from the repository
    /// the link names — in the one form a user is most likely to trust and click. It is therefore
    /// held to the same rule: a URL with no attributable GitHub origin gets no browse link, and
    /// the caller shows the resolved URL itself.
    /// </summary>
    /// <remarks>
    /// The first row is the non-vacuity half. Without it, every rule below would be satisfied by a
    /// method that returned <c>null</c> unconditionally.
    /// </remarks>
    [Theory]
    // Attributable: the browse link names exactly the origin the content is fetched from.
    [InlineData(
        "https://raw.githubusercontent.com/dotnet/runtime/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/src/A.cs",
        "https://github.com/dotnet/runtime/blob/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/src/A.cs")]
    // Traverses out of the repository it appears to name: the fetch lands in attacker/evil.
    [InlineData(
        "https://raw.githubusercontent.com/dotnet/runtime/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/../../../attacker/evil/main/A.cs",
        null)]
    // Encoded separators survive canonicalization, so our reading and the server's may differ.
    [InlineData(
        "https://raw.githubusercontent.com/dotnet/runtime/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/..%2f..%2fattacker/evil/A.cs",
        null)]
    // A credential makes the response depend on the identity presented, not the path named.
    [InlineData(
        "https://token@raw.githubusercontent.com/dotnet/runtime/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/src/A.cs",
        null)]
    // A different port is a different origin.
    [InlineData(
        "https://raw.githubusercontent.com:444/dotnet/runtime/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/src/A.cs",
        null)]
    // A moving ref, not a commit, so the link would not name what was fetched.
    [InlineData(
        "https://raw.githubusercontent.com/dotnet/runtime/main/src/A.cs",
        null)]
    // Not GitHub raw content at all; there is no github.com URL for it.
    [InlineData(
        "https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?api-version=1.0&versionType=commit&version=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&path=/src/A.cs",
        null)]
    [InlineData("http://raw.githubusercontent.com/dotnet/runtime/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/src/A.cs", null)]
    [InlineData("not a url", null)]
    [InlineData(null, null)]
    public void ABrowseLink_IsOnlyOfferedForAnAttributableGitHubOrigin(string? resolvedUrl, string? expected)
        => Assert.Equal(expected, SLF.SourceLinkProvenance.BrowseUrl(resolvedUrl));

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
    /// The identity is a cache key for a persistent source index, so two distinct origins sharing
    /// one identity serve one repository's source for another's assembly. Azure DevOps repository
    /// names and Git ref names may both contain <c>/</c> and <c>@</c> — <c>git check-ref-format</c>
    /// accepts <c>branch@tip</c> — so any delimiter-joined key is ambiguous. Varying only the owner
    /// does not exercise this; the parts have to be able to eat each other's delimiters.
    /// </summary>
    [Theory]
    [InlineData("repo@branch", "tip", "repo", "branch@tip")]
    [InlineData("a/b", "c", "a", "b/c")]
    [InlineData("repo", "a|b", "repo|a", "b")]
    [InlineData("repo", "4:x", "repo4", ":x")]
    public void TwoOriginsDifferingOnlyInWhereADelimiterFalls_DoNotShareOneIdentity(
        string leftRepository, string leftRevision, string rightRepository, string rightRevision)
    {
        var left = new SLF.SourceLinkOrigin("h", "org", leftRepository, leftRevision, "u");
        var right = new SLF.SourceLinkOrigin("h", "org", rightRepository, rightRevision, "u");

        Assert.NotEqual(left, right);
        Assert.NotEqual(left.Identity, right.Identity);
    }

    /// <summary>
    /// Azure's Items API accepts the revision as the flat <c>version</c> parameter and as
    /// <c>versionDescriptor.version</c>, and the descriptor is the one the host honours. Reading
    /// only <c>version</c> reported the losing selector, so a URL carrying both named one revision
    /// while fetching the other.
    /// </summary>
    [Theory]
    [InlineData("version=$SHA&versionDescriptor.version=$OTHER", null)]
    [InlineData("versionDescriptor.version=$OTHER", "$OTHER")]
    [InlineData("version=$SHA&versionDescriptor.version=$SHA", "$SHA")]
    [InlineData("version=$SHA", "$SHA")]
    public void TheAzureRevision_IsTheSelectorTheHostHonours(string query, string? expected)
    {
        query = query.Replace("$SHA", Sha, StringComparison.Ordinal)
            .Replace("$OTHER", OtherSha, StringComparison.Ordinal);
        expected = expected?.Replace("$SHA", Sha, StringComparison.Ordinal)
            .Replace("$OTHER", OtherSha, StringComparison.Ordinal);

        var result = Determine(
            $$$"""{"documents":{"/_/*":"https://dev.azure.com/contoso/widgets/_apis/git/repositories/core/items?versionType=commit&{{{query}}}&path=/*"}}""",
            "/_/A.cs");

        if (expected is null)
        {
            Assert.False(result.IsEstablished);
            Assert.Contains("versionDescriptor.version", result.Reason, StringComparison.Ordinal);
        }
        else
        {
            Assert.True(result.IsEstablished, result.Reason);
            Assert.Equal(expected, result.Origin!.Value.Revision);
        }
    }

    /// <summary>
    /// A SourceLink payload that is present but blank is not the same as an assembly that ships no
    /// SourceLink. Returning the empty resolver for it recreates the success-shaped emptiness the
    /// repository's failure-visibility rule forbids: a truncated or blanked-out map would be
    /// indistinguishable from absence.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void APresentButBlankMap_SaysSoRatherThanLookingLikeNoSourceLink(string payload)
    {
        var present = SLF.SourceLinkResolver.Parse(payload);

        Assert.NotNull(present.ParseError);
        Assert.Null(SLF.SourceLinkResolver.Parse(null).ParseError);
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
