using QuerySpace;
using QuerySpace.Rows;

namespace ILInspector.Analysis.Tests;

public sealed class OptimizationOpportunityRowSpaceTests
{
    [Fact]
    public void RowScope_DeclaresTheExecutableVocabulary()
    {
        var scope = OptimizationOpportunityRowSpace.RowScope;

        Assert.Equal(
            OptimizationOpportunityRowSpace.RowScopeIdentity,
            scope.Descriptor.Identity);
        Assert.Equal(
            [OptimizationOpportunityRowSpace.RowSet],
            scope.Descriptor.RowSets);
        Assert.Equal(
            scope.Vocabulary.Keys.Count,
            scope.Descriptor.Facets.Count);
        Assert.Equal(
            scope.Vocabulary.NamedOrders.Count,
            scope.Descriptor.Orders.Count);
        Assert.Equal(
            [RowSelectionStageKind.Top],
            scope.Descriptor.Stages);
        Assert.Contains(
            scope.Vocabulary.Keys,
            key => key.Key == OptimizationOpportunityRowSpace.KindKey);

        PortableQueryOrderOperation curatedOrder =
            Assert.Single(
                OptimizationOpportunityRowSpace.PerformanceTriage
                    .Intent.Order);
        Assert.True(curatedOrder.Role.IsBaseline);
        Assert.Equal(PortableQueryOrderKind.Named, curatedOrder.Kind);
        Assert.Equal(
            OptimizationOpportunityRowSpace.TriageOrderKey,
            curatedOrder.Reference);
        Assert.Equal(
            PortableQueryDirection.Descending,
            curatedOrder.Direction);
    }

    [Fact]
    public void CuratedQueries_ClassifyKnownShapesAndRetainOther()
    {
        Assert.Equal(
            10,
            OptimizationOpportunityRowSpace.CuratedQueries.Length);
        Assert.Equal(
            OptimizationOpportunityRowSpace.CuratedQueries.Length,
            OptimizationOpportunityRowSpace.CuratedQueries
                .Select(query => query.Identity)
                .Distinct(StringComparer.Ordinal)
                .Count());

        foreach (string shape
            in OptimizationOpportunityRowSpace.KnownShapes)
        {
            OptimizationOpportunityKind kind =
                OptimizationOpportunityRowSpace.KindForShape(shape);
            Assert.NotEqual(OptimizationOpportunityKind.Other, kind);
            Assert.Equal(
                [shape, $"{shape}-upper"],
                OptimizationOpportunityRowSpace.Select(
                        OptimizationOpportunityRowSpace.QueryForKind(kind),
                        [
                            Opportunity(shape, shape),
                            Opportunity(
                                $"{shape}-upper",
                                shape.ToUpperInvariant()),
                        ])
                    .Select(row => row.Method.Name));
        }

        Assert.Equal(
            OptimizationOpportunityKind.Other,
            OptimizationOpportunityRowSpace.KindForShape(
                "future-shape"));
        Assert.Equal(
            ["Future"],
            OptimizationOpportunityRowSpace.Select(
                    OptimizationOpportunityRowSpace.Other,
                    [
                        Opportunity("Known", "box-value-type"),
                        Opportunity("Future", "future-shape"),
                    ])
                .Select(row => row.Method.Name));
        Assert.True(
            OptimizationOpportunityRowSpace.Any(
                OptimizationOpportunityRowSpace.Other,
                [Opportunity("Future", "future-shape")]));
        Assert.False(
            OptimizationOpportunityRowSpace.Any(
                OptimizationOpportunityRowSpace.Other,
                [Opportunity("Known", "box-value-type")]));
    }

