using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries.Definitions;

using InspectWeb.Engine.CatalogFacade;

namespace InspectWeb.Engine.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserProductHomeDemosTests
{
    [Fact]
    public void ListHomeDemos_MatchesProductCatalogOrderAndLabels()
    {
        using var document = JsonDocument.Parse(CatalogExports.ListHomeDemos());
        var demos = document.RootElement.GetProperty("demos");
        IReadOnlyList<EcosystemDemoDescriptor> expectedDemos =
            EcosystemPackCatalog.DiscoverDemos();
        Assert.Equal(expectedDemos.Count, demos.GetArrayLength());
        for (var i = 0; i < demos.GetArrayLength(); i++)
        {
            var expected = expectedDemos[i];
            var actual = demos[i];
            Assert.Equal(expected.ScenarioId, actual.GetProperty("id").GetString());
            Assert.Equal(expected.Title, actual.GetProperty("title").GetString());
            Assert.Equal(expected.Summary, actual.GetProperty("summary").GetString());
        }
    }

    [Fact]
    public void ResolveHomeDemo_UnknownId_ReturnsNotFound()
    {
        using var missing = JsonDocument.Parse(CatalogExports.ResolveHomeDemo("not-a-demo"));
        Assert.False(missing.RootElement.GetProperty("found").GetBoolean());
        Assert.Equal(JsonValueKind.Null, missing.RootElement.GetProperty("demo").ValueKind);

        using var legacy = JsonDocument.Parse(CatalogExports.ResolveHomeDemo("stj"));
        Assert.False(legacy.RootElement.GetProperty("found").GetBoolean());

        using var missingNull = JsonDocument.Parse(CatalogExports.ResolveHomeDemo(null!));
        Assert.False(missingNull.RootElement.GetProperty("found").GetBoolean());

        using var missingBlank = JsonDocument.Parse(CatalogExports.ResolveHomeDemo(" "));
        Assert.False(missingBlank.RootElement.GetProperty("found").GetBoolean());
    }

    [Fact]
    public void CatalogProjectionUsesEcosystemDescriptorMetadata()
    {
        EcosystemDemoSelection selection =
            Select(ProductDemoIds.AspirePostgresCallGraph);
        Assert.NotEqual(
            selection.Descriptor.Title,
            selection.Scenario.Title);
        Assert.NotEqual(
            selection.Descriptor.Summary,
            selection.Scenario.Description);

        using var document = JsonDocument.Parse(
            CatalogExports.ResolveHomeDemo(
                ProductDemoIds.AspirePostgresCallGraph));
        JsonElement demo = document.RootElement.GetProperty("demo");
        Assert.Equal(
            selection.Descriptor.Title,
            demo.GetProperty("title").GetString());
        Assert.Equal(
            selection.Descriptor.Summary,
            demo.GetProperty("summary").GetString());
    }

