using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.PortableQueries;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageQueryTests
{
    [Fact]
    public async Task ExecuteToEnvelopeReturnsPackageQueryDocument()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.One", verified: true),
        ];
        var source = new FakePackageSource(
            candidates,
            candidates.ToDictionary(
                candidate => $"{candidate.Id.ToLowerInvariant()}@1.0.0",
                candidate => Manifest(candidate.Id)));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        InspectionEnvelope<PackageQueryDocument> envelope =
            await PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken);

        PackageQueryMatch result = Assert.Single(envelope.Content.Results);
        Assert.Equal("Contoso.One", result.Package.PackageId);
        Assert.Empty(envelope.Content.Failures);
        Assert.Equal(1, envelope.Content.Summary.Matches);
        Assert.Equal(
            PackageQueryCompletionKind.MatchLimitReached,
            envelope.Content.Summary.Completion);
        InspectionShare.NonProjectable share =
            Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Equal("package-query/share", share.Path);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public async Task ExecuteToEnvelopeSinkSeesOnlyNonterminalEvents()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.One", verified: true),
        ];
        var source = new FakePackageSource(
            candidates,
            candidates.ToDictionary(
                candidate => $"{candidate.Id.ToLowerInvariant()}@1.0.0",
                candidate => Manifest(candidate.Id)));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));
        var sink = new RecordingPackageQueryNonterminalSink();

        InspectionEnvelope<PackageQueryDocument> envelope =
            await PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                contentProvider: null,
                sink,
                TestContext.Current.CancellationToken);

        Assert.Contains(
            sink.Events,
            queryEvent => queryEvent is PackageQueryEvent.Progress);
        Assert.Equal(
            envelope.Content.Results,
            sink.Events
                .OfType<PackageQueryEvent.Match>()
                .Select(queryEvent => queryEvent.Value));
        Assert.Equal(
            envelope.Content.Failures,
            sink.Events
                .OfType<PackageQueryEvent.Failure>()
                .Select(queryEvent => queryEvent.Value));
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public async Task ExecuteToEnvelopeCancellationProducesNoDocument()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.One", verified: true),
        ];
        var source = new FakePackageSource(
            candidates,
            candidates.ToDictionary(
                candidate => $"{candidate.Id.ToLowerInvariant()}@1.0.0",
                candidate => Manifest(candidate.Id)));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));
        using var cancellation = new CancellationTokenSource();
        var sink = new CancelOnMatchSink(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                contentProvider: null,
                sink,
                cancellation.Token).AsTask());

        Assert.True(sink.SawMatch);
    }

    [Fact]
    public void TermDescriptors_HaveStableOrderedVocabulary()
    {
        Assert.Equal(
            [
                ("package", 10),
                ("prefix", 20),
                ("prerelease", 30),
                ("dependencies", 100),
                ("depends", 200),
                ("downloads", 300),
                ("readme", 400),
                ("tool", 500),
                ("tool-format", 510),
                ("skill", 600),
            ],
            PackageQuery.Terms.Select(term =>
                (term.Key, term.Weight)));
        Assert.Equal(
            [
                PackageQueryTermRole.Population,
                PackageQueryTermRole.Population,
                PackageQueryTermRole.Population,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
            ],
            PackageQuery.Terms.Select(term => term.Role));
        Assert.Equal(
            ".NET Tool",
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.ToolTermKey).Label);
        Assert.Equal(
            "embedded SKILL.md",
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.SkillTermKey).Label);
        PackageQueryTermDescriptor toolFormat = PackageQuery.Terms.Single(
            term => term.Key == PackageQuery.ToolFormatTermKey);
        Assert.True(toolFormat.CombinesWithinSelectionGroup);
        Assert.Equal(
            PackageQuery.ToolReplacementGroupId,
            toolFormat.ReplacementGroupId);
        Assert.Equal(PackageQuery.ToolDisplayGroupId, toolFormat.DisplayGroupId);
        Assert.Equal(
            PackageQuery.ToolReplacementGroupId,
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.ToolTermKey).ReplacementGroupId);
        Assert.Equal(["v1", "v2"], toolFormat.Options.Select(option => option.Value));
        Assert.Equal(
            "downloads",
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.DownloadsTermKey).SelectionGroupId);
    }

    [Fact]
    public void TermDescriptors_ExposeClosedAndFreeValueShapes()
    {
        PackageQueryTermDescriptor depends = PackageQuery.Terms.Single(
            term => term.Key == PackageQuery.DependsTermKey);
        Assert.Equal(
            [PortableQueryModel.TextOf(PortableQueryOperator.Equal)],
            depends.Operators);
        Assert.Equal(PackageQueryAcquisitionTier.Nuspec, depends.Tier);
        Assert.Equal(PackageQueryTermControlKind.Input, depends.ControlKind);
        Assert.Equal(
            ["10k", "100k", "1m"],
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.DownloadsTermKey)
                .Options.Select(option => option.Value));
    }

    [Theory]
    [InlineData(
        "unknown",
        PortableQueryOperator.Equal,
        "Microsoft.Extensions.DependencyInjection",
        PackageQueryRequestFailureReason.UnknownTerm)]
    [InlineData(
        "depends",
        PortableQueryOperator.NotEqual,
        "Microsoft.Extensions.DependencyInjection",
        PackageQueryRequestFailureReason.TermOperatorNotAdmitted)]
    [InlineData(
        "depends",
        PortableQueryOperator.Equal,
        "not/a/package",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    public void PlanInput_RejectsInvalidTermsBeforeExecution(
        string key,
        PortableQueryOperator @operator,
        string value,
        PackageQueryRequestFailureReason reason)
    {
        PackageQueryRequestFailure rejected = Rejected(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms: [new PortableQueryTerm(key, @operator, value)]));

        Assert.Equal(reason, rejected.Reason);
    }

    [Fact]
    public void PlanInput_CollapsesEquivalentBoundTerms()
    {
        var exact = new PortableQueryTerm(
            PackageQuery.DependsTermKey,
            PortableQueryOperator.Equal,
            "Microsoft.Extensions.DependencyInjection");
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms: [exact, exact]));
        Assert.Single(plan.Terms);

        PackageQueryPlan normalized = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms:
                [
                    exact,
                    new(
                        PackageQuery.DependsTermKey,
                        PortableQueryOperator.Equal,
                        "microsoft.extensions.dependencyinjection"),
                ]));
        Assert.Single(normalized.Terms);

        PackageQueryPlan exclusiveNormalized = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms:
                [
                    Term(PackageQuery.DownloadsTermKey, "1m"),
                    Term(PackageQuery.DownloadsTermKey, "1M"),
                ]));
        Assert.Single(exclusiveNormalized.Terms);

        PackageQueryPlan reverseExclusiveNormalized = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms:
                [
                    Term(PackageQuery.DownloadsTermKey, "1M"),
                    Term(PackageQuery.DownloadsTermKey, "1m"),
                ]));
        Assert.Equal(
            exclusiveNormalized.Intent.Terms,
            reverseExclusiveNormalized.Intent.Terms);

        PackageQueryPlan structuralNormalized = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.DependencyInjection",
                terms:
                [
                    Term(
                        PackageQuery.PackageTermKey,
                        "microsoft.extensions.dependencyinjection"),
                ]));
        Assert.Single(
            structuralNormalized.Intent.Terms,
            term => term.Key == PackageQuery.PackageTermKey);

        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleTerms,
            Rejected(PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms:
                [
                    Term(PackageQuery.DownloadsTermKey, "100k"),
                    Term(PackageQuery.DownloadsTermKey, "1m"),
                ])).Reason);
    }

    [Fact]
    public void PlanInput_RetainsStagesAndExplicitBounds()
    {
        RowSelectionIntent<string> selection = RowSelectionIntent<string>.Create(
        [
            RowSelectionIntentOperation<string>.Tail(4),
            RowSelectionIntentOperation<string>.Window(2, 3),
        ]);
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                maximumCandidates: 17,
                maximumMatches: null,
                rowSelection: selection));

        Assert.Equal(17, plan.MaximumCandidates);
        Assert.Null(plan.MaximumMatches);
        Assert.Equal(
            [RowSelectionStageKind.Tail, RowSelectionStageKind.Window],
            plan.RowSelection.Operations.Select(operation => operation.Kind));
        Assert.Equal(
            [RowSelectionStageKind.Tail, RowSelectionStageKind.Window],
            plan.Intent.Stages.Select(stage => stage.Kind));
        Assert.Contains(
            plan.Intent.Terms,
            term => term.Key == PackageQuery.PrefixTermKey
                && term.Value == "Microsoft.Extensions.");
        Assert.Contains(
            plan.Intent.Terms,
            term => term.Key == PackageQuery.PrereleaseTermKey
                && term.Value == "stable");
        Assert.Equal(
            [("candidates", 17)],
            plan.Intent.Bounds.Select(bound =>
                (bound.Dimension, bound.RequestedMaximum)));
    }

    [Fact]
    public async Task ExecuteAsync_DependsTermsUseNuGetIdentityAndRetainRanges()
    {
        SearchResult[] candidates =
        [
            Match("Microsoft.Extensions.Hosting"),
            Match("Microsoft.Extensions.Logging"),
        ];
        var source = new FakePackageSource(
            candidates,
            new Dictionary<string, byte[]>
            {
                ["microsoft.extensions.hosting@1.0.0"] = Manifest(
                    "Microsoft.Extensions.Hosting",
                    dependencies:
                    """
                    <group targetFramework="net10.0">
                      <dependency id="Microsoft.Extensions.DependencyInjection" version="[10.0.0, 11.0.0)" />
                      <dependency id="Microsoft.Extensions.Configuration" version="[10.0.0, 11.0.0)" />
                    </group>
                    <group targetFramework="net9.0">
                      <dependency id="microsoft.extensions.configuration" version="[10.0.0, 11.0.0)" />
                    </group>
                    <group targetFramework="net8.0">
                      <dependency id="Microsoft.Extensions.Configuration" version="10.0.0" />
                    </group>
                    <group targetFramework="net7.0">
                      <dependency id="Microsoft.Extensions.Configuration" version="[10.0.0, 11.0.0)" />
                    </group>
                    """),
                ["microsoft.extensions.logging@1.0.0"] = Manifest(
                    "Microsoft.Extensions.Logging",
                    dependencies:
                    """
                    <group targetFramework="net10.0">
                      <dependency id="Microsoft.Extensions.DependencyInjection" version="[10.0.0, 11.0.0)" />
                    </group>
                    """),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms:
                [
                    new(
                        PackageQuery.DependsTermKey,
                        PortableQueryOperator.Equal,
                        "microsoft.extensions.configuration"),
                    new(
                        PackageQuery.DependsTermKey,
                        PortableQueryOperator.Equal,
                        "Microsoft.Extensions.DependencyInjection"),
                ],
                maximumCandidates: 2,
                maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("Microsoft.Extensions.Hosting", match.Package.PackageId);
        Assert.Equal(PackageQueryAcquisitionTier.Nuspec, match.Tier);
        PackageQueryEvidence[] termEvidence =
        [
            .. match.Evidence.Where(evidence => evidence.Term is not null),
        ];
        Assert.Equal(2, termEvidence.Length);
        Assert.Contains(
            termEvidence,
            evidence => evidence.Term!.Value
                == "microsoft.extensions.configuration");
        Assert.Contains(
            termEvidence,
            evidence => evidence.Term!.Value
                == "Microsoft.Extensions.DependencyInjection");
        Assert.Contains(
            "Microsoft.Extensions.Configuration [10.0.0, 11.0.0)",
            termEvidence.Single(evidence =>
                evidence.Term!.Value
                    == "microsoft.extensions.configuration").Value,
            StringComparison.Ordinal);
        Assert.Contains(
            "Microsoft.Extensions.DependencyInjection [10.0.0, 11.0.0)",
            termEvidence.Single(evidence =>
                evidence.Term!.Value
                    == "Microsoft.Extensions.DependencyInjection").Value,
            StringComparison.Ordinal);
        PackageQueryEvidence configurationEvidence =
            termEvidence.Single(evidence =>
                evidence.Term!.Value
                    == "microsoft.extensions.configuration");
        Assert.Equal(3, configurationEvidence.Summary!.Count);
        Assert.Equal(
            [
                "Microsoft.Extensions.Configuration 10.0.0",
                "Microsoft.Extensions.Configuration [10.0.0, 11.0.0)",
                "microsoft.extensions.configuration [10.0.0, 11.0.0)",
            ],
            configurationEvidence.Summary.Preview.Select(value =>
                value.ToString()));
        Assert.Equal(2, source.ManifestRequests.Count);
        Assert.Equal(0, source.PackageRequests);
    }

    [Theory]
    [InlineData("", PackageQueryRequestFailureReason.InvalidPackageInput)]
    [InlineData("\u202EContoso.", PackageQueryRequestFailureReason.InvalidPackageInput)]
    [InlineData("Contoso.*", PackageQueryRequestFailureReason.InvalidCandidateLimit, 0, 1)]
    [InlineData("Contoso.*", PackageQueryRequestFailureReason.InvalidCandidateLimit, PackageQuery.MaximumCandidates + 1, 1)]
    [InlineData("Contoso.*", PackageQueryRequestFailureReason.InvalidMatchLimit, 1, 0)]
    [InlineData("Contoso.*", PackageQueryRequestFailureReason.InvalidMatchLimit, 1, PackageQuery.MaximumCandidates + 1)]
    public void Plan_RejectsInvalidScopeAndBoundsWithoutThrowing(
        string prefix,
        PackageQueryRequestFailureReason expected,
        int maximumCandidates = 1,
        int maximumMatches = 1)
    {
        PackageQueryPlanResult result = PackageQuery.Plan(
            new PackageQueryRequest(
                prefix,
                MaximumCandidates: maximumCandidates,
                MaximumMatches: maximumMatches));

        Assert.Equal(
            expected,
            Assert.IsType<PackageQueryPlanResult.Rejected>(result)
                .Failure.Reason);
    }

    [Fact]
    public void Plan_InvalidMatchLimitReportsTheAcceptedRange()
    {
        PackageQueryRequestFailure failure = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumMatches: PackageQuery.MaximumCandidates + 1)));

        Assert.Equal(
            $"The package-query match limit must be between 1 and {PackageQuery.MaximumCandidates}.",
            failure.Message);
    }

    [Fact]
    public void Plan_AcceptsScopeOnlyQuery()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(new PackageQueryRequest("Contoso.*")));

        Assert.Empty(plan.Terms);
        Assert.Equal("Contoso.", plan.Prefix.ToString());
        Assert.Equal(PackageQuery.DefaultMaximumCandidates, plan.MaximumCandidates);
        Assert.Equal(PackageQuery.DefaultMaximumMatches, plan.MaximumMatches);
    }

    [Fact]
    public void Plan_PreservesAbsentMatchLimit()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 500,
                    MaximumMatches: null)));

        Assert.Equal(500, plan.MaximumCandidates);
        Assert.Null(plan.MaximumMatches);
    }

    [Fact]
    public void Plan_TreatsOneTrailingWildcardAsPrefixShorthand()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(new PackageQueryRequest("System.*")));

        Assert.Equal("System.", plan.Prefix.ToString());
        Assert.Equal(
            "Package ID matches prefix \"System.\".",
            plan.PrefixEvidence.ToString());
        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidPackageInput,
            Rejected(PackageQuery.Plan(
                new PackageQueryRequest("System.*.Json")))
                .Reason);
        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidPackageInput,
            Rejected(PackageQuery.Plan(
                new PackageQueryRequest("*")))
                .Reason);
    }

    [Fact]
    public void Plan_AcceptsMatchLimitAboveCandidateLimit()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 50)));

        Assert.Equal(50, plan.MaximumCandidates);
        Assert.Equal(PackageQuery.DefaultMaximumMatches, plan.MaximumMatches);
    }

    [Fact]
    public void Plan_AcceptsMaximumBounds()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: PackageQuery.MaximumCandidates,
                    MaximumMatches: PackageQuery.MaximumCandidates)));

        Assert.Equal(
            PackageQuery.MaximumCandidates,
            plan.MaximumCandidates);
        Assert.Equal(
            PackageQuery.MaximumCandidates,
            plan.MaximumMatches);
    }

    [Fact]
    public void Plan_RequiresThePackageContentCandidateBound()
    {
        PackageQueryRequestFailure rejected = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.SkillTermKey, "true")],
                    MaximumCandidates:
                        PackageQuery.MaximumPackageContentCandidates + 1)));

        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidCandidateLimit,
            rejected.Reason);

        PackageQueryPlan accepted = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.SkillTermKey, "true")],
                    MaximumCandidates:
                        PackageQuery.MaximumPackageContentCandidates)));
        Assert.Equal(
            PackageQueryAcquisitionTier.PackageContent,
            Assert.Single(accepted.BoundTerms).Descriptor.Tier);
    }

    [Fact]
    public void Plan_RejectsUnknownAndIncompatibleTerms()
    {
        PackageQueryRequestFailure unknown = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term("unknown", "true")])));
        Assert.Equal(
            PackageQueryRequestFailureReason.UnknownTerm,
            unknown.Reason);

        PackageQueryRequestFailure incompatible = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(PackageQuery.ToolTermKey, "true"),
                        Term(PackageQuery.ToolFormatTermKey, "v1"),
                    ])));
        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleTerms,
            incompatible.Reason);

        PackageQueryPlan toolVersions = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(PackageQuery.ToolFormatTermKey, "v1"),
                        Term(PackageQuery.ToolFormatTermKey, "v2"),
                    ],
                    MaximumCandidates:
                        PackageQuery.MaximumPackageContentCandidates)));
        Assert.Equal(
            ["v1", "v2"],
            toolVersions.Terms.Select(term => term.Value));
    }

    [Fact]
    public void ResolveIntent_RequiresPopulationPrereleaseAndCandidateBound()
    {
        PackageQueryRequestFailure missingPopulation = Rejected(
            PackageQuery.ResolveIntent(PortableQueryIntent.Create(
                [],
                [new("candidates", 10)],
                [],
                []),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            PackageQueryRequestFailureReason.RequiredPopulationMissing,
            missingPopulation.Reason);

        PackageQueryRequestFailure missingBound = Rejected(
            PackageQuery.ResolveIntent(PortableQueryIntent.Create(
                [
                    Term(PackageQuery.PrefixTermKey, "Contoso."),
                    Term(PackageQuery.PrereleaseTermKey, "stable"),
                ],
                [],
                [],
                []),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            PackageQueryRequestFailureReason.RequiredCandidateBoundMissing,
            missingBound.Reason);

        PackageQueryRequestFailure missingPrerelease = Rejected(
            PackageQuery.ResolveIntent(PortableQueryIntent.Create(
                [Term(PackageQuery.PrefixTermKey, "Contoso.")],
                [new("candidates", 10)],
                [],
                []),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            PackageQueryRequestFailureReason.RequiredPrereleaseMissing,
            missingPrerelease.Reason);
    }

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
            "Package ID matches prefix \"Contoso.\".",
            match.Evidence[0].Value);
        Assert.Contains(
            "Example.Dependency [1.0.0]",
            match.Evidence[1].Value,
            StringComparison.Ordinal);
        Assert.Equal(PackageQueryEvidenceScope.Query, match.Evidence[0].Scope);
        Assert.All(match.Evidence.Skip(1), evidence =>
            Assert.Equal(PackageQueryEvidenceScope.Package, evidence.Scope));
        Assert.Equal(1, Assert.IsType<PackageQueryEvidenceSummary>(
            match.Evidence[1].Summary).Count);
        Assert.Contains(
            "1,500,000 total downloads",
            match.Evidence[2].Value,
            StringComparison.Ordinal);
        Assert.Contains(
            "CLI v2",
            match.Evidence[4].Value,
            StringComparison.Ordinal);
        Assert.Equal(["Contoso.Tool"], content.Requests);
        Assert.All(
            match.Evidence,
            evidence =>
            {
                Assert.NotEmpty(evidence.Value);
                Assert.True(
                    InertString.IsPermitted(
                        TextPolicy.Prose,
                        evidence.Value));
            });
    }

    [Fact]
    public async Task ExecuteAsync_DependsEvidenceCountsDistinctDeclarationsAcrossGroups()
    {
        var source = SourceFor(Manifest(
            "Contoso.Package",
            dependencies:
            """
            <group targetFramework="net8.0">
              <dependency id="Gamma" version="[1.0.0]" />
              <dependency id="beta" version="[1.0.0]" />
              <dependency id="Alpha" version="[1.0.0]" />
            </group>
            <group targetFramework="net9.0">
              <dependency id="BETA" version="[2.0.0]" />
              <dependency id="Alpha" version="[2.0.0]" />
              <dependency id="Zeta" version="[1.0.0]" />
            </group>
            """));
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Contoso.*", [Term(PackageQuery.DependsTermKey, "Alpha")],
                MaximumCandidates: 1, MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(PackageQuery.ExecuteAsync(
            source, plan, TestContext.Current.CancellationToken));

        PackageQueryMatch match = Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence evidence = Assert.Single(match.Evidence,
            item => item.Id == PackageQuery.DependsTermKey);
        PackageQueryEvidenceSummary summary =
            Assert.IsType<PackageQueryEvidenceSummary>(evidence.Summary);
        Assert.Equal(2, summary.Count);
        Assert.Equal(["Alpha [1.0.0]", "Alpha [2.0.0]"],
            summary.Preview.Select(item => item.ToString()));
        Assert.Equal(
            "2 dependency declarations: Alpha [1.0.0], Alpha [2.0.0].",
            evidence.Value);
        Assert.Equal(PackageQueryEvidenceScope.Package, evidence.Scope);
        Assert.Single(source.ManifestRequests);
        Assert.Equal(0, source.PackageRequests);
    }

    [Fact]
    public async Task ExecuteAsync_SkillEvidenceUsesBoundedActualInventoryPaths()
    {
        string longPath = $"skills/{new string('a', 200)}/SKILL.md";
        var archive = new FakePackageContent(
            ("skills/z/skill.MD", "Not parsed as skill frontmatter."),
            ("docs/SKILL.md", ""),
            ("skills/SKILL.md", ""),
            ("skills/x/SKILL.md", ""),
            ("skills/b\nname/SKILL.md", ""),
            (longPath, ""),
            ("skills/not-skill.md", ""),
            ("SKILL.md", ""));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent> { ["Contoso.Package"] = archive });
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Contoso.*", [Term(PackageQuery.SkillTermKey, "true")],
                MaximumCandidates: 1, MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(PackageQuery.ExecuteAsync(
            source, plan, content, TestContext.Current.CancellationToken));

        PackageQueryMatch match = Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence evidence = Assert.Single(match.Evidence,
            item => item.Id == PackageQuery.SkillTermKey);
        PackageQueryEvidenceSummary summary =
            Assert.IsType<PackageQueryEvidenceSummary>(evidence.Summary);
        Assert.Equal(5, summary.Count);
        Assert.Equal(PackageQuery.MaximumEvidencePreviewItems, summary.Preview.Length);
        Assert.Equal("skills/SKILL.md", summary.Preview[0].ToString());
        Assert.True(summary.Preview[1].IsTruncated);
        Assert.True(summary.Preview[2].WasEncoded);
        Assert.All(summary.Preview, preview =>
        {
            Assert.True(preview.ToString().Length <= PackageQuery.MaximumEvidencePreviewCharacters);
            Assert.True(InertString.IsPermitted(TextPolicy.Field, preview.ToString()));
        });
        Assert.StartsWith("5 skill documents: skills/SKILL.md, ", evidence.Value);
        Assert.EndsWith("(+2 more).", evidence.Value);
        Assert.DoesNotContain("\n", evidence.Value);
        Assert.DoesNotContain(longPath, evidence.Value);
        Assert.Equal(PackageQueryEvidenceScope.Package, evidence.Scope);
        Assert.Single(source.ManifestRequests);
        Assert.Single(content.Requests);
        Assert.Empty(archive.EntryRequests);
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
        Assert.Contains(
            ".NET tool package type",
            match.Evidence[^1].Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_PackageContentTermsMatchSkillsAndToolFormats()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.V1"),
            Match("Contoso.V2"),
            Match("Contoso.Library"),
        ];
        var source = new FakePackageSource(
            candidates,
            candidates.ToDictionary(
                candidate =>
                    $"{candidate.Id.ToLowerInvariant()}@1.0.0",
                candidate => Manifest(
                    candidate.Id,
                    packageTypes: candidate.Id == "Contoso.Library"
                        ? ""
                        : """
                          <packageTypes>
                            <packageType name="DotnetTool" />
                          </packageTypes>
                          """)));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.V1"] = new FakePackageContent(
                    ("tools/net8.0/any/DotnetToolSettings.xml",
                        "<DotNetCliTool><Commands /></DotNetCliTool>"),
                    ("SKILLS/demo/skill.MD", "# Demo")),
                ["Contoso.V2"] = new FakePackageContent(
                    ("tools/any/any/DotnetToolSettings.xml",
                        "\uFEFF<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                        + "<DotNetCliTool Version=\"2\"><Commands /></DotNetCliTool>")),
                ["Contoso.Library"] = new FakePackageContent(
                    ("skills/SKILL.md", "# Library")),
            });

        PackageQueryPlan anyToolPlan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.ToolTermKey, "true")],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));
        List<PackageQueryEvent> anyToolEvents = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                anyToolPlan,
                TestContext.Current.CancellationToken));
        List<PackageQueryMatch> anyTools =
        [
            .. anyToolEvents
                .OfType<PackageQueryEvent.Match>()
                .Select(item => item.Value),
        ];
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            anyTools.Select(item => item.Package.PackageId));
        Assert.Equal(
            Enumerable.Repeat(
                "The package manifest declares the .NET tool package type.",
                2),
            anyTools.Select(item => item.Evidence[^1].Value));
        Assert.All(
            anyTools,
            item => Assert.Equal(
                PackageQueryAcquisitionTier.Nuspec,
                item.Tier));
        Assert.Empty(content.Requests);

        content.Requests.Clear();
        PackageQueryPlan v1Plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.ToolFormatTermKey, "v1")],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));
        List<PackageQueryEvent> v1Events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                v1Plan,
                content,
                TestContext.Current.CancellationToken));
        PackageQueryMatch v1 = Assert.Single(
            v1Events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("Contoso.V1", v1.Package.PackageId);
        Assert.Equal(PackageQueryAcquisitionTier.PackageContent, v1.Tier);
        Assert.Equal(
            [PackageQuery.PrefixEvidenceId, PackageQuery.ToolFormatTermKey],
            v1.Evidence.Select(evidence => evidence.Id));
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.Requests);

        content.Requests.Clear();
        PackageQueryPlan v2Plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.ToolFormatTermKey, "v2")],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));
        List<PackageQueryEvent> v2Events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                v2Plan,
                content,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            "Contoso.V2",
            Assert.Single(v2Events.OfType<PackageQueryEvent.Match>())
                .Value.Package.PackageId);
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.Requests);

        content.Requests.Clear();
        PackageQueryPlan bothVersionsPlan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(PackageQuery.ToolFormatTermKey, "v1"),
                        Term(PackageQuery.ToolFormatTermKey, "v2"),
                    ],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));
        List<PackageQueryEvent> bothVersionEvents = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                bothVersionsPlan,
                content,
                TestContext.Current.CancellationToken));
        List<PackageQueryMatch> bothVersions =
        [
            .. bothVersionEvents
                .OfType<PackageQueryEvent.Match>()
                .Select(item => item.Value),
        ];
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            bothVersions.Select(item => item.Package.PackageId));
        Assert.Equal(
            [
                [PackageQuery.PrefixEvidenceId, PackageQuery.ToolFormatTermKey],
                [PackageQuery.PrefixEvidenceId, PackageQuery.ToolFormatTermKey],
            ],
            bothVersions.Select(item =>
                item.Evidence.Select(evidence => evidence.Id)));
        Assert.Contains(
            "CLI v1 format",
            bothVersions[0].Evidence[^1].Value,
            StringComparison.Ordinal);
        Assert.Contains(
            "CLI v2 format",
            bothVersions[1].Evidence[^1].Value,
            StringComparison.Ordinal);
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.Requests);
        Assert.Equal(
            [0, 1, 2],
            bothVersionEvents
                .OfType<PackageQueryEvent.Progress>()
                .Where(item => item.Value.Phase
                    == PackageQueryProgressPhase.PackageContent)
                .Select(item => item.Value.Completed));

        content.Requests.Clear();
        PackageQueryPlan bothVersionsAndSkillPlan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(PackageQuery.ToolFormatTermKey, "v1"),
                        Term(PackageQuery.ToolFormatTermKey, "v2"),
                        Term(PackageQuery.SkillTermKey, "true"),
                    ],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));
        PackageQueryMatch versionAndSkill = Assert.Single(
            (await CollectAsync(
                PackageQuery.ExecuteAsync(
                    source,
                    bothVersionsAndSkillPlan,
                    content,
                    TestContext.Current.CancellationToken)))
                .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("Contoso.V1", versionAndSkill.Package.PackageId);
        Assert.Equal(
            [
                PackageQuery.PrefixEvidenceId,
                PackageQuery.ToolFormatTermKey,
                PackageQuery.SkillTermKey,
            ],
            versionAndSkill.Evidence.Select(evidence => evidence.Id));
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.Requests);

        content.Requests.Clear();
        PackageQueryPlan skillPlan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.SkillTermKey, "true")],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));
        List<PackageQueryEvent> skillEvents = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                skillPlan,
                content,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            ["Contoso.V1", "Contoso.Library"],
            skillEvents.OfType<PackageQueryEvent.Match>()
                .Select(item => item.Value.Package.PackageId));
        Assert.Equal(
            [
                "1 skill document: SKILLS/demo/skill.MD.",
                "1 skill document: skills/SKILL.md.",
            ],
            skillEvents.OfType<PackageQueryEvent.Match>()
                .Select(item => item.Value.Evidence[^1].Value));
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2", "Contoso.Library"],
            content.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<DotNetCliTool Version=\"3\"><Commands /></DotNetCliTool>")]
    public async Task ExecuteAsync_BroadToolUsesManifestWithoutReadingSettings(
        string? settings)
    {
        var source = SourceFor(
            Manifest(
                "Contoso.Tool",
                packageTypes:
                """
                <packageTypes>
                  <packageType name="DotnetTool" />
                </packageTypes>
                """),
            "Contoso.Tool");
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Tool"] = settings is null
                    ? new FakePackageContent()
                    : new FakePackageContent(
                        ("tools/net8.0/any/DotnetToolSettings.xml",
                            settings)),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.ToolTermKey, "true")],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        PackageQueryMatch match = Assert.Single(
            (await CollectAsync(
                PackageQuery.ExecuteAsync(
                    source,
                    plan,
                    TestContext.Current.CancellationToken)))
                .OfType<PackageQueryEvent.Match>()).Value;

        Assert.Equal(
            "The package manifest declares the .NET tool package type.",
            match.Evidence[^1].Value);
        Assert.Empty(content.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidToolSettingsRemainVisible()
    {
        var source = SourceFor(
            Manifest(
                "Contoso.Tool",
                packageTypes:
                """
                <packageTypes>
                  <packageType name="DotnetTool" />
                </packageTypes>
                """),
            "Contoso.Tool");
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Tool"] = new FakePackageContent(
                    ("tools/net8.0/any/DotnetToolSettings.xml",
                        "<DotNetCliTool Version=\"2\"><Commands>")),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.ToolFormatTermKey, "v2")],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        PackageQueryFailure failure =
            Assert.Single(events.OfType<PackageQueryEvent.Failure>()).Value;
        Assert.Equal(
            PackageQueryFailureKind.PackageContentEvaluation,
            failure.Kind);
        Assert.Equal(
            1,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Failures);
    }

    [Fact]
    public async Task ExecuteAsync_PackageContentAcquisitionFailureRemainsVisible()
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>(),
            "package payload unavailable");
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.SkillTermKey, "true")],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        PackageQueryFailure failure =
            Assert.Single(events.OfType<PackageQueryEvent.Failure>()).Value;
        Assert.Equal(
            PackageQueryFailureKind.PackageContentAcquisition,
            failure.Kind);
        Assert.Equal("package payload unavailable", failure.Message);
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(1, summary.Failures);
        Assert.Equal(0, summary.Matches);
    }

    [Fact]
    public async Task ExecuteAsync_PackageContentTermRequiresProviderBeforeSourceWork()
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.SkillTermKey, "true")],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CollectAsync(
                PackageQuery.ExecuteAsync(
                    source,
                    plan,
                    TestContext.Current.CancellationToken)));

        Assert.Empty(source.ManifestRequests);
        Assert.Equal(0, source.LastSearchTake);
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
            "Package ID matches prefix \"System.\".",
            Assert.Single(match.Evidence).Value);
        Assert.Equal("System.", plan.Prefix.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_AllEmptyDependencyGroupsMatchNoDependencies()
    {
        byte[] manifest = Manifest(
            "Contoso.EmptyGroups",
            dependencies:
            """
            <group targetFramework="net8.0"></group>
            <group targetFramework="net9.0"></group>
            """);

        var noDependenciesSource = SourceFor(
            manifest,
            "Contoso.EmptyGroups");
        PackageQueryPlan noDependencies = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.DependenciesTermKey, "none")],
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));
        List<PackageQueryEvent> noDependencyEvents = await CollectAsync(
            PackageQuery.ExecuteAsync(
                noDependenciesSource,
                noDependencies,
                TestContext.Current.CancellationToken));
        PackageQueryMatch emptyMatch =
            Assert.Single(noDependencyEvents.OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence emptyEvidence = Assert.Single(emptyMatch.Evidence,
            evidence => evidence.Id == PackageQuery.DependenciesTermKey);
        PackageQueryEvidenceSummary emptySummary =
            Assert.IsType<PackageQueryEvidenceSummary>(emptyEvidence.Summary);
        Assert.Equal(0, emptySummary.Count);
        Assert.Empty(emptySummary.Preview);
        Assert.Equal("0 dependencies.", emptyEvidence.Value);
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

    private static PackageQueryPlan Accepted(PackageQueryPlanResult result) =>
        Assert.IsType<PackageQueryPlanResult.Accepted>(result).Plan;

    private static PackageQueryRequestFailure Rejected(
        PackageQueryPlanResult result) =>
        Assert.IsType<PackageQueryPlanResult.Rejected>(result).Failure;

    private static PortableQueryTerm Term(string key, string value) =>
        new(key, PortableQueryOperator.Equal, value);

    private static SearchResult Match(
        string packageId,
        string version = "1.0.0",
        bool verified = false,
        long totalDownloads = 0)
        => new(
            packageId,
            version,
            TotalDownloads: totalDownloads,
            Verified: verified);

    private static FakePackageSource SourceFor(
        byte[] manifest,
        string packageId = "Contoso.Package",
        PackageSearchTruncationReason truncationReason =
            PackageSearchTruncationReason.None) =>
        new(
            [Match(packageId)],
            new Dictionary<string, byte[]>
            {
                [$"{packageId.ToLowerInvariant()}@1.0.0"] = manifest,
            })
        {
            SearchTruncationReason = truncationReason,
        };

    private static byte[] Manifest(
        string packageId,
        string version = "1.0.0",
        string dependencies = "",
        string packageTypes = "",
        string readme = "") =>
        Encoding.UTF8.GetBytes(
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{{packageId}}</id>
                <version>{{version}}</version>
                <authors>Manifest Author</authors>
                <description>Package query test.</description>
                {{packageTypes}}
                {{readme}}
                <dependencies>{{dependencies}}</dependencies>
              </metadata>
            </package>
            """);

    private static async Task<List<PackageQueryEvent>> CollectAsync(
        IAsyncEnumerable<PackageQueryEvent> source)
    {
        List<PackageQueryEvent> events = [];
        await foreach (PackageQueryEvent item in source)
            events.Add(item);
        return events;
    }

    private sealed class RecordingPackageQueryNonterminalSink
        : IPackageQueryNonterminalSink
    {
        internal List<PackageQueryEvent.Nonterminal> Events { get; } = [];

        public ValueTask ReportAsync(
            PackageQueryEvent.Nonterminal queryEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(queryEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancelOnMatchSink(CancellationTokenSource cancellation)
        : IPackageQueryNonterminalSink
    {
        internal bool SawMatch { get; private set; }

        public ValueTask ReportAsync(
            PackageQueryEvent.Nonterminal queryEvent,
            CancellationToken cancellationToken)
        {
            if (queryEvent is PackageQueryEvent.Match)
            {
                SawMatch = true;
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ValueTask.CompletedTask;
        }
    }

    private static async Task AssertNoMatchesAsync(
        FakePackageSource source,
        PortableQueryTerm term)
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [term],
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
        Assert.Equal(
            PackageQueryCompletionKind.Exhausted,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Completion);
    }

    private static PackageSourceResultFactory CreateResultFactory()
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                PackageSourceAssociation.Create(),
                factory =>
                {
                    captured = factory;
                    return new FactoryOnlyPackageSourceClient(factory.Source);
                });
        return Assert.IsType<PackageSourceResultFactory>(captured);
    }

    private sealed class FactoryOnlyPackageSourceClient(
        PackageSourceResultIdentity source)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source { get; } = source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.None;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
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

    private sealed class FakePackageSource(
        IReadOnlyList<SearchResult> matches,
        IReadOnlyDictionary<string, byte[]> manifests)
        : IPackageSourceClient
    {
        private readonly PackageSourceResultFactory _results =
            CreateResultFactory();

        public PackageSourceFailureKind? SearchFailureKind { get; init; }
        public PackageSearchTruncationReason SearchTruncationReason
        {
            get;
            init;
        }
        public Action<int>? OnManifestRequest { get; set; }
        public List<string> ManifestRequests { get; } = [];
        public int LastSearchTake { get; private set; }
        public int PackageRequests { get; private set; }
        public PackageSourceResultIdentity Source => _results.Source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Search
            | PackageSourceCapabilities.Manifest;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSearchTake = take;
            PackageSourceOperationResult<PackageSearchResult> result =
                SearchFailureKind is null
                    ? _results.SucceededSearch(
                        _results.Search(
                            matches,
                            SearchTruncationReason))
                    : _results.FailedSearch(SearchFailureKind.Value);
            return Task.FromResult(result);
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
            OnManifestRequest?.Invoke(ManifestRequests.Count);
            PackageSourceOperationResult<PackageSourceManifest> result =
                manifests.TryGetValue(key, out byte[]? content)
                    ? _results.SucceededManifest(
                        coordinate,
                        _results.Manifest(coordinate, content))
                    : _results.FailedManifest(
                        coordinate,
                        PackageSourceFailureKind.NotFound);
            return Task.FromResult(result);
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            PackageRequests++;
            throw new NotSupportedException();
        }

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

    private sealed class FakePackageQueryContentProvider(
        IReadOnlyDictionary<string, IPackageContent> content,
        string unavailableMessage = "package content unavailable")
        : IPackageQueryContentProvider
    {
        public List<string> Requests { get; } = [];

        public ValueTask<PackageQueryContentResult> GetContentAsync(
            PackageQueryPackage package,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(package.PackageId);
            return ValueTask.FromResult<PackageQueryContentResult>(
                content.TryGetValue(
                    package.PackageId,
                    out IPackageContent? packageContent)
                    ? new PackageQueryContentResult.Available(packageContent)
                    : new PackageQueryContentResult.Unavailable(
                        unavailableMessage));
        }
    }

    private sealed class FakePackageContent(
        params (string Path, string Content)[] entries)
        : IPackageContent
    {
        readonly IReadOnlyDictionary<string, byte[]> _entries =
            entries.ToDictionary(
                entry => entry.Path,
                entry => Encoding.UTF8.GetBytes(entry.Content),
                StringComparer.Ordinal);

        public string? RootPath => null;
        public string? NupkgPath => null;
        public bool FromCache => false;
        public string ProducerKey => "nuget.org";
        public bool RequiresArchiveTreeMatch => false;
        public List<string> EntryRequests { get; } = [];

        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            EntryRequests.Add(relativePath);
            if (_entries.TryGetValue(relativePath, out byte[]? content))
            {
                stream = new MemoryStream(content, writable: false);
                return true;
            }

            stream = null;
            return false;
        }

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [NotNullWhen(true)] out Stream? stream)
        {
            EntryRequests.Add(relativePath);
            if (!_entries.TryGetValue(relativePath, out byte[]? content)
                || content.LongLength > maxExpandedBytes)
            {
                stream = null;
                return false;
            }

            stream = new MemoryStream(content, writable: false);
            return true;
        }

        public IEnumerable<string> EnumerateEntries() => _entries.Keys;
    }
}
