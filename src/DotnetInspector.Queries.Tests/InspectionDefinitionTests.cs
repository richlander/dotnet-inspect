using System.Text;
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
            AssertDefinitionSemanticsEqual(record, roundTripped);
            Assert.Equal(json, InspectionDefinitionJson.Serialize(roundTripped));
        }
    }

    [Fact]
    public void Parse_RejectsDuplicateProperties()
    {
        const string json = """
            { "schemaVersion": 1, "kind": "query", "id": "q", "id": "other" }
            """;

        var ex = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(json));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<JsonException>(ex.InnerException);
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
    public void ViewJson_PreservesSingleAndMultipleLibraryCompatibility()
    {
        var single = Assert.IsType<ViewDefinition>(InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "view", "id": "single", "library": "A" }
            """));
        var multiple = Assert.IsType<ViewDefinition>(InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "view", "id": "multiple", "libraries": ["A", "B"] }
            """));

        Assert.Equal(["A"], single.Libraries);
        Assert.Equal(["A", "B"], multiple.Libraries);
        Assert.Contains(
            "\"library\": \"A\"",
            InspectionDefinitionJson.Serialize(single),
            StringComparison.Ordinal);
        Assert.Contains(
            "\"libraries\": [",
            InspectionDefinitionJson.Serialize(multiple),
            StringComparison.Ordinal);

        var dual = Assert.Throws<InspectionDefinitionException>(() =>
            InspectionDefinitionJson.Parse(
                """
                {
                  "schemaVersion": 1,
                  "kind": "view",
                  "id": "dual",
                  "library": "A",
                  "libraries": ["B"]
                }
                """));
        Assert.IsType<ArgumentException>(dual.InnerException);

        Assert.Throws<InspectionDefinitionException>(() =>
            InspectionDefinitionJson.Parse(
                """
                { "schemaVersion": 1, "kind": "view", "id": "null", "libraries": [null] }
                """));
        Assert.Throws<InspectionDefinitionException>(() =>
            InspectionDefinitionJson.Parse(
                """
                { "schemaVersion": 1, "kind": "view", "id": "one", "libraries": ["A"] }
                """));
        Assert.Throws<InspectionDefinitionException>(() =>
            InspectionDefinitionJson.Parse(
                """
                { "schemaVersion": 1, "kind": "view", "id": "empty", "libraries": [] }
                """));
        Assert.Throws<InspectionDefinitionException>(() =>
            InspectionDefinitionJson.Parse(
                """
                { "schemaVersion": 1, "kind": "view", "id": "order", "libraries": ["B", "A"] }
                """));
        Assert.Throws<InspectionDefinitionException>(() =>
            InspectionDefinitionJson.Parse(
                """
                { "schemaVersion": 1, "kind": "query", "id": "q", "libraries": null }
                """));
    }

    [Fact]
    public void Parse_RejectsNullNestedArrayElements()
    {
        Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            {
              "schemaVersion": 1,
              "kind": "workspace",
              "id": "ws",
              "contexts": [
                { "name": "c", "members": [ null ] }
              ]
            }
            """));

        Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            {
              "schemaVersion": 1,
              "kind": "workspace",
              "id": "ws",
              "contexts": [ null ]
            }
            """));

        Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            {
              "schemaVersion": 1,
              "kind": "navigation",
              "id": "n",
              "focus": "t",
              "tabs": [ null ]
            }
            """));
    }

    [Fact]
    public void Parse_EnforcesCoordinateBudgetAcrossNestedLists()
    {
        // Two contexts of 600 members each = 1200 > MaxCoordinatesPerRecord (1024).
        var members = string.Join(
            ",",
            Enumerable.Range(0, 600).Select(i =>
                $$"""{"kind":"package","id":"P{{i}}","version":"1.0.0","framework":"net10.0"}"""));
        var json = $$"""
            {
              "schemaVersion": 1,
              "kind": "workspace",
              "id": "ws",
              "contexts": [
                { "name": "a", "members": [ {{members}} ] },
                { "name": "b", "members": [ {{members}} ] }
              ]
            }
            """;

        var ex = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(json));
        Assert.Contains("coordinate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsDualRuntimeIdentifierSpellings()
    {
        var ex = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            {
              "schemaVersion": 1,
              "kind": "workspace",
              "id": "ws",
              "contexts": [
                {
                  "name": "c",
                  "rid": "win-x64",
                  "runtimeIdentifier": "linux-x64",
                  "members": [ { "kind": "package", "id": "P", "version": "1.0.0", "framework": "net10.0" } ]
                }
              ]
            }
            """));
        Assert.Contains("rid and runtimeIdentifier", ex.Message, StringComparison.Ordinal);
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
    public void Registry_RejectsSubscribeAndFilesystemCoordinates_AndCrossKindPeers()
    {
        var subscribeWs = new InspectionDefinitionRegistry();
        subscribeWs.Add(new WorkspaceDefinition(
            1,
            "ws",
            [new WorkspaceContextDefinition("c", subscribe: ":Platform@10.0.10")]));
        subscribeWs.Add(new ScenarioDefinition(1, "s", workspace: "ws"));
        var subscribeEx = Assert.Throws<InspectionDefinitionException>(() => subscribeWs.ResolveScenario("s"));
        Assert.Contains("subscribe", subscribeEx.Message, StringComparison.OrdinalIgnoreCase);

        var nav = new InspectionDefinitionRegistry();
        nav.Add(new NavigationDefinition(
            1,
            "n",
            [new NavigationTabDefinition("t", subscribe: ":Platform")],
            focus: "t"));
        nav.Add(new ScenarioDefinition(1, "s", navigation: "n", input: "x"));
        var navEx = Assert.Throws<InspectionDefinitionException>(() => nav.ResolveScenario("s"));
        Assert.Contains("subscribe", navEx.Message, StringComparison.OrdinalIgnoreCase);

        foreach (DefinitionMemberCoordinate coordinate in new DefinitionMemberCoordinate[]
                 {
                     new DefinitionMemberCoordinate.ProjectCoordinate("proj.csproj", "net10.0"),
                     new DefinitionMemberCoordinate.LocalCoordinate("/tmp/a.dll"),
                     new DefinitionMemberCoordinate.DirectoryCoordinate("/tmp/out", "net10.0"),
                 })
        {
            var registry = new InspectionDefinitionRegistry();
            registry.Add(new WorkspaceDefinition(
                1,
                "ws",
                [new WorkspaceContextDefinition("c", members: [coordinate])]));
            registry.Add(new ScenarioDefinition(1, "s", workspace: "ws"));
            var ex = Assert.Throws<InspectionDefinitionException>(() => registry.ResolveScenario("s"));
            Assert.Contains("filesystem", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Cross-kind: scenario workspace id that is only a view must fail visibly.
        var cross = new InspectionDefinitionRegistry();
        cross.Add(new ViewDefinition(1, "looks-like-ws", type: "T"));
        cross.Add(new ScenarioDefinition(1, "s", workspace: "looks-like-ws"));
        var crossEx = Assert.Throws<InspectionDefinitionException>(() => cross.ResolveScenario("s"));
        Assert.Contains("unknown workspace", crossEx.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Parse_RejectsCrossKindRecordAndCoordinateFields()
    {
        var queryEx = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "query", "id": "q", "queryId": "surface", "workspace": "ws" }
            """));
        Assert.Contains("must not set 'workspace'", queryEx.Message, StringComparison.Ordinal);

        var packageEx = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            {
              "schemaVersion": 1,
              "kind": "workspace",
              "id": "ws",
              "contexts": [
                {
                  "name": "c",
                  "members": [
                    { "kind": "package", "id": "P", "version": "1.0.0", "framework": "net10.0", "path": "x.csproj" }
                  ]
                }
              ]
            }
            """));
        Assert.Contains("must not set 'path'", packageEx.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WrapsSemanticConstructorFailures()
    {
        var viewEx = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "view", "id": "v", "memberAnchor": "deadbeef01" }
            """));
        Assert.IsType<ArgumentException>(viewEx.InnerException);

        var contextEx = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            {
              "schemaVersion": 1,
              "kind": "workspace",
              "id": "ws",
              "contexts": [ { "name": "c", "subscribe": "   " } ]
            }
            """));
        Assert.IsType<ArgumentException>(contextEx.InnerException);
    }

    [Fact]
    public void Parse_RejectsNullCatalogGroupEntries()
    {
        Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "catalog", "id": "c", "groups": [ null ] }
            """));
    }

    [Fact]
    public void Workspace_RejectsDuplicateContextNames()
    {
        Assert.Throws<ArgumentException>(() => new WorkspaceDefinition(
            1,
            "ws",
            [
                new WorkspaceContextDefinition(
                    "c",
                    members: [new DefinitionMemberCoordinate.PackageCoordinate("P", "1.0.0", "net10.0")]),
                new WorkspaceContextDefinition(
                    "c",
                    members: [new DefinitionMemberCoordinate.PackageCoordinate("Q", "1.0.0", "net10.0")]),
            ]));
    }

    [Fact]
    public void Parse_RejectsExplicitNullForeignFieldsAndNonCanonicalKinds()
    {
        var nullForeign = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "query", "id": "q", "queryId": "surface", "workspace": null }
            """));
        Assert.Contains("must not set 'workspace'", nullForeign.Message, StringComparison.Ordinal);

        var kindCase = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "QUERY", "id": "q", "queryId": "surface" }
            """));
        Assert.Contains("Unknown definition kind", kindCase.Message, StringComparison.Ordinal);

        var kindPad = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": " query ", "id": "q", "queryId": "surface" }
            """));
        Assert.Contains("Unknown definition kind", kindPad.Message, StringComparison.Ordinal);

        var dualRidNull = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            {
              "schemaVersion": 1,
              "kind": "workspace",
              "id": "ws",
              "contexts": [
                {
                  "name": "c",
                  "rid": "win-x64",
                  "runtimeIdentifier": null,
                  "members": [ { "kind": "package", "id": "P", "version": "1.0.0", "framework": "net10.0" } ]
                }
              ]
            }
            """));
        Assert.Contains("rid and runtimeIdentifier", dualRidNull.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsBlankNavigationSubscribeWithCoordinate()
    {
        var ex = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            {
              "schemaVersion": 1,
              "kind": "navigation",
              "id": "n",
              "focus": "t",
              "tabs": [
                {
                  "id": "t",
                  "subscribe": "   ",
                  "coordinate": { "kind": "package", "id": "P", "version": "1.0.0", "framework": "net10.0" }
                }
              ]
            }
            """));
        Assert.Contains("subscribe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scenario_RequiresExactlyOneSource_AndContextNeedsWorkspace()
    {
        Assert.Throws<ArgumentException>(() => new ScenarioDefinition(
            1, "s", workspace: "ws", input: "x"));
        Assert.Throws<ArgumentException>(() => new ScenarioDefinition(
            1, "s", input: "x", context: "c"));
        Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "scenario", "id": "s", "workspace": "ws", "input": "x" }
            """));
    }

    [Fact]
    public void Parse_StringOverload_EnforcesByteBudgetBeforeDecodeWork()
    {
        // Over-budget UTF-8 length must fail closed on the string overload.
        var oversize = new string('x', InspectionDefinitionJson.MaxUtf8ByteLength + 8);
        var ex = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(oversize));
        Assert.Contains("byte limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_UntypedTryGet_IsAmbiguousAcrossKinds()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new ViewDefinition(1, "same", type: "T"));
        registry.Add(new QueryDefinition(1, "same", queryId: "surface"));

        Assert.True(registry.TryGet<ViewDefinition>("same", out _));
        Assert.True(registry.TryGet<QueryDefinition>("same", out _));
        Assert.False(registry.TryGet<InspectionDefinitionRecord>("same", out _));
    }


    [Fact]
    public void Parse_RejectsBlankWorkspaceSubscribeWithMembers()
    {
        var ex = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            {
              "schemaVersion": 1,
              "kind": "workspace",
              "id": "ws",
              "contexts": [
                {
                  "name": "c",
                  "subscribe": "   ",
                  "members": [ { "kind": "package", "id": "P", "version": "1.0.0", "framework": "net10.0" } ]
                }
              ]
            }
            """));
        Assert.Contains("subscribe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsBlankScenarioPeerReferences()
    {
        var ex = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "scenario", "id": "s", "workspace": "ws", "navigation": "   " }
            """));
        Assert.Contains("navigation", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "scenario", "id": "s", "workspace": "   ", "input": "x" }
            """));
    }

    [Fact]
    public void ResolveScenario_Contexts_AreMutationResistant()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            1,
            "ws",
            [new WorkspaceContextDefinition(
                "c",
                members: [new DefinitionMemberCoordinate.PackageCoordinate("P", "1.0.0", "net10.0")])]));
        registry.Add(new ScenarioDefinition(1, "s", workspace: "ws"));
        var resolved = registry.ResolveScenario("s");
        var original = Assert.Single(resolved.Contexts);
        var mutable = Assert.IsAssignableFrom<IList<ResolvedWorkspaceContext>>(resolved.Contexts);
        Assert.True(mutable.IsReadOnly);
        Assert.ThrowsAny<Exception>(() => mutable[0] = null!);
        Assert.ThrowsAny<Exception>(() => mutable.Clear());
        Assert.Same(original, Assert.Single(resolved.Contexts));
        Assert.NotNull(resolved.SelectedContext);
    }

    [Fact]
    public void WorkspaceContextAddress_UsesExactOrdinalValueEquality()
    {
        var first = new WorkspaceContextAddress("workspace", "context");
        var equal = new WorkspaceContextAddress("workspace", "context");

        Assert.Equal(first, equal);
        Assert.NotEqual(
            first,
            new WorkspaceContextAddress("Workspace", "context"));
        Assert.NotEqual(
            first,
            new WorkspaceContextAddress("workspace", "Context"));
        Assert.Throws<ArgumentException>(
            () => new WorkspaceContextAddress("", "context"));
        Assert.Throws<ArgumentException>(
            () => new WorkspaceContextAddress("workspace", " "));
    }

    [Fact]
    public void ResolveScenario_IssuesContextAddressesAndDescriptors()
    {
        var member = new DefinitionMemberCoordinate.PackageCoordinate(
            "P",
            "1.0.0",
            "net10.0");
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            1,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "first",
                    framework: "net10.0",
                    members: [member]),
                new WorkspaceContextDefinition(
                    "second",
                    framework: "net10.0",
                    runtimeIdentifier: "linux-x64",
                    members: [member]),
            ]));
        registry.Add(new ScenarioDefinition(
            1,
            "scenario",
            workspace: "workspace",
            context: "second"));

        ResolvedScenario resolved = registry.ResolveScenario("scenario");

        Assert.Equal(
            [
                new WorkspaceContextAddress("workspace", "first"),
                new WorkspaceContextAddress("workspace", "second"),
            ],
            resolved.Contexts.Select(context => context.Address));
        Assert.Equal("first", resolved.Contexts[0].Descriptor.Name);
        Assert.Equal("net10.0", resolved.Contexts[0].Descriptor.Framework);
        Assert.Null(resolved.Contexts[0].Descriptor.RuntimeIdentifier);
        Assert.Equal("linux-x64", resolved.Contexts[1].Descriptor.RuntimeIdentifier);
        Assert.Same(resolved.Contexts[1], resolved.SelectedContext);
        Assert.Same(
            resolved.Contexts[1].Descriptor,
            resolved.SelectedContext!.Descriptor);
    }

    [Fact]
    public void Registry_RecordsAndScenarios_AreEnumerationSnapshots()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new QueryDefinition(1, "q1"));
        registry.Add(new ScenarioDefinition(1, "s1", input: "path.dll"));

        var recordsSnapshot = registry.Records;
        var scenariosSnapshot = registry.Scenarios;
        Assert.Equal(2, recordsSnapshot.Count);
        Assert.Single(scenariosSnapshot);

        registry.Add(new QueryDefinition(1, "q2"));
        Assert.Equal(2, recordsSnapshot.Count);
        Assert.Single(scenariosSnapshot);
        Assert.Equal(3, registry.Records.Count);
        Assert.Single(registry.Scenarios);
    }

    [Fact]
    public void Serialize_RejectsRecordsThatExceedCoordinateBudget()
    {
        var members = Enumerable.Range(0, InspectionDefinitionJson.MaxCoordinatesPerRecord + 1)
            .Select(i => (DefinitionMemberCoordinate)new DefinitionMemberCoordinate.PackageCoordinate(
                $"P{i}",
                "1.0.0",
                "net10.0"))
            .ToArray();
        var oversized = new WorkspaceDefinition(
            1,
            "ws",
            [new WorkspaceContextDefinition("c", members: members)]);

        var ex = Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Serialize(oversized));
        Assert.Contains("coordinate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_RejectsGroupDepthAndNodeLimitsBeforeRecursiveWalks()
    {
        CatalogGroupDefinition group = new("leaf");
        for (int depth = 1;
            depth < InspectionDefinitionJson.MaxGroupDepth;
            depth++)
        {
            group = new CatalogGroupDefinition($"g{depth}", children: [group]);
        }

        var maximumDepth = new WorkspaceDefinition(
            1,
            "ws",
            [new WorkspaceContextDefinition("c", subscribe: ":Platform")],
            groups: [group]);
        string json = InspectionDefinitionJson.Serialize(maximumDepth);
        var parsed = Assert.IsType<WorkspaceDefinition>(
            InspectionDefinitionJson.Parse(json));
        Assert.Single(parsed.Groups);

        var excessiveDepth = new WorkspaceDefinition(
            1,
            "ws",
            [new WorkspaceContextDefinition("c", subscribe: ":Platform")],
            groups:
            [
                new CatalogGroupDefinition(
                    "too-deep",
                    children: [group]),
            ]);
        var depthException = Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Serialize(excessiveDepth));
        Assert.Contains(
            "depth",
            depthException.Message,
            StringComparison.OrdinalIgnoreCase);

        CatalogGroupDefinition[] excessiveNodes = Enumerable
            .Range(0, InspectionDefinitionJson.MaxGroupsPerRecord + 1)
            .Select(index => new CatalogGroupDefinition($"g{index}"))
            .ToArray();
        var excessiveCount = new WorkspaceDefinition(
            1,
            "ws",
            [new WorkspaceContextDefinition("c", subscribe: ":Platform")],
            groups: excessiveNodes);
        var countException = Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Serialize(excessiveCount));
        Assert.Contains(
            "group",
            countException.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsBlankQueryIdAndViewSelectors()
    {
        var queryEx = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "query", "id": "q", "queryId": "   " }
            """));
        Assert.Contains("queryId", queryEx.Message, StringComparison.OrdinalIgnoreCase);

        var viewEx = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "view", "id": "v", "type": "   " }
            """));
        Assert.Contains("type", viewEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoordinateConstructors_RejectBlankRequiredFields()
    {
        Assert.Throws<ArgumentException>(() =>
            new DefinitionMemberCoordinate.PackageCoordinate("   "));
        Assert.Throws<ArgumentException>(() =>
            new DefinitionMemberCoordinate.PlatformCoordinate(" "));
        Assert.Throws<ArgumentException>(() =>
            new DefinitionMemberCoordinate.EmbeddedCoordinate(" ", "digest", "name"));
        Assert.Throws<ArgumentException>(() =>
            new DefinitionMemberCoordinate.EmbeddedCoordinate("ref", " ", "name"));
        Assert.Throws<ArgumentException>(() =>
            new DefinitionMemberCoordinate.EmbeddedCoordinate("ref", "digest", " "));
        Assert.Throws<ArgumentException>(() =>
            new DefinitionMemberCoordinate.ProjectCoordinate("\t"));
        Assert.Throws<ArgumentException>(() =>
            new DefinitionMemberCoordinate.LocalCoordinate(""));
        Assert.Throws<ArgumentException>(() =>
            new DefinitionMemberCoordinate.DirectoryCoordinate("  "));
    }

    [Fact]
    public void Collections_RejectNullElements()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkspaceContextDefinition(
                "c",
                members: [null!]));
        Assert.Throws<ArgumentException>(() =>
            new CatalogGroupDefinition("g", members: [null!]));
        Assert.Throws<ArgumentException>(() =>
            new CatalogGroupDefinition("g", children: [null!]));
        Assert.Throws<ArgumentException>(() =>
            new NavigationDefinition(1, "n", [null!], focus: "t"));
    }

    [Fact]
    public void Collections_FreezeUsesIndexerSnapshotNotSecondEnumeration()
    {
        // Enumerator yields null while indexer returns a real coordinate. Freeze must not
        // retain the enumerated null via a second pass.
        var lying = new IndexerOnlyMemberList(
            new DefinitionMemberCoordinate.PackageCoordinate("P", "1.0.0", "net10.0"));
        var context = new WorkspaceContextDefinition("c", members: lying);
        Assert.Single(context.Members);
        Assert.IsType<DefinitionMemberCoordinate.PackageCoordinate>(context.Members[0]);
        _ = InspectionDefinitionJson.Serialize(
            new WorkspaceDefinition(1, "ws", [context]));
    }

    [Fact]
    public void WorkspaceDefinition_ValidatesUniquenessOnFrozenContexts()
    {
        var a = new WorkspaceContextDefinition(
            "a",
            members: [new DefinitionMemberCoordinate.PackageCoordinate("P", "1.0.0", "net10.0")]);
        var b = new WorkspaceContextDefinition(
            "b",
            members: [new DefinitionMemberCoordinate.PackageCoordinate("Q", "1.0.0", "net10.0")]);
        // Enumerator yields unique names; indexer exposes a duplicate of a.
        var lying = new DualViewContextList(enumerate: [a, b], index: [a, a]);
        var ex = Assert.Throws<ArgumentException>(() => new WorkspaceDefinition(1, "ws", lying));
        Assert.Contains("Duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkspaceDefinition_ValidatesNonEmptyOnFrozenContexts()
    {
        // Source reports Count == 1 before freeze, then empty during Freeze's Count read.
        var lying = new ShrinkingCountContextList();
        var ex = Assert.Throws<ArgumentException>(() => new WorkspaceDefinition(1, "ws", lying));
        Assert.Contains("at least one context", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NavigationDefinition_ValidatesNonEmptyOnFrozenTabs()
    {
        var lying = new ShrinkingCountTabList();
        var ex = Assert.Throws<ArgumentException>(
            () => new NavigationDefinition(1, "n", lying, focus: "t"));
        Assert.Contains("at least one tab", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class IndexerOnlyMemberList : IReadOnlyList<DefinitionMemberCoordinate>
    {
        private readonly DefinitionMemberCoordinate _item;

        public IndexerOnlyMemberList(DefinitionMemberCoordinate item) => _item = item;

        public int Count => 1;

        public DefinitionMemberCoordinate this[int index] =>
            index == 0 ? _item : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<DefinitionMemberCoordinate> GetEnumerator()
        {
            yield return null!;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private sealed class DualViewContextList : IReadOnlyList<WorkspaceContextDefinition>
    {
        private readonly WorkspaceContextDefinition[] _enumerate;
        private readonly WorkspaceContextDefinition[] _index;

        public DualViewContextList(
            WorkspaceContextDefinition[] enumerate,
            WorkspaceContextDefinition[] index)
        {
            _enumerate = enumerate;
            _index = index;
        }

        public int Count => _index.Length;

        public WorkspaceContextDefinition this[int index] => _index[index];

        public IEnumerator<WorkspaceContextDefinition> GetEnumerator() =>
            ((IEnumerable<WorkspaceContextDefinition>)_enumerate).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private sealed class ShrinkingCountContextList : IReadOnlyList<WorkspaceContextDefinition>
    {
        private int _countReads;

        public int Count => _countReads++ == 0 ? 1 : 0;

        public WorkspaceContextDefinition this[int index] =>
            throw new InvalidOperationException("Indexer must not run after Count collapses to empty.");

        public IEnumerator<WorkspaceContextDefinition> GetEnumerator()
        {
            yield break;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private sealed class ShrinkingCountTabList : IReadOnlyList<NavigationTabDefinition>
    {
        private int _countReads;

        public int Count => _countReads++ == 0 ? 1 : 0;

        public NavigationTabDefinition this[int index] =>
            throw new InvalidOperationException("Indexer must not run after Count collapses to empty.");

        public IEnumerator<NavigationTabDefinition> GetEnumerator()
        {
            yield break;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    [Fact]
    public void Serialize_RejectsInvalidUtf16InRecordFields()
    {
        var lone = new string(['\uD800']);
        var query = new QueryDefinition(1, "q", queryId: lone);
        var ex = Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Serialize(query));
        Assert.Contains("UTF-16", ex.Message, StringComparison.Ordinal);
        Assert.IsType<EncoderFallbackException>(ex.InnerException);
    }

    [Fact]
    public void Parse_RejectsEscapedInvalidUtf16Surrogates()
    {
        var ex = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(
            """
            { "schemaVersion": 1, "kind": "\uD800", "id": "q" }
            """));
        Assert.Contains("UTF-16", ex.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public void Parse_RejectsInvalidUtf16InStringOverload()
    {
        var invalid = "{\n  \"schemaVersion\": 1,\n  \"kind\": \"query\",\n  \"id\": \"q\",\n  \"queryId\": \"surface\"\n}".ToCharArray();
        // Inject an unpaired high surrogate into the id value region is hard; use a lone surrogate payload.
        var lone = new string(['\uD800']);
        var ex = Assert.Throws<InspectionDefinitionException>(() => InspectionDefinitionJson.Parse(lone));
        Assert.Contains("UTF-16", ex.Message, StringComparison.Ordinal);
        Assert.IsType<EncoderFallbackException>(ex.InnerException);
    }

    private static void AssertDefinitionSemanticsEqual(
        InspectionDefinitionRecord expected,
        InspectionDefinitionRecord actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);

        switch (expected)
        {
            case CatalogDefinition catalog:
                var actualCatalog = Assert.IsType<CatalogDefinition>(actual);
                AssertGroupsEqual(catalog.Groups, actualCatalog.Groups);
                break;
            case WorkspaceDefinition workspace:
                var actualWorkspace = Assert.IsType<WorkspaceDefinition>(actual);
                Assert.Equal(workspace.Title, actualWorkspace.Title);
                Assert.Equal(workspace.Description, actualWorkspace.Description);
                Assert.Equal(workspace.Contexts.Count, actualWorkspace.Contexts.Count);
                for (var i = 0; i < workspace.Contexts.Count; i++)
                {
                    AssertContextEqual(workspace.Contexts[i], actualWorkspace.Contexts[i]);
                }

                AssertGroupsEqual(workspace.Groups, actualWorkspace.Groups);
                break;
            case QueryDefinition query:
                var actualQuery = Assert.IsType<QueryDefinition>(actual);
                Assert.Equal(query.QueryId, actualQuery.QueryId);
                break;
            case ViewDefinition view:
                var actualView = Assert.IsType<ViewDefinition>(actual);
                Assert.Equal(view.Lens, actualView.Lens);
                Assert.Equal(view.Type, actualView.Type);
                Assert.Equal(view.MemberAnchor, actualView.MemberAnchor);
                Assert.Equal(view.MemberSignature, actualView.MemberSignature);
                Assert.Equal(view.MemberKey, actualView.MemberKey);
                Assert.Equal(view.Section, actualView.Section);
                Assert.Equal(view.Libraries, actualView.Libraries);
                break;
            case NavigationDefinition navigation:
                var actualNavigation = Assert.IsType<NavigationDefinition>(actual);
                Assert.Equal(navigation.Focus, actualNavigation.Focus);
                Assert.Equal(navigation.Tabs.Count, actualNavigation.Tabs.Count);
                for (var i = 0; i < navigation.Tabs.Count; i++)
                {
                    AssertTabEqual(navigation.Tabs[i], actualNavigation.Tabs[i]);
                }

                break;
            case ScenarioDefinition scenario:
                var actualScenario = Assert.IsType<ScenarioDefinition>(actual);
                Assert.Equal(scenario.Title, actualScenario.Title);
                Assert.Equal(scenario.Description, actualScenario.Description);
                Assert.Equal(scenario.Workspace, actualScenario.Workspace);
                Assert.Equal(scenario.Context, actualScenario.Context);
                Assert.Equal(scenario.Input, actualScenario.Input);
                Assert.Equal(scenario.Query, actualScenario.Query);
                Assert.Equal(scenario.View, actualScenario.View);
                Assert.Equal(scenario.Navigation, actualScenario.Navigation);
                break;
            default:
                throw new Xunit.Sdk.XunitException($"Unhandled record kind {expected.Kind}.");
        }
    }

    private static void AssertGroupsEqual(
        IReadOnlyList<CatalogGroupDefinition> expected,
        IReadOnlyList<CatalogGroupDefinition> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Name, actual[i].Name);
            AssertCoordinatesEqual(expected[i].Members, actual[i].Members);
            AssertGroupsEqual(expected[i].Children, actual[i].Children);
        }
    }

    private static void AssertContextEqual(
        WorkspaceContextDefinition expected,
        WorkspaceContextDefinition actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Framework, actual.Framework);
        Assert.Equal(expected.RuntimeIdentifier, actual.RuntimeIdentifier);
        Assert.Equal(expected.Subscribe, actual.Subscribe);
        AssertCoordinatesEqual(expected.Members, actual.Members);
    }

    private static void AssertTabEqual(
        NavigationTabDefinition expected,
        NavigationTabDefinition actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Subscribe, actual.Subscribe);
        Assert.Equal(expected.Framework, actual.Framework);
        Assert.Equal(expected.RuntimeIdentifier, actual.RuntimeIdentifier);
        Assert.Equal(expected.Coordinate, actual.Coordinate);
    }

    private static void AssertCoordinatesEqual(
        IReadOnlyList<DefinitionMemberCoordinate> expected,
        IReadOnlyList<DefinitionMemberCoordinate> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i], actual[i]);
    }
}