    [Fact]
    public void ResolveHomeDemo_StjSerializer_ProjectsPackageAndTypeView()
    {
        using var document = JsonDocument.Parse(
            CatalogExports.ResolveHomeDemo(ProductDemoIds.StjSerializer));
        var root = document.RootElement.GetProperty("demo");
        Assert.True(document.RootElement.GetProperty("found").GetBoolean());
        Assert.Equal(ProductDemoIds.StjSerializer, root.GetProperty("id").GetString());
        Assert.Equal("System.Text.Json", root.GetProperty("title").GetString());

        var members = root.GetProperty("workspaceMembers");
        Assert.Equal(1, members.GetArrayLength());
        Assert.Equal("package", members[0].GetProperty("kind").GetString());
        Assert.Equal("System.Text.Json", members[0].GetProperty("id").GetString());
        Assert.Equal("10.0.0", members[0].GetProperty("version").GetString());
        Assert.Equal("net10.0", members[0].GetProperty("framework").GetString());

        Assert.Equal(0, root.GetProperty("focusTabIndex").GetInt32());
        var view = root.GetProperty("view");
        Assert.Equal("System.Text.Json.JsonSerializer", view.GetProperty("type").GetString());
        Assert.Equal(ProductDemoSections.Methods, view.GetProperty("section").GetString());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("memberAnchor").ValueKind);
    }

    [Fact]
    public void ResolveHomeDemo_ExtensionsCallGraph_ProjectsPackagesAndMemberAnchor()
    {
        using var document = JsonDocument.Parse(
            CatalogExports.ResolveHomeDemo(ProductDemoIds.ExtensionsCallGraph));
        var root = document.RootElement.GetProperty("demo");
        var members = root.GetProperty("workspaceMembers");
        Assert.Equal(3, members.GetArrayLength());
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            members[0].GetProperty("id").GetString());
        Assert.Equal("Microsoft.Extensions.Logging", members[1].GetProperty("id").GetString());
        Assert.Equal("Microsoft.Extensions.Http", members[2].GetProperty("id").GetString());

        Assert.Equal(0, root.GetProperty("focusTabIndex").GetInt32());
        Assert.Equal("di", root.GetProperty("tabs")[0].GetProperty("id").GetString());

        var view = root.GetProperty("view");
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
            view.GetProperty("type").GetString());
        Assert.Equal("74b6b4b321", view.GetProperty("memberAnchor").GetString());
        Assert.Equal("method:TryAddEnumerable", view.GetProperty("memberKey").GetString());
        Assert.Equal(ProductDemoSections.CallGraph, view.GetProperty("section").GetString());
    }

    [Fact]
    public void ToRunPlan_AllProductHomeDemosHaveSupportedBrowserShape()
    {
        foreach (var entry in EcosystemPackCatalog.DiscoverDemos())
        {
            BrowserHomeDemoRunPlan plan = BrowserProductHomeDemos.ToRunPlan(
                Select(entry.ScenarioId).Scenario);

            Assert.InRange(plan.FocusRequestIndex, 0, plan.Requests.Length - 1);
            if (plan.Section == ProductDemoSections.Methods)
            {
                Assert.Null(plan.Member);
            }
            else
            {
                Assert.Equal(ProductDemoSections.CallGraph, plan.Section);
                Assert.NotNull(plan.Member);
            }
        }
    }

    [Fact]
    public void StjSerializer_RunPlanOwnsTypeOnlyMethodsSelection()
    {
        BrowserHomeDemoRunPlan plan =
            BrowserProductHomeDemos.ToRunPlan(
                Select(ProductDemoIds.StjSerializer).Scenario);

        Assert.Single(plan.Requests);
        BrowserPackageRequest request = Assert.IsType<
            BrowserHomeDemoRunRequest.Package>(plan.Requests[0]).Request;
        Assert.Equal("System.Text.Json", request.PackageId);
        Assert.Equal("10.0.0", request.Version);
        Assert.Equal("net10.0", request.TargetFramework);
        Assert.Equal(0, plan.FocusRequestIndex);
        Assert.Equal("System.Text.Json.JsonSerializer", plan.TypeId);
        Assert.Equal(ProductDemoSections.Methods, plan.Section);
        Assert.Null(plan.Member);
    }

    [Fact]
    public void ExtensionsCallGraph_RunPlanOwnsWorkspaceFocusAndMemberSelection()
    {
        BrowserHomeDemoRunPlan plan =
            BrowserProductHomeDemos.ToRunPlan(
                Select(ProductDemoIds.ExtensionsCallGraph).Scenario);

        Assert.Equal(3, plan.Requests.Length);
        BrowserPackageRequest[] requests =
        [
            .. plan.Requests.Select(request =>
                Assert.IsType<BrowserHomeDemoRunRequest.Package>(request).Request),
        ];
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            requests[0].PackageId);
        Assert.Equal("10.0.0", requests[0].Version);
        Assert.Equal("net10.0", requests[0].TargetFramework);
        Assert.Equal(0, plan.FocusRequestIndex);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
            plan.TypeId);
        Assert.Equal(ProductDemoSections.CallGraph, plan.Section);
        BrowserHomeDemoRunMember member = Assert.IsType<BrowserHomeDemoRunMember>(plan.Member);
        Assert.Equal("TryAddEnumerable", member.Name);
        Assert.Equal("method", member.MemberKind);
        Assert.Equal("74b6b4b321", member.AnchorDigest);
        Assert.Equal("call-graph", member.MemberSection);
    }

    [Fact]
    public void ToRunPlan_DerivesNonFirstFocusForTypeOnlyMethodsView()
    {
        BrowserHomeDemoRunPlan plan = BrowserProductHomeDemos.ToRunPlan(
            ResolveSyntheticScenario(ProductDemoSections.Methods));

        Assert.Equal(1, plan.FocusRequestIndex);
        BrowserPackageRequest focus = Assert.IsType<
            BrowserHomeDemoRunRequest.Package>(
                plan.Requests[plan.FocusRequestIndex]).Request;
        Assert.Equal(
            "Demo.Second",
            focus.PackageId);
        Assert.Equal("Demo.Second.Target", plan.TypeId);
        Assert.Equal(ProductDemoSections.Methods, plan.Section);
        Assert.Null(plan.Member);
    }

    [Fact]
    public void ToRunPlan_PlatformCoordinatePreservesSourceNativeFocus()
    {
        BrowserHomeDemoRunPlan plan =
            BrowserProductHomeDemos.ToRunPlan(
                ResolveSyntheticPlatformScenario());

        BrowserHomeDemoRunRequest.Platform request = Assert.IsType<
            BrowserHomeDemoRunRequest.Platform>(Assert.Single(plan.Requests));
        Assert.Equal("runtime", request.Family);
        Assert.Equal("System.Text.Json", request.Assembly);
        Assert.Equal("10.0.0", request.Version);
        Assert.Equal("net10.0", request.TargetFramework);
        Assert.Equal(0, plan.FocusRequestIndex);
        Assert.Equal("System.Text.Json.JsonSerializer", plan.TypeId);
        Assert.Equal(ProductDemoSections.Methods, plan.Section);
    }

    [Fact]
    public void ToRunPlan_RejectsMixedPackageAndPlatformWorkspace()
    {
        InspectionDefinitionException error =
            Assert.Throws<InspectionDefinitionException>(
                () => BrowserProductHomeDemos.ToRunPlan(
                    ResolveSyntheticPlatformScenario(includePackage: true)));

        Assert.Contains(
            "does not support mixed package and Platform workspaces",
            error.Message);
    }

    [Fact]
    public void ToRunPlan_RejectsUnsupportedPlatformFamily()
    {
        InspectionDefinitionException error =
            Assert.Throws<InspectionDefinitionException>(
                () => BrowserProductHomeDemos.ToRunPlan(
                    ResolveSyntheticPlatformScenario(
                        family: "unsupported")));

        Assert.Contains(
            "Platform family 'unsupported' is not supported",
            error.Message);
    }

    [Theory]
    [InlineData("11.0.0", "net10.0")]
    public void ToRunPlan_RejectsNonUniformPlatformTarget(
        string secondVersion,
        string secondFramework)
    {
        InspectionDefinitionException error =
            Assert.Throws<InspectionDefinitionException>(
                () => BrowserProductHomeDemos.ToRunPlan(
                    ResolveSyntheticPlatformScenario(
                        secondPlatform:
                            ("runtime",
                                "System.Runtime",
                                secondVersion,
                                secondFramework))));

        Assert.Contains(
            "must use one exact target framework and Platform version",
            error.Message);
    }

    [Fact]
    public void ToRunPlan_RejectsPlatformFrameworkConflictingWithContext()
    {
        InspectionDefinitionException error =
            Assert.Throws<InspectionDefinitionException>(
                () => BrowserProductHomeDemos.ToRunPlan(
                    ResolveSyntheticPlatformScenario(
                        contextFramework: "net9.0")));

        Assert.Contains(
            "framework 'net10.0' conflicts with workspace context framework 'net9.0'",
            error.Message);
    }

    [Fact]
    public void ToRunPlan_RejectsFloatingPlatformVersion()
    {
        InspectionDefinitionException error =
            Assert.Throws<InspectionDefinitionException>(
                () => BrowserProductHomeDemos.ToRunPlan(
                    ResolveSyntheticPlatformScenario(
                        version: "latest")));

        Assert.Contains(
            "must pin an exact version for browser execution",
            error.Message);
    }

    [Fact]
    public void ToRunPlan_PlatformWorkspacePreservesNonFirstFocus()
    {
        BrowserHomeDemoRunPlan plan =
            BrowserProductHomeDemos.ToRunPlan(
                ResolveSyntheticPlatformScenario(
                    secondPlatform:
                        ("runtime",
                            "System.Runtime",
                            "10.0.0",
                            "net10.0"),
                    focusSecond: true));

        Assert.Equal(2, plan.Requests.Length);
        Assert.Equal(
            ["System.Text.Json", "System.Runtime"],
            plan.Requests.Select(request =>
                Assert.IsType<BrowserHomeDemoRunRequest.Platform>(request)
                    .Assembly));
        Assert.Equal(1, plan.FocusRequestIndex);
    }

    [Fact]
    public void ToRunPlan_RejectsCaseInsensitivePlatformDuplicates()
    {
        InspectionDefinitionException error =
            Assert.Throws<InspectionDefinitionException>(
                () => BrowserProductHomeDemos.ToRunPlan(
                    ResolveSyntheticPlatformScenario(
                        secondPlatform:
                            ("runtime",
                                "system.text.json",
                                "10.0.0",
                                "net10.0"))));

        Assert.Contains(
            "contains duplicate Platform coordinates",
            error.Message);
    }

    [Fact]
    public void ToRunPlan_RejectsUnsupportedBrowserSection()
    {
        InspectionDefinitionException error = Assert.Throws<InspectionDefinitionException>(
            () => BrowserProductHomeDemos.ToRunPlan(
                ResolveSyntheticScenario(ProductDemoSections.Callers)));

        Assert.Contains("does not implement section 'Callers'", error.Message);
    }

    [Fact]
    public void ToRunPlan_RejectsLibraryScopedView()
    {
        InspectionDefinitionException error = Assert.Throws<InspectionDefinitionException>(
            () => BrowserProductHomeDemos.ToRunPlan(
                ResolveSyntheticScenario(
                    ProductDemoSections.Methods,
                    library: "Demo.Second")));

        Assert.Contains("does not support library-scoped views", error.Message);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("package")]
    [InlineData("navigation")]
    public void ToRunPlan_RejectsRuntimeIdentifierScopes(string source)
    {
        InspectionDefinitionException error = Assert.Throws<InspectionDefinitionException>(
            () => BrowserProductHomeDemos.ToRunPlan(
                ResolveSyntheticScenario(
                    ProductDemoSections.Methods,
                    contextRuntimeIdentifier: source == "context"
                        ? "linux-x64"
                        : null,
                    packageRuntimeIdentifier: source == "package"
                        ? "linux-x64"
                        : null,
                    navigationRuntimeIdentifier: source == "navigation"
                        ? "linux-x64"
                        : null)));

        Assert.Contains(
            "does not support runtime-identifier-scoped package workspaces",
            error.Message);
    }

    [Fact]
    public void ToRunPlan_RejectsFocusOutsideSelectedContext()
    {
        InspectionDefinitionException error = Assert.Throws<InspectionDefinitionException>(
            () => BrowserProductHomeDemos.ToRunPlan(
                ResolveSyntheticScenario(
                    ProductDemoSections.Methods,
                    includeFocusedPackageInContext: false)));

        Assert.Contains(
            "navigation focus is not a member of its selected workspace context",
            error.Message);
    }

    [Fact]
    public async Task RunHomeDemo_UnknownId_ReturnsNotFound()
    {
        using var document = JsonDocument.Parse(
            await CatalogExports.RunHomeDemo("not-a-demo"));

        Assert.False(document.RootElement.GetProperty("found").GetBoolean());
        Assert.Empty(document.RootElement.GetProperty("packages").EnumerateArray());
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("activation").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("callGraph").ValueKind);

        using var missingNull = JsonDocument.Parse(
            await CatalogExports.RunHomeDemo(null!));
        Assert.False(missingNull.RootElement.GetProperty("found").GetBoolean());
    }

    private static ResolvedScenario ResolveSyntheticScenario(
        string section,
        string? library = null,
        string? contextRuntimeIdentifier = null,
        string? packageRuntimeIdentifier = null,
        string? navigationRuntimeIdentifier = null,
        bool includeFocusedPackageInContext = true)
    {
        const int version = InspectionDefinitionJson.CurrentSchemaVersion;
        var first = new DefinitionMemberCoordinate.PackageCoordinate(
            "Demo.First",
            "1.0.0",
            "net11.0");
        var second = new DefinitionMemberCoordinate.PackageCoordinate(
            "Demo.Second",
            "2.0.0",
            "net11.0",
            packageRuntimeIdentifier);
        DefinitionMemberCoordinate[] contextMembers =
            includeFocusedPackageInContext ? [first, second] : [first];
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            version,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    framework: "net11.0",
                    runtimeIdentifier: contextRuntimeIdentifier,
                    members: contextMembers),
            ]));
        registry.Add(new ViewDefinition(
            version,
            "view",
            type: "Demo.Second.Target",
            section: section,
            library: library));
        registry.Add(new NavigationDefinition(
            version,
            "navigation",
            [
                new NavigationTabDefinition("first", coordinate: first),
                new NavigationTabDefinition(
                    "second",
                    coordinate: second,
                    runtimeIdentifier: navigationRuntimeIdentifier),
            ],
            focus: "second"));
        registry.Add(new ScenarioDefinition(
            version,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: "view",
            navigation: "navigation"));
        return registry.ResolveScenario("scenario");
    }

    private static ResolvedScenario ResolveSyntheticPlatformScenario(
        bool includePackage = false,
        string family = "runtime",
        string version = "10.0.0",
        string framework = "net10.0",
        string? contextFramework = null,
        (string Family, string Assembly, string Version, string Framework)?
            secondPlatform = null,
        bool focusSecond = false)
    {
        const int schemaVersion =
            InspectionDefinitionJson.CurrentSchemaVersion;
        var platform = new DefinitionMemberCoordinate.PlatformCoordinate(
            family,
            "System.Text.Json",
            version,
            framework);
        DefinitionMemberCoordinate.PlatformCoordinate? second =
            secondPlatform is { } value
                ? new DefinitionMemberCoordinate.PlatformCoordinate(
                    value.Family,
                    value.Assembly,
                    value.Version,
                    value.Framework)
                : null;
        List<DefinitionMemberCoordinate> members = [platform];
        if (second is not null)
            members.Add(second);
        if (includePackage)
        {
            members.Add(
                new DefinitionMemberCoordinate.PackageCoordinate(
                    "Demo.Package",
                    "1.0.0",
                    framework));
        }
        List<NavigationTabDefinition> tabs =
            [new("platform", coordinate: platform)];
        if (second is not null)
            tabs.Add(new NavigationTabDefinition("second", coordinate: second));
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            schemaVersion,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    framework: contextFramework ?? framework,
                    members: [.. members]),
            ]));
        registry.Add(new ViewDefinition(
            schemaVersion,
            "view",
            type: "System.Text.Json.JsonSerializer",
            section: ProductDemoSections.Methods));
        registry.Add(new NavigationDefinition(
            schemaVersion,
            "navigation",
            [.. tabs],
            focus: focusSecond ? "second" : "platform"));
        registry.Add(new ScenarioDefinition(
            schemaVersion,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: "view",
            navigation: "navigation"));
        return registry.ResolveScenario("scenario");
    }

    private static EcosystemDemoSelection Select(string scenarioId) =>
        Assert.IsType<EcosystemDemoSelectionResult.Known>(
            EcosystemPackCatalog.SelectDemo(scenarioId)).Selection;
}
