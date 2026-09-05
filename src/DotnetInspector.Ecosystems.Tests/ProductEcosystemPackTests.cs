using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems.Tests;

public sealed class ProductEcosystemPackTests
{
    [Fact]
    public void AspireIsTheOnlyShippedScannerAndRetainsTheOwnerBinding()
    {
        Assert.Equal(
            [false, false, false, true],
            EcosystemPackCatalog.Discover().Select(pack => pack.HasScanner));
        var selected = Assert.IsType<EcosystemScannerSelectionResult.Known>(
            EcosystemPackCatalog.SelectScanner(EcosystemPackIds.Aspire));
        Assert.Same(EcosystemIntegrationScanner.AspireBinding, selected.Binding);
        Assert.All(
            new[]
            {
                EcosystemPackIds.Platform,
                EcosystemPackIds.MicrosoftExtensions,
                EcosystemPackIds.AspNetCore,
            },
            id => Assert.IsType<EcosystemScannerSelectionResult.Unavailable>(
                EcosystemPackCatalog.SelectScanner(id)));
    }

    [Fact]
    public void ShippedManifestMatchesLiteralPolicy()
    {
        EcosystemPackDescriptor[] packs = [.. EcosystemPackCatalog.Discover()];

        Assert.Collection(
            packs,
            platform => AssertPack(
                platform,
                EcosystemPackIds.Platform,
                "Platform",
                100,
                packageSet: null,
                ProductDemoIds.StjSerializer,
                ProductDemoIds.StjSerializeCallGraph,
                ProductDemoIds.StjGetDecimalCallGraph),
            extensions => AssertPack(
                extensions,
                EcosystemPackIds.MicrosoftExtensions,
                "Microsoft.Extensions",
                200,
                PackageSetIds.MicrosoftExtensions,
                ProductDemoIds.ExtensionsCallGraph,
                ProductDemoIds.ConfigBindCallGraph,
                ProductDemoIds.OptionsAddCallGraph,
                ProductDemoIds.DiTryAddCallGraph,
                ProductDemoIds.HttpAddHttpClientCallGraph),
            aspNetCore => AssertPack(
                aspNetCore,
                EcosystemPackIds.AspNetCore,
                "ASP.NET Core",
                300,
                PackageSetIds.AspNetCore),
            aspire => AssertPack(
                aspire,
                EcosystemPackIds.Aspire,
                "Aspire",
                400,
                PackageSetIds.Aspire,
                ProductDemoIds.AspirePostgresCallGraph,
                ProductDemoIds.AspireRedisCallGraph));
    }

    [Fact]
    public void EveryPackageSetReferenceResolves()
    {
        PackageSetId[] expected =
        [
            PackageSetIds.MicrosoftExtensions,
            PackageSetIds.AspNetCore,
            PackageSetIds.Aspire,
        ];
        PackageSetId[] actual =
        [
            .. EcosystemPackCatalog.Discover()
                .Select(pack => pack.PackageSet)
                .OfType<PackageSetId>(),
        ];

        Assert.Equal(expected, actual);
        Assert.All(
            actual,
            id => Assert.IsType<PackageSetLookupResult.Known>(
                PackageSetCatalog.Lookup(id)));
    }

    [Fact]
    public void ShippedPackManifestCarriesOnlyPackageSetIdentity()
    {
        Type[] packTypes =
        [
            typeof(EcosystemPackRegistration),
            typeof(EcosystemPackDescriptor),
        ];

        Assert.All(
            packTypes,
            type =>
            {
                Assert.Contains(
                    type.GetProperties(),
                    property => property.PropertyType == typeof(PackageSetId));
                Assert.DoesNotContain(
                    type.GetProperties(),
                    property => CarriesPackageSetState(property.PropertyType));
            });
    }

