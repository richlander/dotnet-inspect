using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries.Definitions;

namespace InspectWeb.Engine.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserProductHomeDemosTests
{
    [Fact]
    public void ListHomeDemos_MatchesProductCatalogOrderAndLabels()
    {
        using var document = JsonDocument.Parse(InspectionEngine.ListHomeDemos());
        var demos = document.RootElement.GetProperty("demos");
        Assert.Equal(ProductInspectionDemos.Entries.Count, demos.GetArrayLength());
        for (var i = 0; i < demos.GetArrayLength(); i++)
        {
            var expected = ProductInspectionDemos.Entries[i];
            var actual = demos[i];
            Assert.Equal(expected.Id, actual.GetProperty("id").GetString());
            Assert.Equal(expected.Title, actual.GetProperty("title").GetString());
            Assert.Equal(expected.Summary, actual.GetProperty("summary").GetString());
        }
    }

    [Fact]
    public void ResolveHomeDemo_UnknownId_ReturnsNotFound()
    {
        using var missing = JsonDocument.Parse(InspectionEngine.ResolveHomeDemo("not-a-demo"));
        Assert.False(missing.RootElement.GetProperty("found").GetBoolean());
        Assert.Equal(JsonValueKind.Null, missing.RootElement.GetProperty("demo").ValueKind);

        using var legacy = JsonDocument.Parse(InspectionEngine.ResolveHomeDemo("stj"));
        Assert.False(legacy.RootElement.GetProperty("found").GetBoolean());
    }

    [Fact]
    public void ResolveHomeDemo_StjSerializer_ProjectsPackageAndTypeView()
    {
        using var document = JsonDocument.Parse(
            InspectionEngine.ResolveHomeDemo(ProductInspectionDemos.StjSerializerScenarioId));
        var root = document.RootElement.GetProperty("demo");
        Assert.True(document.RootElement.GetProperty("found").GetBoolean());
        Assert.Equal(ProductInspectionDemos.StjSerializerScenarioId, root.GetProperty("id").GetString());
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
            InspectionEngine.ResolveHomeDemo(ProductInspectionDemos.ExtensionsCallGraphScenarioId));
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
    public void ExtensionsCallGraph_RunPlanOwnsWorkspaceFocusAndMemberSelection()
    {
        BrowserHomeDemoRunPlan plan =
            BrowserProductHomeDemos.ToCallGraphRunPlan(
                ProductInspectionDemos.ResolveHomeScenario(
                    ProductInspectionDemos.ExtensionsCallGraphScenarioId));

        Assert.Equal(3, plan.Requests.Length);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            plan.Requests[0].PackageId);
        Assert.Equal("10.0.0", plan.Requests[0].Version);
        Assert.Equal("net10.0", plan.Requests[0].TargetFramework);
        Assert.Equal(0, plan.FocusRequestIndex);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
            plan.TypeId);
        Assert.Equal("TryAddEnumerable", plan.MemberName);
        Assert.Equal("method", plan.MemberKind);
        Assert.Equal("74b6b4b321", plan.MemberAnchorDigest);
        Assert.Equal("call-graph", plan.MemberSection);
    }

    [Fact]
    public void ToCallGraphRunPlan_DerivesNonFirstFocusFromNavigation()
    {
        const int version = InspectionDefinitionJson.CurrentSchemaVersion;
        var first = new DefinitionMemberCoordinate.PackageCoordinate(
            "Demo.First",
            "1.0.0",
            "net11.0");
        var second = new DefinitionMemberCoordinate.PackageCoordinate(
            "Demo.Second",
            "2.0.0",
            "net11.0");
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            version,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    framework: "net11.0",
                    members: [first, second]),
            ]));
        registry.Add(new ViewDefinition(
            version,
            "view",
            type: "Demo.Second.Target",
            memberAnchor: "1234567890",
            memberKey: "method:Run",
            section: ProductDemoSections.CallGraph));
        registry.Add(new NavigationDefinition(
            version,
            "navigation",
            [
                new NavigationTabDefinition("first", coordinate: first),
                new NavigationTabDefinition("second", coordinate: second),
            ],
            focus: "second"));
        registry.Add(new ScenarioDefinition(
            version,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: "view",
            navigation: "navigation"));

        BrowserHomeDemoRunPlan plan = BrowserProductHomeDemos.ToCallGraphRunPlan(
            registry.ResolveScenario("scenario"));

        Assert.Equal(1, plan.FocusRequestIndex);
        Assert.Equal(
            "Demo.Second",
            plan.Requests[plan.FocusRequestIndex].PackageId);
    }

    [Fact]
    public async Task RunHomeDemo_UnknownId_ReturnsNotFound()
    {
        using var document = JsonDocument.Parse(
            await InspectionEngine.RunHomeDemo("not-a-demo"));

        Assert.False(document.RootElement.GetProperty("found").GetBoolean());
        Assert.Empty(document.RootElement.GetProperty("packages").EnumerateArray());
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("activation").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("callGraph").ValueKind);
    }

}
