using DotnetInspector.Packages;
using QuerySpace;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public partial class PackageQueryTests
{
    [Theory]
    [InlineData("aspire")]
    [InlineData("Aspire")]
    [InlineData("ecosystem.aspire")]
    [InlineData("  ECOSYSTEM.Aspire ")]
    public void EcosystemPopulation_CanonicalizesShortAndCanonicalIdentities(
        string spelling)
    {
        PackageQueryEcosystemMembershipCatalog catalog = EcosystemCatalog(
            Ecosystem("ecosystem.aspire", ["Aspire.Hosting"], ["Aspire."]));

        PackageQueryPlan plan = Accepted(PackageQuery.PlanEcosystemInput(
            spelling,
            catalog,
            maximumMatches: null));

        Assert.Equal("ecosystem.aspire", plan.Ecosystem?.Value);
        Assert.Equal("ecosystem.aspire", plan.Prefix.ToString());
        Assert.Null(plan.PackageInput);
        Assert.Contains(
            plan.Intent.Terms,
            term => term.Key == PackageQuery.EcosystemTermKey
                && term.Value == "ecosystem.aspire");
        Assert.Empty(plan.Terms);
        PackageQueryPlan canonical = Accepted(PackageQuery.PlanEcosystemInput(
            "ecosystem.aspire",
            catalog,
            maximumMatches: null));
        Assert.Equal(
            PortableQueryPayloadCodec.Encode(
                canonical.Intent,
                TestContext.Current.CancellationToken),
            PortableQueryPayloadCodec.Encode(
                plan.Intent,
                TestContext.Current.CancellationToken));
        Assert.Equal(PackageQuery.DefaultMaximumCandidates, plan.MaximumCandidates);
    }

    [Fact]
    public void EcosystemPopulation_IsExclusiveWithOtherPopulations()
    {
        PackageQueryEcosystemMembershipCatalog catalog = EcosystemCatalog(
            Ecosystem("ecosystem.aspire", ["Aspire.Hosting"], ["Aspire."]),
            Ecosystem("ecosystem.ai", ["Microsoft.Extensions.AI"]));

        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleTerms,
            Rejected(PackageQuery.PlanInput(
                "Contoso.*",
                catalog,
                [Term(PackageQuery.EcosystemTermKey, "aspire")])).Reason);
        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleTerms,
            Rejected(PackageQuery.PlanInput(
                "Contoso.Package",
                catalog,
                [Term(PackageQuery.EcosystemTermKey, "ecosystem.aspire")])).Reason);
        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleTerms,
            Rejected(PackageQuery.PlanEcosystemInput(
                "aspire",
                catalog,
                [Term(PackageQuery.PrefixTermKey, "Contoso.")])).Reason);
        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleTerms,
            Rejected(PackageQuery.PlanEcosystemInput(
                "aspire",
                catalog,
                [Term(PackageQuery.EcosystemTermKey, "ai")])).Reason);

        // The same Ecosystem in both spellings is one population.
        PackageQueryPlan duplicate = Accepted(PackageQuery.PlanEcosystemInput(
            "aspire",
            catalog,
            [Term(PackageQuery.EcosystemTermKey, "ecosystem.aspire")]));
        Assert.Equal("ecosystem.aspire", duplicate.Ecosystem?.Value);
    }

    [Fact]
    public void EcosystemPopulation_RejectsMalformedUnknownAndPopulationLess()
    {
        PackageQueryEcosystemMembershipCatalog catalog = EcosystemCatalog(
            Ecosystem("ecosystem.platform"));

        foreach (string malformed in new[] { "", "Aspire!", "ecosystem.", "Kestrel" })
        {
            PackageQueryRequestFailure invalid = Rejected(
                PackageQuery.PlanEcosystemInput(malformed, catalog));
            Assert.Equal(
                PackageQueryRequestFailureReason.InvalidEcosystem,
                invalid.Reason);
            Assert.Equal([PackageQuery.EcosystemTermKey], invalid.TermKeys);
        }

        PackageQueryRequestFailure unknown = Rejected(
            PackageQuery.PlanEcosystemInput("unknown", catalog));
        Assert.Equal(
            PackageQueryRequestFailureReason.UnknownEcosystem,
            unknown.Reason);
        Assert.Equal("ecosystem.unknown", unknown.EcosystemId);
        Assert.Equal([PackageQuery.EcosystemTermKey], unknown.TermKeys);

        PackageQueryRequestFailure unbound = Rejected(
            PackageQuery.PlanEcosystemInput("Platform", catalog));
        Assert.Equal(
            PackageQueryRequestFailureReason
                .EcosystemPackagePopulationUnavailable,
            unbound.Reason);
        Assert.Equal("ecosystem.platform", unbound.EcosystemId);
    }

    [Fact]
    public async Task EcosystemPopulation_PortableIntentWithoutCatalogFailsBeforeSourceWork()
    {
        PackageQueryPlan catalogPlan = Accepted(PackageQuery.PlanEcosystemInput(
            "aspire",
            EcosystemCatalog(Ecosystem("ecosystem.aspire", ["Aspire.Hosting"])),
            maximumMatches: null));
        PackageQueryPlan portable = Accepted(
            PackageQuery.ResolveIntent(
                catalogPlan.Intent,
                TestContext.Current.CancellationToken));
        var source = new EcosystemPackageSource();

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CollectAsync(PackageQuery.ExecuteAsync(
                    source,
                    portable,
                    TestContext.Current.CancellationToken)));

        Assert.Contains(
            "ecosystem-membership binding",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(source.VersionRequests);
        Assert.Empty(source.PrefixSearches);
    }

    [Fact]
    public async Task EcosystemPopulation_AdmitsCoreThenPrefixesOnceInOrder()
    {
        var source = new EcosystemPackageSource
        {
            Versions = { "Contoso.Core", "Contoso.Hosting" },
            Prefixes =
            {
                ["Contoso."] = ["contoso.core", "Contoso.Extra"],
                ["Fabrikam."] = ["Fabrikam.One"],
            },
        };
        PackageQueryPlan plan = Accepted(PackageQuery.PlanEcosystemInput(
            "contoso",
            EcosystemCatalog(Ecosystem(
                "ecosystem.contoso",
                ["Contoso.Core", "Contoso.Hosting"],
                ["Contoso.", "Fabrikam."])),
            maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch[] matches =
        [
            .. events.OfType<PackageQueryEvent.Match>()
                .Select(queryEvent => queryEvent.Value),
        ];
        Assert.Equal(
            ["Contoso.Core", "Contoso.Hosting", "Contoso.Extra", "Fabrikam.One"],
            matches.Select(match => match.Package.PackageId));
        Assert.Equal(
            [
                "exact package Contoso.Core",
                "exact package Contoso.Hosting",
                "package prefix Contoso.",
                "package prefix Fabrikam.",
            ],
            matches.Select(match => EvidenceProperty(
                match,
                PackageQuery.EcosystemEvidenceId,
                "basis")));
        Assert.All(matches, match =>
        {
            PackageQueryEvidence scope = match.Evidence[0];
            Assert.Equal(PackageQuery.EcosystemEvidenceId, scope.Id);
            Assert.Equal(PackageQueryEvidenceScope.Query, scope.Scope);
            Assert.Equal(
                "ecosystem.contoso",
                EvidenceProperty(scope, "ecosystem"));
            Assert.Null(match.Package.Manifest);
        });
        Assert.Equal(["Contoso.Core", "Contoso.Hosting"], source.VersionRequests);
        Assert.Equal(
            [("Contoso.", 200), ("Fabrikam.", 197)],
            source.PrefixSearches);
        Assert.Empty(source.ManifestRequests);
        PackageQuerySummary summary = Assert.IsType<PackageQueryEvent.Completed>(
            events[^1]).Value;
        Assert.Equal("ecosystem.contoso", summary.Prefix.ToString());
        Assert.Equal(4, summary.Candidates);
        Assert.Equal(4, summary.Matches);
        Assert.Equal(PackageQueryCompletionKind.Exhausted, summary.Completion);
    }

    [Fact]
    public async Task EcosystemPopulation_SharedCandidateLimitStopsLaterSources()
    {
        var source = new EcosystemPackageSource
        {
            Versions = { "Contoso.Core", "Other.Core" },
            Prefixes =
            {
                ["Contoso."] = ["Contoso.Core", "Contoso.A", "Contoso.B"],
                ["Fabrikam."] = ["Fabrikam.One"],
            },
        };
        PackageQueryPlan plan = Accepted(PackageQuery.PlanEcosystemInput(
            "contoso",
            EcosystemCatalog(Ecosystem(
                "ecosystem.contoso",
                ["Contoso.Core", "Other.Core"],
                ["Contoso.", "Fabrikam."])),
            maximumCandidates: 3,
            maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            ["Contoso.Core", "Other.Core", "Contoso.A"],
            events.OfType<PackageQueryEvent.Match>()
                .Select(queryEvent => queryEvent.Value.Package.PackageId));
        // One remaining candidate plus the one already-admitted core ID.
        Assert.Equal([("Contoso.", 2)], source.PrefixSearches);
        PackageQuerySummary summary = Assert.IsType<PackageQueryEvent.Completed>(
            events[^1]).Value;
        Assert.Equal(3, summary.Candidates);
        Assert.Equal(
            PackageQueryCompletionKind.CandidateLimitReached,
            summary.Completion);
    }

    [Fact]
    public async Task EcosystemPopulation_CoreLimitSkipsRemainingCoreAndPrefixes()
    {
        var source = new EcosystemPackageSource
        {
            Versions = { "Contoso.A", "Contoso.B", "Contoso.C" },
        };
        PackageQueryPlan plan = Accepted(PackageQuery.PlanEcosystemInput(
            "contoso",
            EcosystemCatalog(Ecosystem(
                "ecosystem.contoso",
                ["Contoso.A", "Contoso.B", "Contoso.C"],
                ["Contoso."])),
            maximumCandidates: 2,
            maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(["Contoso.A", "Contoso.B"], source.VersionRequests);
        Assert.Empty(source.PrefixSearches);
        Assert.Equal(
            PackageQueryCompletionKind.CandidateLimitReached,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Completion);
    }

    [Fact]
    public async Task EcosystemPopulation_WithoutPrefixesCompletesExhaustedAfterCore()
    {
        var source = new EcosystemPackageSource
        {
            Versions = { "Contoso.A", "Contoso.B" },
        };
        PackageQueryPlan plan = Accepted(PackageQuery.PlanEcosystemInput(
            "contoso",
            EcosystemCatalog(Ecosystem(
                "ecosystem.contoso",
                ["Contoso.A", "Contoso.B", "Contoso.Missing"])),
            maximumCandidates: 2,
            maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            ["Contoso.A", "Contoso.B"],
            events.OfType<PackageQueryEvent.Match>()
                .Select(queryEvent => queryEvent.Value.Package.PackageId));
        Assert.Empty(source.PrefixSearches);
        PackageQuerySummary summary = Assert.IsType<PackageQueryEvent.Completed>(
            events[^1]).Value;
        Assert.Equal(2, summary.Candidates);
        Assert.Equal(
            PackageQueryCompletionKind.CandidateLimitReached,
            summary.Completion);

        source.VersionRequests.Clear();
        PackageQueryPlan roomy = Accepted(PackageQuery.PlanEcosystemInput(
            "contoso",
            EcosystemCatalog(Ecosystem(
                "ecosystem.contoso",
                ["Contoso.A", "Contoso.B", "Contoso.Missing"])),
            maximumCandidates: 5,
            maximumMatches: null));
        List<PackageQueryEvent> exhausted = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                roomy,
                TestContext.Current.CancellationToken));

        // An absent core package is not a candidate and never falls back to search.
        Assert.Equal(
            ["Contoso.A", "Contoso.B", "Contoso.Missing"],
            source.VersionRequests);
        Assert.Empty(source.PrefixSearches);
        PackageQuerySummary exhaustedSummary =
            Assert.IsType<PackageQueryEvent.Completed>(exhausted[^1]).Value;
        Assert.Equal(2, exhaustedSummary.Candidates);
        Assert.Equal(
            PackageQueryCompletionKind.Exhausted,
            exhaustedSummary.Completion);
    }

    [Fact]
    public async Task EcosystemPopulation_UnresolvableRootFailsAsThatCandidateOnly()
    {
        var source = new EcosystemPackageSource
        {
            Versions = { "Contoso.A", "Contoso.B" },
            FailedVersions = { "Contoso.Broken" },
            Prefixes = { ["Contoso."] = ["Contoso.C"] },
        };
        PackageQueryPlan plan = Accepted(PackageQuery.PlanEcosystemInput(
            "contoso",
            EcosystemCatalog(Ecosystem(
                "ecosystem.contoso",
                ["Contoso.A", "Contoso.Broken", "Contoso.B"],
                ["Contoso."])),
            maximumCandidates: 10,
            maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            ["Contoso.A", "Contoso.B", "Contoso.C"],
            events.OfType<PackageQueryEvent.Match>()
                .Select(queryEvent => queryEvent.Value.Package.PackageId));
        PackageQueryFailure failure = Assert.Single(
            events.OfType<PackageQueryEvent.Failure>()).Value;
        Assert.Equal("Contoso.Broken", failure.PackageId);
        PackageQuerySummary summary = Assert.IsType<PackageQueryEvent.Completed>(
            events[^1]).Value;
        Assert.Equal(4, summary.Candidates);
        Assert.Equal(1, summary.Failures);
        Assert.NotEqual(PackageQueryCompletionKind.Failed, summary.Completion);
    }

    [Fact]
    public async Task EcosystemPopulation_PrefixPageLimitOutranksCandidateLimit()
    {
        var source = new EcosystemPackageSource
        {
            Versions = { "Contoso.Core" },
            Prefixes =
            {
                ["Contoso."] = ["Contoso.A"],
                ["Fabrikam."] = ["Fabrikam.A", "Fabrikam.B", "Fabrikam.C"],
            },
            Truncation =
            {
                ["Contoso."] = PackageSearchTruncationReason.SourcePageLimit,
            },
        };
        PackageQueryPlan plan = Accepted(PackageQuery.PlanEcosystemInput(
            "contoso",
            EcosystemCatalog(Ecosystem(
                "ecosystem.contoso",
                ["Contoso.Core"],
                ["Contoso.", "Fabrikam."])),
            maximumCandidates: 4,
            maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            ["Contoso.Core", "Contoso.A", "Fabrikam.A", "Fabrikam.B"],
            events.OfType<PackageQueryEvent.Match>()
                .Select(queryEvent => queryEvent.Value.Package.PackageId));
        Assert.Equal(
            PackageQueryCompletionKind.SourcePageLimitReached,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Completion);
    }

    [Fact]
    public async Task EcosystemPopulation_PrefixSearchFailureFailsTheQueryAndLaterSourcesRun()
    {
        var source = new EcosystemPackageSource
        {
            Versions = { "Contoso.Core" },
            Prefixes = { ["Fabrikam."] = ["Fabrikam.One"] },
            FailedPrefixes = { "Contoso." },
        };
        PackageQueryPlan plan = Accepted(PackageQuery.PlanEcosystemInput(
            "contoso",
            EcosystemCatalog(Ecosystem(
                "ecosystem.contoso",
                ["Contoso.Core"],
                ["Contoso.", "Fabrikam."])),
            maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            ["Contoso.Core", "Fabrikam.One"],
            events.OfType<PackageQueryEvent.Match>()
                .Select(queryEvent => queryEvent.Value.Package.PackageId));
        Assert.Equal(
            PackageQueryFailureKind.Search,
            Assert.Single(events.OfType<PackageQueryEvent.Failure>()).Value.Kind);
        Assert.Equal(
            PackageQueryCompletionKind.Failed,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Completion);
    }

    [Fact]
    public async Task EcosystemPopulation_ManifestTermsUseTheMergedPopulation()
    {
        var source = new EcosystemPackageSource
        {
            Versions = { "Contoso.Core" },
            Prefixes = { ["Contoso."] = ["Contoso.Core", "Contoso.Extra", "Contoso.Plain"] },
            Manifests =
            {
                ["contoso.core@1.0.0"] = Manifest(
                    "Contoso.Core",
                    dependencies:
                    """<dependency id="Example.Dependency" version="1.0.0" />"""),
                ["contoso.extra@1.0.0"] = Manifest(
                    "Contoso.Extra",
                    dependencies:
                    """<dependency id="Example.Dependency" version="2.0.0" />"""),
                ["contoso.plain@1.0.0"] = Manifest("Contoso.Plain"),
            },
        };
        PackageQueryPlan plan = Accepted(PackageQuery.PlanEcosystemInput(
            "ecosystem.contoso",
            EcosystemCatalog(Ecosystem(
                "ecosystem.contoso",
                ["Contoso.Core"],
                ["Contoso."])),
            [Term(PackageQuery.DependsTermKey, "Example.Dependency")],
            maximumMatches: null));
        Assert.True(plan.RequiresManifest);

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch[] matches =
        [
            .. events.OfType<PackageQueryEvent.Match>()
                .Select(queryEvent => queryEvent.Value),
        ];
        Assert.Equal(
            ["Contoso.Core", "Contoso.Extra"],
            matches.Select(match => match.Package.PackageId));
        Assert.All(matches, match =>
        {
            Assert.Equal(PackageQueryAcquisitionTier.Nuspec, match.Tier);
            Assert.Equal(PackageQuery.EcosystemEvidenceId, match.Evidence[0].Id);
        });
        Assert.Equal(
            ["exact package Contoso.Core", "package prefix Contoso."],
            matches.Select(match => EvidenceProperty(
                match,
                PackageQuery.EcosystemEvidenceId,
                "basis")));
        // The already-admitted core ID acquires no second manifest, and the
        // Ecosystem identity never becomes a package-ID prefix search.
        Assert.Equal(
            ["contoso.core@1.0.0", "contoso.extra@1.0.0", "contoso.plain@1.0.0"],
            source.ManifestRequests);
        Assert.Equal([("Contoso.", 200)], source.PrefixSearches);
        PackageQuerySummary summary = Assert.IsType<PackageQueryEvent.Completed>(
            events[^1]).Value;
        Assert.Equal(3, summary.Candidates);
        Assert.Equal(PackageQueryCompletionKind.Exhausted, summary.Completion);
    }

    /// <summary>
    /// A package source with authoritative per-ID version listings and
    /// per-prefix search results, recording each source operation.
    /// </summary>
    private sealed class EcosystemPackageSource : IPackageSourceClient
    {
        private readonly PackageSourceResultFactory _results =
            CreateResultFactory();

        public HashSet<string> Versions { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string[]> Prefixes { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, PackageSearchTruncationReason> Truncation
        {
            get;
        } = new(StringComparer.Ordinal);
        public HashSet<string> FailedPrefixes { get; } =
            new(StringComparer.Ordinal);
        public HashSet<string> FailedVersions { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, byte[]> Manifests { get; } =
            new(StringComparer.Ordinal);
        public List<string> VersionRequests { get; } = [];
        public List<(string Prefix, int Take)> PrefixSearches { get; } = [];
        public List<string> ManifestRequests { get; } = [];
        public PackageSourceResultIdentity Source => _results.Source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Search
            | PackageSourceCapabilities.VersionEnumeration
            | PackageSourceCapabilities.Manifest;

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VersionRequests.Add(packageId);
            if (FailedVersions.Contains(packageId))
            {
                return Task.FromResult(_results.FailedVersions(
                    PackageSourceFailureKind.Transport));
            }

            PackageCandidateObservation[] candidates = Versions.Contains(packageId)
                ?
                [
                    _results.Candidate(
                        PackageSourceCoordinate.Create(packageId, "1.0.0"),
                        PackageDiscoveryContract.CompleteVersionEnumeration,
                        PackageListingState.Listed),
                ]
                : [];
            return Task.FromResult(_results.SucceededVersions(
                _results.Versions(candidates, hasAuthoritativeListingState: true)));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrefixSearches.Add((prefix, take));
            if (FailedPrefixes.Contains(prefix))
            {
                return Task.FromResult(_results.FailedSearch(
                    PackageSourceFailureKind.Transport));
            }

            string[] ids = Prefixes.TryGetValue(prefix, out string[]? found)
                ? found
                : [];
            PackageSearchTruncationReason truncation = ids.Length > take
                ? PackageSearchTruncationReason.RequestedLimit
                : Truncation.GetValueOrDefault(prefix);
            return Task.FromResult(_results.SucceededSearch(
                _results.Search(
                    [.. ids.Take(take).Select(id => Match(id))],
                    truncation)));
        }

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(packageId, version);
            string key = $"{coordinate.PackageId}@{coordinate.Version}";
            ManifestRequests.Add(key);
            return Task.FromResult(
                Manifests.TryGetValue(key, out byte[]? content)
                    ? _results.SucceededManifest(
                        coordinate,
                        _results.Manifest(coordinate, content))
                    : _results.FailedManifest(
                        coordinate,
                        PackageSourceFailureKind.NotFound));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