    [Fact]
    public void ShippedDemoManifestMatchesLiteralPolicy()
    {
        var expected = new[]
        {
            (100, ProductDemoIds.StjSerializer, EcosystemPackIds.Platform, "System.Text.Json", "Browse a real package API"),
            (200, ProductDemoIds.ExtensionsCallGraph, EcosystemPackIds.MicrosoftExtensions, "Cross-package call graph", "Trace calls across three packages"),
            (300, ProductDemoIds.StjSerializeCallGraph, EcosystemPackIds.Platform, "Serialize call graph", "Dense package-local STJ graph"),
            (400, ProductDemoIds.ConfigBindCallGraph, EcosystemPackIds.MicrosoftExtensions, "Configuration Bind", "Recursive binder call graph"),
            (500, ProductDemoIds.OptionsAddCallGraph, EcosystemPackIds.MicrosoftExtensions, "Options hub", "Inbound fan-in at AddOptions"),
            (600, ProductDemoIds.DiTryAddCallGraph, EcosystemPackIds.MicrosoftExtensions, "DI TryAdd hub", "Keyed/scoped Try* fan-in"),
            (700, ProductDemoIds.HttpAddHttpClientCallGraph, EcosystemPackIds.MicrosoftExtensions, "AddHttpClient", "HttpClient factory registration"),
            (800, ProductDemoIds.StjGetDecimalCallGraph, EcosystemPackIds.Platform, "JsonElement.GetDecimal", "STJ number parse path"),
            (900, ProductDemoIds.AspirePostgresCallGraph, EcosystemPackIds.Aspire, "Aspire AddPostgres", "PostgreSQL resource registration graph"),
            (1000, ProductDemoIds.AspireRedisCallGraph, EcosystemPackIds.Aspire, "Aspire AddRedis", "Redis resource registration graph"),
        };

        EcosystemDemoDescriptor[] actual =
            [.. EcosystemPackCatalog.DiscoverDemos()];
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Item1, actual[index].Order);
            Assert.Equal(expected[index].Item2, actual[index].ScenarioId);
            Assert.Equal(expected[index].Item3, actual[index].Ecosystem);
            Assert.Equal(expected[index].Item4, actual[index].Title);
            Assert.Equal(expected[index].Item5, actual[index].Summary);
        }
    }

    [Fact]
    public void ExistingDemoSourcesPreserveDonorRecordsAndRunPlans()
    {
        ResolvedScenario stj = Select(ProductDemoIds.StjSerializer);
        Assert.Equal("System.Text.Json.JsonSerializer", stj.View!.Type);
        Assert.Equal(ProductDemoSections.Methods, stj.View.Section);
        AssertPackageAndNavigation(
            stj,
            "System.Text.Json",
            "10.0.0",
            "net10.0");
        ProductDemoRunPlan stjPlan = ProductDemoRunPlan.Create(stj);
        Assert.Same(stj.SelectedContext, stjPlan.Context);
        Assert.Same(stj.Navigation!.FocusTab, stjPlan.Focus);
        Assert.Null(stjPlan.Member);

        AssertCallGraph(
            ProductDemoIds.ExtensionsCallGraph,
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
            "74b6b4b321",
            "TryAddEnumerable",
            expectedMemberCount: 3);
        AssertCallGraph(
            ProductDemoIds.StjSerializeCallGraph,
            "System.Text.Json",
            "System.Text.Json.JsonSerializer",
            "1dc14dd1fb",
            "Serialize");
        AssertCallGraph(
            ProductDemoIds.ConfigBindCallGraph,
            "Microsoft.Extensions.Configuration.Binder",
            "Microsoft.Extensions.Configuration.ConfigurationBinder",
            "a6a6257f65",
            "Bind");
        AssertCallGraph(
            ProductDemoIds.OptionsAddCallGraph,
            "Microsoft.Extensions.Options",
            "Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions",
            "1e6bfaf2ae",
            "AddOptions");
        AssertCallGraph(
            ProductDemoIds.DiTryAddCallGraph,
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
            "6ce164c602",
            "TryAdd");
        AssertCallGraph(
            ProductDemoIds.HttpAddHttpClientCallGraph,
            "Microsoft.Extensions.Http",
            "Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions",
            "5c44566d15",
            "AddHttpClient");
        AssertCallGraph(
            ProductDemoIds.StjGetDecimalCallGraph,
            "System.Text.Json",
            "System.Text.Json.JsonElement",
            "cfd9980a6c",
            "GetDecimal");
    }

    [Fact]
    public void AspireDemoSourcesMatchLiteralPinsAndAnchors()
    {
        AssertAspireDemo(
            ProductDemoIds.AspirePostgresCallGraph,
            "Aspire.Hosting.PostgreSQL",
            "Aspire.Hosting.PostgresBuilderExtensions",
            "e5a66a2bd9",
            "AddPostgres");
        AssertAspireDemo(
            ProductDemoIds.AspireRedisCallGraph,
            "Aspire.Hosting.Redis",
            "Aspire.Hosting.RedisBuilderExtensions",
            "7618364a03",
            "AddRedis");
    }

    [Fact]
    public void EveryShippedDemoBindsAKnownProductSection()
    {
        foreach (EcosystemDemoDescriptor descriptor in EcosystemPackCatalog.DiscoverDemos())
        {
            ResolvedScenario scenario = Select(descriptor.ScenarioId);
            Assert.True(
                ProductDemoSections.IsKnown(scenario.View!.Section),
                $"Product demo '{descriptor.ScenarioId}' has unsupported section '{scenario.View.Section}'.");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ecosystem.")]
    [InlineData("ecosystem.1example")]
    [InlineData("ecosystem.Example")]
    [InlineData("ecosystem.example_name")]
    [InlineData("ecosystem.example--name")]
    [InlineData("ecosystem.example-")]
    public void InvalidPackIdentityTextIsRejected(string? value)
    {
        Assert.False(EcosystemPackId.TryCreate(value, out EcosystemPackId? id));
        Assert.Null(id);
    }

    private static void AssertPack(
        EcosystemPackDescriptor pack,
        EcosystemPackId id,
        string title,
        int order,
        PackageSetId? packageSet,
        params string[] demos)
    {
        Assert.Equal(id, pack.Id);
        Assert.Equal(title, pack.Title);
        Assert.False(string.IsNullOrWhiteSpace(pack.Summary));
        Assert.Equal(order, pack.Order);
        Assert.Equal(packageSet, pack.PackageSet);
        Assert.Equal(demos, pack.Demos.Select(demo => demo.ScenarioId));
    }

    private static void AssertCallGraph(
        string scenarioId,
        string packageId,
        string type,
        string anchor,
        string memberName,
        string packageVersion = "10.0.0",
        string framework = "net10.0",
        int expectedMemberCount = 1)
    {
        ResolvedScenario scenario = Select(scenarioId);
        AssertPackageAndNavigation(
            scenario,
            packageId,
            packageVersion,
            framework,
            expectedMemberCount);
        Assert.Equal(type, scenario.View!.Type);
        Assert.Equal(anchor, scenario.View.MemberAnchor);
        Assert.Equal($"method:{memberName}", scenario.View.MemberKey);
        Assert.Equal(ProductDemoSections.CallGraph, scenario.View.Section);
        Assert.Null(scenario.View.MemberSignature);
        ProductDemoRunPlan plan = ProductDemoRunPlan.Create(scenario);
        Assert.Same(scenario.SelectedContext, plan.Context);
        Assert.Same(scenario.Navigation!.FocusTab, plan.Focus);
        Assert.Equal(scenario.View.Type, plan.TypeName);
        Assert.Equal(ProductDemoSections.CallGraph, plan.Section);
        Assert.Equal(memberName, plan.Member!.Name);
        Assert.Equal("method", plan.Member.Kind);
        Assert.Equal(anchor, plan.Member.Anchor);
    }

    private static void AssertAspireDemo(
        string scenarioId,
        string packageId,
        string type,
        string anchor,
        string memberName)
    {
        ResolvedScenario scenario = Select(scenarioId);
        WorkspaceMemberCoordinate.PackageMember package = Package(scenario);
        Assert.Equal(packageId, package.PackageId);
        Assert.Equal("13.5.3", package.Version);
        Assert.Equal("net8.0", package.Framework);
        AssertCallGraph(
            scenarioId,
            packageId,
            type,
            anchor,
            memberName,
            packageVersion: "13.5.3",
            framework: "net8.0");
    }

    private static void AssertPackageAndNavigation(
        ResolvedScenario scenario,
        string packageId,
        string packageVersion,
        string framework,
        int expectedMemberCount = 1)
    {
        Assert.True(scenario.CreatesAssemblyContextGroup);
        Assert.Equal(expectedMemberCount, scenario.SelectedContext!.Members.Count);
        Assert.All(
            scenario.SelectedContext.Members,
            member =>
            {
                WorkspaceMemberCoordinate.PackageMember package =
                    Assert.IsType<WorkspaceMemberCoordinate.PackageMember>(member);
                Assert.Equal(packageVersion, package.Version);
                Assert.Equal(framework, package.Framework);
            });

        Assert.NotNull(scenario.Navigation);
        Assert.Equal(expectedMemberCount, scenario.Navigation!.Tabs.Count);
        Assert.Equal(0, scenario.Navigation.FocusIndex);
        WorkspaceMemberCoordinate.PackageMember focus =
            Assert.IsType<WorkspaceMemberCoordinate.PackageMember>(
                scenario.Navigation.FocusTab.Coordinate);
        Assert.Equal(packageId, focus.PackageId);
        Assert.Equal(packageVersion, focus.Version);
        Assert.Equal(framework, focus.Framework);
    }

    private static WorkspaceMemberCoordinate.PackageMember Package(
        ResolvedScenario scenario) =>
        Assert.IsType<WorkspaceMemberCoordinate.PackageMember>(
            Assert.Single(scenario.SelectedContext!.Members));

    private static bool CarriesPackageSetState(Type type) =>
        type == typeof(PackageSetDescriptor)
        || type == typeof(PackageSetRegistration)
        || type == typeof(PackageSetRegistry)
        || type == typeof(PackageCoordinate)
        || (type.IsArray && CarriesPackageSetState(type.GetElementType()!))
        || (type.IsGenericType
            && type.GetGenericArguments().Any(CarriesPackageSetState));

    private static ResolvedScenario Select(string scenarioId) =>
        Assert.IsType<EcosystemDemoSelectionResult.Known>(
            EcosystemPackCatalog.SelectDemo(scenarioId)).Selection.Scenario;
}