    [Fact]
    public void CuratedQuery_ResolvesThroughTheSharedRowScope()
    {
        RowQueryResolutionResult<OptimizationOpportunity> resolution =
            OptimizationOpportunityRowSpace.Resolve(
                OptimizationOpportunityRowSpace.Boxing,
                PortableQueryIntent.Empty);
        ResolvedRowQueryPlan<OptimizationOpportunity> plan =
            Assert.IsType<ResolvedRowQueryPlan<OptimizationOpportunity>>(
                resolution.Plan);

        RowSelectionResult<OptimizationOpportunity> result =
            OptimizationOpportunityRowSpace.Apply(
                [
                    Opportunity(
                        "LowerBox",
                        "box-value-type",
                        confidence: "low"),
                    Opportunity("Array", "small-array"),
                    Opportunity(
                        "HigherBox",
                        "box-value-type",
                        confidence: "high"),
                ],
                [],
                plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["HigherBox", "LowerBox"],
            result.Values.Select(row => row.Method.Name));

        Assert.Equal(
            ["LowerBox", "HigherBox"],
            OptimizationOpportunityRowSpace.SelectPartition(
                    OptimizationOpportunityRowSpace.Boxing,
                    [
                        Opportunity(
                            "LowerBox",
                            "box-value-type",
                            confidence: "low"),
                        Opportunity(
                            "HigherBox",
                            "box-value-type",
                            confidence: "high"),
                    ])
                .Select(row => row.Method.Name));
    }

    [Fact]
    public void PerformanceCandidates_PreserveGeneratedStringRowsOnly()
    {
        TypeRef generatedType =
            TypeRef.Definition("Rows", "Example", "Generated");
        OptimizationOpportunity generatedBox =
            Opportunity("GeneratedBox", "box-value-type") with
            {
                Method = Method("GeneratedBox", generatedType),
            };
        OptimizationOpportunity generatedString =
            Opportunity(
                "GeneratedString",
                AnalysisFindings.StringMaterializationShape) with
            {
                Method = Method("GeneratedString", generatedType),
            };
        OptimizationOpportunity fanout =
            Opportunity("Fanout", "allocation-fanout");

        IReadOnlySet<TypeRef> generatedTypes =
            new HashSet<TypeRef> { generatedType };
        Assert.Equal(
            ["GeneratedString"],
            OptimizationOpportunityRowSpace.PerformanceCandidates(
                    [generatedBox, generatedString],
                    [fanout],
                    generatedTypes,
                    includeAllocationFanout: false)
                .Select(row => row.Method.Name));
        Assert.Equal(
            ["GeneratedString", "Fanout"],
            OptimizationOpportunityRowSpace.PerformanceCandidates(
                    [generatedBox, generatedString],
                    [fanout],
                    generatedTypes,
                    includeAllocationFanout: true)
                .Select(row => row.Method.Name));
    }

    [Fact]
    public void ShapeMembership_IsAppliedBeforeTopRanking()
    {
        PortableQueryIntent intent =
            PortableQueryIntent.Create(
                [],
                [],
                [PortableQueryStage.Top(1)],
                []);
        ResolvedRowQueryPlan<OptimizationOpportunity> plan =
            Assert.IsType<ResolvedRowQueryPlan<OptimizationOpportunity>>(
                OptimizationOpportunityRowSpace.Resolve(
                    OptimizationOpportunityRowSpace.PerformanceTriage,
                    intent).Plan);
        Assert.Null(plan.BaselineOrder);

        RowSelectionResult<OptimizationOpportunity> result =
            OptimizationOpportunityRowSpace.Apply(
                [
                    Opportunity(
                        "Higher",
                        "linq-scan-in-loop",
                        confidence: "high"),
                    Opportunity(
                        "Admitted",
                        "small-array",
                        confidence: "low"),
                ],
                ["small-array"],
                plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Admitted"],
            result.Values.Select(row => row.Method.Name));
    }

    private static OptimizationOpportunity Opportunity(
        string name,
        string shape,
        string confidence = "medium") =>
        new(
            Method(name),
            shape,
            "evidence",
            "fix",
            confidence,
            InLoop: false,
            ILOffset: null,
            Caveat: null,
            RootReach: 1);

    private static MethodIdentity Method(
        string name,
        TypeRef? declaringType = null) =>
        new(
            "Rows",
            Guid.Empty,
            declaringType
                ?? TypeRef.Definition("Rows", "Example", "Probe"),
            name,
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);
}
