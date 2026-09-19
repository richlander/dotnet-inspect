using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.PortableQueries;
using DotnetInspector.QueryOperations;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspector.Services;
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
                ("dependency-target", 150),
                ("depends", 200),
                ("depends-ecosystem", 210),
                ("license", 250),
                ("downloads", 300),
                ("readme", 400),
                ("tool", 500),
                ("tool-format", 510),
                ("references", 550),
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
        PackageQueryTermDescriptor dependsEcosystem =
            PackageQuery.Terms.Single(
                term => term.Key == PackageQuery.DependsEcosystemTermKey);
        Assert.Equal(
            "canonical ecosystem ID",
            dependsEcosystem.ValueKind);
        Assert.Equal(
            PackageQueryAcquisitionTier.Nuspec,
            dependsEcosystem.Tier);
        Assert.Equal(
            PackageQueryTermControlKind.Input,
            dependsEcosystem.ControlKind);
        PackageQueryTermDescriptor dependencyTarget =
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.DependencyTargetTermKey);
        Assert.Equal(
            "all or NuGet target framework",
            dependencyTarget.ValueKind);
        Assert.Equal(
            PackageQueryTermControlKind.Input,
            dependencyTarget.ControlKind);
        Assert.Equal(
            "dependency-target",
            dependencyTarget.SelectionGroupId);
        Assert.Equal(
            ["10k", "100k", "1m"],
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.DownloadsTermKey)
                .Options.Select(option => option.Value));
        PackageQueryTermDescriptor license = PackageQuery.Terms.Single(
            term => term.Key == PackageQuery.LicenseTermKey);
        Assert.Equal(PackageQueryAcquisitionTier.Nuspec, license.Tier);
        Assert.Equal(PackageQueryTermControlKind.Choice, license.ControlKind);
        Assert.Equal(
            ["any", "MIT", "OSMF"],
            license.Options.Select(option => option.Value));
        PackageQueryTermDescriptor references = PackageQuery.Terms.Single(
            term => term.Key == PackageQuery.ReferencesTermKey);
        Assert.Equal(
            PackageQueryAcquisitionTier.PackageContent,
            references.Tier);
        Assert.Equal(
            PackageQueryTermControlKind.Input,
            references.ControlKind);
        Assert.Equal("assembly simple name", references.ValueKind);
        Assert.Null(references.SelectionGroupId);
    }

    [Fact]
    public void OperationRoute_ProjectsTheCompleteExecutableVocabulary()
    {
        IQueryOperationRoute route = PackageQuery.OperationRoute;

        Assert.Equal(PackageQuery.OperationRouteIdentity, route.Identity);
        Assert.Equal(PackageQuery.OperationIdentity, route.OperationIdentity);
        Assert.Equal(
            PackageQuery.OperationSubjectRole,
            route.SubjectRole);
        Assert.Equal(
            PackageQuery.OperationResultGrain,
            route.ResultGrain);
        Assert.Equal(
            [PackageQuery.OperationPackagesRowSet],
            route.RowSets);
        Assert.Equal(
            PackageQuery.OperationProfileIdentity,
            route.ProfileIdentity);
        Assert.Equal(
            PackageQuery.VocabularyIdentity,
            route.Capabilities.Vocabulary);
        Assert.Equal(
            PackageQuery.Terms.Select(term => term.Key),
            route.Capabilities.Terms.Select(term =>
                term.Binding.Key));
        Assert.All(
            route.Capabilities.Terms,
            term => Assert.Equal(
                [PortableQueryOperator.Equal],
                term.Operators));
        Assert.Equal(
            ["candidates", "matches"],
            route.Capabilities.Dimensions);
        Assert.Equal(
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
            ],
            route.Capabilities.Stages);

        Assert.All(
            [
                PackageQuery.ToolFormatTermKey,
                PackageQuery.ReferencesTermKey,
                PackageQuery.SkillTermKey,
            ],
            key =>
            {
                QueryOperationTermCapability packageContent =
                    route.Capabilities.Terms.Single(term =>
                        term.Binding.Key == key);
                Assert.Contains(
                    packageContent.Binding.Effects,
                    effect =>
                        effect.Kind
                            == QueryOperationEffectKind.Capability
                        && effect.Identity
                            == PackageQuery.PackageContentCapability);
                Assert.Contains(
                    packageContent.Binding.Effects,
                    effect =>
                        effect.Kind
                            == QueryOperationEffectKind.AcquisitionTier
                        && effect.Identity == "package-content");
            });
    }

    [Fact]
    public void RegisteredTerms_AreTheEffectiveOperationProjection()
    {
        Assert.Equal(
            PackageQuery.OperationRoute.Capabilities.Terms.Select(
                capability => capability.Binding.Key),
            PackageQuery.RegisteredTerms.Select(term =>
                term.Descriptor.Key));
        Assert.Equal(
            PackageQuery.OperationRoute.Capabilities.Terms.Select(
                capability => capability.Operators.Single()),
            PackageQuery.RegisteredTerms.Select(term =>
                term.Operators.Single()));
        Assert.Equal(
            PackageQuery.RegisteredTerms.Select(term =>
                term.Descriptor),
            PackageQuery.Terms);
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
    [InlineData(
        "license",
        PortableQueryOperator.Equal,
        "Apache-2.0",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    [InlineData(
        "dependency-target",
        PortableQueryOperator.NotEqual,
        "net8.0",
        PackageQueryRequestFailureReason.TermOperatorNotAdmitted)]
    [InlineData(
        "dependency-target",
        PortableQueryOperator.Equal,
        "not/a/tfm",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    [InlineData(
        "references",
        PortableQueryOperator.Equal,
        "System.Text.Json, Version=10.0.0.0",
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
    public void PlanInput_BindsAllAndCanonicalDependencyTargets()
    {
        PackageQueryPlan implicitAll = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms: [Term(PackageQuery.DependsTermKey, "Dependency")]));
        Assert.Equal(
            PackageQueryDependencyTargetKind.All,
            implicitAll.DependencyTarget.Kind);
        Assert.Null(
            implicitAll.DependencyTarget.RequestedTargetFramework);

        PackageQueryPlan explicitAll = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "ALL"),
                ]));
        Assert.Equal(
            PackageQueryDependencyTargetKind.All,
            explicitAll.DependencyTarget.Kind);
        Assert.Contains(
            explicitAll.Terms,
            term => term.Key == PackageQuery.DependencyTargetTermKey
                && term.Value == "ALL");

        PackageQueryPlan framework = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "NET8.0"),
                ]));
        Assert.Equal(
            PackageQueryDependencyTargetKind.TargetFramework,
            framework.DependencyTarget.Kind);
        Assert.Equal(
            "net8.0",
            framework.DependencyTarget.RequestedTargetFramework);

        PackageQueryPlan any = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "ANY"),
                ]));
        Assert.Equal(
            "any",
            any.DependencyTarget.RequestedTargetFramework);

        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleTerms,
            Rejected(PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "all"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "net8.0"),
                ])).Reason);
    }

    [Fact]
    public void PlanInput_BindsAssemblyReferenceNamesCaseInsensitively()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.ReferencesTermKey, "System.Text.Json"),
                    Term(PackageQuery.ReferencesTermKey, "system.text.json"),
                ],
                maximumCandidates:
                    PackageQuery.MaximumPackageContentCandidates));

        PortableQueryTerm term = Assert.Single(
            plan.Terms,
            term => term.Key == PackageQuery.ReferencesTermKey);
        Assert.Equal("System.Text.Json", term.Value);
        Assert.True(plan.RequiresPackageContent);
    }

    [Fact]
    public void PlanInput_DependencyTargetRequiresDependencyPredicate()
    {
        PackageQueryRequestFailure failure = Rejected(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "net8.0"),
                    Term(PackageQuery.DownloadsTermKey, "100k"),
                ]));

        Assert.Equal(
            PackageQueryRequestFailureReason
                .DependencyTargetRequiresDependencyPredicate,
            failure.Reason);
        Assert.Equal(
            [PackageQuery.DependencyTargetTermKey],
            failure.TermKeys);
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
    public void PlanInput_EnforcesPortableInspectionTermBoundary()
    {
        PortableQueryTerm[] maximum = InspectionTerms(
            PackageQuery.MaximumInspectionTerms);
        PackageQueryPlan accepted = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms: maximum));

        Assert.Equal(
            PortableQueryPayloadCodec.MaxTerms,
            accepted.Intent.Terms.Count);
        string payload = PortableQueryPayloadCodec.Encode(
            accepted.Intent,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            payload,
            PortableQueryPayloadCodec.Encode(
                PortableQueryPayloadCodec.Decode(
                    payload,
                    TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken));

        PackageQueryRequestFailure rejected = Rejected(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms: InspectionTerms(
                    PackageQuery.MaximumInspectionTerms + 1)));
        Assert.Equal(
            PackageQueryRequestFailureReason.TooManyTerms,
            rejected.Reason);
        Assert.Equal(
            PackageQuery.MaximumInspectionTerms + 1,
            rejected.Value);

        Assert.Equal(
            PackageQueryRequestFailureReason.TooManyTerms,
            Rejected(PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms:
                [
                    .. Enumerable.Repeat(
                        maximum[0],
                        PackageQuery.MaximumInspectionTerms + 1),
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
        Assert.Equal(
            [
                "microsoft.extensions.configuration",
                "Microsoft.Extensions.DependencyInjection",
            ],
            match.Answers.Select(answer => answer.Value));
        PackageQueryEvidence configurationEvidence =
            termEvidence.Single(evidence =>
                evidence.Term!.Value
                    == "microsoft.extensions.configuration");
        Assert.Equal(4, configurationEvidence.Summary!.Count);
        Assert.Equal(
            [
                "net10.0: Microsoft.Extensions.Configuration [10.0.0, 11.0.0)",
                "net7.0: Microsoft.Extensions.Configuration [10.0.0, 11.0.0)",
                "net8.0: Microsoft.Extensions.Configuration 10.0.0",
            ],
            configurationEvidence.Summary.Preview.Select(value =>
                value.ToString()));
        Assert.Equal(2, source.ManifestRequests.Count);
        Assert.Equal(0, source.PackageRequests);
    }

    [Fact]
    public async Task ExecuteAsync_DependencyTargetSelectsOneCompatibleGroup()
    {
        var source = SourceFor(
            Manifest(
                "Polly.Core",
                dependencies:
                """
                <group targetFramework="netstandard2.0">
                  <dependency id="Microsoft.Bcl.AsyncInterfaces" version="8.0.0" />
                  <dependency id="Microsoft.Bcl.TimeProvider" version="8.0.0" />
                  <dependency id="System.ComponentModel.Annotations" version="5.0.0" />
                  <dependency id="System.Threading.Tasks.Extensions" version="4.5.4" />
                </group>
                <group targetFramework="net8.0" />
                """),
            "Polly.Core");

        PackageQueryPlan all = Accepted(
            PackageQuery.PlanInput(
                "Polly.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsTermKey,
                        "System.Threading.Tasks.Extensions"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        PackageQueryMatch allMatch = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                all,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal(
            PackageQueryDependencyTargetKind.All,
            all.DependencyTarget.Kind);
        Assert.Equal(
            "netstandard2.0: System.Threading.Tasks.Extensions 4.5.4",
            Assert.Single(allMatch.Evidence.Single(evidence =>
                evidence.Id == PackageQuery.DependsTermKey).Summary!.Preview)
                .ToString());

        PackageQueryPlan net12Dependency = Accepted(
            PackageQuery.PlanInput(
                "Polly.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsTermKey,
                        "System.Threading.Tasks.Extensions"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "net12.0"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        Assert.Empty(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                net12Dependency,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>());

        PackageQueryPlan net12Empty = Accepted(
            PackageQuery.PlanInput(
                "Polly.*",
                terms:
                [
                    Term(PackageQuery.DependenciesTermKey, "none"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "net12.0"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        PackageQueryMatch emptyMatch = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                net12Empty,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal(
            "net8.0",
            EvidenceProperty(
                emptyMatch,
                PackageQuery.DependencyTargetTermKey,
                "selected-group"));

        PackageQueryPlan netStandardDependency = Accepted(
            PackageQuery.PlanInput(
                "Polly.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsTermKey,
                        "System.Threading.Tasks.Extensions"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "netstandard2.0"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        PackageQueryMatch selectedMatch = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                netStandardDependency,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal(
            [
                PackageQuery.PrefixEvidenceId,
                PackageQuery.DependencyTargetTermKey,
                PackageQuery.DependsTermKey,
            ],
            selectedMatch.Evidence.Select(evidence => evidence.Id));
        Assert.Equal(
            "netstandard2.0",
            EvidenceProperty(
                selectedMatch.Evidence[1],
                "selected-group"));
        Assert.Equal(
            PackageQueryEvidenceScope.Package,
            selectedMatch.Evidence[1].Scope);
    }

    [Fact]
    public async Task ExecuteAsync_DependsEcosystemMatchesExactAndPrefixMembership()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.Exact"),
            Match("Contoso.Prefix"),
            Match("Contoso.Similar"),
        ];
        var source = new FakePackageSource(
            candidates,
            new Dictionary<string, byte[]>
            {
                ["contoso.exact@1.0.0"] = Manifest(
                    "Contoso.Exact",
                    dependencies:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Aspire.Hosting" version="[9.0.0, 10.0.0)" />
                    </group>
                    """),
                ["contoso.prefix@1.0.0"] = Manifest(
                    "Contoso.Prefix",
                    dependencies:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Aspire.Future.Integration" version="9.1.0" />
                    </group>
                    """),
                ["contoso.similar@1.0.0"] = Manifest(
                    "Contoso.Similar",
                    dependencies:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Contoso.Aspire.Hosting" version="1.0.0" />
                    </group>
                    """),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                EcosystemCatalog(
                    Ecosystem(
                        "ecosystem.aspire",
                        exactPackages: ["Aspire.Hosting"],
                        packagePrefixes: ["Aspire."])),
                terms:
                [
                    Term(
                        PackageQuery.DependsEcosystemTermKey,
                        "ecosystem.aspire"),
                ],
                maximumCandidates: 3,
                maximumMatches: null));

        PackageQueryMatch[] matches =
        [
            .. (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken)))
                .OfType<PackageQueryEvent.Match>()
                .Select(queryEvent => queryEvent.Value),
        ];

        Assert.Equal(
            ["Contoso.Exact", "Contoso.Prefix"],
            matches.Select(match => match.Package.PackageId));
        Assert.All(matches, match => Assert.Equal(
            "ecosystem.aspire",
            Assert.Single(match.Answers).Value));
        Assert.Equal(
            "net8.0: Aspire.Hosting [9.0.0, 10.0.0)"
                + " -> ecosystem.aspire (exact package Aspire.Hosting)",
            Assert.Single(matches[0].Evidence.Single(evidence =>
                evidence.Id == PackageQuery.DependsEcosystemTermKey)
                .Summary!.Preview).ToString());
        Assert.Equal(
            "net8.0: Aspire.Future.Integration 9.1.0"
                + " -> ecosystem.aspire (package prefix Aspire.)",
            Assert.Single(matches[1].Evidence.Single(evidence =>
                evidence.Id == PackageQuery.DependsEcosystemTermKey)
                .Summary!.Preview).ToString());
        Assert.Equal(3, source.ManifestRequests.Count);
        Assert.Equal(0, source.PackageRequests);
    }

    [Fact]
    public async Task ExecuteAsync_DependsEcosystemTermsAndWithinSelectedGroup()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.Both"),
            Match("Contoso.Split"),
        ];
        var source = new FakePackageSource(
            candidates,
            new Dictionary<string, byte[]>
            {
                ["contoso.both@1.0.0"] = Manifest(
                    "Contoso.Both",
                    dependencies:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Aspire.Hosting" version="9.0.0" />
                      <dependency id="Microsoft.Extensions.AI" version="9.0.0" />
                    </group>
                    """),
                ["contoso.split@1.0.0"] = Manifest(
                    "Contoso.Split",
                    dependencies:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Aspire.Hosting" version="9.0.0" />
                    </group>
                    <group targetFramework="net9.0">
                      <dependency id="Microsoft.Extensions.AI" version="9.0.0" />
                    </group>
                    """),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                EcosystemCatalog(
                    Ecosystem(
                        "ecosystem.aspire",
                        packagePrefixes: ["Aspire."]),
                    Ecosystem(
                        "ecosystem.ai",
                        packagePrefixes: ["Microsoft.Extensions.AI"])),
                terms:
                [
                    Term(
                        PackageQuery.DependsEcosystemTermKey,
                        "ecosystem.aspire"),
                    Term(
                        PackageQuery.DependsEcosystemTermKey,
                        "ecosystem.ai"),
                    Term(PackageQuery.DependencyTargetTermKey, "net8.0"),
                ],
                maximumCandidates: 2,
                maximumMatches: null));

        PackageQueryMatch match = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;

        Assert.Equal("Contoso.Both", match.Package.PackageId);
        Assert.Equal(
            2,
            match.Evidence.Count(evidence =>
                evidence.Id == PackageQuery.DependsEcosystemTermKey));
        Assert.Equal(
            ["ecosystem.ai", "ecosystem.aspire", "net8.0"],
            match.Answers
                .Select(answer => answer.Value)
                .Order(StringComparer.Ordinal));
        Assert.All(
            match.Evidence.Where(evidence =>
                evidence.Id == PackageQuery.DependsEcosystemTermKey),
            evidence => Assert.StartsWith(
                "net8.0:",
                Assert.Single(evidence.Summary!.Preview).ToString(),
                StringComparison.Ordinal));
    }

    [Fact]
    public void DependsEcosystemRejectsMalformedUnknownAndUnboundIdentities()
    {
        PackageQueryEcosystemMembershipCatalog catalog = EcosystemCatalog(
            Ecosystem("ecosystem.platform"));

        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidTermValue,
            Rejected(PackageQuery.PlanInput(
                "Contoso.*",
                catalog,
                [Term(PackageQuery.DependsEcosystemTermKey, "Aspire")]))
                .Reason);

        PackageQueryRequestFailure unknown = Rejected(
            PackageQuery.PlanInput(
                "Contoso.*",
                catalog,
                [
                    Term(
                        PackageQuery.DependsEcosystemTermKey,
                        "ecosystem.unknown"),
                ]));
        Assert.Equal(
            PackageQueryRequestFailureReason.UnknownEcosystem,
            unknown.Reason);
        Assert.Equal("ecosystem.unknown", unknown.EcosystemId);

        PackageQueryRequestFailure unbound = Rejected(
            PackageQuery.PlanInput(
                "Contoso.*",
                catalog,
                [
                    Term(
                        PackageQuery.DependsEcosystemTermKey,
                        "ecosystem.platform"),
                ]));
        Assert.Equal(
            PackageQueryRequestFailureReason
                .EcosystemPackagePopulationUnavailable,
            unbound.Reason);
        Assert.Equal("ecosystem.platform", unbound.EcosystemId);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsPortableUnboundEcosystemBeforeSourceWork()
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan portablePlan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsEcosystemTermKey,
                        "ecosystem.aspire"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CollectAsync(PackageQuery.ExecuteAsync(
                    source,
                    portablePlan,
                    TestContext.Current.CancellationToken)));

        Assert.Contains(
            "ecosystem-membership binding",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, source.LastSearchTake);
        Assert.Empty(source.ManifestRequests);
    }

    [Fact]
    public async Task ExecuteAsync_DependencyTargetAllAndAnyRemainDistinct()
    {
        var source = SourceFor(Manifest(
            "Contoso.Package",
            dependencies:
            """
            <group targetFramework="any">
              <dependency id="Universal.Dependency" version="1.0.0" />
            </group>
            <group targetFramework="net8.0">
              <dependency id="Net8.Dependency" version="8.0.0" />
            </group>
            """));

        PackageQueryPlan all = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Net8.Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "all"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        PackageQueryMatch allMatch = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                all,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence allTarget = Assert.Single(
            allMatch.Evidence,
            evidence =>
                evidence.Id == PackageQuery.DependencyTargetTermKey);
        Assert.Equal(PackageQueryEvidenceScope.Query, allTarget.Scope);
        Assert.Equal("all", EvidenceProperty(allTarget, "target"));

        PackageQueryPlan anyNet8 = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Net8.Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "any"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        Assert.Empty(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                anyNet8,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>());

        PackageQueryPlan anyUniversal = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsTermKey,
                        "Universal.Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "any"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        PackageQueryMatch anyMatch = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                anyUniversal,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence anyTarget = Assert.Single(
            anyMatch.Evidence,
            evidence =>
                evidence.Id == PackageQuery.DependencyTargetTermKey);
        Assert.Equal(PackageQueryEvidenceScope.Package, anyTarget.Scope);
        Assert.Equal("any", EvidenceProperty(anyTarget, "selected-group"));
        Assert.Equal(
            "any: Universal.Dependency 1.0.0",
            Assert.Single(anyMatch.Evidence.Single(evidence =>
                evidence.Id == PackageQuery.DependsTermKey).Summary!.Preview)
                .ToString());
    }

    [Fact]
    public async Task ExecuteAsync_DependencyTargetAnyUsesAllImplicitRuns()
    {
        var source = SourceFor(Manifest(
            "Contoso.Package",
            dependencies:
            """
            <dependency id="Early.Dependency" version="1.0.0" />
            <group targetFramework="net8.0" />
            <dependency id="Late.Dependency" version="2.0.0" />
            """));
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Late.Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "any"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));

        PackageQueryMatch match = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;

        Assert.Equal(
            "any: Late.Dependency 2.0.0",
            Assert.Single(match.Evidence.Single(evidence =>
                evidence.Id == PackageQuery.DependsTermKey).Summary!.Preview)
                .ToString());
    }

    [Fact]
    public async Task ExecuteAsync_DependencyTargetDistinguishesNoGroupsFromNoMatch()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.NoGroups"),
            Match("Contoso.Net8Only"),
        ];
        var source = new FakePackageSource(
            candidates,
            new Dictionary<string, byte[]>
            {
                ["contoso.nogroups@1.0.0"] =
                    Manifest("Contoso.NoGroups"),
                ["contoso.net8only@1.0.0"] = Manifest(
                    "Contoso.Net8Only",
                    dependencies:
                    """
                    <group targetFramework="net8.0" />
                    """),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependenciesTermKey, "none"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "net6.0"),
                ],
                maximumCandidates: 2,
                maximumMatches: null));

        PackageQueryMatch match = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;

        Assert.Equal("Contoso.NoGroups", match.Package.PackageId);
        Assert.Equal(
            PackageDependencyGroupSelectionStatus.NoDependencyGroups.ToString(),
            EvidenceProperty(
                match,
                PackageQuery.DependencyTargetTermKey,
                "selection-status"));
    }

    [Fact]
    public async Task ExecuteAsync_LicenseAnswersAreSemanticAndNuspecOnly()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.Mit"),
            Match("Contoso.Osmf"),
            Match("Contoso.Generic"),
        ];
        var source = new FakePackageSource(
            candidates,
            new Dictionary<string, byte[]>
            {
                ["contoso.mit@1.0.0"] = Manifest(
                    "Contoso.Mit",
                    license: """<license type="expression">MIT</license>"""),
                ["contoso.osmf@1.0.0"] = Manifest(
                    "Contoso.Osmf",
                    license: """<license type="file">licenses/OSMFEULA.txt</license>"""),
                ["contoso.generic@1.0.0"] = Manifest(
                    "Contoso.Generic",
                    license: """<license type="file">LICENSE.txt</license>"""),
            });

        PackageQueryMatch mit = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                Accepted(PackageQuery.PlanInput(
                    "Contoso.*",
                    terms: [Term(PackageQuery.LicenseTermKey, "MIT")],
                    maximumCandidates: 3,
                    maximumMatches: null)),
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("MIT", Assert.Single(mit.Answers).Value);
        PackageQueryEvidence mitEvidence = Assert.Single(
            mit.Evidence,
            evidence => evidence.Id == PackageQuery.LicenseTermKey);
        Assert.Equal(
            PackageLicenseDeclarationKind.Expression.ToString(),
            EvidenceProperty(mitEvidence, "declaration-kind"));
        Assert.Equal(
            "MIT",
            EvidenceProperty(mitEvidence, "declaration-value"));

        PackageQueryMatch osmf = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                Accepted(PackageQuery.PlanInput(
                    "Contoso.*",
                    terms: [Term(PackageQuery.LicenseTermKey, "OSMF")],
                    maximumCandidates: 3,
                    maximumMatches: null)),
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("OSMF", Assert.Single(osmf.Answers).Value);
        PackageQueryEvidence osmfEvidence = Assert.Single(
            osmf.Evidence,
            evidence => evidence.Id == PackageQuery.LicenseTermKey);
        Assert.Equal(
            PackageLicenseDeclarationKind.File.ToString(),
            EvidenceProperty(osmfEvidence, "declaration-kind"));
        Assert.Equal(
            "licenses/OSMFEULA.txt",
            EvidenceProperty(osmfEvidence, "declaration-value"));

        PackageQueryMatch[] any =
        [
            .. (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                Accepted(PackageQuery.PlanInput(
                    "Contoso.*",
                    terms: [Term(PackageQuery.LicenseTermKey, "any")],
                    maximumCandidates: 3,
                    maximumMatches: null)),
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()
            .Select(item => item.Value),
        ];
        Assert.Equal(3, any.Length);
        Assert.All(any, match =>
            Assert.Equal("true", Assert.Single(match.Answers).Value));
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
        Assert.Equal(
            ["net8.0: Alpha [1.0.0]", "net9.0: Alpha [2.0.0]"],
            summary.Preview.Select(item => item.ToString()));
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
        Assert.Equal("true", Assert.Single(match.Answers,
            answer => answer.Id == PackageQuery.ToolTermKey).Value);
        Assert.Equal(
            "DotnetTool",
            EvidenceProperty(match.Evidence[^1], "package-type"));
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
        Assert.All(
            anyTools,
            item => Assert.Equal(
                "true",
                Assert.Single(item.Answers).Value));
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
        Assert.Equal(
            "1",
            EvidenceProperty(
                bothVersions[0].Evidence[^1],
                "settings-version"));
        Assert.Equal(
            "2",
            EvidenceProperty(
                bothVersions[1].Evidence[^1],
                "settings-version"));
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
            ["SKILLS/demo/skill.MD", "skills/SKILL.md"],
            skillEvents.OfType<PackageQueryEvent.Match>()
                .Select(item => Assert.Single(
                    item.Value.Evidence[^1].Summary!.Preview).ToString()));
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2", "Contoso.Library"],
            content.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_AssemblyReferencesMatchAcrossAdmittedGroups()
    {
        byte[] caller = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        byte[] target = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var archive = FakePackageContent.FromBytes(
            ("ref/net8.0/Caller.dll", caller),
            ("lib/net9.0/Caller.dll", caller),
            ("lib/net10.0/nested/Caller.dll", caller),
            ("ref/net11.0/Caller.dll", caller),
            ("lib/net8.0/Target.dll", target),
            ("lib/net7.0/Native.dll", WithoutManagedMetadata(caller)),
            ("runtimes/linux-x64/lib/net8.0/Excluded.dll", caller));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Package"] = archive,
            });
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(
                            PackageQuery.ReferencesTermKey,
                            "ilinspector.analysis.callergraphtarget"),
                        Term(PackageQuery.ReferencesTermKey, "System.Runtime"),
                    ],
                    MaximumCandidates: 1,
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
                "ilinspector.analysis.callergraphtarget",
                "System.Runtime",
            ],
            match.Answers.Select(answer => answer.Value));
        PackageQueryEvidence targetEvidence = Assert.Single(
            match.Evidence,
            evidence => evidence.Term?.Value
                == "ilinspector.analysis.callergraphtarget");
        PackageQueryEvidenceSummary targetSummary =
            Assert.IsType<PackageQueryEvidenceSummary>(
                targetEvidence.Summary);
        Assert.Equal(4, targetSummary.Count);
        Assert.Equal(
            [
                "net10.0: lib/net10.0/nested/Caller.dll -> ILInspector.Analysis.CallerGraphTarget",
                "net9.0: lib/net9.0/Caller.dll -> ILInspector.Analysis.CallerGraphTarget",
                "net11.0: ref/net11.0/Caller.dll -> ILInspector.Analysis.CallerGraphTarget",
            ],
            targetSummary.Preview.Select(item => item.ToString()));
        Assert.Equal(
            PackageQuery.MaximumEvidencePreviewItems,
            targetSummary.Preview.Length);
        Assert.DoesNotContain(
            archive.EntryRequests,
            path => path.StartsWith("runtimes/", StringComparison.Ordinal));
        Assert.Contains("lib/net7.0/Native.dll", archive.EntryRequests);
    }

    [Fact]
    public async Task ExecuteAsync_AssemblyReferenceNonmatchIsNotAResult()
    {
        byte[] target = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Package"] = FakePackageContent.FromBytes(
                    ("lib/net8.0/Target.dll", target)),
            });
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(
                            PackageQuery.ReferencesTermKey,
                            "Contoso.NotReferenced"),
                    ],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Empty(events.OfType<PackageQueryEvent.Failure>());
    }

    [Fact]
    public async Task ExecuteAsync_MalformedAdmittedAssemblyRemainsVisible()
    {
        byte[] caller = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Package"] = FakePackageContent.FromBytes(
                    ("lib/net8.0/Caller.dll", caller),
                    ("lib/net8.0/Broken.dll", [0x42, 0x61, 0x64])),
            });
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(
                            PackageQuery.ReferencesTermKey,
                            "ILInspector.Analysis.CallerGraphTarget"),
                    ],
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
        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
    }

    [Fact]
    public async Task ExecuteAsync_IncompleteAssemblyReferenceTableRemainsVisible()
    {
        byte[] caller = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        await AssertAssemblyReferenceEvaluationFailureAsync(
            FakePackageContent.FromBytes(
                ("lib/net8.0/Caller.dll",
                    WithMalformedAssemblyReferenceName(caller))),
            expectedEntryRequests: 1);
    }

    [Fact]
    public async Task
        ExecuteAsync_CompleteMetadataModuleWithoutAssemblyDefinitionIsSkipped()
    {
        FakePackageContent archive = FakePackageContent.FromBytes(
            ("lib/net8.0/Module.dll", AssemblyReferenceModule()));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Package"] = archive,
            });
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(
                            PackageQuery.ReferencesTermKey,
                            "Contoso.Reference"),
                    ],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Empty(events.OfType<PackageQueryEvent.Failure>());
        Assert.Equal(["lib/net8.0/Module.dll"], archive.EntryRequests);
    }

    [Fact]
    public async Task
        ExecuteAsync_IncompleteAssemblyReferenceTableInMetadataModuleRemainsVisible()
    {
        await AssertAssemblyReferenceEvaluationFailureAsync(
            FakePackageContent.FromBytes(
                ("lib/net8.0/Module.dll",
                    WithMalformedAssemblyReferenceName(
                        AssemblyReferenceModule()))),
            expectedEntryRequests: 1);
    }

    [Fact]
    public async Task
        ExecuteAsync_EmptyAssemblyReferenceNameInMetadataModuleRemainsVisible()
    {
        await AssertAssemblyReferenceEvaluationFailureAsync(
            FakePackageContent.FromBytes(
                ("lib/net8.0/Module.dll",
                    WithEmptyAssemblyReferenceName(
                        AssemblyReferenceModule()))),
            expectedEntryRequests: 1);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyAssemblyDefinitionNameRemainsVisible()
    {
        byte[] caller = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        await AssertAssemblyReferenceEvaluationFailureAsync(
            FakePackageContent.FromBytes(
                ("lib/net8.0/Caller.dll",
                    WithEmptyAssemblyDefinitionName(caller))),
            expectedEntryRequests: 1);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyModuleDefinitionNameRemainsVisible()
    {
        await AssertAssemblyReferenceEvaluationFailureAsync(
            FakePackageContent.FromBytes(
                ("lib/net8.0/Module.dll",
                    WithEmptyModuleDefinitionName(
                        AssemblyReferenceModule()))),
            expectedEntryRequests: 1);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedModuleDefinitionNameRemainsVisible()
    {
        await AssertAssemblyReferenceEvaluationFailureAsync(
            FakePackageContent.FromBytes(
                ("lib/net8.0/Module.dll",
                    WithMalformedModuleDefinitionName(
                        AssemblyReferenceModule()))),
            expectedEntryRequests: 1);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedAssemblyDefinitionNameRemainsVisible()
    {
        byte[] caller = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        await AssertAssemblyReferenceEvaluationFailureAsync(
            FakePackageContent.FromBytes(
                ("lib/net8.0/Caller.dll",
                    WithMalformedAssemblyDefinitionName(caller))),
            expectedEntryRequests: 1);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedAssemblyReferenceNameRemainsVisible()
    {
        await AssertAssemblyReferenceEvaluationFailureAsync(
            FakePackageContent.FromBytes(
                ("lib/net8.0/Module.dll",
                    WithMalformedAssemblyReferenceNameUtf8(
                        AssemblyReferenceModule()))),
            expectedEntryRequests: 1);
    }

    [Fact]
    public async Task ExecuteAsync_AssemblyReferenceBudgetsRejectBeforeExpansion()
    {
        byte[] caller = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var tooManyEntries = Enumerable
            .Range(0, PackageQuery.MaximumAssemblyReferenceAssets + 1)
            .Select(index =>
                ($"lib/net8.0/Caller{index:D3}.dll", caller))
            .ToArray();
        await AssertAssemblyReferenceEvaluationFailureAsync(
            FakePackageContent.FromBytes(tooManyEntries),
            expectedEntryRequests: 0);

        await AssertAssemblyReferenceEvaluationFailureAsync(
            FakePackageContent.FromBytesWithDeclaredLengths(
                ("lib/net8.0/Caller.dll",
                    caller,
                    PackageQuery.MaximumAssemblyReferenceEntryBytes + 1)),
            expectedEntryRequests: 0);

        var aggregateEntries = Enumerable
            .Range(
                0,
                checked((int)(
                    PackageQuery.MaximumAssemblyReferenceTotalBytes
                    / PackageQuery.MaximumAssemblyReferenceEntryBytes)) + 1)
            .Select(index =>
                ($"lib/net8.0/Caller{index:D2}.dll",
                    caller,
                    PackageQuery.MaximumAssemblyReferenceEntryBytes))
            .ToArray();
        await AssertAssemblyReferenceEvaluationFailureAsync(
            FakePackageContent.FromBytesWithDeclaredLengths(
                aggregateEntries),
            expectedEntryRequests: 0);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateAssemblyAssetPathRemainsVisible()
    {
        byte[] caller = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        await AssertAssemblyReferenceEvaluationFailureAsync(
            FakePackageContent.FromBytesWithDuplicatePath(
                "lib/net8.0/Caller.dll",
                caller),
            expectedEntryRequests: 0);
    }

    [Fact]
    public async Task ExecuteAsync_RealPackageMatchesAssemblyReferenceWithinBudgets()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PackageQuery",
            "microsoft.extensions.http.10.0.0.nupkg");
        var archive = new InMemoryPackageContent(
            await File.ReadAllBytesAsync(
                path,
                TestContext.Current.CancellationToken),
            fromCache: true,
            producerKey: "nuget.org");
        PackageCompileAsset[] assets =
        [
            .. PackageCompileAssetSelector.Select(
                archive,
                "Microsoft.Extensions.Http").CandidateAssets,
        ];
        Assert.Equal(5, assets.Length);
        long[] lengths =
        [
            .. assets.Select(asset =>
            {
                Assert.True(
                    archive.TryGetEntryLength(asset.Path, out long length));
                return length;
            }),
        ];
        Assert.Equal(93_456, lengths.Max());
        Assert.Equal(450_880, lengths.Sum());
        Assert.All(lengths, length => Assert.InRange(
            length,
            1,
            PackageQuery.MaximumAssemblyReferenceEntryBytes));
        Assert.InRange(
            lengths.Sum(),
            1,
            PackageQuery.MaximumAssemblyReferenceTotalBytes);

        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Microsoft.Extensions.Http"] = archive,
            });
        var source = SourceFor(
            Manifest("Microsoft.Extensions.Http"),
            "Microsoft.Extensions.Http");
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Microsoft.Extensions.Http",
                    [
                        Term(
                            PackageQuery.ReferencesTermKey,
                            "Microsoft.Extensions.DependencyInjection.Abstractions"),
                    ],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidenceSummary summary =
            Assert.IsType<PackageQueryEvidenceSummary>(
                Assert.Single(
                    match.Evidence,
                    evidence =>
                        evidence.Id == PackageQuery.ReferencesTermKey)
                    .Summary);
        Assert.Equal(5, summary.Count);
        Assert.Equal(
            PackageQuery.MaximumEvidencePreviewItems,
            summary.Preview.Length);
    }

    [Fact]
    public async Task ExecuteAsync_ReferencePackFitsAssemblyReferenceBudgets()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "CompleteRestoration",
            "microsoft.netcore.app.ref.10.0.10.nupkg");
        var archive = new InMemoryPackageContent(
            await File.ReadAllBytesAsync(
                path,
                TestContext.Current.CancellationToken),
            fromCache: true,
            producerKey: "nuget.org");
        PackageCompileAsset[] assets =
        [
            .. PackageCompileAssetSelector.Select(
                archive,
                "Microsoft.NETCore.App.Ref").CandidateAssets,
        ];
        Assert.Equal(167, assets.Length);
        long[] lengths =
        [
            .. assets.Select(asset =>
            {
                Assert.True(
                    archive.TryGetEntryLength(asset.Path, out long length));
                return length;
            }),
        ];
        Assert.Equal(862_544, lengths.Max());
        Assert.Equal(6_046_168, lengths.Sum());
        Assert.InRange(
            lengths.Max(),
            1,
            PackageQuery.MaximumAssemblyReferenceEntryBytes);
        Assert.InRange(
            lengths.Sum(),
            1,
            PackageQuery.MaximumAssemblyReferenceTotalBytes);

        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Microsoft.NETCore.App.Ref"] = archive,
            });
        var source = SourceFor(
            Manifest("Microsoft.NETCore.App.Ref"),
            "Microsoft.NETCore.App.Ref");
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Microsoft.NETCore.App.Ref",
                    [
                        Term(
                            PackageQuery.ReferencesTermKey,
                            "System.Runtime"),
                    ],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidenceSummary summary =
            Assert.IsType<PackageQueryEvidenceSummary>(
                Assert.Single(
                    match.Evidence,
                    evidence =>
                        evidence.Id == PackageQuery.ReferencesTermKey)
                    .Summary);
        Assert.True(summary.Count > PackageQuery.MaximumEvidencePreviewItems);
        Assert.Equal(
            PackageQuery.MaximumEvidencePreviewItems,
            summary.Preview.Length);
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
            "DotnetTool",
            EvidenceProperty(match.Evidence[^1], "package-type"));
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
            "System.",
            EvidenceProperty(Assert.Single(match.Evidence), "prefix"));
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
        Assert.Equal(
            "none",
            Assert.Single(emptyMatch.Answers).Value);
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

    private static PackageQueryEcosystemMembershipCatalog EcosystemCatalog(
        params PackageQueryEcosystemMembershipDeclaration[] declarations) =>
        new(declarations);

    private static PackageQueryEcosystemMembershipDeclaration Ecosystem(
        string id,
        string[]? exactPackages = null,
        string[]? packagePrefixes = null) =>
        new(
            WorkspaceEcosystemRegistrationId.Create(id),
            (exactPackages ?? []).Select(package => new PackageCoordinate(package)),
            (packagePrefixes ?? []).Select(prefix =>
                new PackagePrefixDeclaration(prefix)));

    private static string EvidenceProperty(
        PackageQueryMatch match,
        string evidenceId,
        string propertyName) =>
        EvidenceProperty(
            Assert.Single(
                match.Evidence,
                evidence => evidence.Id == evidenceId),
            propertyName);

    private static string EvidenceProperty(
        PackageQueryEvidence evidence,
        string propertyName) =>
        Assert.Single(
            evidence.Properties,
            property => property.Name == propertyName).Value;

    private static PortableQueryTerm[] InspectionTerms(int count) =>
    [
        .. Enumerable.Range(0, count).Select(index =>
            Term(
                PackageQuery.DependsTermKey,
                $"Contoso.Dependency.{index:D2}")),
    ];

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
        string readme = "",
        string license = "") =>
        Encoding.UTF8.GetBytes(
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{{packageId}}</id>
                <version>{{version}}</version>
                <authors>Manifest Author</authors>
                <description>Package query test.</description>
                {{license}}
                {{packageTypes}}
                {{readme}}
                <dependencies>{{dependencies}}</dependencies>
              </metadata>
            </package>
            """);

    private static byte[] WithoutManagedMetadata(byte[] image)
    {
        byte[] native = image.ToArray();
        using var reader = new PEReader(
            new MemoryStream(native, writable: false));
        PEHeader header = Assert.IsType<PEHeader>(reader.PEHeaders.PEHeader);
        int directories = reader.PEHeaders.PEHeaderStartOffset
            + 24
            + (header.Magic == PEMagic.PE32Plus ? 112 : 96);
        native.AsSpan(directories + (14 * 8), 8).Clear();
        return native;
    }

    private static byte[] WithMalformedAssemblyReferenceName(byte[] image)
    {
        byte[] malformed = image.ToArray();
        using var reader = new PEReader(
            new MemoryStream(malformed, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        Assert.NotEmpty(metadata.AssemblyReferences);
        Assert.True(
            metadata.GetHeapSize(HeapIndex.Blob) <= ushort.MaxValue
            && metadata.GetHeapSize(HeapIndex.String) <= ushort.MaxValue);
        int nameOffset = reader.PEHeaders.MetadataStartOffset
            + metadata.GetTableMetadataOffset(TableIndex.AssemblyRef)
            + (4 * sizeof(ushort))
            + sizeof(uint)
            + sizeof(ushort);
        BinaryPrimitives.WriteUInt16LittleEndian(
            malformed.AsSpan(nameOffset, sizeof(ushort)),
            ushort.MaxValue);
        return malformed;
    }

    private static byte[] WithEmptyAssemblyReferenceName(byte[] image)
    {
        byte[] malformed = image.ToArray();
        using var reader = new PEReader(
            new MemoryStream(malformed, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        Assert.NotEmpty(metadata.AssemblyReferences);
        Assert.True(
            metadata.GetHeapSize(HeapIndex.Blob) <= ushort.MaxValue
            && metadata.GetHeapSize(HeapIndex.String) <= ushort.MaxValue);
        int nameOffset = reader.PEHeaders.MetadataStartOffset
            + metadata.GetTableMetadataOffset(TableIndex.AssemblyRef)
            + (4 * sizeof(ushort))
            + sizeof(uint)
            + sizeof(ushort);
        malformed.AsSpan(nameOffset, sizeof(ushort)).Clear();
        return malformed;
    }

    private static byte[] WithEmptyAssemblyDefinitionName(byte[] image)
    {
        byte[] malformed = image.ToArray();
        using var reader = new PEReader(
            new MemoryStream(malformed, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        Assert.True(metadata.IsAssembly);
        Assert.True(
            metadata.GetHeapSize(HeapIndex.Blob) <= ushort.MaxValue
            && metadata.GetHeapSize(HeapIndex.String) <= ushort.MaxValue);
        int nameOffset = reader.PEHeaders.MetadataStartOffset
            + metadata.GetTableMetadataOffset(TableIndex.Assembly)
            + sizeof(uint)
            + (4 * sizeof(ushort))
            + sizeof(uint)
            + sizeof(ushort);
        malformed.AsSpan(nameOffset, sizeof(ushort)).Clear();
        return malformed;
    }

    private static byte[] WithEmptyModuleDefinitionName(byte[] image)
    {
        byte[] malformed = image.ToArray();
        using var reader = new PEReader(
            new MemoryStream(malformed, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        Assert.True(metadata.GetHeapSize(HeapIndex.String) <= ushort.MaxValue);
        int nameOffset = reader.PEHeaders.MetadataStartOffset
            + metadata.GetTableMetadataOffset(TableIndex.Module)
            + sizeof(ushort);
        malformed.AsSpan(nameOffset, sizeof(ushort)).Clear();
        return malformed;
    }

    private static byte[] WithMalformedModuleDefinitionName(byte[] image) =>
        WithMalformedUtf8(
            image,
            metadata => metadata.GetModuleDefinition().Name);

    private static byte[] WithMalformedAssemblyDefinitionName(byte[] image) =>
        WithMalformedUtf8(
            image,
            metadata => metadata.GetAssemblyDefinition().Name);

    private static byte[] WithMalformedAssemblyReferenceNameUtf8(byte[] image) =>
        WithMalformedUtf8(
            image,
            metadata =>
                metadata.GetAssemblyReference(
                    Assert.Single(metadata.AssemblyReferences)).Name);

    private static byte[] WithMalformedUtf8(
        byte[] image,
        Func<MetadataReader, StringHandle> selectHandle)
    {
        byte[] malformed = image.ToArray();
        using var reader = new PEReader(
            new MemoryStream(malformed, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        StringHandle handle = selectHandle(metadata);
        Assert.False(handle.IsNil);
        int stringOffset = reader.PEHeaders.MetadataStartOffset
            + metadata.GetHeapMetadataOffset(HeapIndex.String)
            + MetadataTokens.GetHeapOffset(handle);
        malformed[stringOffset] = 0xff;
        return malformed;
    }

    private static byte[] AssemblyReferenceModule()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("Module.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contoso.Reference"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            @namespace: default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

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

    private static async Task AssertAssemblyReferenceEvaluationFailureAsync(
        FakePackageContent archive,
        int expectedEntryRequests)
    {
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Package"] = archive,
            });
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(
                            PackageQuery.ReferencesTermKey,
                            "ILInspector.Analysis.CallerGraphTarget"),
                    ],
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
        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Equal(expectedEntryRequests, archive.EntryRequests.Count);
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
            | PackageSourceCapabilities.Manifest
            | PackageSourceCapabilities.VersionEnumeration;

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
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageCandidateObservation[] candidates =
            [
                .. matches
                    .Where(match => StringComparer.OrdinalIgnoreCase.Equals(
                        match.Id,
                        packageId))
                    .Select(match =>
                        _results.Candidate(
                            PackageSourceCoordinate.Create(
                                match.Id,
                                match.Version),
                            PackageDiscoveryContract.CompleteVersionEnumeration,
                            PackageListingState.Listed)),
            ];
            return Task.FromResult(
                _results.SucceededVersions(
                    _results.Versions(
                        candidates,
                        hasAuthoritativeListingState: true)));
        }

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

    private sealed class FakePackageContent
        : IPackageContent, IPackageContentEntryManifest
    {
        readonly IReadOnlyDictionary<string, byte[]> _entries;
        readonly IReadOnlyDictionary<string, long> _declaredLengths;
        readonly IReadOnlyList<string> _entryPaths;

        public FakePackageContent(
            params (string Path, string Content)[] entries)
            : this(
                entries.ToDictionary(
                    entry => entry.Path,
                    entry => Encoding.UTF8.GetBytes(entry.Content),
                    StringComparer.Ordinal),
                declaredLengths: null)
        {
        }

        private FakePackageContent(
            IReadOnlyDictionary<string, byte[]> entries,
            IReadOnlyDictionary<string, long>? declaredLengths,
            IReadOnlyList<string>? entryPaths = null)
        {
            _entries = entries;
            _declaredLengths = declaredLengths
                ?? entries.ToDictionary(
                    entry => entry.Key,
                    entry => (long)entry.Value.LongLength,
                    StringComparer.Ordinal);
            _entryPaths = entryPaths ?? [.. entries.Keys];
        }

        public static FakePackageContent FromBytes(
            params (string Path, byte[] Content)[] entries) =>
            new(
                entries.ToDictionary(
                    entry => entry.Path,
                    entry => entry.Content,
                    StringComparer.Ordinal),
                declaredLengths: null);

        public static FakePackageContent FromBytesWithDeclaredLengths(
            params (string Path, byte[] Content, long DeclaredLength)[] entries) =>
            new(
                entries.ToDictionary(
                    entry => entry.Path,
                    entry => entry.Content,
                    StringComparer.Ordinal),
                entries.ToDictionary(
                    entry => entry.Path,
                    entry => entry.DeclaredLength,
                    StringComparer.Ordinal));

        public static FakePackageContent FromBytesWithDuplicatePath(
            string path,
            byte[] content) =>
            new(
                new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    [path] = content,
                },
                declaredLengths: null,
                entryPaths: [path, path]);

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

        public IEnumerable<string> EnumerateEntries() => _entryPaths;

        public bool TryGetEntryLength(
            string relativePath,
            out long length) =>
            _declaredLengths.TryGetValue(relativePath, out length);

        public IReadOnlyList<PackageContentEntry>
            EnumerateEntriesWithLengths() =>
        [
            .. _declaredLengths.Select(entry =>
                new PackageContentEntry(entry.Key, entry.Value)),
        ];
    }
}
