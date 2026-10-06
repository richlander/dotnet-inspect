using DotnetInspector.Queries;

namespace DotnetInspector.Sections.Tests;

public sealed class SectionPipelineSubstrateTests
{
    [Theory]
    [InlineData("")]
    [InlineData("@dependencies")]
    [InlineData("Dependencies")]
    [InlineData("package.dependencies")]
    [InlineData("two--words")]
    [InlineData("trailing-")]
    public void FacetSetIdentityRejectsNonCanonicalValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new ViewFacetSetId(value));
    }

    [Fact]
    public void FacetSetDescriptorRequiresExplicitUniqueMembership()
    {
        var facet = new ViewFacetId("package.dependencies");

        Assert.Throws<ArgumentException>(() =>
            new ViewFacetSetDescriptor(
                new ViewFacetSetId("dependencies"),
                "Dependencies",
                InspectionViewFacetCatalog.Registry,
                []));
        Assert.Throws<ArgumentException>(() =>
            new ViewFacetSetDescriptor(
                new ViewFacetSetId("dependencies"),
                "Dependencies",
                InspectionViewFacetCatalog.Registry,
                [facet, facet]));
        Assert.Throws<ArgumentException>(() =>
            new ViewFacetSetDescriptor(
                new ViewFacetSetId("dependencies"),
                "Dependencies",
                InspectionViewFacetCatalog.Registry,
                [new ViewFacetId("package.unregistered")]));
    }

    [Fact]
    public void CompiledFacetSetsPreserveIdentityTitleMembershipAndOrder()
    {
        var dependencies = new ViewFacetSetDescriptor(
            new ViewFacetSetId("dependencies"),
            "Shared title",
            InspectionViewFacetCatalog.Registry,
            [
                new ViewFacetId("package.dependencies"),
                new ViewFacetId("package.dependency-hierarchy"),
            ]);
        var audit = new ViewFacetSetDescriptor(
            new ViewFacetSetId("audit"),
            "Shared title",
            InspectionViewFacetCatalog.Registry,
            [new ViewFacetId("package.overview")]);

        SectionCatalog<TestModel> catalog = new SectionPipeline<TestModel>()
            .WithoutComputedPoles()
            .Add<PrimarySection>()
            .Add<DetailedSection>()
            .AddFacetSetCategory("@Dependencies", dependencies, PrimarySection.Name)
            .AddFacetSetCategory("@Audit", audit, DetailedSection.Name)
            .Compile();

        Assert.Equal([dependencies, audit], catalog.AuthoredFacetSets);
        Assert.Equal(
            ["package.dependencies", "package.dependency-hierarchy"],
            catalog.AuthoredFacetSets[0].Facets.Select(static facet => facet.Value));
        Assert.Equal(
            ["Shared title", "Shared title"],
            catalog.AuthoredFacetSets.Select(static set => set.Title));
        Assert.Equal(
            ["dependencies", "audit"],
            catalog.AuthoredFacetSets.Select(static set => set.Id.Value));
        Assert.All(
            catalog.AuthoredCategories,
            static category => Assert.NotNull(category.FacetSet));
    }

    [Fact]
    public void PipelineRejectsDuplicateFacetSetIdentity()
    {
        var first = new ViewFacetSetDescriptor(
            new ViewFacetSetId("dependencies"),
            "Dependencies",
            InspectionViewFacetCatalog.Registry,
            [new ViewFacetId("package.dependencies")]);
        var duplicate = new ViewFacetSetDescriptor(
            new ViewFacetSetId("dependencies"),
            "Another title",
            InspectionViewFacetCatalog.Registry,
            [new ViewFacetId("package.dependency-hierarchy")]);
        var pipeline = new SectionPipeline<TestModel>()
            .Add<PrimarySection>()
            .Add<DetailedSection>()
            .AddFacetSetCategory("@Dependencies", first, PrimarySection.Name);

        Assert.Throws<InvalidOperationException>(() =>
            pipeline.AddFacetSetCategory("@Other", duplicate, DetailedSection.Name));
    }

    [Fact]
    public void PipelineRejectsDuplicateCategoryDoorBeforeFacetMetadataCanDrift()
    {
        var dependencies = new ViewFacetSetDescriptor(
            new ViewFacetSetId("dependencies"),
            "Dependencies",
            InspectionViewFacetCatalog.Registry,
            [new ViewFacetId("package.dependencies")]);
        var pipeline = new SectionPipeline<TestModel>()
            .WithoutComputedPoles()
            .Add<PrimarySection>()
            .Add<DetailedSection>()
            .AddFacetSetCategory(
                "@Dependencies",
                dependencies,
                PrimarySection.Name);

        Assert.Throws<InvalidOperationException>(() =>
            pipeline.AddCategory("@dependencies", DetailedSection.Name));

        SectionCatalog<TestModel> catalog = pipeline.Compile();
        Assert.Equal(
            [PrimarySection.Name],
            catalog.CategoryMap["@Dependencies"]);
        CompiledSectionCategory category =
            Assert.Single(catalog.AuthoredCategories);
        Assert.Same(dependencies, category.FacetSet);
        Assert.Equal([PrimarySection.Name], category.Sections);
    }

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
        var hostOnly = new InspectionQuery<int>(
            "host",
            InspectionCost.NetworkFree);
        InspectionQueryCatalog<string> queries =
            new InspectionQueryRegistry<string>()
                .Add(first, static context => context.Length)
                .Add(second, static context => context.Length * 2)
                .Add(hostOnly, static context => context.Length * 3)
                .Compile();
        var domain = new CompiledInspectionDomain<string>(queries);
        CompiledInspectionLens<string, TestModel> lens =
            domain.CompileLens<TestModel>(pipeline =>
                pipeline
                    .Add<PrimarySection>(first)
                    .Add<DetailedSection>(second));
        CompiledInspectionPlan<string> original =
            lens.Plan(
                SectionViewLevel.Detailed,
                hostDemand:
                [
                    new HostQueryDemand("Host-only query", hostOnly),
                ]);

        CompiledInspectionPlan<string> filtered =
            original.WithoutQueries(
                queries,
                new HashSet<InspectionQueryDefinition> { second });
        InspectionQueryResults results = filtered.Run("test");

        Assert.Equal([first, hostOnly], filtered.RequestedQueries);
        Assert.Equal([first], filtered.SectionPlan.Queries);
        Assert.Equal(
            [new SectionQueryDemand(PrimarySection.Name, first)],
            filtered.SectionDemand);
        Assert.Equal(
            [new HostQueryDemand("Host-only query", hostOnly)],
            filtered.HostDemand);
        Assert.True(filtered.SectionPlan.Activate().SetEquals([first]));
        Assert.True(
            filtered.SectionPlan.Activate(filtered.HostDemand)
                .SetEquals([first, hostOnly]));
        Assert.Equal(4, results.Get(first));
        Assert.Equal(12, results.Get(hostOnly));
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
