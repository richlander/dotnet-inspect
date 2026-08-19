using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

public class InspectionDefinitionTests
{
    [Fact]
    public void JsonRoundTrip_PreservesEveryRecordKind()
    {
        InspectionDefinitionRecord[] records =
        [
            new CatalogDefinition(
                1,
                "extensions",
                [
                    new CatalogGroupDefinition(
                        "Extensions",
                        [
                            new DefinitionMemberCoordinate.PackageCoordinate(
                                "Microsoft.Extensions.Logging",
                                "10.0.0",
                                "net10.0"),
                        ]),
                ]),
            new WorkspaceDefinition(
                1,
                "ws",
                [
                    new WorkspaceContextDefinition(
                        "main",
                        framework: "net10.0",
                        members:
                        [
                            new DefinitionMemberCoordinate.PackageCoordinate("A", "1.0.0", "net10.0"),
                        ]),
                ],
                title: "Workspace"),
            new QueryDefinition(1, "q1", queryId: "surface"),
            new ViewDefinition(
                1,
                "v1",
                type: "N.T",
                memberAnchor: "deadbeef01",
                section: "call-graph"),
            new NavigationDefinition(
                1,
                "n1",
                [
                    new NavigationTabDefinition(
                        "t1",
                        coordinate: new DefinitionMemberCoordinate.PackageCoordinate("A", "1.0.0", "net10.0")),
                ],
                focus: "t1"),
            new ScenarioDefinition(
                1,
                "s1",
                title: "Demo",
                workspace: "ws",
                context: "main",
                view: "v1",
                navigation: "n1"),
        ];

        foreach (var record in records)
        {
            var json = InspectionDefinitionJson.Serialize(record);
            var roundTripped = InspectionDefinitionJson.Parse(json);
            Assert.Equal(record.Kind, roundTripped.Kind);
            Assert.Equal(record.Id, roundTripped.Id);
            Assert.Equal(record.SchemaVersion, roundTripped.SchemaVersion);
            Assert.Equal(json, InspectionDefinitionJson.Serialize(roundTripped));
        }
    }

    [Fact]
    public void Parse_RejectsDuplicateProperties()
    {
        const string json = """
            { "schemaVersion": 1, "kind": "query", "id": "q", "id": "other" }
            """;

        var ex = Assert.Throws<JsonException>(() => InspectionDefinitionJson.Parse(json));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsUnknownProperties()
    {
        const string json = """
            { "schemaVersion": 1, "kind": "query", "id": "q", "versoin": 1 }
            """;

        Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(json));
    }

