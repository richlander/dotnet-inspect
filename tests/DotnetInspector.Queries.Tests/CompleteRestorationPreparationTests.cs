using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;

namespace DotnetInspector.Queries.Tests;

public sealed class CompleteRestorationPreparationTests
{
    [Fact]
    public void Version2Definition_PreparesExactWorkspacePlanBeforeConstruction()
    {
        InspectionDefinitionRegistry registry = Version2Registry();
        var authority = new TestIntentAuthority();

        var ready = Assert.IsType<CompleteRestorationPreparationResult.Ready>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "scenario",
                authority));

        Assert.Same(authority.Identity, ready.Plan.Intent);
        Assert.IsType<CompleteRestorationRequestBasis.DefinitionInput>(
            ready.Plan.Request);
        var recipe =
            Assert.IsType<CompleteRestorationRecipe.Version2>(
                ready.Plan.Recipe);
        Assert.Equal("scenario", recipe.Definitions.Scenario.Id);
        Assert.Single(ready.Plan.WorkspacePlan.Contexts);
        Assert.IsType<WorkspaceMemberCoordinate.PackageMember>(
            ready.Plan.WorkspacePlan.Contexts[0].Members[0]);
    }

    [Fact]
    public void LibraryScopedVersion1Definition_FailsWithoutQueryOwnerMigration()
    {
        InspectionDefinitionRegistry registry = Version1Registry(
            new ViewDefinition(
                InspectionDefinitionSchema.Version1,
                "view",
                lens: "api",
                type: "System.Text.Json.JsonSerializer",
                libraries: ["System.Text.Json"]));
        var authority = new TestIntentAuthority();

        var failed = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "scenario",
                authority));

        Assert.Same(authority.Identity, failed.Intent);
        Assert.IsType<CompleteRestorationFailure.LegacyLoweringFailed>(
            failed.Failure);
    }

    [Fact]
    public void DirectPackageVersion1Definition_PreparesLegacyRuntimeRecipe()
    {
        InspectionDefinitionRegistry registry = Version1Registry(
            new ViewDefinition(
                InspectionDefinitionSchema.Version1,
                "view",
                lens: "api",
                type: "System.Text.Json.JsonSerializer"));

        var ready = Assert.IsType<CompleteRestorationPreparationResult.Ready>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "scenario",
                new TestIntentAuthority()));

        var recipe =
            Assert.IsType<CompleteRestorationRecipe.LegacyDirectPackage>(
                ready.Plan.Recipe);
        Assert.Equal(
            CompleteRestorationLegacySource.DefinitionV1,
            recipe.Source);
        Assert.Equal("package", recipe.Scenario.Navigation!.FocusTabId);
    }

    [Fact]
    public void InvalidVersion1Facet_FailsBeforeConstruction()
    {
        InspectionDefinitionRegistry registry = Version1Registry(
            new ViewDefinition(
                InspectionDefinitionSchema.Version1,
                "view",
                lens: "API",
                type: "System.Text.Json.JsonSerializer"));

        var failed = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "scenario",
                new TestIntentAuthority()));

        Assert.IsType<CompleteRestorationFailure.LegacyLoweringFailed>(
            failed.Failure);
    }

    [Fact]
    public void InactiveVersion1NavigationOutsideWorkspace_FailsComposition()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(Workspace(InspectionDefinitionSchema.Version1));
        registry.Add(new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            "navigation",
            [
                new NavigationTabDefinition("package", coordinate: Package()),
                new NavigationTabDefinition(
                    "missing",
                    coordinate:
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "Newtonsoft.Json",
                            "13.0.3",
                            "net9.0")),
            ],
            "package"));
        registry.Add(new ViewDefinition(
            InspectionDefinitionSchema.Version1,
            "view",
            lens: "overview"));
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: "view",
            navigation: "navigation"));

        var failed = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "scenario",
                new TestIntentAuthority()));

        Assert.IsType<CompleteRestorationFailure.InvalidDefinitionSet>(
            failed.Failure);
    }

    [Fact]
    public void NormalizedSourceWithOneEffectiveTarget_Prepares()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version1,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    members:
                    [
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "System.Text.Json",
                            "9.0.4",
                            "net9.0"),
                    ]),
                new WorkspaceContextDefinition(
                    "second",
                    framework: "net9.0",
                    members:
                    [
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "System.Text.Json",
                            "9.0.4",
                            "net9.0"),
                    ]),
            ]));
        registry.Add(new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            "navigation",
            [
                new NavigationTabDefinition(
                    "package",
                    coordinate:
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "system.text.json",
                            "9.0.4",
                            "NET9.0")),
            ],
            "package"));
        registry.Add(new ViewDefinition(
            InspectionDefinitionSchema.Version1,
            "view",
            lens: "overview"));
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: "view",
            navigation: "navigation"));

        Assert.IsType<CompleteRestorationPreparationResult.Ready>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "scenario",
                new TestIntentAuthority()));
    }

    [Fact]
    public void DormantVersion2NavigationOutsideWorkspace_FailsComposition()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(Workspace(InspectionDefinitionSchema.Version2));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [
                new NavigationTabDefinition("package", coordinate: Package()),
                new NavigationTabDefinition(
                    "platform",
                    coordinate:
                        new DefinitionMemberCoordinate.PlatformCoordinate(
                            "runtime",
                            Framework: "net9.0")),
            ],
            "package"));
        registry.Add(new CommittedViewDefinition(
            InspectionDefinitionSchema.Version2,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition(
                    "package",
                    new PortableSubjectRequest.Package(),
                    new PortableRetainedSubjectContext.Package()),
                new CommittedViewStateDefinition("platform"),
            ]));
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version2,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: "view",
            navigation: "navigation"));

        var failed = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "scenario",
                new TestIntentAuthority()));

        Assert.IsType<CompleteRestorationFailure.InvalidDefinitionSet>(
            failed.Failure);
    }

    [Fact]
    public void Version1DefinitionQuery_FailsWithoutQueryOwnerMigration()
    {
        InspectionDefinitionRegistry registry = Version1Registry(
            new ViewDefinition(
                InspectionDefinitionSchema.Version1,
                "view",
                lens: "overview"));
        registry.Add(new QueryDefinition(
            InspectionDefinitionSchema.Version1,
            "query",
            "legacy-query"));
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "query-scenario",
            workspace: "workspace",
            context: "context",
            query: "query",
            view: "view",
            navigation: "navigation"));

        var failed = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "query-scenario",
                new TestIntentAuthority()));

        Assert.IsType<CompleteRestorationFailure.LegacyLoweringFailed>(
            failed.Failure);
    }

    [Fact]
    public void Version1DefinitionWithoutNavigation_FailsInsteadOfHandoff()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(Workspace(InspectionDefinitionSchema.Version1));
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "scenario",
            workspace: "workspace",
            context: "context"));

        var failed = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "scenario",
                new TestIntentAuthority()));

        Assert.IsType<CompleteRestorationFailure.LegacyLoweringFailed>(
            failed.Failure);
    }

    [Fact]
    public void FocusedNonPackageVersion1Definition_FailsInsteadOfHandoff()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
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
            ]));
        registry.Add(new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            "navigation",
            [
                new NavigationTabDefinition(
                    "platform",
                    coordinate:
                        new DefinitionMemberCoordinate.PlatformCoordinate(
                            "runtime",
                            Framework: "net10.0")),
            ],
            "platform"));
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "scenario",
            workspace: "workspace",
            context: "context",
            navigation: "navigation"));

        var failed = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "scenario",
                new TestIntentAuthority()));

        Assert.IsType<CompleteRestorationFailure.LegacyLoweringFailed>(
            failed.Failure);
    }

    [Fact]
    public void PacketV1LibraryScope_FailsWithoutQueryOwnerMigration()
    {
        InspectionDefinitionRegistry registry = Version1Registry(
            new ViewDefinition(
                InspectionDefinitionSchema.Version1,
                "view",
                lens: "api",
                type: "System.Text.Json.JsonSerializer",
                libraries: ["System.Text.Json"]));
        ResolvedScenario resolved = registry.ResolveScenario("scenario");
        var definitions = new WorkspaceSharePacketDefinitionSet(
            resolved.Workspace!,
            Assert.IsType<NavigationDefinition>(
                registry.Records.Single(
                    record => record.Kind
                        == InspectionDefinitionKind.Navigation)),
            resolved.View!,
            resolved.Scenario);
        WorkspaceSharePacket packet =
            Assert.IsType<WorkspaceSharePacketProjectionResult>(
                WorkspaceSharePacketTransposer.ToPacket(
                    definitions,
                    TestContext.Current.CancellationToken))
                .Packet!;
        string encoded = WorkspaceSharePacketCodec.Encode(packet);

        var failed = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                encoded,
                new TestIntentAuthority()));

        Assert.IsType<CompleteRestorationFailure.LegacyLoweringFailed>(
            failed.Failure);
    }

    [Theory]
    [InlineData(null, null, "net9.0", null)]
    [InlineData("net9.0", null, "net9.0", "linux-x64")]
    public void PacketV1ExplicitNullTarget_SelectsUnqualifiedSource(
        string? selectedFramework,
        string? selectedRuntimeIdentifier,
        string? qualifiedFramework,
        string? qualifiedRuntimeIdentifier)
    {
        var packet = new WorkspaceSharePacket(
            [
                new WorkspaceShareTab(
                    WorkspaceShareSourceKind.Package,
                    "system.text.json",
                    "9.0.4",
                    selectedFramework,
                    selectedRuntimeIdentifier),
                new WorkspaceShareTab(
                    WorkspaceShareSourceKind.Package,
                    "system.text.json",
                    "9.0.4",
                    qualifiedFramework,
                    qualifiedRuntimeIdentifier),
            ],
            [
                new WorkspaceShareContext([0]),
                new WorkspaceShareContext([1]),
            ],
            activeTabIndex: 0,
            selectedContextIndex: 0,
            lens: "overview",
            type: null,
            memberAnchor: null,
            memberSignature: null,
            section: null,
            libraries: []);
        string encoded = WorkspaceSharePacketCodec.Encode(packet);

        Assert.IsType<CompleteRestorationPreparationResult.Ready>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                encoded,
                new TestIntentAuthority()));
    }

    [Fact]
    public void SupersededIntent_ReturnsNoConstructiblePlan()
    {
        var authority = new TestIntentAuthority
        {
            Status = CompleteRestorationIntentStatus.Superseded,
        };

        var superseded =
            Assert.IsType<CompleteRestorationPreparationResult.Superseded>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version2Registry(),
                    "scenario",
                    authority));

        Assert.Same(authority.Identity, superseded.Intent);
    }

    private static InspectionDefinitionRegistry Version2Registry()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(Workspace(InspectionDefinitionSchema.Version2));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [new NavigationTabDefinition("package", coordinate: Package())],
            "package"));
        registry.Add(new CommittedViewDefinition(
            InspectionDefinitionSchema.Version2,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition(
                    "package",
                    new PortableSubjectRequest.Package(),
                    new PortableRetainedSubjectContext.Package(),
                    facet: "package.overview"),
            ]));
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version2,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: "view",
            navigation: "navigation"));
        return registry;
    }

    private static InspectionDefinitionRegistry Version1Registry(
        ViewDefinition view)
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(Workspace(InspectionDefinitionSchema.Version1));
        registry.Add(new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            "navigation",
            [new NavigationTabDefinition("package", coordinate: Package())],
            "package"));
        registry.Add(view);
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: view.Id,
            navigation: "navigation"));
        return registry;
    }

    private static WorkspaceDefinition Workspace(int schemaVersion) =>
        new(
            schemaVersion,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    framework: "net10.0",
                    members: [Package()]),
            ]);

    private static DefinitionMemberCoordinate.PackageCoordinate Package() =>
        new("System.Text.Json", "10.0.0", "net10.0");

    private sealed class TestIntentAuthority :
        ICompleteRestorationIntentAuthority
    {
        public CompleteRestorationIntentIdentity Identity { get; } = new();

        public CompleteRestorationIntentStatus Status { get; set; } =
            CompleteRestorationIntentStatus.Current;

        public CancellationToken Revocation => CancellationToken.None;
    }
}
