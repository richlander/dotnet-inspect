using System.Collections.Immutable;
using DotnetInspector.PortableQueries;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class LibraryQueryTests
{
    [Fact]
    public void OperationRoute_ProjectsReferencesCapability()
    {
        LibraryQueryRegisteredTerm term =
            Assert.Single(LibraryQuery.RegisteredTerms);

        Assert.Equal(LibraryQuery.ReferencesTermKey, term.Descriptor.Key);
        Assert.Equal(
            [PortableQueryOperator.Equal],
            term.Operators);
        Assert.Equal(
            LibraryQuery.OperationLibrariesRowSet,
            Assert.Single(LibraryQuery.OperationRoute.RowSets));
    }

    [Fact]
    public void ResolveIntent_RejectsInvalidOperandAtomically()
    {
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [
                new(
                    LibraryQuery.ReferencesTermKey,
                    PortableQueryOperator.Equal,
                    "System.Runtime"),
                new(
                    LibraryQuery.ReferencesTermKey,
                    PortableQueryOperator.Equal,
                    "System.Runtime, Version=11.0.0.0"),
            ],
            [],
            [],
            []);

        Assert.IsType<LibraryQueryPlanResult.Rejected>(
            LibraryQuery.ResolveIntent(
                intent,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ResolveIntent_CollapsesCaseVariants()
    {
        LibraryQueryPlan plan = Accepted(
            LibraryQuery.CreateIntent(
                ["System.Runtime", "system.runtime"]));

        Assert.Equal(["System.Runtime"], plan.RequiredReferences);
    }

    [Fact]
    public async Task Execute_MatchesDirectReferencesWithAndSemantics()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            typeof(LibraryQueryTests).Assembly.Location);
        string[] names = DirectReferences(group);
        Assert.True(names.Length >= 2);

        LibraryQueryDocument matching = LibraryQuery.Execute(
            LibraryQueryPopulation.FromGroup(group),
            Accepted(LibraryQuery.CreateIntent(names.Take(2))));
        LibraryQueryDocument nonmatching = LibraryQuery.Execute(
            LibraryQueryPopulation.FromGroup(group),
            Accepted(LibraryQuery.CreateIntent(
                [names[0], "No.Such.Direct.Reference"])));

        LibraryQueryMatch match = Assert.Single(matching.Results);
        Assert.Equal(0, match.Occurrence);
        Assert.Equal(names.Take(2), match.Answers);
        Assert.Empty(nonmatching.Results);
        Assert.Equal(
            LibraryQueryCompletionKind.Complete,
            nonmatching.Summary.Completion);
    }

    [Fact]
    public async Task Execute_PreservesDuplicateOccurrencesInOrder()
    {
        string path = typeof(LibraryQueryTests).Assembly.Location;
        ResolvedAssemblyReference first =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("first"));
        ResolvedAssemblyReference second =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("second"));
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                new(first, policy),
                new(second, policy),
            ]);
        string reference = DirectReferences(group)[0];

        LibraryQueryDocument document = LibraryQuery.Execute(
            LibraryQueryPopulation.FromGroup(group),
            Accepted(LibraryQuery.CreateIntent([reference])));

        Assert.Equal([0, 1], document.Results.Select(row => row.Occurrence));
        Assert.Equal(2, document.Summary.Population);
        Assert.True(document.Summary.IsExact);
    }

    [Fact]
    public async Task Execute_PreservesTypedAcquisitionFailure()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            typeof(LibraryQueryTests).Assembly.Location);
        var failure = new CandidateOpenFailure(
            CandidateOpenFailureKind.Unreadable,
            "Access was denied.");
        var population = new LibraryQueryPopulation(
            group,
            [
                new LibraryQueryPopulationOccurrence.Unavailable(
                    0,
                    "/private/Denied.dll",
                    failure),
            ]);

        LibraryQueryDocument document = LibraryQuery.Execute(
            population,
            Accepted(LibraryQuery.CreateIntent(["System.Runtime"])));

        LibraryQueryFailure actual = Assert.Single(document.Failures);
        Assert.Equal(CandidateOpenFailureKind.Unreadable, actual.Kind);
        Assert.Equal(
            LibraryQueryCompletionKind.EvaluationFailures,
            document.Summary.Completion);
        Assert.False(document.Summary.IsExact);
    }

    [Fact]
    public async Task Execute_ReportsCandidateLimit()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            typeof(LibraryQueryTests).Assembly.Location);
        ImmutableArray<LibraryQueryPopulationOccurrence> occurrences =
        [
            .. Enumerable.Range(0, LibraryQuery.DefaultMaximumCandidates + 1)
                .Select(index =>
                    (LibraryQueryPopulationOccurrence)new
                        LibraryQueryPopulationOccurrence.Unavailable(
                            index,
                            $"candidate-{index}.dll",
                            new(
                                CandidateOpenFailureKind.InvalidImage,
                                "Not a managed assembly."))),
        ];

        LibraryQueryDocument document = LibraryQuery.Execute(
            new(group, occurrences),
            Accepted(LibraryQuery.CreateIntent(["System.Runtime"])));

        Assert.Equal(
            LibraryQuery.DefaultMaximumCandidates,
            document.Summary.Evaluated);
        Assert.Equal(
            LibraryQueryCompletionKind
                .CandidateLimitReachedWithEvaluationFailures,
            document.Summary.Completion);
    }

    private static LibraryQueryPlan Accepted(PortableQueryIntent intent) =>
        Assert.IsType<LibraryQueryPlanResult.Accepted>(
            LibraryQuery.ResolveIntent(
                intent,
                cancellationToken: TestContext.Current.CancellationToken)).Plan;

    private static string[] DirectReferences(AssemblyContextGroup group)
    {
        var available = Assert.IsType<
            AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Available>(
                    AssemblyContextReferencesQuery.ExecuteParticipant(
                        group,
                        group.Participants[0]));
        return
        [
            .. available.Value
                .Select(reference => reference.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        string path) =>
        workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "Library Query tests")),
                    new TestBindingPolicy()),
            ]);

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
