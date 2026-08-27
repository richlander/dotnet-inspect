namespace SourceLinkFetch;

public static partial class SourceLinkProvenance
{
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

        if (ContainsEncodedSeparator(url, out string encoded))
        {
            // Uri preserves these verbatim through canonicalization, so a canonicalize-then-check
            // step passes while a server that percent-decodes before resolving dot segments still
            // traverses out of the path this URL appears to name.
            rejection = $"it contains the encoded sequence '{encoded}', which canonicalization does not resolve";
            return false;
        }

        // The host is judged as Uri reports it, before CanonicalHost applies IDNA mapping. That
        // mapping deletes a soft hyphen and a zero-width space rather than preserving them, so a
        // check made only on the canonical form would silently sanitize a hostile host into a
        // clean one and attribute it -- the opposite of the reject-don't-sanitize rule the threat
        // model settles on, and a regression of the round-17 refusal this restates.
        //
        // "As Uri reports it" is the honest limit, not "as written": Uri itself removes U+202A
        // through U+202E from an authority before this code runs, measured. That is deliberately
        // not pinned here -- ALiveFormatCharacterInAHostLabel_IsNotAttributable says why -- and it
        // concedes nothing, because what Uri hands back carries no scalar that can act on a sink.
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
        ReadOnlySpan<char> pairs = QueryAfterDelimiter(query);
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
        ReadOnlySpan<char> pairs = QueryAfterDelimiter(query);
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

    private static ReadOnlySpan<char> QueryAfterDelimiter(string query)
    {
        ReadOnlySpan<char> pairs = query;
        return !pairs.IsEmpty && pairs[0] == '?' ? pairs[1..] : pairs;
    }

    /// <summary>
    /// Detects percent-encoded path separators, which survive <see cref="Uri"/> canonicalization.
    /// </summary>
    private static bool ContainsEncodedSeparator(string url, out string encoded)
    {
        for (int i = 0; i + 2 < url.Length; i++)
        {
            if (url[i] != '%')
            {
                continue;
            }

            ReadOnlySpan<char> pair = url.AsSpan(i + 1, 2);
            if (pair.Equals("2f", StringComparison.OrdinalIgnoreCase) ||
                pair.Equals("5c", StringComparison.OrdinalIgnoreCase))
            {
                encoded = url.Substring(i, 3);
                return true;
            }
        }

        encoded = "";
        return false;
    }
}
