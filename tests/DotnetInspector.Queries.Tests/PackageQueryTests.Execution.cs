using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Packages;
using QuerySpace;
using QuerySpace.Operations;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspector.Services;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public partial class PackageQueryTests
{
    [Fact]
    public async Task ExecuteAsync_FiltersBeforeMatchLimitAndStopsManifestAcquisition()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.One", totalDownloads: 1),
            Match("Contoso.Two", totalDownloads: 1_000_000),
            Match("Contoso.Three", totalDownloads: 1),
            Match("Contoso.Four", totalDownloads: 1),
            Match("Contoso.Five", totalDownloads: 1_000_000),
            Match("Contoso.Six", totalDownloads: 1_000_000),
        ];
        var source = new FakePackageSource(
            candidates,
            candidates.ToDictionary(
                candidate =>
                    $"{candidate.Id.ToLowerInvariant()}@1.0.0",
                candidate => Manifest(candidate.Id)));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.DownloadsTermKey, "1m")],
                    MaximumCandidates: 6,
                    MaximumMatches: 2)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            ["Contoso.Two", "Contoso.Five"],
            events.OfType<PackageQueryEvent.Match>()
                .Select(item => item.Value.Package.PackageId));
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(PackageQueryCompletionKind.MatchLimitReached, summary.Completion);
        Assert.Equal(5, summary.Candidates);
        Assert.Equal(2, summary.Matches);
        Assert.Empty(source.ManifestRequests);
        Assert.Equal(6, source.LastSearchTake);
        Assert.Equal(0, source.PackageRequests);
    }

    [Fact]
    public async Task ExecuteAsync_AbsentMatchLimitRunsAllThousandMatchesToCompletion()
    {
        SearchResult[] candidates =
        [
            .. Enumerable.Range(1, 1_000)
                .Select(index =>
                    Match(
                        $"Contoso.{index:D4}",
                        totalDownloads: 10_000)),
        ];
        var source = new FakePackageSource(
            candidates,
            candidates.ToDictionary(
                candidate =>
                    $"{candidate.Id.ToLowerInvariant()}@1.0.0",
                candidate => Manifest(candidate.Id)));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.DownloadsTermKey, "10k")],
                    MaximumCandidates: 1_000,
                    MaximumMatches: null)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            candidates.Select(candidate => candidate.Id),
            events.OfType<PackageQueryEvent.Match>()
                .Select(item => item.Value.Package.PackageId));
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Null(summary.MatchLimit);
        Assert.Equal(1_000, summary.Matches);
        Assert.Equal(
            PackageQueryCompletionKind.Exhausted,
            summary.Completion);
        Assert.Empty(source.ManifestRequests);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsProductOrderedEvidenceForEverySelectedFacet()
    {
        var source = new FakePackageSource(
            [
                Match(
                    "Contoso.Tool",
                    verified: true,
                    totalDownloads: 1_500_000),
            ],
            new Dictionary<string, byte[]>
            {
                ["contoso.tool@1.0.0"] = Manifest(
                    "Contoso.Tool",
                    dependencies:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Example.Dependency" version="[1.0.0]" />
                    </group>
                    """,
                    packageTypes:
                    """
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                    """,
                    readme: "<readme>README.md</readme>"),
            });
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Tool"] = new FakePackageContent(
                    ("tools/net8.0/any/DotnetToolSettings.xml",
                        "<DotNetCliTool Version=\"2\"><Commands /></DotNetCliTool>")),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(PackageQuery.DependsTermKey, "Example.Dependency"),
                        Term(PackageQuery.DownloadsTermKey, "1m"),
                        Term(PackageQuery.ReadmeTermKey, "true"),
                        Term(PackageQuery.ToolFormatTermKey, "v2"),
                    ],
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal(PackageQueryAcquisitionTier.PackageContent, match.Tier);
        Assert.Equal(
            [
                PackageQuery.PrefixEvidenceId,
                PackageQuery.DependsTermKey,
                PackageQuery.DownloadsTermKey,
                PackageQuery.ReadmeTermKey,
                PackageQuery.ToolFormatTermKey,
            ],
            match.Evidence.Select(evidence => evidence.Id));
        Assert.Equal(
            ["Example.Dependency", "1m", "true", "v2"],
            match.Answers.Select(answer => answer.Value));
        Assert.Equal(
            "Contoso.",
            EvidenceProperty(match.Evidence[0], "prefix"));
        Assert.Equal(
            "net8.0: Example.Dependency [1.0.0]",
            Assert.Single(match.Evidence[1].Summary!.Preview).ToString());
        Assert.Equal(PackageQueryEvidenceScope.Query, match.Evidence[0].Scope);
        Assert.All(match.Evidence.Skip(1), evidence =>
            Assert.Equal(PackageQueryEvidenceScope.Package, evidence.Scope));
        Assert.Equal(1, Assert.IsType<PackageQueryEvidenceSummary>(
            match.Evidence[1].Summary).Count);
        Assert.Equal(1_500_000, match.Evidence[2].Number);
        Assert.Equal(
            "2",
            EvidenceProperty(match.Evidence[4], "settings-version"));
        Assert.Equal(["Contoso.Tool"], content.Requests);
        Assert.All(
            match.Evidence,
            evidence =>
            {
                Assert.True(
                    evidence.Properties.Length > 0
                    || evidence.Number is not null
                    || evidence.Summary is not null);
            });
    }

    [Fact]
    public async Task ExecuteAsync_RequiresEverySelectedTerm()
    {
        var source = new FakePackageSource(
            [
                Match("Contoso.Tool", totalDownloads: 100_000),
                Match("Contoso.Library", totalDownloads: 100_000),
                Match("Contoso.LowDownloadTool"),
            ],
            new Dictionary<string, byte[]>
            {
                ["contoso.tool@1.0.0"] = Manifest(
                    "Contoso.Tool",
                    packageTypes:
                    """
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                    """),
                ["contoso.library@1.0.0"] = Manifest(
                    "Contoso.Library"),
                ["contoso.lowdownloadtool@1.0.0"] = Manifest(
                    "Contoso.LowDownloadTool",
                    packageTypes:
                    """
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                    """),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(PackageQuery.DownloadsTermKey, "100k"),
                        Term(PackageQuery.ToolTermKey, "true"),
                    ],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match = Assert.Single(
            events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("Contoso.Tool", match.Package.PackageId);
        Assert.Equal(
            [
                PackageQuery.PrefixEvidenceId,
                PackageQuery.DownloadsTermKey,
                PackageQuery.ToolTermKey,
            ],
            match.Evidence.Select(evidence => evidence.Id));
        Assert.Equal("true", Assert.Single(match.Answers,
            answer => answer.Id == PackageQuery.ToolTermKey).Value);
        Assert.Equal(
            "DotnetTool",
            EvidenceProperty(match.Evidence[^1], "package-type"));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsCloseTermNegatives()
    {
        await AssertNoMatchesAsync(
            new FakePackageSource(
                [Match("Contoso.Downloads", totalDownloads: 999_999)],
                new Dictionary<string, byte[]>
                {
                    ["contoso.downloads@1.0.0"] =
                        Manifest("Contoso.Downloads"),
                }),
            Term(PackageQuery.DownloadsTermKey, "1m"));

        await AssertNoMatchesAsync(
            SourceFor(Manifest("Contoso.NoReadme"), "Contoso.NoReadme"),
            Term(PackageQuery.ReadmeTermKey, "true"));

        await AssertNoMatchesAsync(
            SourceFor(
                Manifest(
                    "Contoso.BlankReadme",
                    readme: "<readme> </readme>"),
                "Contoso.BlankReadme"),
            Term(PackageQuery.ReadmeTermKey, "true"));

        await AssertNoMatchesAsync(
            SourceFor(
                Manifest(
                    "Contoso.Dependent",
                    dependencies:
                    """
                    <dependency id="Example.Dependency" version="[1.0.0]" />
                    """),
                "Contoso.Dependent"),
            Term(PackageQuery.DependenciesTermKey, "none"));

        await AssertNoMatchesAsync(
            SourceFor(
                Manifest(
                    "Contoso.NotTool",
                    packageTypes:
                    """
                    <packageTypes>
                      <packageType name="DotnetTooling" />
                    </packageTypes>
                    """),
                "Contoso.NotTool"),
            Term(PackageQuery.ToolTermKey, "true"));
    }

    [Fact]
    public async Task ExecuteAsync_MillionDownloadsIncludesExactThreshold()
    {
        var source = new FakePackageSource(
            [Match("Contoso.Downloads", totalDownloads: 1_000_000)],
            new Dictionary<string, byte[]>
            {
                ["contoso.downloads@1.0.0"] =
                    Manifest("Contoso.Downloads"),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.DownloadsTermKey, "1m")],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Single(events.OfType<PackageQueryEvent.Match>());
    }

    [Fact]
    public async Task ExecuteAsync_ScopeOnlyQueryCarriesNonEmptyScopeEvidence()
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence evidence = Assert.Single(match.Evidence);
        Assert.Equal(PackageQuery.PrefixEvidenceId, evidence.Id);
        Assert.Equal(PackageQueryEvidenceScope.Query, evidence.Scope);
        Assert.Null(evidence.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_UsesNormalizedTrailingWildcardPrefix()
    {
        var source = SourceFor(
            Manifest("System.Text.Json"),
            "System.Text.Json");
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "System.*",
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("System.Text.Json", match.Package.PackageId);
        Assert.Equal(
            "System.",
            EvidenceProperty(Assert.Single(match.Evidence), "prefix"));
        Assert.Equal("System.", plan.Prefix.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_PerCandidateFailureDoesNotBecomeTerminalFailure()
    {
        var source = new FakePackageSource(
            [
                Match("Other.Package"),
                Match("Contoso.Valid"),
            ],
            new Dictionary<string, byte[]>
            {
                ["contoso.valid@1.0.0"] = Manifest("Contoso.Valid"),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 2,
                    MaximumMatches: 2)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Single(events.OfType<PackageQueryEvent.Failure>());
        Assert.Single(events.OfType<PackageQueryEvent.Match>());
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(PackageQueryCompletionKind.Exhausted, summary.Completion);
        Assert.Equal(2, summary.Candidates);
        Assert.Equal(1, summary.Failures);
    }

    [Fact]
    public async Task ExecuteAsync_TerminalSearchFailureProducesFailedCompletion()
    {
        var source = new FakePackageSource(
            [],
            new Dictionary<string, byte[]>())
        {
            SearchFailureKind = PackageSourceFailureKind.Timeout,
        };
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(new PackageQueryRequest("Contoso.*")));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Single(events.OfType<PackageQueryEvent.Failure>());
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(PackageQueryCompletionKind.Failed, summary.Completion);
        Assert.Equal(0, summary.Candidates);
        Assert.Equal(1, summary.Failures);
        Assert.Equal(
            [
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Search, 0, 1),
            ],
            events.OfType<PackageQueryEvent.Progress>()
                .Select(item => item.Value));
    }

    [Fact]
    public async Task ExecuteAsync_TerminalSearchContractFailureProducesFailedCompletion()
    {
        var source = new FakePackageSource(
            [
                Match("Contoso.One"),
                Match("Contoso.Two"),
            ],
            new Dictionary<string, byte[]>());
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryFailure failure =
            Assert.Single(events.OfType<PackageQueryEvent.Failure>()).Value;
        Assert.Equal(PackageQueryFailureKind.SearchContract, failure.Kind);
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(PackageQueryCompletionKind.Failed, summary.Completion);
        Assert.Equal(0, summary.Candidates);
        Assert.Equal(1, summary.Failures);
        Assert.Empty(source.ManifestRequests);
        Assert.Equal(
            [
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Search, 0, 1),
            ],
            events.OfType<PackageQueryEvent.Progress>()
                .Select(item => item.Value));
    }

    [Fact]
    public async Task ExecuteAsync_ReportsBoundedProgressBeforeSparseCompletion()
    {
        var source = new FakePackageSource(
            [
                Match("Contoso.One"),
                Match("Contoso.Two"),
            ],
            new Dictionary<string, byte[]>
            {
                ["contoso.one@1.0.0"] = Manifest("Contoso.One"),
                ["contoso.two@1.0.0"] = Manifest("Contoso.Two"),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.ToolTermKey, "true")],
                    MaximumCandidates: 2,
                    MaximumMatches: 2)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                new FakePackageQueryContentProvider(
                    new Dictionary<string, IPackageContent>()),
                TestContext.Current.CancellationToken));
        PackageQueryProgress[] progress =
        [
            .. events.OfType<PackageQueryEvent.Progress>()
                .Select(item => item.Value),
        ];

        Assert.Equal(
            [
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Search, 0, 1),
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Search, 1, 1),
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Manifest, 1, 2),
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Manifest, 2, 2),
            ],
            progress);
        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.IsType<PackageQueryEvent.Completed>(events[^1]);
        Assert.Single(events.OfType<PackageQueryEvent.Completed>());
    }

    [Fact]
    public async Task ExecuteAsync_PreservesCandidateLimitAfterFiltering()
    {
        var source = SourceFor(
            Manifest("Contoso.Library"),
            "Contoso.Library",
            PackageSearchTruncationReason.RequestedLimit);
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.ToolTermKey, "true")],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                new FakePackageQueryContentProvider(
                    new Dictionary<string, IPackageContent>()),
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(
            PackageQueryCompletionKind.CandidateLimitReached,
            summary.Completion);
        Assert.Equal(1, summary.Candidates);
        Assert.Equal(0, summary.Matches);
    }

    [Fact]
    public async Task ExecuteAsync_CountsOutOfPrefixCandidateBeforeMatchLimit()
    {
        var source = new FakePackageSource(
            [
                new SearchResult("Other.Malformed", "1.0.0"),
                Match("Contoso.Valid"),
            ],
            new Dictionary<string, byte[]>
            {
                ["contoso.valid@1.0.0"] = Manifest("Contoso.Valid"),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.DependenciesTermKey, "none")],
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(PackageQueryCompletionKind.MatchLimitReached, summary.Completion);
        Assert.Equal(2, summary.Candidates);
        Assert.Equal(1, summary.Failures);
        Assert.Equal(
            [
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Search, 0, 1),
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Search, 1, 1),
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Manifest, 1, 2),
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Manifest, 2, 2),
            ],
            events.OfType<PackageQueryEvent.Progress>()
                .Select(item => item.Value));
    }

    [Fact]
    public async Task ExecuteAsync_ExactExhaustionAtMatchLimitIsConservative()
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.DependenciesTermKey, "none")],
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageQueryCompletionKind.MatchLimitReached,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Completion);
        Assert.Single(source.ManifestRequests);
    }

    [Theory]
    [InlineData(
        PackageSearchTruncationReason.SourcePageLimit,
        PackageQueryCompletionKind.SourcePageLimitReached)]
    [InlineData(
        PackageSearchTruncationReason.ClientPageLimit,
        PackageQueryCompletionKind.ClientPageLimitReached)]
    public async Task ExecuteAsync_PreservesPaginationCompletion(
        PackageSearchTruncationReason truncationReason,
        PackageQueryCompletionKind expected)
    {
        var source = SourceFor(
            Manifest("Contoso.Package"),
            truncationReason: truncationReason);
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 2,
                    MaximumMatches: 2)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            expected,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Completion);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationStopsFurtherManifestWork()
    {
        var source = new FakePackageSource(
            [
                Match("Contoso.One"),
                Match("Contoso.Two"),
                Match("Contoso.Three"),
            ],
            new Dictionary<string, byte[]>
            {
                ["contoso.one@1.0.0"] = Manifest("Contoso.One"),
                ["contoso.two@1.0.0"] = Manifest("Contoso.Two"),
                ["contoso.three@1.0.0"] = Manifest("Contoso.Three"),
            });
        using var cancellation = new CancellationTokenSource();
        source.OnManifestRequest = count =>
        {
            if (count == 1)
                cancellation.Cancel();
        };
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.DependenciesTermKey, "none")],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CollectAsync(
                PackageQuery.ExecuteAsync(
                    source,
                    plan,
                    cancellation.Token)));

        Assert.Single(source.ManifestRequests);
        Assert.Equal(0, source.PackageRequests);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExecuteAsync_CancellationAfterMatchSuppressesCompletion(
        int maximumMatches)
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        using var cancellation = new CancellationTokenSource();
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 2,
                    MaximumMatches: maximumMatches)));
        await using IAsyncEnumerator<PackageQueryEvent> events =
            PackageQuery.ExecuteAsync(
                    source,
                    plan,
                    cancellation.Token)
                .GetAsyncEnumerator(cancellation.Token);

        do
        {
            Assert.True(await events.MoveNextAsync());
        }
        while (events.Current is not PackageQueryEvent.Match);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await events.MoveNextAsync());
    }
}
