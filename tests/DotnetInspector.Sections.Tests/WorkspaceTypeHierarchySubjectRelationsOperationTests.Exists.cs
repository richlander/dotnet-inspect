using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Sections.Tests;

public sealed partial class ExactTypeInspectionOperationTests
{
    [Fact]
    public async Task WorkspaceHierarchy_ExistsDecidesAtTheFirstCandidateWithoutRows()
    {
        ResolvedAssemblyReference first =
            ResolvedImage(
                "HierarchyExistsFirst",
                BuildAssembly(
                    "HierarchyExistsFirst",
                    "Hierarchy.ExistsFirst",
                    typeof(IDisposable)));
        ResolvedAssemblyReference second =
            ResolvedImage(
                "HierarchyExistsSecond",
                BuildAssembly(
                    "HierarchyExistsSecond",
                    "Hierarchy.ExistsSecond",
                    typeof(IDisposable)));
        ResolvedAssemblyReference core =
            ResolvedRuntime(typeof(IDisposable).Assembly);
        await using var workspace = new InspectionWorkspace();
        (_, WorkspaceDeclarationPopulation population) =
            CreatePopulation(workspace, first, second, core);

        WorkspaceTypeHierarchySubjectRelationsExecution execution =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new(
                    new(
                        population.Receipt.Members[2].Occurrence,
                        TypeName("System", "IDisposable")),
                    new(
                        HierarchySelection(SubjectRelationForm.Interface),
                        exists: new SubjectRelationPopulationExistsRequest())),
                MetadataOperationPolicy.Unbounded,
                cancellationToken: TestContext.Current.CancellationToken);