    [Fact]
    public void Parse_RejectsUnknownKindAndSchemaVersion()
    {
        Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """{ "schemaVersion": 1, "kind": "bundle", "id": "x" }"""));
        Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """{ "schemaVersion": 99, "kind": "query", "id": "x" }"""));
    }

    [Fact]
    public void View_RejectsConflictingMemberSelectors()
    {
        Assert.Throws<ArgumentException>(() => new ViewDefinition(
            1,
            "v",
            type: "T",
            memberAnchor: "a",
            memberSignature: "sig"));
    }

    [Fact]
    public void Registry_RejectsDuplicateIdsWithinKind_AndResolvesPeerComposition()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            1,
            "ws",
            [new WorkspaceContextDefinition(
                "c",
                members: [new DefinitionMemberCoordinate.PackageCoordinate("P", "1.0.0", "net10.0")])]));
        registry.Add(new ViewDefinition(1, "v", type: "T"));
        registry.Add(new NavigationDefinition(
            1,
            "n",
            [new NavigationTabDefinition(
                "t",
                coordinate: new DefinitionMemberCoordinate.PackageCoordinate("P", "1.0.0", "net10.0"))],
            focus: "t"));
        registry.Add(new ScenarioDefinition(1, "s", workspace: "ws", view: "v", navigation: "n"));

        Assert.Throws<InspectionDefinitionException>(() =>
            registry.Add(new ScenarioDefinition(1, "s", workspace: "ws")));

        var resolved = registry.ResolveScenario("s");
        Assert.Equal("s", resolved.ScenarioId);
        Assert.True(resolved.CreatesAssemblyContextGroup);
        Assert.Equal("c", resolved.SelectedContextName);
        Assert.Single(resolved.Contexts);
        Assert.IsType<WorkspaceMemberCoordinate.PackageMember>(resolved.SelectedContext!.Members[0]);
        Assert.Equal("T", resolved.View!.Type);
        Assert.Equal(0, resolved.Navigation!.FocusIndex);
    }

    [Fact]
    public void Registry_UnknownPeerReference_FailsVisibly()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new ScenarioDefinition(1, "s", workspace: "missing"));

        var ex = Assert.Throws<InspectionDefinitionException>(() => registry.ResolveScenario("s"));
        Assert.Contains("unknown workspace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_WorkspaceFreeScenario_CreatesNoAssemblyGroup()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new ScenarioDefinition(1, "source-only", input: "bundle:doc"));
        registry.Add(new ViewDefinition(1, "v", type: "T"));
        // Re-add with view via new registry
        registry = new InspectionDefinitionRegistry();
        registry.Add(new ScenarioDefinition(1, "source-only", input: "bundle:doc", view: "v"));
        registry.Add(new ViewDefinition(1, "v", type: "T"));

        var resolved = registry.ResolveScenario("source-only");
        Assert.False(resolved.CreatesAssemblyContextGroup);
        Assert.Empty(resolved.Contexts);
        Assert.Null(resolved.SelectedContext);
        Assert.Equal("bundle:doc", resolved.Scenario.Input);
    }

    [Fact]
    public void Registry_DoesNotActivateImplicitlyFromRecordCount()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new ScenarioDefinition(1, "only", input: "x"));
        Assert.Throws<InspectionDefinitionException>(() => registry.ResolveScenario("other"));
    }

    [Fact]
    public void ProductHomeDemos_ResolveCallGraphByMemberAnchor()
    {
        Assert.Equal(
            ["stj-serializer", "extensions-callgraph", "platform-list"],
            ProductInspectionDemos.HomeScenarioIds);
        Assert.Equal(3, ProductInspectionDemos.Entries.Count);
        Assert.True(ProductInspectionDemos.HasScenario("extensions-callgraph"));

        // Per-demo resolve — does not require materializing the other home demos.
        var callGraph = ProductInspectionDemos.ResolveHomeScenario("extensions-callgraph");
        Assert.Equal("Cross-package call graph", callGraph.Title);
        Assert.True(callGraph.CreatesAssemblyContextGroup);
        Assert.Equal(3, callGraph.SelectedContext!.Members.Count);
        Assert.All(
            callGraph.SelectedContext.Members,
            member => Assert.IsType<WorkspaceMemberCoordinate.PackageMember>(member));

        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
            callGraph.View!.Type);
        Assert.Equal("74b6b4b321", callGraph.View.MemberAnchor);
        Assert.Equal("method:TryAddEnumerable", callGraph.View.MemberKey);
        Assert.Equal("call-graph", callGraph.View.Section);
        Assert.Null(callGraph.View.MemberSignature);

        Assert.NotNull(callGraph.Navigation);
        Assert.Equal(3, callGraph.Navigation!.Tabs.Count);
        Assert.Equal("di", callGraph.Navigation.FocusTabId);
        Assert.Equal(0, callGraph.Navigation.FocusIndex);
        var focus = Assert.IsType<WorkspaceMemberCoordinate.PackageMember>(
            callGraph.Navigation.FocusTab.Coordinate);
        Assert.Equal("Microsoft.Extensions.DependencyInjection.Abstractions", focus.PackageId);
        Assert.Equal("10.0.0", focus.Version);
    }

    [Fact]
    public void ProductHomeDemos_StjAndPlatformSelections()
    {
        var stj = ProductInspectionDemos.ResolveHomeScenario("stj-serializer");
        Assert.Equal("System.Text.Json.JsonSerializer", stj.View!.Type);
        var stjPackage = Assert.IsType<WorkspaceMemberCoordinate.PackageMember>(
            stj.SelectedContext!.Members[0]);
        Assert.Equal("System.Text.Json", stjPackage.PackageId);

        var platform = ProductInspectionDemos.ResolveHomeScenario("platform-list");
        Assert.Equal("System.Collections.Generic.List`1", platform.View!.Type);
        Assert.Equal("System.Private.CoreLib", platform.View.Library);
        Assert.Equal(2, platform.SelectedContext!.Members.Count);
        Assert.Contains(
            platform.SelectedContext.Members,
            member => member is WorkspaceMemberCoordinate.PlatformMember platformMember
                && platformMember.Family == "runtime"
                && platformMember.Version == "10.0.10");
        Assert.Equal("runtime", platform.Navigation!.FocusTabId);
        Assert.IsType<WorkspaceMemberCoordinate.PlatformMember>(
            platform.Navigation.FocusTab.Coordinate);
    }

    [Fact]
    public void ProductHomeDemos_FactoryRegistry_IsMetadataOnlyUntilResolved()
    {
        // Catalog surface is three entries; factories are not invoked by listing.
        Assert.Equal(3, ProductInspectionDemos.Entries.Count);
        Assert.All(
            ProductInspectionDemos.Entries,
            entry =>
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Id));
                Assert.False(string.IsNullOrWhiteSpace(entry.Title));
                Assert.NotNull(entry.CreateRecords);
            });

        // Full materialization is opt-in (4 records × 3 demos).
        var all = ProductInspectionDemos.CreateRegistry();
        Assert.Equal(12, all.Records.Count);

        // Each factory owns exactly one scenario composition.
        foreach (var entry in ProductInspectionDemos.Entries)
        {
            var records = entry.CreateRecords();
            Assert.Equal(4, records.Length);
            Assert.Contains(records, record =>
                record is ScenarioDefinition scenario && scenario.Id == entry.Id);

            foreach (var record in records)
            {
                var json = InspectionDefinitionJson.Serialize(record);
                var parsed = InspectionDefinitionJson.Parse(json);
                Assert.Equal(record.Kind, parsed.Kind);
                Assert.Equal(record.Id, parsed.Id);
            }
        }

        Assert.False(ProductInspectionDemos.TryResolveHomeScenario("missing", out _));
        Assert.True(ProductInspectionDemos.TryResolveHomeScenario("stj-serializer", out var resolved));
        Assert.Equal("stj-serializer", resolved.ScenarioId);
    }
}
