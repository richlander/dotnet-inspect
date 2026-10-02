using DotnetInspector.Fixtures;
using DotnetInspector.ResearchSections;

using ILInspector.Analysis;
using ILInspector.Research;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Queries.Tests;

public sealed class LibraryDependencyStructureQueryTests
{
    [Fact]
    public void QuerySpace_DeclaresEveryIssuedRowFamily()
    {
        QuerySpaceBinding querySpace =
            LibraryDependencyStructureQuery.QuerySpace;

        Assert.Equal(
            LibraryDependencyStructureQuery.RowSets,
            querySpace.Descriptor.Operation.RowSets);
        Assert.Equal(8, querySpace.RowScopes.Count);
        Assert.Equal(
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            querySpace.Descriptor.Terminals);
        Assert.All(
            LibraryDependencyStructureQuery.RowSets,
            rowSet => Assert.Contains(
                querySpace.RowScopes,
                scope => scope.Descriptor.RowSets.Contains(rowSet)));
        Assert.Contains(
            LibraryDependencyStructureQuery
                .NamespaceEdgesScope.Descriptor.Facets,
            facet => facet.Key
                == LibraryDependencyStructureQuery
                    .NamespaceEdgeRelationshipCountKey);
        Assert.Contains(
            LibraryDependencyStructureQuery
                .CyclesScope.Descriptor.Facets,
            facet => facet.Key
                == LibraryDependencyStructureQuery.CycleNamespaceKey);
        Assert.Contains(
            RowSelectionStageKind.Top,
            LibraryDependencyStructureQuery
                .NamespaceEdgesScope.Descriptor.Stages);
    }

    [Fact]
    public void Execute_PreservesFocusedAnalysisAvailability()
    {
        LibraryBodyAnalysisExecution availableAnalysis =
            Analysis(LibraryBodyAnalysisFeatures.MethodEvidence);
        QuerySpaceRequest request = Request(
            LibraryDependencyStructureQuery.NamespaceNodesRowSet);

        var available =
            Assert.IsType<
                LibraryDependencyStructureQueryResult.Available>(
                    LibraryDependencyStructureInspection.Execute(
                        availableAnalysis,
                        request));
        Assert.Same(
            availableAnalysis.CallGraph.Receipt,
            available.Document.AnalysisReceipt);
        Assert.Equal(
            available.Document.Namespaces.Length,
            available.Rows.Namespaces.Length);

        var unavailable =
            Assert.IsType<
                LibraryDependencyStructureQueryResult.Unavailable>(
                    LibraryDependencyStructureInspection.Execute(
                        Analysis(LibraryBodyAnalysisFeatures.None),
                        request));
        Assert.Equal(
            LibraryDependencyStructureUnavailableReason
                .MethodEvidenceNotRequested,
            unavailable.Outcome.Reason);
    }

    [Fact]
    public void Select_ProjectsEveryRowSetAfterBuildingTheDocument()
    {
        LibraryDependencyStructureDocument document = Document();
        foreach (string rowSet
            in LibraryDependencyStructureQuery.RowSets)
        {
            var available =
                Assert.IsType<
                    LibraryDependencyStructureQueryResult.Available>(
                        LibraryDependencyStructureInspection.Select(
                            document,
                            Request(rowSet)));

            Assert.Equal(
                DocumentCount(document, rowSet),
                SelectedCount(available.Rows, rowSet));
            Assert.Equal(
                SelectedCount(available.Rows, rowSet),
                TotalSelectedCount(available.Rows));
            Assert.Null(available.Count);
        }
    }

