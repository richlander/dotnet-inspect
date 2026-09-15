using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class InspectionDefinitionV2Tests
{
    [Fact]
    public void JsonRoundTrip_PreservesEveryVersion2RecordKind()
    {
        InspectionDefinitionRecord[] records =
        [
            Workspace(InspectionDefinitionSchema.Version2),
            Navigation(focus: null),
            View(),
            Scenario(),
        ];

        foreach (InspectionDefinitionRecord record in records)
        {
            string json = InspectionDefinitionJson.Serialize(record);
            InspectionDefinitionRecord roundTripped =
                InspectionDefinitionJson.Parse(json);

            Assert.IsType(record.GetType(), roundTripped);
            Assert.Equal(record.SchemaVersion, roundTripped.SchemaVersion);
            Assert.Equal(record.Id, roundTripped.Id);
            Assert.Equal(json, InspectionDefinitionJson.Serialize(roundTripped));
        }
    }

    [Fact]
    public void Json_SchemaVersion1SpellingRemainsUnchanged()
    {
        var query = new QueryDefinition(
            InspectionDefinitionSchema.Version1,
            "surface",
            "api");

        Assert.Equal(
            """
            {
              "schemaVersion": 1,
              "kind": "query",
              "id": "surface",
              "queryId": "api"
            }
            """,
            InspectionDefinitionJson.Serialize(query));
    }

    [Fact]
    public void JsonRoundTrip_WorkspaceSubjectRetainsMemberContext()
    {
        PortableLibraryIdentity library = Library();
        MetadataTypeDefinitionName type = Type("System.Text.Json", "JsonSerializer");
        var view = new CommittedViewDefinition(
            InspectionDefinitionSchema.Version2,
            "views",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition(
                    "stj",
                    new PortableSubjectRequest.Workspace(),
                    new PortableRetainedSubjectContext.Member(
                        library,
                        type,
                        memberAnchor: "74b6b4b321"),
                    facet: "workspace.overview"),
            ]);

        string json = InspectionDefinitionJson.Serialize(view);
        var roundTripped = Assert.IsType<CommittedViewDefinition>(
            InspectionDefinitionJson.Parse(json));
        CommittedViewStateDefinition state = roundTripped.States[1];
        var context = Assert.IsType<PortableRetainedSubjectContext.Member>(
            state.Context);

        Assert.Null(roundTripped.States[0].Navigation);
        Assert.IsType<PortableSubjectRequest.Workspace>(state.Subject);
        Assert.Equal(library, context.LibraryIdentity);
        Assert.Equal(type, context.TypeIdentity);
        Assert.Equal("74b6b4b321", context.MemberAnchor);
        Assert.Equal(json, InspectionDefinitionJson.Serialize(roundTripped));
    }

    [Fact]
    public void JsonRoundTrip_WorkspaceSubjectCanHaveNoActiveOccurrence()
    {
        var navigation = new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [
                new NavigationTabDefinition(
                    "stj",
                    coordinate: Package()),
            ],
            focus: null);
        var view = new CommittedViewDefinition(
            InspectionDefinitionSchema.Version2,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace(),
                    facet: "workspace.overview"),
                new CommittedViewStateDefinition("stj"),
            ]);

        string navigationJson = InspectionDefinitionJson.Serialize(navigation);
        string viewJson = InspectionDefinitionJson.Serialize(view);
        var parsedNavigation =
            Assert.IsType<CommittedNavigationDefinition>(
                InspectionDefinitionJson.Parse(navigationJson));
        var parsedView = Assert.IsType<CommittedViewDefinition>(
            InspectionDefinitionJson.Parse(viewJson));

        Assert.Contains("\"focus\": null", navigationJson, StringComparison.Ordinal);
        Assert.Null(parsedNavigation.Focus);
        Assert.Null(parsedView.States[0].Navigation);
        Assert.Null(parsedView.States[1].Subject);
    }

    [Fact]
    public void PrepareScenario_PackageSubjectForExactTab_IsVersion2()
    {
        var registry = Registry(
            focus: "stj",
            packageState: new CommittedViewStateDefinition(
                "stj",
                new PortableSubjectRequest.Package(),
                new PortableRetainedSubjectContext.Package(),
                facet: "package.overview"));

        var prepared =
            Assert.IsType<InspectionDefinitionScenarioPreparationResult.Version2>(
                registry.PrepareScenario("scenario"));

        Assert.Equal("stj", prepared.Definitions.Navigation!.Focus);
        Assert.IsType<PortableSubjectRequest.Package>(
            prepared.Definitions.View!.States[1].Subject);
        Assert.Same(
            prepared.Definitions.Scenario,
            WorkspaceDefinitionConsumer.GetCommittedDefinitions(
                registry,
                "scenario").Scenario);
    }

    [Fact]
    public void JsonRoundTrip_SameTabCanRetainDistinctTypeContexts()
    {
        CommittedViewDefinition first = WorkspaceWithTypeContext(
            "first",
            Type("N", "First"));
        CommittedViewDefinition second = WorkspaceWithTypeContext(
            "second",
            Type("N", "Second"));

        var firstRoundTrip = Assert.IsType<CommittedViewDefinition>(
            InspectionDefinitionJson.Parse(
                InspectionDefinitionJson.Serialize(first)));
        var secondRoundTrip = Assert.IsType<CommittedViewDefinition>(
            InspectionDefinitionJson.Parse(
                InspectionDefinitionJson.Serialize(second)));

        var firstContext =
            Assert.IsType<PortableRetainedSubjectContext.Type>(
                firstRoundTrip.States[1].Context);
        var secondContext =
            Assert.IsType<PortableRetainedSubjectContext.Type>(
                secondRoundTrip.States[1].Context);
        Assert.Equal("stj", firstRoundTrip.States[1].Navigation);
        Assert.Equal("stj", secondRoundTrip.States[1].Navigation);
        Assert.NotEqual(firstContext.TypeIdentity, secondContext.TypeIdentity);
    }

    [Fact]
    public void PrepareScenario_RejectsMissingAndReorderedStates()
    {
        var missing = Registry(
            states:
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
            ]);
        var missingException = Assert.Throws<InspectionDefinitionException>(
            () => missing.PrepareScenario("scenario"));
        Assert.Contains(
            "one state for every navigation tab",
            missingException.Message,
            StringComparison.Ordinal);

        var reordered = Registry(
            extraTab: true,
            states:
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition("other"),
                new CommittedViewStateDefinition("stj"),
            ]);
        var reorderedException = Assert.Throws<InspectionDefinitionException>(
            () => reordered.PrepareScenario("scenario"));
        Assert.Contains(
            "must reference navigation tab 'stj'",
            reorderedException.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareScenario_RejectsInvalidContextBeforeVersionDispatch()
    {
        var registry = Registry();
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version2,
            "invalid-context",
            workspace: "workspace",
            context: "missing",
            view: "view",
            navigation: "navigation"));

        var exception = Assert.Throws<InspectionDefinitionException>(
            () => registry.PrepareScenario("invalid-context"));

        Assert.Contains("unknown context", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructors_RejectInvalidFocusAndWorkspaceRowContext()
    {
        Assert.Throws<ArgumentException>(() => new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [
                new NavigationTabDefinition(
                    "platform",
                    coordinate: new DefinitionMemberCoordinate.PlatformCoordinate(
                        "runtime")),
            ],
            focus: "platform"));

        var registry = Registry(
            states:
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace(),
                    new PortableRetainedSubjectContext.Package()),
                new CommittedViewStateDefinition("stj"),
            ]);
        var exception = Assert.Throws<InspectionDefinitionException>(
            () => registry.PrepareScenario("scenario"));
        Assert.Contains(
            "Workspace state cannot retain Package context",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Constructors_RejectInvalidSubjectContextAncestry()
    {
        Assert.Throws<ArgumentException>(() =>
            new CommittedViewStateDefinition(
                "stj",
                context: new PortableRetainedSubjectContext.Type(
                    Library(),
                    Type("N", "T"))));
        Assert.Throws<ArgumentException>(() =>
            new CommittedViewStateDefinition(
                "stj",
                new PortableSubjectRequest.Package()));
        Assert.Throws<ArgumentException>(() =>
            new CommittedViewStateDefinition(
                "stj",
                facet: "package.overview"));
    }

    [Fact]
    public void PrepareScenario_RejectsDecoratedNonPackageRows()
    {
        var registry = Registry(
            extraTab: true,
            states:
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition("stj"),
                new CommittedViewStateDefinition(
                    "other",
                    new PortableSubjectRequest.Workspace()),
            ]);

        var exception = Assert.Throws<InspectionDefinitionException>(
            () => registry.PrepareScenario("scenario"));
        Assert.Contains(
            "must remain undecorated",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareScenario_RejectsMixedVersionsAndQueryReferences()
    {
        var mixed = new InspectionDefinitionRegistry();
        mixed.Add(Workspace(InspectionDefinitionSchema.Version1));
        mixed.Add(Navigation());
        mixed.Add(View());
        mixed.Add(Scenario());

        var mixedException = Assert.Throws<InspectionDefinitionException>(
            () => mixed.PrepareScenario("scenario"));
        Assert.Contains("mixes schema version", mixedException.Message, StringComparison.Ordinal);

        var queries = Registry(
            packageState: new CommittedViewStateDefinition(
                "stj",
                new PortableSubjectRequest.Package(),
                new PortableRetainedSubjectContext.Package(),
                facet: "package.overview",
                queries: ["package-query"]));
        var queryException = Assert.Throws<InspectionDefinitionException>(
            () => queries.PrepareScenario("scenario"));
        Assert.Contains(
            "cannot reference queries",
            queryException.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsUnknownAndDuplicateNestedVersion2Properties()
    {
        const string unknown = """
            {
              "schemaVersion": 2,
              "kind": "view",
              "id": "view",
              "states": [
                {
                  "navigation": null,
                  "subject": { "kind": "workspace", "label": "Workspace" }
                }
              ]
            }
            """;
        Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Parse(unknown));

        const string duplicate = """
            {
              "schemaVersion": 2,
              "kind": "view",
              "id": "view",
              "states": [
                {
                  "navigation": "stj",
                  "context": {
                    "kind": "library",
                    "library": {
                      "name": "System.Text.Json",
                      "version": "10.0.0.0",
                      "culture": null,
                      "publicKeyToken": null,
                      "name": "Other"
                    }
                  }
                }
              ]
            }
            """;
        var exception = Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Parse(duplicate));
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public void Parse_RequiresNullableFocusAndCanonicalPortableIdentity()
    {
        const string missingFocus = """
            {
              "schemaVersion": 2,
              "kind": "navigation",
              "id": "navigation",
              "tabs": [
                {
                  "id": "stj",
                  "coordinate": {
                    "kind": "package",
                    "id": "System.Text.Json",
                    "version": "10.0.0",
                    "framework": "net10.0"
                  }
                }
              ]
            }
            """;
        Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Parse(missingFocus));

        const string malformedIdentity = """
            {
              "schemaVersion": 2,
              "kind": "view",
              "id": "view",
              "states": [
                { "navigation": null, "subject": { "kind": "workspace" } },
                {
                  "navigation": "stj",
                  "subject": { "kind": "workspace" },
                  "context": {
                    "kind": "library",
                    "library": {
                      "name": "System.Text.Json",
                      "version": "10.0",
                      "culture": null,
                      "publicKeyToken": "CC7B13FFCD2DDD51"
                    }
                  }
                }
              ]
            }
            """;
        Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Parse(malformedIdentity));
    }

    [Theory]
    [InlineData("10.0.0.1\0")]
    [InlineData("10\0.0.0.1")]
    public void PortableLibraryIdentity_RejectsNonDecimalVersionCharacters(
        string version)
    {
        Assert.Throws<ArgumentException>(
            () => new PortableLibraryIdentity(
                "System.Text.Json",
                version,
                null,
                null));

        string json = $$"""
            {
              "schemaVersion": 2,
              "kind": "view",
              "id": "view",
              "states": [
                { "navigation": null, "subject": { "kind": "workspace" } },
                {
                  "navigation": "stj",
                  "subject": { "kind": "workspace" },
                  "context": {
                    "kind": "library",
                    "library": {
                      "name": "System.Text.Json",
                      "version": {{JsonSerializer.Serialize(version)}},
                      "culture": null,
                      "publicKeyToken": null
                    }
                  }
                }
              ]
            }
            """;

        Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Parse(json));
    }

    [Fact]
    public void Version2ProjectionToPacketFormat1_IsNonProjectable()
    {
        var prepared =
            Assert.IsType<InspectionDefinitionScenarioPreparationResult.Version2>(
                Registry().PrepareScenario("scenario"));

        WorkspaceSharePacketProjectionResult result =
            WorkspaceSharePacketTransposer.ToPacket(
                prepared.Definitions,
                TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            result.Failure?.Kind);
        Assert.Equal("schemaVersion", result.Failure?.Path);
    }

    [Fact]
    public void PrepareScenario_FocusedNonPackageVersion1ReturnsCompatibilityHandoff()
    {
        var registry = new InspectionDefinitionRegistry();
        var workspace = new WorkspaceDefinition(
            InspectionDefinitionSchema.Version1,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    members:
                    [
                        new DefinitionMemberCoordinate.PlatformCoordinate(
                            "runtime",
                            Framework: "net10.0"),
                    ]),
            ]);
        var navigation = new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            "navigation",
            [
                new NavigationTabDefinition(
                    "platform",
                    coordinate: new DefinitionMemberCoordinate.PlatformCoordinate(
                        "runtime",
                        Framework: "net10.0")),
            ],
            "platform");
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "scenario",
            workspace: workspace.Id,
            navigation: navigation.Id);
        registry.Add(workspace);
        registry.Add(navigation);
        registry.Add(scenario);

        var handoff = Assert.IsType<
            InspectionDefinitionScenarioPreparationResult
                .LegacyCompatibilityRequired>(
                    registry.PrepareScenario(scenario.Id));

        Assert.Same(scenario, handoff.Plan.Scenario);
        Assert.Same(workspace, handoff.Plan.Workspace);
        Assert.Same(navigation, handoff.Plan.Navigation);
        Assert.Same(navigation.Tabs[0], handoff.Plan.FocusedTab);
    }

    [Fact]
    public void PrepareScenario_LibraryScopedVersion1ReturnsCompatibilityHandoff()
    {
        var registry = new InspectionDefinitionRegistry();
        WorkspaceDefinition workspace = Workspace(
            InspectionDefinitionSchema.Version1);
        var navigation = new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            "navigation",
            [
                new NavigationTabDefinition(
                    "package",
                    coordinate: Package()),
            ],
            "package");
        var view = new ViewDefinition(
            InspectionDefinitionSchema.Version1,
            "view",
            lens: "api",
            type: "System.Text.Json.JsonSerializer",
            libraries: ["System.Text.Json"]);
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "scenario",
            workspace: workspace.Id,
            context: "context",
            view: view.Id,
            navigation: navigation.Id);
        registry.Add(workspace);
        registry.Add(navigation);
        registry.Add(view);
        registry.Add(scenario);

        var handoff = Assert.IsType<
            InspectionDefinitionScenarioPreparationResult
                .LegacyCompatibilityRequired>(
                    registry.PrepareScenario(scenario.Id));

        Assert.Same(view, handoff.Plan.View);
        Assert.Same(navigation.Tabs[0], handoff.Plan.FocusedTab);
    }

    [Fact]
    public void PrepareScenario_LegacyCompatibilityRejectsDuplicateTabIds()
    {
        var registry = new InspectionDefinitionRegistry();
        WorkspaceDefinition workspace = Workspace(
            InspectionDefinitionSchema.Version1);
        var navigation = new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            "navigation",
            [
                new NavigationTabDefinition(
                    "platform",
                    coordinate: new DefinitionMemberCoordinate.PlatformCoordinate(
                        "runtime")),
                new NavigationTabDefinition(
                    "platform",
                    coordinate: new DefinitionMemberCoordinate.EmbeddedCoordinate(
                        "payload.dll",
                        new string('a', 64),
                        "Payload")),
            ],
            "platform");
        registry.Add(workspace);
        registry.Add(navigation);
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "scenario",
            workspace: workspace.Id,
            navigation: navigation.Id));

        var exception = Assert.Throws<InspectionDefinitionException>(
            () => registry.PrepareScenario("scenario"));

        Assert.Contains("duplicate tab id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareScenario_WorkspaceBackedVersion1WithoutNavigationUsesCompatibility()
    {
        var registry = new InspectionDefinitionRegistry();
        WorkspaceDefinition workspace = Workspace(
            InspectionDefinitionSchema.Version1);
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "scenario",
            workspace: workspace.Id);
        registry.Add(workspace);
        registry.Add(scenario);

        var handoff = Assert.IsType<
            InspectionDefinitionScenarioPreparationResult
                .LegacyCompatibilityRequired>(
                    registry.PrepareScenario(scenario.Id));

        Assert.Same(workspace, handoff.Plan.Workspace);
        Assert.Null(handoff.Plan.Navigation);
        Assert.Null(handoff.Plan.FocusedTab);
        Assert.Equal(
            scenario.Id,
            registry.ResolveScenario(scenario.Id).ScenarioId);
    }

    [Fact]
    public void PrepareScenario_WorkspaceFreeVersion1KeepsExistingPath()
    {
        var registry = new InspectionDefinitionRegistry();
        var navigation = new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            "navigation",
            [
                new NavigationTabDefinition(
                    "platform",
                    coordinate: new DefinitionMemberCoordinate.PlatformCoordinate(
                        "runtime")),
            ],
            "platform");
        registry.Add(navigation);
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "scenario",
            input: "bundle:input",
            navigation: navigation.Id));

        var prepared =
            Assert.IsType<InspectionDefinitionScenarioPreparationResult.Version1>(
                registry.PrepareScenario("scenario"));

        Assert.Null(prepared.Scenario.Workspace);
        Assert.Equal("bundle:input", prepared.Scenario.Scenario.Input);
        Assert.Equal("platform", prepared.Scenario.Navigation!.FocusTabId);
    }

    [Fact]
    public void PrepareScenario_ValidatesReachedCatalogVersions()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new CatalogDefinition(
            InspectionDefinitionSchema.Version2,
            "base-catalog",
            [new CatalogGroupDefinition("Base")]));
        registry.Add(new CatalogDefinition(
            InspectionDefinitionSchema.Version1,
            "overlay-catalog",
            [new CatalogGroupDefinition("Overlay")]));
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version2,
            "workspace",
            [new WorkspaceContextDefinition(
                "context",
                subscribe: ":Base:Child+Overlay@1.0.0")]));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [new NavigationTabDefinition(
                "platform",
                subscribe: ":Base:Child+Overlay@1.0.0")],
            focus: null));
        registry.Add(new CommittedViewDefinition(
            InspectionDefinitionSchema.Version2,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition("platform"),
            ]));
        registry.Add(Scenario());

        var exception = Assert.Throws<InspectionDefinitionException>(
            () => registry.PrepareScenario("scenario"));

        Assert.Contains(
            "catalog 'overlay-catalog'",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("schema version 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareScenario_RetainsBaseAndOverlayCatalogs()
    {
        var registry = new InspectionDefinitionRegistry();
        var @base = new CatalogDefinition(
            InspectionDefinitionSchema.Version2,
            "base-catalog",
            [new CatalogGroupDefinition("Base")]);
        var overlay = new CatalogDefinition(
            InspectionDefinitionSchema.Version2,
            "overlay-catalog",
            [new CatalogGroupDefinition("Overlay")]);
        registry.Add(@base);
        registry.Add(overlay);
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version2,
            "workspace",
            [new WorkspaceContextDefinition(
                "context",
                subscribe: ":Base:Child+Overlay@1.0.0")]));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [new NavigationTabDefinition(
                "platform",
                subscribe: ":Base:Child+Overlay@1.0.0")],
            focus: null));
        registry.Add(new CommittedViewDefinition(
            InspectionDefinitionSchema.Version2,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition("platform"),
            ]));
        registry.Add(Scenario());

        var prepared =
            Assert.IsType<InspectionDefinitionScenarioPreparationResult.Version2>(
                registry.PrepareScenario("scenario"));

        Assert.Equal([@base, overlay], prepared.Definitions.Catalogs);
        Assert.Contains(@base, prepared.Definitions.Records);
        Assert.Contains(overlay, prepared.Definitions.Records);
    }

    [Fact]
    public void Version1PacketProjectionRejectsMixedSchemaVersions()
    {
        var coordinate =
            new DefinitionMemberCoordinate.PackageCoordinate(
                "P",
                "1.0.0",
                "net10.0");
        var navigation = new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            "navigation",
            [new NavigationTabDefinition("tab", coordinate: coordinate)],
            "tab");
        var view = new ViewDefinition(
            InspectionDefinitionSchema.Version1,
            "view");
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: view.Id,
            navigation: navigation.Id);
        var mixed = new WorkspaceSharePacketDefinitionSet(
            new WorkspaceDefinition(
                InspectionDefinitionSchema.Version2,
                "workspace",
                [
                    new WorkspaceContextDefinition(
                        "context",
                        framework: "net10.0",
                        members: [coordinate]),
                ]),
            navigation,
            view,
            scenario);

        WorkspaceSharePacketProjectionResult result =
            WorkspaceSharePacketTransposer.ToPacket(
                mixed,
                TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(
            WorkspaceSharePacketProjectionFailureKind.InvalidDefinitionSet,
            result.Failure?.Kind);
        Assert.Equal("workspace.schemaVersion", result.Failure?.Path);
    }

    private static InspectionDefinitionRegistry Registry(
        string? focus = "stj",
        CommittedViewStateDefinition? packageState = null,
        IReadOnlyList<CommittedViewStateDefinition>? states = null,
        bool extraTab = false)
    {
        var registry = new InspectionDefinitionRegistry();
        WorkspaceDefinition workspace = Workspace(
            InspectionDefinitionSchema.Version2);
        CommittedNavigationDefinition navigation = Navigation(
            focus,
            extraTab);
        CommittedViewDefinition view = states is null
            ? new CommittedViewDefinition(
                InspectionDefinitionSchema.Version2,
                "view",
                extraTab
                    ?
                    [
                        new CommittedViewStateDefinition(
                            null,
                            new PortableSubjectRequest.Workspace()),
                        packageState ?? new CommittedViewStateDefinition("stj"),
                        new CommittedViewStateDefinition("other"),
                    ]
                    :
                    [
                        new CommittedViewStateDefinition(
                            null,
                            new PortableSubjectRequest.Workspace()),
                        packageState ?? new CommittedViewStateDefinition("stj"),
                    ])
            : new CommittedViewDefinition(
                InspectionDefinitionSchema.Version2,
                "view",
                states);
        ScenarioDefinition scenario = Scenario();
        registry.Add(workspace);
        registry.Add(navigation);
        registry.Add(view);
        registry.Add(scenario);
        return registry;
    }

    private static WorkspaceDefinition Workspace(int schemaVersion) =>
        new(
            schemaVersion,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    members: [Package()]),
            ]);

    private static CommittedNavigationDefinition Navigation(
        string? focus = "stj",
        bool extraTab = false) =>
        new(
            InspectionDefinitionSchema.Version2,
            "navigation",
            extraTab
                ?
                [
                    new NavigationTabDefinition(
                        "stj",
                        coordinate: Package()),
                    new NavigationTabDefinition(
                        "other",
                        coordinate:
                            new DefinitionMemberCoordinate.PlatformCoordinate(
                                "runtime",
                                Framework: "net10.0")),
                ]
                :
                [
                    new NavigationTabDefinition(
                        "stj",
                        coordinate: Package()),
                ],
            focus);

    private static CommittedViewDefinition View() =>
        new(
            InspectionDefinitionSchema.Version2,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition("stj"),
            ]);

    private static ScenarioDefinition Scenario() =>
        new(
            InspectionDefinitionSchema.Version2,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: "view",
            navigation: "navigation");

    private static CommittedViewDefinition WorkspaceWithTypeContext(
        string id,
        MetadataTypeDefinitionName type) =>
        new(
            InspectionDefinitionSchema.Version2,
            id,
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition(
                    "stj",
                    new PortableSubjectRequest.Workspace(),
                    new PortableRetainedSubjectContext.Type(
                        Library(),
                        type)),
            ]);

    private static DefinitionMemberCoordinate.PackageCoordinate Package() =>
        new("System.Text.Json", "10.0.0", "net10.0");

    private static PortableLibraryIdentity Library() =>
        new(
            "System.Text.Json",
            "10.0.0.0",
            null,
            "cc7b13ffcd2ddd51");

    private static MetadataTypeDefinitionName Type(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                segments.ToImmutableArray())).Name;
}
