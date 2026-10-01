using DotnetInspector.Queries;

namespace DotnetInspector.Sections.Tests;

public sealed class SectionPipelineSubstrateTests
{
    [Fact]
    public void CompiledDomainPlansAndExecutesHostNeutralSectionLens()
    {
        var primaryQuery = new InspectionQuery<int>(
            "primary",
            InspectionCost.NetworkFree);
        var detailedQuery = new InspectionQuery<int>(
            "detailed",
            InspectionCost.Moderated);
        InspectionQueryCatalog<string> queries =
            new InspectionQueryRegistry<string>()
                .Add(primaryQuery, static context => context.Length)
                .Add(detailedQuery, static context => context.Length * 2)
                .Compile();
        var domain = new CompiledInspectionDomain<string>(queries);
        CompiledInspectionLens<string, TestModel> lens =
            domain.CompileLens<TestModel>(pipeline =>
                pipeline
                    .UseCuratedCatalog()
                    .Add<PrimarySection>(primaryQuery)
                    .Add<DetailedSection>(detailedQuery)
                    .AddBaseCategory(
                        "@Test",
                        PrimarySection.Name,
                        DetailedSection.Name));

        CompiledInspectionPlan<string> plan =
            lens.Plan(SectionViewLevel.Minimal);
        InspectionQueryResults results = plan.Run("test");

        Assert.Equal([primaryQuery], plan.RequestedQueries);
        Assert.Equal(4, results.Get(primaryQuery));
        Assert.False(results.TryGet(detailedQuery, out _));
    }

    [Fact]
    public void ExplicitSelectionRetainsUnboundedSectionDemand()
    {
        var query = new InspectionQuery<int>(
            "unbounded",
            InspectionCost.Unbounded);
        var pipeline = new SectionPipeline<TestModel>()
            .UseCuratedCatalog()
            .Add<UnboundedSection>(query)
            .AddBaseCategory("@Test", UnboundedSection.Name);
        var include = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            UnboundedSection.Name,
        };

        HashSet<InspectionQueryDefinition> automatic =
            pipeline.GetRequiredQueries(SectionViewLevel.Detailed);
        HashSet<InspectionQueryDefinition> explicitSelection =
            pipeline.GetRequiredQueries(SectionViewLevel.Minimal, include);

        Assert.Empty(automatic);
        Assert.Equal([query], explicitSelection);
    }

    [Fact]
    public void WithoutQueriesReplansAndFiltersDemandAttribution()
    {
        var first = new InspectionQuery<int>(
            "first",
            InspectionCost.NetworkFree);
        var second = new InspectionQuery<int>(
            "second",
            InspectionCost.NetworkFree);
        InspectionQueryCatalog<string> queries =
            new InspectionQueryRegistry<string>()
                .Add(first, static context => context.Length)
                .Add(second, static context => context.Length * 2)
                .Compile();
        var domain = new CompiledInspectionDomain<string>(queries);
        CompiledInspectionLens<string, TestModel> lens =
            domain.CompileLens<TestModel>(pipeline =>
                pipeline
                    .Add<PrimarySection>(first)
                    .Add<DetailedSection>(second));
        CompiledInspectionPlan<string> original =
            lens.Plan(SectionViewLevel.Detailed);

        CompiledInspectionPlan<string> filtered =
            original.WithoutQueries(
                queries,
                new HashSet<InspectionQueryDefinition> { second });
        InspectionQueryResults results = filtered.Run("test");

        Assert.Equal([first], filtered.RequestedQueries);
        Assert.Equal(
            [new SectionQueryDemand(PrimarySection.Name, first)],
            filtered.SectionDemand);
        Assert.Equal(4, results.Get(first));
        Assert.False(results.TryGet(second, out _));
    }

    private sealed record TestModel;

    private sealed class PrimarySection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Primary";
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static bool CanRender(TestModel model) => true;
    }

    private sealed class DetailedSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Detailed";
        public static bool IsExpensive => true;
        public static SectionCost Cost => SectionCost.Moderated;
        public static bool CanRender(TestModel model) => true;
    }

    private sealed class UnboundedSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Unbounded";
        public static bool IsExpensive => true;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(TestModel model) => true;
    }
}
