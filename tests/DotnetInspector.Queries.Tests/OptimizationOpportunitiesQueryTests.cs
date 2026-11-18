using System.Collections.Immutable;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using QuerySpace;
using QuerySpace.Rows;

namespace DotnetInspector.Queries.Tests;

public sealed class OptimizationOpportunitiesQueryTests
{
    [Fact]
    public void Execute_MissingOptimizationAcquisitionRemainsTypedFailure()
    {
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));

        OptimizationOpportunitiesResult result =
            OptimizationOpportunitiesQuery.Execute(
                analysis.Optimization,
                includeAllocationFanout: false);

        var failed =
            Assert.IsType<OptimizationOpportunitiesResult.Failed>(
                result);
        Assert.Contains(
            "were not requested",
            failed.Error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Select_ComposesCandidatesAndExecutesResolvedPlan()
    {
        TypeRef generatedType =
            TypeRef.Definition("Rows", "Example", "Generated");
        var available = new OptimizationOpportunitiesResult.Available(
            [
                Opportunity("Ordinary", "small-array"),
                Opportunity(
                    "GeneratedBox",
                    "box-value-type",
                    generatedType),
                Opportunity(
                    "GeneratedString",
                    AnalysisFindings.StringMaterializationShape,
                    generatedType),
                Opportunity("RetiredSyncCall", "sync-call-in-async"),
            ],
            [Opportunity("Fanout", "allocation-fanout")],
            ImmutableHashSet.Create(generatedType),
            []);
        ResolvedRowQueryPlan<OptimizationOpportunity> plan =
            Assert.IsType<ResolvedRowQueryPlan<OptimizationOpportunity>>(
                OptimizationOpportunityRowSpace.Resolve(
                    OptimizationOpportunityRowSpace.PerformanceTriage,
                    PortableQueryIntent.Empty).Plan);

        ImmutableArray<OptimizationOpportunity> selected =
            OptimizationOpportunitiesQuery.Select(
                available,
                [],
                plan);

        Assert.Equal(
            ["Fanout", "GeneratedString", "Ordinary"],
            selected.Select(row => row.Method.Name).Order());
    }

    [Fact]
    public void Select_AppliesShapeMembershipBeforeTop()
    {
        var available = new OptimizationOpportunitiesResult.Available(
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
            [],
            [],
            []);
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

        ImmutableArray<OptimizationOpportunity> selected =
            OptimizationOpportunitiesQuery.Select(
                available,
                ["small-array"],
                plan);

        Assert.Equal(
            ["Admitted"],
            selected.Select(row => row.Method.Name));
    }

    private static OptimizationOpportunity Opportunity(
        string name,
        string shape,
        TypeRef? declaringType = null,
        string confidence = "medium") =>
        new(
            new MethodIdentity(
                "Rows",
                Guid.Empty,
                declaringType
                    ?? TypeRef.Definition(
                        "Rows",
                        "Example",
                        "Probe"),
                name,
                [],
                TypeRef.CoreLib("System", "Void"),
                0x06000001,
                IsStatic: true),
            shape,
            "evidence",
            "fix",
            confidence,
            InLoop: false,
            ILOffset: null,
            Caveat: null,
            RootReach: 1);
}
