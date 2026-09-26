using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed class BodySignalComparisonQueryTests
{
    static readonly string SystemTextJson9 = Path.Combine(
        AppContext.BaseDirectory,
        "RealAssets",
        "BodySignalComparison",
        "9.0.0",
        "System.Text.Json.dll");

    static readonly string SystemTextJson10 = Path.Combine(
        AppContext.BaseDirectory,
        "RealAssets",
        "BodySignalComparison",
        "10.0.0",
        "System.Text.Json.dll");

    [Fact]
    public void Execute_ReturnsResearchOwnedEvidenceFromFocusedAnalysis()
    {
        BodySignalComparisonBinding oldBinding = Bind(
            FixtureCatalog.DiffPair.OldAssemblyPath());
        BodySignalComparisonBinding newBinding = Bind(
            FixtureCatalog.DiffPair.NewAssemblyPath());

        var compared = Assert.IsType<BodySignalComparisonResult.Compared>(
            BodySignalComparisonQuery.Execute(
                new BodySignalComparisonInput(
                    [oldBinding],
                    [newBinding]),
                TestContext.Current.CancellationToken));

        // An untargeted whole-assembly comparison makes no target request.
        Assert.Null(compared.Resolution);
        Assert.Empty(compared.Correspondences);
        ResearchChange regression = Assert.Single(
            compared.Comparison.Changes,
            change => change.Descriptor.Id == AnalysisFindings.AllocationDescriptor.Id
                && change.Subject.Display.Contains(
                    "RegressesAllocInLoop",
                    StringComparison.Ordinal));
        Assert.Equal("1", regression.OldValue);
        Assert.Equal("2", regression.NewValue);
        Assert.Equal("in-loop", regression.Shape);
        Assert.Equal(1, regression.DirectionScore);
    }

    [Fact]
    public void Definition_IsUnbounded()
        => Assert.Equal(
            InspectionCost.Unbounded,
            BodySignalComparisonQuery.Definition.Cost);

    [Fact]
    public void Execute_EmptyFocusedPopulations_ReturnsEmptyComparison()
    {
        var compared = Assert.IsType<BodySignalComparisonResult.Compared>(
            BodySignalComparisonQuery.Execute(
                new BodySignalComparisonInput([], []),
                TestContext.Current.CancellationToken));

        Assert.Empty(compared.Comparison.Changes);
    }

    [Fact]
    public void Execute_MissingBinding_IsTypedPopulationRejection()
    {
        var rejected = Assert.IsType<BodySignalComparisonResult.PopulationRejected>(
            BodySignalComparisonQuery.Execute(
                new BodySignalComparisonInput([null!], []),
                TestContext.Current.CancellationToken));

        Assert.Equal(QueryPopulationRejectionKind.MissingBinding, rejected.Rejection.Kind);
        Assert.Equal(QueryComparisonProfile.BodySignal, rejected.Rejection.Profile);
    }

    // #8570: string-keyed body-signal targeting doubled the generic list of
    // Serialize<TValue> and dropped every generic method in System.Text.Json
    // 10.0.0. Typed targeting pairs the generic member by correspondence key.
    [Fact]
    public void BodySignalComparisonQuery_PairsGenericSystemTextJsonMember()
    {
        BodySignalComparisonBinding before = Bind(SystemTextJson9);
        BodySignalComparisonBinding after = Bind(SystemTextJson10);
        MetadataTypeDefinitionName serializer = TypeName(
            "System.Text.Json.JsonSerializer");

        // Serialize:1 is Serialize<TValue>(TValue, JsonSerializerOptions).
        ResearchComparison generic = AssertPaired(
            before,
            after,
            new ComparisonMemberSelection(
                serializer,
                MemberTargetSelector.Parse("Serialize:1")),
            "Serialize");
        RetainedFindingComparison<DirectCall> genericCalls = Assert.Single(
            generic.RetainedComparisons.Get<DirectCall>(
                AnalysisFindings.CallSiteDescriptor));
        Assert.Contains("TValue", genericCalls.Subject.Display, StringComparison.Ordinal);
        Assert.Equal(
            ["GetTypeInfo", "WriteString"],
            PresentCallees(genericCalls));

        // Serialize:4 is the non-generic Serialize(object, Type, JsonSerializerOptions).
        ResearchComparison nonGeneric = AssertPaired(
            before,
            after,
            new ComparisonMemberSelection(
                serializer,
                MemberTargetSelector.Parse("Serialize:4")),
            "Serialize");
        RetainedFindingComparison<DirectCall> nonGenericCalls = Assert.Single(
            nonGeneric.RetainedComparisons.Get<DirectCall>(
                AnalysisFindings.CallSiteDescriptor));
        Assert.Contains(
            "Serialize(object, System.Type, System.Text.Json.JsonSerializerOptions)",
            nonGenericCalls.Subject.Display,
            StringComparison.Ordinal);
        Assert.Equal(
            ["GetTypeInfo", "ValidateInputType", "WriteStringAsObject"],
            PresentCallees(nonGenericCalls));
    }

    [Fact]
    public void Execute_TargetWithoutEitherEndpoint_KeepsAbsentCorrespondenceVisible()
    {
        BodySignalComparisonBinding before = Bind(
            FixtureCatalog.DiffPair.OldAssemblyPath());
        BodySignalComparisonBinding after = Bind(
            FixtureCatalog.DiffPair.NewAssemblyPath());

        var compared = Assert.IsType<BodySignalComparisonResult.Compared>(
            BodySignalComparisonQuery.Execute(
                new BodySignalComparisonInput(
                    [before],
                    [after],
                    MemberSelections:
                    [
                        new ComparisonMemberSelection(
                            TypeName("DiffFixtureSample.DiffSample"),
                            MemberTargetSelector.Parse("NoSuchMember")),
                    ],
                    RetainedComparisons: [AnalysisFindings.CallSiteDescriptor]),
                TestContext.Current.CancellationToken));

        Assert.NotNull(compared.Resolution);
        Assert.NotEmpty(compared.Correspondences);
        Assert.All(
            compared.Correspondences,
            outcome => Assert.IsType<ResearchTargetCorrespondenceOutcome.Absent>(outcome));
        Assert.Empty(compared.Comparison.Changes);
        Assert.Empty(
            compared.Comparison.RetainedComparisons.Get<DirectCall>(
                AnalysisFindings.CallSiteDescriptor));
    }

    static ResearchComparison AssertPaired(
        BodySignalComparisonBinding before,
        BodySignalComparisonBinding after,
        ComparisonMemberSelection selection,
        string memberName)
    {
        var compared = Assert.IsType<BodySignalComparisonResult.Compared>(
            BodySignalComparisonQuery.Execute(
                new BodySignalComparisonInput(
                    [before],
                    [after],
                    MemberSelections: [selection],
                    RetainedComparisons: [AnalysisFindings.CallSiteDescriptor]),
                TestContext.Current.CancellationToken));
        var paired = Assert.IsType<ResearchTargetCorrespondenceOutcome.Paired>(
            Assert.Single(compared.Correspondences));
        Assert.Equal(memberName, paired.Before.Target.Target.ApiMember.Member.Name);
        Assert.Equal(memberName, paired.After.Target.Target.ApiMember.Member.Name);
        Assert.NotNull(paired.Before.Target.Address);
        Assert.NotNull(paired.After.Target.Address);
        return compared.Comparison;
    }

    static string[] PresentCallees(RetainedFindingComparison<DirectCall> retained)
    {
        var complete = Assert.IsType<FindingComparison<DirectCall>.Complete>(
            retained.Comparison.Value);
        Assert.True(complete.Transition.IsSameTopology);
        return
        [
            .. complete.Pairs
                .Select(pair => Assert.IsType<PairFinding<DirectCall>.Present>(
                    pair.Value).New.Payload.Callee.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    static MetadataTypeDefinitionName TypeName(string serialized)
        => Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.ParseSerialized(serialized)).Name;

    static BodySignalComparisonBinding Bind(string path)
    {
        const LibraryBodyAnalysisFeatures features =
            LibraryBodyAnalysisFeatures.MethodEvidence
            | LibraryBodyAnalysisFeatures.Allocations
            | LibraryBodyAnalysisFeatures.OptimizationOpportunities;
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(features));
        return new(
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "body-signal comparison query test")),
            MetadataSource.DefaultAssemblyReferenceResolver(path),
            new BodySignalAnalysisInput(
                execution.Allocations,
                execution.Safety,
                execution.CallGraph,
                execution.Optimization));
    }
}
