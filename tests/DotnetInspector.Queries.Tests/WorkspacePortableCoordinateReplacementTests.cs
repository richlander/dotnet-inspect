using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspacePortableCoordinateReplacementTests
{
    private const string ProducerUrl =
        "https://api.nuget.org/v3/index.json";
    private const string AvaloniaToken = "c8d484a7012f9a8b";

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AvaloniaForwarder_DerivesRestorableTypeOrConstructor(
        bool member)
    {
        InMemoryPackageStore store = await StoreAsync(
            ("Avalonia", "11.3.14"),
            ("Avalonia", "12.1.2"),
            ("System.Text.Json", "10.0.0"));
        CompleteRestorationExecutionOptions options = Options(store);
        CommittedScenarioDefinitionSet input = AvaloniaDefinitions(
            member
                ? new PortableSubjectRequest.Member()
                : new PortableSubjectRequest.Type(),
            member ? "member.overview" : "type.metadata",
            includeUnrelated: !member,
            focus: member ? "avalonia" : "json");

        WorkspacePortableCoordinateReplacementResult result =
            await WorkspacePortableCoordinateReplacement.ExecuteAsync(
                input,
                new("avalonia", version: "12.1.2"),
                options,
                TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        var scope = Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            result.ScopeResult);
        Assert.Equal(WorkspaceScopeOperationKind.Add, scope.Effect);
        Assert.Equal(WorkspaceScopeOperationKind.Add, scope.Association.Kind);
        NavigationCoordinateRetentionResult retention =
            result.CoordinateRetention!;
        Assert.NotSame(
            retention.SourceWorkspace,
            retention.DestinationWorkspace);
        Assert.Same(
            retention.DestinationWorkspace,
            scope.Association.Workspace);
        Assert.Equal(
            NavigationCoordinateRetentionDisposition.ExactPath,
            retention.Disposition);
        CommittedScenarioDefinitionSet derived = result.Definitions!;
        Assert.Equal(InspectionDefinitionSchema.Version4,
            derived.Scenario.SchemaVersion);
        Assert.Equal(input.Navigation!.Focus, derived.Navigation!.Focus);
        Assert.Equal("12.1.2",
            Assert.IsType<DefinitionMemberCoordinate.PackageCoordinate>(
                derived.Workspace!.Contexts[0].Members[0]).Version);
        if (!member)
        {
            Assert.Same(input.Workspace!.Contexts[1],
                derived.Workspace.Contexts[1]);
            Assert.Same(input.Navigation.Tabs[1],
                derived.Navigation.Tabs[1]);
            Assert.Same(input.View!.States[2],
                derived.View!.States[2]);
            Assert.Equal(input.Workspace.Registrations,
                derived.Workspace.Registrations);
        }

        TestHost host = await RestoreAsync(derived, options);
        try
        {
            var resolved =
                Assert.IsType<CompleteRestorationResolvedState.Version4>(
                    host.Activation!.Snapshot.Resolved);
            CompleteRestorationResolvedViewState state =
                resolved.States.Single(
                    candidate => candidate.NavigationId == "avalonia");
            Assert.Equal(
                member
                    ? StructuralSubjectKind.Member
                    : StructuralSubjectKind.Type,
                state.Initialization!.Subject!.Kind);
            Assert.Equal(
                member ? "member.overview" : "type.metadata",
                state.Initialization.Lens!.Facet.Value);
            Assert.Equal("Avalonia.Base",
                state.Initialization.Context!.Type!.Library
                    .Identity.Assembly.Name);
            Assert.NotNull(state.Initialization.Context.Member);
            var portable = Assert.IsType<
                PortableRetainedSubjectContext.Member>(
                    derived.View!.States[1].Context);
            Assert.Equal("Avalonia.Base", portable.LibraryIdentity.Name);
            Assert.NotNull(portable.MemberAnchor);
            Assert.Null(portable.MemberSignature);
        }
        finally
        {
            Assert.True((await host.Workspace!.CloseAsync()).Succeeded);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ActiveLibrary_MovingTypeTruncatesToPairedLibrary()
    {
        InMemoryPackageStore store = await StoreAsync(
            ("Avalonia", "11.3.14"),
            ("Avalonia", "12.1.2"));
        CommittedScenarioDefinitionSet input = AvaloniaDefinitions(
            new PortableSubjectRequest.Library(),
            "library.references");

        WorkspacePortableCoordinateReplacementResult result =
            await WorkspacePortableCoordinateReplacement.ExecuteAsync(
                input,
                new("avalonia", version: "12.1.2"),
                Options(store),
                TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        Assert.Equal(
            NavigationCoordinateRetentionDisposition
                .ActiveLibraryContainmentTruncated,
            result.CoordinateRetention!.Disposition);
        CommittedViewStateDefinition state = result.Definitions!.View!.States[1];
        Assert.IsType<PortableSubjectRequest.Library>(state.Subject);
        var context =
            Assert.IsType<PortableRetainedSubjectContext.Library>(
                state.Context);
        Assert.Equal("Avalonia.Markup", context.LibraryIdentity.Name);
        Assert.Equal("12.1.2.0", context.LibraryIdentity.Version);
        Assert.Equal("library.references", state.Facet);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnavailableExactInspector_RemainsExactInDerivedState()
    {
        InMemoryPackageStore store = await StoreAsync(
            ("Avalonia", "11.3.14"),
            ("Avalonia", "12.1.2"));
        CompleteRestorationExecutionOptions options = Options(store);
        ViewFacetAvailabilitySnapshot availability = new(
            options.Facets.Descriptors.Select(descriptor =>
                new ViewFacetAvailabilityFact(
                    descriptor.Id,
                    descriptor.Id.Value == "type.metadata"
                        ? new ViewFacetAvailability.Unavailable(
                            ViewFacetUnavailableReason.CapabilityAbsent(
                                "Metadata unavailable."))
                        : ViewFacetAvailability.Available.Instance)));

        WorkspacePortableCoordinateReplacementResult result =
            await WorkspacePortableCoordinateReplacement.ExecuteAsync(
                AvaloniaDefinitions(
                    new PortableSubjectRequest.Type(),
                    "type.metadata"),
                new("avalonia", version: "12.1.2"),
                options with
                {
                    FacetAvailability = (_, _) => availability,
                },
                TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        Assert.Equal("type.metadata",
            result.Definitions!.View!.States[1].Facet);
        Assert.IsType<NavigationLensActivationResult.Unavailable>(
            result.NavigationResult!.LensResolution!.Activation);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SameCoordinate_ReturnsCompleteSameScenario()
    {
        InMemoryPackageStore store =
            await StoreAsync(("Avalonia", "11.3.14"));
        CommittedScenarioDefinitionSet input = AvaloniaDefinitions(
            new PortableSubjectRequest.Type(),
            "type.metadata");

        WorkspacePortableCoordinateReplacementResult result =
            await WorkspacePortableCoordinateReplacement.ExecuteAsync(
                input,
                new("avalonia", version: "11.3.14"),
                Options(store),
                TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        var scope = Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            result.ScopeResult);
        Assert.Equal(WorkspaceScopeOperationKind.Add, scope.Effect);
        NavigationCoordinateRetentionResult retention =
            result.CoordinateRetention!;
        Assert.NotSame(
            retention.SourceWorkspace,
            retention.DestinationWorkspace);
        Assert.Same(
            retention.DestinationWorkspace,
            scope.Association.Workspace);
        Assert.Equal(
            Encode(input),
            Encode(result.Definitions!));
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(InspectionDefinitionSchema.Version2)]
    [InlineData(InspectionDefinitionSchema.Version3)]
    public async Task EarlierCommittedVersions_AreRetainedWithoutUpgrade(
        int schemaVersion)
    {
        InMemoryPackageStore store = await StoreAsync(
            ("Avalonia", "11.3.14"),
            ("Avalonia", "12.1.2"));
        CommittedScenarioDefinitionSet input =
            PackageOnlyDefinitions(schemaVersion);

        WorkspacePortableCoordinateReplacementResult result =
            await WorkspacePortableCoordinateReplacement.ExecuteAsync(
                input,
                new("avalonia", version: "12.1.2"),
                Options(store),
                TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        Assert.Equal(schemaVersion, result.Definitions!.Scenario.SchemaVersion);
        Assert.Equal(schemaVersion, result.Definitions.View!.SchemaVersion);
        Assert.Equal(schemaVersion,
            WorkspaceSharePacketTransposer.ToPacket(
                result.Definitions,
                TestContext.Current.CancellationToken).Packet!.FormatVersion);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task FrameworkChange_UpdatesSingletonDeclarationsAndPreservesRid()
    {
        InMemoryPackageStore store =
            await StoreAsync(("Avalonia", "11.3.14"));
        CommittedScenarioDefinitionSet input = AvaloniaDefinitions(
            new PortableSubjectRequest.Package(),
            "package.overview",
            framework: "net6.0",
            runtimeIdentifier: "linux-x64");

        WorkspacePortableCoordinateReplacementResult result =
            await WorkspacePortableCoordinateReplacement.ExecuteAsync(
                input,
                new("avalonia", framework: "net8.0"),
                Options(store),
                TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        WorkspaceContextDefinition context =
            result.Definitions!.Workspace!.Contexts[0];
        Assert.Equal("net8.0", context.Framework);
        Assert.Equal("linux-x64", context.RuntimeIdentifier);
        var member =
            Assert.IsType<DefinitionMemberCoordinate.PackageCoordinate>(
                context.Members[0]);
        Assert.Equal("linux-x64", member.RuntimeIdentifier);
        Assert.Equal("net8.0",
            Assert.IsType<DefinitionMemberCoordinate.PackageCoordinate>(
                result.Definitions.Navigation!.Tabs[0].Coordinate).Framework);
    }

    [Theory]
    [InlineData(
        "floating",
        WorkspacePortableCoordinateReplacementFailureKind
            .FloatingSourceUnsupported)]
    [InlineData(
        "group",
        WorkspacePortableCoordinateReplacementFailureKind
            .NavigationSourceNotDirectPackage)]
    [InlineData(
        "repeated",
        WorkspacePortableCoordinateReplacementFailureKind
            .NavigationSourceAmbiguous)]
    [InlineData(
        "duplicate-destination",
        WorkspacePortableCoordinateReplacementFailureKind
            .DuplicateDestinationNavigationSource)]
    [InlineData(
        "shared-framework",
        WorkspacePortableCoordinateReplacementFailureKind
            .SharedFrameworkChangeUnsupported)]
    [InlineData(
        "query",
        WorkspacePortableCoordinateReplacementFailureKind
            .QueryStateUnsupported)]
    public async Task UnsupportedInputs_AreTypedBeforeWorkspaceConstruction(
        string kind,
        WorkspacePortableCoordinateReplacementFailureKind expected)
    {
        (CommittedScenarioDefinitionSet definitions,
            WorkspacePackageCoordinateReplacementRequest request) =
            UnsupportedInput(kind);

        WorkspacePortableCoordinateReplacementResult result =
            await WorkspacePortableCoordinateReplacement.ExecuteAsync(
                definitions,
                request,
                Options(new InMemoryPackageStore()),
                TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Failure!.Kind);
        Assert.Null(result.ScopeResult);
        Assert.Null(result.NavigationResult);
        Assert.Null(result.CoordinateRetention);
    }

    [Fact]
    public async Task InvalidDestination_IsTypedBeforeWorkspaceConstruction()
    {
        WorkspacePortableCoordinateReplacementResult result =
            await WorkspacePortableCoordinateReplacement.ExecuteAsync(
                PackageOnlyDefinitions(
                    InspectionDefinitionSchema.Version4),
                new("avalonia", framework: "not a framework"),
                Options(new InMemoryPackageStore()),
                TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(
            WorkspacePortableCoordinateReplacementFailureKind
                .InvalidDestination,
            result.Failure!.Kind);
        Assert.Null(result.ScopeResult);
        Assert.Null(result.NavigationResult);
        Assert.Null(result.CoordinateRetention);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task MissingDestination_ReturnsNoSuccessfulDefinitions()
    {
        InMemoryPackageStore store =
            await StoreAsync(("Avalonia", "11.3.14"));

        WorkspacePortableCoordinateReplacementResult result =
            await WorkspacePortableCoordinateReplacement.ExecuteAsync(
                AvaloniaDefinitions(
                    new PortableSubjectRequest.Package(),
                    "package.overview"),
                new("avalonia", version: "12.1.2"),
                Options(store),
                TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(
            WorkspacePortableCoordinateReplacementFailureKind
                .DestinationAcquisitionFailed,
            result.Failure!.Kind);
        Assert.Null(result.Definitions);
        Assert.Null(result.ScopeResult);
        Assert.Null(result.NavigationResult);
        Assert.Null(result.CoordinateRetention);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(InspectionDefinitionSchema.Version2)]
    [InlineData(InspectionDefinitionSchema.Version3)]
    [InlineData(InspectionDefinitionSchema.Version4)]
    public async Task WorkspaceActive_PackageOnlyContextIsOmittedAndRestorable(
        int schemaVersion)
    {
        InMemoryPackageStore store = await StoreAsync(
            ("Avalonia", "11.3.14"), ("Avalonia", "12.1.2"));
        CompleteRestorationExecutionOptions options = Options(store);
        var package = new DefinitionMemberCoordinate.PackageCoordinate(
            "Avalonia", "11.3.14", "net8.0");
        var input = Definitions(
            schemaVersion,
            [new WorkspaceContextDefinition("context", "net8.0", members: [package])],
            [new NavigationTabDefinition("avalonia", coordinate: package)],
            [
                new(null, new PortableSubjectRequest.Workspace()),
                new("avalonia", new PortableSubjectRequest.Workspace(),
                    facet: "workspace.overview"),
            ],
            "avalonia");

        var result = await WorkspacePortableCoordinateReplacement.ExecuteAsync(
            input, new("avalonia", version: "12.1.2"), options,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        Assert.IsType<WorkspaceScopeOperationResult.Committed>(result.ScopeResult);
        Assert.Equal(schemaVersion, result.Definitions!.Scenario.SchemaVersion);
        var state = result.Definitions.View!.States[1];
        Assert.IsType<PortableSubjectRequest.Workspace>(state.Subject);
        Assert.Null(state.Context);
        Assert.Equal("workspace.overview", state.Facet);
        Assert.Equal("avalonia", result.Definitions.Navigation!.Focus);
        var portable = WorkspaceSharePacketTransposer.ToCommittedDefinitions(
            WorkspaceSharePacketCodec.Decode(
                Encode(result.Definitions), TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        TestHost host = await RestoreAsync(portable, options);
        Assert.True((await host.Workspace!.CloseAsync()).Succeeded);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WorkspaceActive_DeeperRetainedContextIsPreserved()
    {
        InMemoryPackageStore store = await StoreAsync(
            ("Avalonia", "11.3.14"), ("Avalonia", "12.1.2"));
        CompleteRestorationExecutionOptions options = Options(store);
        var input = AvaloniaDefinitions(
            new PortableSubjectRequest.Workspace(), "workspace.overview");

        var result = await WorkspacePortableCoordinateReplacement.ExecuteAsync(
            input, new("avalonia", version: "12.1.2"), options,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        var state = result.Definitions!.View!.States[1];
        Assert.IsType<PortableSubjectRequest.Workspace>(state.Subject);
        var retained = Assert.IsType<PortableRetainedSubjectContext.Member>(state.Context);
        Assert.Equal("Avalonia.Base", retained.LibraryIdentity.Name);
        Assert.NotNull(retained.MemberAnchor);
        Assert.Equal("workspace.overview", state.Facet);
        TestHost host = await RestoreAsync(result.Definitions, options);
        Assert.True((await host.Workspace!.CloseAsync()).Succeeded);
    }

    private static CommittedScenarioDefinitionSet AvaloniaDefinitions(
        PortableSubjectRequest subject,
        string facet,
        bool includeUnrelated = false,
        string focus = "avalonia",
        string framework = "net8.0",
        string? runtimeIdentifier = null,
        string memberSignature = "M:Avalonia.Data.MultiBinding.#ctor()")
    {
        var avalonia = new DefinitionMemberCoordinate.PackageCoordinate(
            "Avalonia",
            "11.3.14",
            framework,
            runtimeIdentifier);
        var contexts = new List<WorkspaceContextDefinition>
        {
            new(
                "avalonia-context",
                framework,
                runtimeIdentifier,
                members: [avalonia]),
        };
        var tabs = new List<NavigationTabDefinition>
        {
            new("avalonia", coordinate: avalonia),
        };
        var states = new List<CommittedViewStateDefinition>
        {
            new(
                null,
                new PortableSubjectRequest.Workspace(),
                facet: "workspace.overview"),
            new(
                "avalonia",
                subject,
                subject is PortableSubjectRequest.Package
                    ? new PortableRetainedSubjectContext.Package()
                    : new PortableRetainedSubjectContext.Member(
                        new PortableLibraryIdentity(
                            "Avalonia.Markup",
                            "11.3.14.0",
                            null,
                            AvaloniaToken),
                        TypeName(),
                        memberSignature: memberSignature),
                facet),
        };
        if (includeUnrelated)
        {
            var json = new DefinitionMemberCoordinate.PackageCoordinate(
                "System.Text.Json",
                "10.0.0",
                "net10.0");
            contexts.Add(
                new WorkspaceContextDefinition(
                    "json-context",
                    "net10.0",
                    members: [json]));
            tabs.Add(new NavigationTabDefinition("json", coordinate: json));
            states.Add(
                new CommittedViewStateDefinition(
                    "json",
                    new PortableSubjectRequest.Package(),
                    new PortableRetainedSubjectContext.Package(),
                    "package.overview"));
        }

        return Definitions(
            InspectionDefinitionSchema.Version4,
            contexts,
            tabs,
            states,
            focus,
            registrations:
            [
                new WorkspaceRegistration.PackagePrefix(
                    new("Microsoft.Extensions.")),
            ]);
    }

    private static CommittedScenarioDefinitionSet PackageOnlyDefinitions(
        int schemaVersion)
    {
        var package = new DefinitionMemberCoordinate.PackageCoordinate(
            "Avalonia",
            "11.3.14",
            "net8.0");
        return Definitions(
            schemaVersion,
            [new WorkspaceContextDefinition(
                "context",
                "net8.0",
                members: [package])],
            [new NavigationTabDefinition("avalonia", coordinate: package)],
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace(),
                    facet: "workspace.overview"),
                new CommittedViewStateDefinition(
                    "avalonia",
                    new PortableSubjectRequest.Package(),
                    new PortableRetainedSubjectContext.Package(),
                    "package.overview"),
            ],
            "avalonia");
    }

    private static (
        CommittedScenarioDefinitionSet Definitions,
        WorkspacePackageCoordinateReplacementRequest Request)
        UnsupportedInput(string kind)
    {
        int schema = InspectionDefinitionSchema.Version4;
        DefinitionMemberCoordinate.PackageCoordinate Package(
            string version,
            string framework = "net8.0") =>
            new("Avalonia", version, framework);

        return kind switch
        {
            "floating" => (
                Definitions(
                    schema,
                    [new WorkspaceContextDefinition(
                        "one",
                        "net8.0",
                        members:
                        [
                            new DefinitionMemberCoordinate.PackageCoordinate(
                                "Avalonia",
                                null,
                                "net8.0"),
                        ])],
                    [new NavigationTabDefinition(
                        "avalonia",
                        coordinate:
                            new DefinitionMemberCoordinate.PackageCoordinate(
                                "Avalonia",
                                null,
                                "net8.0"))],
                    BasicStates("avalonia"),
                    "avalonia"),
                new("avalonia", version: "12.1.2")),
            "group" => (
                Definitions(
                    schema,
                    [new WorkspaceContextDefinition(
                        "one",
                        "net8.0",
                        subscribe: ":group@1.0.0",
                        members: [Package("11.3.14")])],
                    [new NavigationTabDefinition(
                        "avalonia",
                        subscribe: ":group@1.0.0",
                        framework: "net8.0")],
                    BasicStates("avalonia"),
                    focus: null),
                new("avalonia", version: "12.1.2")),
            "repeated" => (
                Definitions(
                    schema,
                    [
                        new WorkspaceContextDefinition(
                            "one", "net8.0",
                            members: [Package("11.3.14")]),
                        new WorkspaceContextDefinition(
                            "two", "net8.0",
                            members: [Package("11.3.14")]),
                    ],
                    [new NavigationTabDefinition(
                        "avalonia",
                        coordinate: Package("11.3.14"))],
                    BasicStates("avalonia"),
                    "avalonia"),
                new("avalonia", version: "12.1.2")),
            "duplicate-destination" => (
                Definitions(
                    schema,
                    [
                        new WorkspaceContextDefinition(
                            "old", "net8.0",
                            members: [Package("11.3.14")]),
                        new WorkspaceContextDefinition(
                            "new", "net8.0",
                            members: [Package("12.1.2")]),
                    ],
                    [
                        new NavigationTabDefinition(
                            "old",
                            coordinate: Package("11.3.14")),
                        new NavigationTabDefinition(
                            "new",
                            coordinate: Package("12.1.2")),
                    ],
                    BasicStates("old", "new"),
                    "old"),
                new("old", version: "12.1.2")),
            "shared-framework" => (
                Definitions(
                    schema,
                    [new WorkspaceContextDefinition(
                        "shared",
                        "net8.0",
                        members:
                        [
                            Package("11.3.14"),
                            new DefinitionMemberCoordinate.PackageCoordinate(
                                "System.Text.Json",
                                "10.0.0",
                                "net8.0"),
                        ])],
                    [new NavigationTabDefinition(
                        "avalonia",
                        coordinate: Package("11.3.14"))],
                    BasicStates("avalonia"),
                    "avalonia"),
                new("avalonia", framework: "net9.0")),
            "query" => (
                Definitions(
                    schema,
                    [new WorkspaceContextDefinition(
                        "one", "net8.0",
                        members: [Package("11.3.14")])],
                    [new NavigationTabDefinition(
                        "avalonia",
                        coordinate: Package("11.3.14"))],
                    [
                        new CommittedViewStateDefinition(
                            null,
                            new PortableSubjectRequest.Workspace(),
                            facet: "workspace.overview"),
                        new CommittedViewStateDefinition(
                            "avalonia",
                            new PortableSubjectRequest.Package(),
                            new PortableRetainedSubjectContext.Package(),
                            "package.overview",
                            queries: ["q"]),
                    ],
                    "avalonia"),
                new("avalonia", version: "12.1.2")),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static IReadOnlyList<CommittedViewStateDefinition> BasicStates(
        params string[] navigationIds) =>
        [
            new CommittedViewStateDefinition(
                null,
                new PortableSubjectRequest.Workspace(),
                facet: "workspace.overview"),
            .. navigationIds.Select(id =>
                new CommittedViewStateDefinition(
                    id,
                    new PortableSubjectRequest.Package(),
                    new PortableRetainedSubjectContext.Package(),
                    "package.overview")),
        ];

    private static CommittedScenarioDefinitionSet Definitions(
        int schemaVersion,
        IReadOnlyList<WorkspaceContextDefinition> contexts,
        IReadOnlyList<NavigationTabDefinition> tabs,
        IReadOnlyList<CommittedViewStateDefinition> states,
        string? focus,
        IReadOnlyList<WorkspaceRegistration>? registrations = null)
    {
        var workspace = new WorkspaceDefinition(
            schemaVersion,
            "workspace",
            contexts,
            registrations: registrations);
        var navigation = new CommittedNavigationDefinition(
            schemaVersion,
            "navigation",
            tabs,
            focus);
        var view = new CommittedViewDefinition(
            schemaVersion,
            "view",
            states);
        var scenario = new ScenarioDefinition(
            schemaVersion,
            "scenario",
            workspace: workspace.Id,
            context: contexts[0].Name,
            view: view.Id,
            navigation: navigation.Id);
        return new CommittedScenarioDefinitionSet(
            scenario,
            workspace,
            navigation,
            view,
            []);
    }

    private static MetadataTypeDefinitionName TypeName() =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "Avalonia.Data",
                ["MultiBinding"])).Name;

    private static CompleteRestorationExecutionOptions Options(
        InMemoryPackageStore store)
    {
        ViewFacetRegistry facets = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot available =
            NavigationSnapshotTestData.AllAvailable(facets);
        return new()
        {
            ContextLoad = new WorkspaceContextLoadOptions
            {
                HttpClient = new HttpClient(new RejectingHandler()),
                SourceAuthorization =
                    new UniformPackageSourceAuthorization(
                        [new PackageSource("nuget.org", ProducerUrl)]),
                PackageStore = store,
            },
            ScopeDeadline = DateTimeOffset.UtcNow.AddMinutes(5),
            Facets = facets,
            FacetAvailability = (_, _) => available,
        };
    }

    private static async Task<InMemoryPackageStore> StoreAsync(
        params (string Id, string Version)[] packages)
    {
        var store = new InMemoryPackageStore();
        string producerKey =
            PackageSourceClientFactory.GetProducerIdentity(
                new PackageSource("nuget.org", ProducerUrl)).Key;
        foreach ((string id, string version) in packages)
        {
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "ApiMatching",
                $"{id.ToLowerInvariant()}.{version}.nupkg");
            await using FileStream content = File.OpenRead(path);
            await store.CommitAsync(
                id,
                version,
                producerKey,
                content,
                TestContext.Current.CancellationToken);
            await using FileStream legacyContent = File.OpenRead(path);
            await store.CommitAsync(
                id,
                version,
                NuGetCache.GetSourceKey(ProducerUrl),
                legacyContent,
                TestContext.Current.CancellationToken);
        }
        return store;
    }

    private static string Encode(CommittedScenarioDefinitionSet definitions)
    {
        WorkspaceSharePacketProjectionResult result =
            WorkspaceSharePacketTransposer.ToPacket(definitions);
        return WorkspaceSharePacketCodec.Encode(
            Assert.IsType<WorkspaceSharePacket>(result.Packet));
    }

    private static async Task<TestHost> RestoreAsync(
        CommittedScenarioDefinitionSet definitions,
        CompleteRestorationExecutionOptions options)
    {
        var authority = new TestIntentAuthority();
        CompleteRestorationPreparationResult preparation =
            CompleteRestorationPreparation.FromCommittedDefinitions(
                definitions,
                authority);
        var host = new TestHost();
        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                options,
                TestContext.Current.CancellationToken);
        Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        return host;
    }

    private sealed class TestIntentAuthority :
        ICompleteRestorationIntentAuthority
    {
        public CompleteRestorationIntentIdentity Identity { get; } = new();

        public CompleteRestorationIntentStatus Status =>
            CompleteRestorationIntentStatus.Current;

        public CancellationToken Revocation => CancellationToken.None;
    }

    private sealed class TestHost :
        ICompleteRestorationHost<InspectionWorkspace>
    {
        public InspectionWorkspace? Workspace { get; private set; }

        public CompleteWorkspaceActivation? Activation { get; private set; }

        public async ValueTask<
            CompleteRestorationHostResult<InspectionWorkspace>>
            ConstructAsync(
                ICompleteRestorationIntentAuthority authority,
                CompleteRestorationPlan plan,
                CompleteWorkspacePreparationCallback prepare,
                CancellationToken cancellationToken = default)
        {
            Workspace = new InspectionWorkspace(plan.WorkspacePlan);
            CompleteWorkspacePreparationResult result =
                await prepare(Workspace, authority.Revocation);
            var prepared =
                Assert.IsType<
                    CompleteWorkspacePreparationResult.Prepared>(result);
            Activation = prepared.Activation;
            return new CompleteRestorationHostResult<InspectionWorkspace>
                .Activated(Workspace, prepared.Activation);
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