    [Fact]
    public void Select_FiltersRanksLimitsAndCountsIssuedRows()
    {
        LibraryDependencyStructureDocument document = Document();
        PortableQueryIntent topIntent =
            PortableQueryIntent.Create(
                [
                    new(
                        LibraryDependencyStructureQuery
                            .NamespaceEdgeRelationshipCountKey,
                        PortableQueryOperator.AtLeast,
                        "1"),
                ],
                [],
                PortableQueryRowSelection.ToStages(
                    RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Top(1),
                    ])),
                []);
        var top =
            Assert.IsType<
                LibraryDependencyStructureQueryResult.Available>(
                    LibraryDependencyStructureInspection.Select(
                        document,
                        LibraryDependencyStructureQuery.CreateRequest(
                            LibraryDependencyStructureQuery
                                .NamespaceEdgesRowSet,
                            topIntent,
                            QuerySpaceTerminalRequirement.Rows)));

        LibraryDependencyNamespaceEdge edge =
            Assert.Single(top.Rows.NamespaceEdges);
        Assert.Equal("Explained", edge.SourceNamespace);
        Assert.Equal("Epsilon", edge.TargetNamespace);
        Assert.Equal(28, edge.Counts.Total);
        Assert.True(
            document.NamespaceEdges.Length
                > top.Rows.NamespaceEdges.Length);

        PortableQueryIntent countIntent =
            PortableQueryIntent.Create(
                [
                    new(
                        LibraryDependencyStructureQuery
                            .NamespaceEdgeSourceNamespaceKey,
                        PortableQueryOperator.Equal,
                        "Explained"),
                ],
                [],
                [],
                []);
        var count =
            Assert.IsType<
                LibraryDependencyStructureQueryResult.Available>(
                    LibraryDependencyStructureInspection.Select(
                        document,
                        LibraryDependencyStructureQuery.CreateRequest(
                            LibraryDependencyStructureQuery
                                .NamespaceEdgesRowSet,
                            countIntent,
                            QuerySpaceTerminalRequirement.Count)));

        Assert.Equal(1, count.Count);
        Assert.Equal(0, TotalSelectedCount(count.Rows));
        Assert.Equal(
            document.NamespaceEdges.Length,
            count.Document.NamespaceEdges.Length);
    }

    [Fact]
    public void Select_CycleMembershipRetainsDocumentCycleIndex()
    {
        LibraryDependencyStructureDocument document = Document();
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [
                new(
                    LibraryDependencyStructureQuery.CycleNamespaceKey,
                    PortableQueryOperator.Equal,
                    "Beta"),
            ],
            [],
            [],
            []);

        var available =
            Assert.IsType<
                LibraryDependencyStructureQueryResult.Available>(
                    LibraryDependencyStructureInspection.Select(
                        document,
                        LibraryDependencyStructureQuery.CreateRequest(
                            LibraryDependencyStructureQuery.CyclesRowSet,
                            intent,
                            QuerySpaceTerminalRequirement.Rows)));

        LibraryDependencyNamespaceCycleRow cycle =
            Assert.Single(available.Rows.Cycles);
        Assert.Equal(0, cycle.CycleIndex);
        Assert.Equal(
            document.Cycles[cycle.CycleIndex].Namespaces,
            cycle.Namespaces);
    }

    [Fact]
    public void Select_RejectsInvalidRequestsAndReportsSemanticBounds()
    {
        Assert.Throws<ArgumentException>(
            () => LibraryDependencyStructureQuery.CreateRequest(
                "unknown-row-set",
                RowSelectionIntent<string>.Create([]),
                QuerySpaceTerminalRequirement.Rows));

        LibraryDependencyStructureDocument document = Document();
        QuerySpaceRequest request =
            LibraryDependencyStructureQuery.CreateRequest(
                LibraryDependencyStructureQuery.NamespaceNodesRowSet,
                RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Window(
                        1,
                        document.Namespaces.Length + 1),
                ]),
                QuerySpaceTerminalRequirement.Rows);

        var failed =
            Assert.IsType<
                LibraryDependencyStructureQueryResult.SelectionFailed>(
                    LibraryDependencyStructureInspection.Select(
                        document,
                        request));
        Assert.Null(failed.ResolutionFailure);
        Assert.Equal(1, failed.SemanticFailure?.StageNumber);
        Assert.Equal(
            document.Namespaces.Length + 1,
            failed.SemanticFailure?.RequiredPosition);
        Assert.Equal(
            document.Namespaces.Length,
            failed.SemanticFailure?.AvailableCount);
    }

    [Fact]
    public void SharedAnalysisFeedsMetricsAndDependencyStructure()
    {
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.ResearchDependencyStructure.AssemblyPath(),
                LibraryBodyAnalysisRequest
                    .CreateCompleteImplementationProfile());

        Assert.IsType<LibraryMetricsResult.Available>(
            LibraryMetricsQuery.Execute(analysis));
        var dependency =
            Assert.IsType<
                LibraryDependencyStructureQueryResult.Available>(
                    LibraryDependencyStructureInspection.Execute(
                        analysis,
                        Request(
                            LibraryDependencyStructureQuery
                                .TypeNodesRowSet)));
        Assert.Same(
            analysis.CallGraph.Receipt,
            dependency.Document.AnalysisReceipt);
    }

    private static LibraryBodyAnalysisExecution Analysis(
        LibraryBodyAnalysisFeatures features) =>
        LibraryBodyAnalysisService.ExecutePath(
            FixtureCatalog.ResearchDependencyStructure.AssemblyPath(),
            LibraryBodyAnalysisRequest.Create(features));

    private static LibraryDependencyStructureDocument Document()
    {
        var available =
            Assert.IsType<
                LibraryDependencyStructureResult.Available>(
                    LibraryDependencyStructure.Execute(
                        Analysis(
                            LibraryBodyAnalysisFeatures
                                .MethodEvidence)));
        return available.Document;
    }

    private static QuerySpaceRequest Request(string rowSet) =>
        LibraryDependencyStructureQuery.CreateRequest(
            rowSet,
            RowSelectionIntent<string>.Create([]),
            QuerySpaceTerminalRequirement.Rows);

    private static int DocumentCount(
        LibraryDependencyStructureDocument document,
        string rowSet) =>
        rowSet switch
        {
            LibraryDependencyStructureQuery.TypeNodesRowSet =>
                document.Types.Length,
            LibraryDependencyStructureQuery.ExternalNodesRowSet =>
                document.ExternalNodes.Length,
            LibraryDependencyStructureQuery.TypeEdgesRowSet =>
                document.TypeEdges.Length,
            LibraryDependencyStructureQuery.ExternalTypeEdgesRowSet =>
                document.ExternalTypeEdges.Length,
            LibraryDependencyStructureQuery.NamespaceNodesRowSet =>
                document.Namespaces.Length,
            LibraryDependencyStructureQuery.NamespaceEdgesRowSet =>
                document.NamespaceEdges.Length,
            LibraryDependencyStructureQuery
                .ExternalNamespaceEdgesRowSet =>
                document.ExternalNamespaceEdges.Length,
            LibraryDependencyStructureQuery.CyclesRowSet =>
                document.Cycles.Length,
            _ => throw new ArgumentOutOfRangeException(
                nameof(rowSet)),
        };

    private static int SelectedCount(
        LibraryDependencyQueryRows rows,
        string rowSet) =>
        rowSet switch
        {
            LibraryDependencyStructureQuery.TypeNodesRowSet =>
                rows.Types.Length,
            LibraryDependencyStructureQuery.ExternalNodesRowSet =>
                rows.ExternalNodes.Length,
            LibraryDependencyStructureQuery.TypeEdgesRowSet =>
                rows.TypeEdges.Length,
            LibraryDependencyStructureQuery.ExternalTypeEdgesRowSet =>
                rows.ExternalTypeEdges.Length,
            LibraryDependencyStructureQuery.NamespaceNodesRowSet =>
                rows.Namespaces.Length,
            LibraryDependencyStructureQuery.NamespaceEdgesRowSet =>
                rows.NamespaceEdges.Length,
            LibraryDependencyStructureQuery
                .ExternalNamespaceEdgesRowSet =>
                rows.ExternalNamespaceEdges.Length,
            LibraryDependencyStructureQuery.CyclesRowSet =>
                rows.Cycles.Length,
            _ => throw new ArgumentOutOfRangeException(
                nameof(rowSet)),
        };

    private static int TotalSelectedCount(
        LibraryDependencyQueryRows rows) =>
        rows.Types.Length
        + rows.ExternalNodes.Length
        + rows.TypeEdges.Length
        + rows.ExternalTypeEdges.Length
        + rows.Namespaces.Length
        + rows.NamespaceEdges.Length
        + rows.ExternalNamespaceEdges.Length
        + rows.Cycles.Length;
}