        SubjectRelationPopulationResult relations =
            execution.Inspection.Content.Relations;
        Assert.True(
            Assert.IsType<SubjectRelationPopulationExistsOutcome.Decided>(
                relations.Exists).Value);
        Assert.Null(relations.Count);
        Assert.Null(relations.Rows);
        Assert.Empty(execution.Inspection.Content.Implementers);
        Assert.Empty(execution.Inspection.Content.DerivedTypes);
        // The first candidate decided the population; the rest were not
        // visited, so the producer evidence is honestly partial.
        Assert.False(relations.Evidence.IsComplete);
        SubjectRelationProducerOutcome producer =
            Assert.Single(relations.Evidence.Producers);
        Assert.True(producer.Coverage.Limited > 0);
        Assert.Null(execution.ContinuationAuthority);
    }

    [Fact]
    public async Task WorkspaceHierarchy_ExistsIsFalseOnlyFromCompleteEvidence()
    {
        ResolvedAssemblyReference leaf =
            ResolvedImage(
                "HierarchyExistsLeaf",
                BuildDerivedAssembly(
                    "HierarchyExistsLeaf",
                    "Hierarchy.ExistsLeaf",
                    typeof(Stream)));
        ResolvedAssemblyReference core =
            ResolvedRuntime(typeof(Stream).Assembly);
        await using var workspace = new InspectionWorkspace();
        (_, WorkspaceDeclarationPopulation population) =
            CreatePopulation(workspace, leaf, core);

        WorkspaceTypeHierarchySubjectRelationsExecution execution =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new(
                    new(
                        population.Receipt.Members[0].Occurrence,
                        TypeName("Hierarchy", "ExistsLeaf")),
                    new(
                        HierarchySelection(SubjectRelationForm.BaseType),
                        count: new SubjectRelationPopulationCountRequest(),
                        exists: new SubjectRelationPopulationExistsRequest())),
                MetadataOperationPolicy.Unbounded,
                cancellationToken: TestContext.Current.CancellationToken);

        SubjectRelationPopulationResult relations =
            execution.Inspection.Content.Relations;
        Assert.False(
            Assert.IsType<SubjectRelationPopulationExistsOutcome.Decided>(
                relations.Exists).Value);
        Assert.Equal(
            0,
            Assert.IsType<SubjectRelationPopulationCountOutcome.Counted>(
                relations.Count).Value);
        Assert.True(relations.Evidence.IsComplete);
    }

    [Fact]
    public async Task WorkspaceHierarchy_ExistsAgreesWithCountWhenBothRun()
    {
        ResolvedAssemblyReference first =
            ResolvedImage(
                "HierarchyExistsAgreeFirst",
                BuildAssembly(
                    "HierarchyExistsAgreeFirst",
                    "Hierarchy.ExistsAgreeFirst",
                    typeof(IDisposable)));
        ResolvedAssemblyReference core =
            ResolvedRuntime(typeof(IDisposable).Assembly);
        await using var workspace = new InspectionWorkspace();
        (_, WorkspaceDeclarationPopulation population) =
            CreatePopulation(workspace, first, core);

        WorkspaceTypeHierarchySubjectRelationsExecution execution =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new(
                    new(
                        population.Receipt.Members[1].Occurrence,
                        TypeName("System", "IDisposable")),
                    new(
                        HierarchySelection(SubjectRelationForm.Interface),
                        count: new SubjectRelationPopulationCountRequest(),
                        exists: new SubjectRelationPopulationExistsRequest())),
                MetadataOperationPolicy.Unbounded,
                cancellationToken: TestContext.Current.CancellationToken);

        SubjectRelationPopulationResult relations =
            execution.Inspection.Content.Relations;
        Assert.True(
            Assert.IsType<SubjectRelationPopulationExistsOutcome.Decided>(
                relations.Exists).Value);
        Assert.True(
            Assert.IsType<SubjectRelationPopulationCountOutcome.Counted>(
                relations.Count).Value >= 1);
        // Count visited every candidate, so the evidence is complete even
        // though Exists stopped asking after the first candidate.
        Assert.True(relations.Evidence.IsComplete);
    }

    [Fact]
    public async Task WorkspaceHierarchy_ExistsNeverReadsAnUnavailableCandidateAsEmpty()
    {
        ResolvedAssemblyReference unavailableCandidate =
            ResolvedImage(
                "HierarchyExistsUnavailable",
                BuildDerivedAssembly(
                    "HierarchyExistsUnavailable",
                    "Hierarchy.ExistsUnavailable",
                    typeof(Stream)));
        ResolvedAssemblyReference leaf =
            ResolvedImage(
                "HierarchyExistsLeafFocus",
                BuildDerivedAssembly(
                    "HierarchyExistsLeafFocus",
                    "Hierarchy.ExistsLeafFocus",
                    typeof(Stream)));
        await using var workspace = new InspectionWorkspace();
        (WorkspaceDeclarationContext candidateContext, _) =
            CreatePopulation(workspace, unavailableCandidate);
        (WorkspaceDeclarationContext focusContext, _) =
            CreatePopulation(workspace, leaf);
        WorkspaceDeclarationPopulation population =
            Assert.IsType<WorkspaceDeclarationPopulationCapture.Captured>(
                    workspace.CaptureDeclarationPopulation(
                        [candidateContext, focusContext]))
                .Population;
        candidateContext.Group!.Dispose();

        WorkspaceTypeHierarchySubjectRelationsExecution execution =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new(
                    new(
                        population.Receipt.Members[1].Occurrence,
                        TypeName("Hierarchy", "ExistsLeafFocus")),
                    new(
                        HierarchySelection(SubjectRelationForm.BaseType),
                        exists: new SubjectRelationPopulationExistsRequest())),
                MetadataOperationPolicy.Unbounded,
                cancellationToken: TestContext.Current.CancellationToken);

        SubjectRelationPopulationResult relations =
            execution.Inspection.Content.Relations;
        Assert.IsType<SubjectRelationPopulationExistsOutcome.Incomplete>(
            relations.Exists);
        Assert.False(relations.Evidence.IsComplete);
        Assert.Contains(
            execution.Inspection.Diagnostics,
            diagnostic => diagnostic.Code
                == "workspace-hierarchy-candidate-unavailable");
    }

    [Fact]
    public void SubjectRelationPopulationRequest_RequiresAtLeastOneTerminal()
    {
        SubjectRelationPopulationSelection selection =
            HierarchySelection(SubjectRelationForm.Interface);

        Assert.Throws<ArgumentException>(
            () => new SubjectRelationPopulationRequest(selection));
        SubjectRelationPopulationRequest existsOnly =
            new(
                selection,
                exists: new SubjectRelationPopulationExistsRequest());
        Assert.Null(existsOnly.Count);
        Assert.Null(existsOnly.Rows);
        Assert.NotNull(existsOnly.Exists);
    }
}
