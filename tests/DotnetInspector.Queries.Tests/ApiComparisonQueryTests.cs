using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class ApiComparisonQueryTests
{
    [Fact]
    public void Execute_RetainsFindingCorrespondenceAndCompatibilityClassification()
    {
        var oldSurface = new ApiSurface();
        var newSurface = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Namespace = "Sample",
                    Name = "Widget",
                    Kind = "class",
                },
            ],
        };

        ApiFindingComparison comparison =
            ApiComparisonQuery.Execute(oldSurface, newSurface);

        var types = Assert.IsType<FindingComparison<ApiTypeHandle>.Complete>(
            comparison.Types.Value);
        Assert.Contains(types.Pairs, pair => pair is PairFinding<ApiTypeHandle>.Added);
        TypeDiff typeDiff = Assert.Single(comparison.ApiDiff.TypeDiffs);
        Assert.Equal("Sample.Widget", typeDiff.TypeFullName);
        Assert.Equal(ChangeKind.TypeAdded, Assert.Single(typeDiff.Changes).Kind);
    }

    [Fact]
    public void RegistryRun_UsesDefinitionIdentityAndNetworkFreeCost()
    {
        var registry = new InspectionQueryRegistry<(ApiSurface Old, ApiSurface New)>()
            .Add(
                ApiComparisonQuery.Definition,
                static context => ApiComparisonQuery.Execute(
                    context.Old,
                    context.New));

        InspectionQueryResults results = registry.Run(
            [ApiComparisonQuery.Definition],
            (new ApiSurface(), new ApiSurface()));

        Assert.Same(
            results.Get(ApiComparisonQuery.Definition),
            results.Get(ApiComparisonQuery.Definition));
        Assert.Equal(
            InspectionCost.NetworkFree,
            registry.CostOf(ApiComparisonQuery.Definition));
    }
}
