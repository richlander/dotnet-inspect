using QuerySpace;
using DotnetInspector.Queries.EmbeddedFixtures;
using QuerySpace.Operations;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class LibraryQueryTests
{
    [Fact]
    public void Route_ExposesReferenceQualificationAndResultSelection()
    {
        IQueryOperationRoute route = LibraryQuery.OperationRoute;

        Assert.Equal(LibraryQuery.OperationRouteIdentity, route.Identity);
        Assert.Equal(
            LibraryQuery.OperationIdentity,
            route.OperationIdentity);
        Assert.Equal(
            LibraryQuery.OperationSubjectRole,
            route.SubjectRole);
        Assert.Equal(
            LibraryQuery.OperationResultGrain,
            route.ResultGrain);
        Assert.Equal(
            [LibraryQuery.OperationLibrariesRowSet],
            route.RowSets);
        Assert.Equal(
            LibraryQuery.OperationProfileIdentity,
            route.ProfileIdentity);
        Assert.Equal(
            [LibraryQuery.CandidatesDimension],
            route.Capabilities.Dimensions);
        Assert.Equal(
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
            ],
            route.Capabilities.Stages);
        Assert.Empty(route.Capabilities.Orders);
        Assert.Collection(
            route.Capabilities.Terms,
            term =>
            {
                Assert.Equal(
                    LibraryQuery.ReferencesTermKey,
                    term.Binding.Key);
                Assert.Equal(
                    QueryOperationTermRole.SubjectQualification,
                    term.Binding.Role);
                Assert.Equal(
                    [PortableQueryOperator.Equal],
                    term.Operators);
            });
    }

    [Fact]
    public void Plan_PreservesRepeatedReferencesAndOrderedRows()
    {
        LibraryQueryPlan plan = Accepted(
            LibraryQuery.Plan(
                new(
                    [
                        Reference("System.Runtime"),
                        Reference("System.Collections"),
                    ],
                    MaximumCandidates: 12,
                    RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Window(2, 5),
                        RowSelectionIntentOperation<string>.Tail(2),
                    ]))));

        Assert.Equal(12, plan.MaximumCandidates);
        Assert.Equal(
            ["System.Collections", "System.Runtime"],
            plan.References.Select(reference =>
                reference.DisplayName));
        Assert.Equal(
            [
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Tail,
            ],
            plan.RowSelection.Operations.Select(operation =>
                operation.Kind));
    }

    [Fact]
    public void Plan_CollapsesEquivalentReferenceSpellings()
    {
        LibraryQueryPlan plan = Accepted(
            LibraryQuery.Plan(
                new(
                    [
                        Reference("System.Runtime"),
                        Reference("system.runtime"),
                    ])));

        LibraryQueryReferencePredicate reference =
            Assert.Single(plan.References);
        Assert.Equal("System.Runtime", reference.DisplayName);
    }

    [Fact]
    public void Execute_ReturnsLibraryGrainReferenceMatch()
    {
        string path = typeof(EmbeddedSourceFixture).Assembly.Location;
        LibraryQueryPlan plan = Accepted(
            LibraryQuery.Plan(
                new([Reference("system.runtime")])));

        LibraryQueryDocument document = LibraryQuery.Execute(
            [
                new(
                    path,
                    "fixture",
                    "1.0.0",
                    AssemblySetSourceKind.Directory,
                    "net11.0"),
            ],
            [],
            plan,
            TestContext.Current.CancellationToken);

        LibraryQueryMatch match = Assert.Single(document.Results);
        Assert.Equal(
            "DotnetInspector.Queries.EmbeddedFixtures",
            match.Library.ToString());
        Assert.Equal("System.Runtime", Assert.Single(
            match.MatchedReferences).ToString());
        Assert.Empty(document.Failures);
        Assert.True(document.Summary.IsComplete);
    }

    [Fact]
    public async Task ExecuteParticipants_PreservesProductIssuedMetadata()
    {
        string assemblyPath = typeof(LibraryQueryTests).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        assemblyPath,
                        AssemblyResolutionProvenance.Local(
                            "Library Query participant tests")),
                    new TestBindingPolicy()),
            ]);
        var references = Assert.IsType<AssemblyContextEntry<
            System.Collections.Immutable.ImmutableArray<
                AssemblyReferenceIdentity>>.Available>(
                    AssemblyContextReferencesQuery.ExecuteParticipant(
                        group,
                        group.Participants[0]));
        string reference = Assert.Single(
            references.Value,
            candidate => candidate.Name == "System.Runtime").Name;
        LibraryQueryPlan plan = Accepted(
            LibraryQuery.Plan(new([Reference(reference)])));

        LibraryQueryDocument document = LibraryQuery.ExecuteParticipants(
            group,
            [
                new(
                    group.Participants[0],
                    "ref/net11.0/LibraryQuery.Tests.dll",
                    "LibraryQuery.Package",
                    "1.2.3",
                    AssemblySetSourceKind.Package,
                    "net11.0"),
            ],
            plan,
            TestContext.Current.CancellationToken);

        LibraryQueryMatch match = Assert.Single(document.Results);
        Assert.Equal(
            "DotnetInspector.Queries.Tests",
            match.Library.ToString());
        Assert.Equal(
            "ref/net11.0/LibraryQuery.Tests.dll",
            match.Path.ToString());
        Assert.Equal("LibraryQuery.Package", match.Source.ToString());
        Assert.Equal("1.2.3", match.Version?.ToString());
        Assert.Equal(AssemblySetSourceKind.Package, match.SourceKind);
        Assert.Equal("net11.0", match.TargetFramework?.ToString());
        Assert.Equal(reference, Assert.Single(match.MatchedReferences).ToString());
        Assert.Equal(1, document.Summary.PopulationCandidates);
        Assert.Equal(1, document.Summary.Candidates);
        Assert.True(document.Summary.IsComplete);
    }

    [Fact]
    public void Execute_SeparatesCandidateBoundFromResultRows()
    {
        string path = typeof(EmbeddedSourceFixture).Assembly.Location;
        LibraryQueryPlan plan = Accepted(
            LibraryQuery.Plan(
                new(
                    MaximumCandidates: 1,
                    RowSelection: RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Head(1),
                    ]))));

        LibraryQueryDocument document = LibraryQuery.Execute(
            [
                new(
                    path,
                    "z-source",
                    null,
                    AssemblySetSourceKind.Directory),
                new(
                    path,
                    "a-source",
                    null,
                    AssemblySetSourceKind.Directory),
            ],
            [],
            plan,
            TestContext.Current.CancellationToken);

        Assert.Single(document.Results);
        Assert.Equal("a-source", document.Results[0].Source.ToString());
        Assert.Equal(2, document.Summary.PopulationCandidates);
        Assert.Equal(1, document.Summary.Candidates);
        Assert.Equal(
            LibraryQueryIncompleteReason.CandidateLimit,
            document.Summary.IncompleteReasons);
        Assert.Equal(
            RowSelectionStageKind.Head,
            Assert.Single(plan.RowSelection.Operations).Kind);
    }

    [Fact]
    public async Task ExecuteToEnvelope_PreservesPopulationFailure()
    {
        string missingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"missing-library-query-{Guid.NewGuid():N}");
        using var httpClient = new HttpClient();
        using AssemblySet population =
            await AssemblySetResolver.CollectAsync(
                httpClient,
                new AssemblySetRequest
                {
                    Directories = [missingDirectory],
                    SourceOrder = [AssemblySetSourceKind.Directory],
                    CancellationToken =
                        TestContext.Current.CancellationToken,
                });
        LibraryQueryPlan plan = Accepted(
            LibraryQuery.Plan(new()));

        InspectionEnvelope<LibraryQueryDocument> envelope =
            LibraryQueryInspection.Execute(
                population,
                plan,
                TestContext.Current.CancellationToken);

        Assert.Empty(envelope.Content.Results);
        LibraryQueryFailure failure =
            Assert.Single(envelope.Content.Failures);
        Assert.Equal(LibraryQueryFailureKind.Population, failure.Kind);
        Assert.Equal(
            LibraryQueryIncompleteReason.PopulationFailure,
            envelope.Content.Summary.IncompleteReasons);
        InspectionShare.NonProjectable share =
            Assert.IsType<InspectionShare.NonProjectable>(
                envelope.Share);
        Assert.Equal("library-query/share", share.Path);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public void Plan_RejectsUnsupportedOperatorAndTop()
    {
        LibraryQueryPlanResult operatorResult =
            LibraryQuery.ResolveIntent(
                PortableQueryIntent.Create(
                    [
                        new(
                            LibraryQuery.ReferencesTermKey,
                            PortableQueryOperator.NotEqual,
                            "System.Runtime"),
                    ],
                    [
                        new(
                            LibraryQuery.CandidatesDimension,
                            1),
                    ],
                    [],
                    []),
                TestContext.Current.CancellationToken);
        LibraryQueryPlanResult topResult =
            LibraryQuery.ResolveIntent(
                PortableQueryIntent.Create(
                    [],
                    [
                        new(
                            LibraryQuery.CandidatesDimension,
                            1),
                    ],
                    [PortableQueryStage.Top(1)],
                    []),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            PortableQueryFailureReason.OperatorNotAdmitted,
            Assert.IsType<LibraryQueryPlanResult.Rejected>(
                operatorResult).Failure.Reason);
        Assert.Equal(
            PortableQueryFailureReason.StageNotAdmitted,
            Assert.IsType<LibraryQueryPlanResult.Rejected>(
                topResult).Failure.Reason);
    }

    private static PortableQueryTerm Reference(string name) =>
        new(
            LibraryQuery.ReferencesTermKey,
            PortableQueryOperator.Equal,
            name);

    private static LibraryQueryPlan Accepted(
        LibraryQueryPlanResult result) =>
        Assert.IsType<LibraryQueryPlanResult.Accepted>(result).Plan;

    private sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
