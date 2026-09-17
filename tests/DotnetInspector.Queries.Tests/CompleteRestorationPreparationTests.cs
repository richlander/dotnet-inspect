using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries.Tests;

public sealed class CompleteRestorationPreparationTests
{
    [Fact]
    public void Version3Definition_PreparesRegistrationOnlyWorkspacePlan()
    {
        InspectionDefinitionRegistry registry = Version3Registry();

        var ready = Assert.IsType<CompleteRestorationPreparationResult.Ready>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "scenario",
                new TestIntentAuthority()));

        Assert.IsType<CompleteRestorationRecipe.Version3>(
            ready.Plan.Recipe);
        Assert.Empty(ready.Plan.WorkspacePlan.Contexts);
        Assert.IsType<WorkspaceRegistration.PackagePrefix>(
            Assert.Single(ready.Plan.WorkspacePlan.Registrations));
    }

    [Fact]
    public void Version3Packet_PreparesRegistrationOnlyWorkspacePlan()
    {
        const string json =
            """{"f":3,"t":[],"g":[],"r":[["p","Microsoft.Extensions."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""";
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);

        var ready = Assert.IsType<CompleteRestorationPreparationResult.Ready>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                WorkspaceSharePacketCodec.Encode(packet),
                new TestIntentAuthority()));

        Assert.IsType<CompleteRestorationRecipe.Version3>(
            ready.Plan.Recipe);
        Assert.Empty(ready.Plan.WorkspacePlan.Contexts);
        Assert.Single(ready.Plan.WorkspacePlan.Registrations);
    }

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
    public void Version2Packet_PreparesExactWorkspacePlanBeforeConstruction()
    {
        InspectionDefinitionRegistry registry = Version2Registry();
        var prepared =
            Assert.IsType<InspectionDefinitionScenarioPreparationResult.Version2>(
                registry.PrepareScenario("scenario"));
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                prepared.Definitions,
                TestContext.Current.CancellationToken);
        string encoded = WorkspaceSharePacketCodec.Encode(
            Assert.IsType<WorkspaceSharePacket>(projection.Packet));
        var authority = new TestIntentAuthority();

        var ready = Assert.IsType<CompleteRestorationPreparationResult.Ready>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                encoded,
                authority));

        Assert.Same(authority.Identity, ready.Plan.Intent);
        Assert.IsType<CompleteRestorationRequestBasis.PacketInput>(
            ready.Plan.Request);
        Assert.IsType<CompleteRestorationRecipe.Version2>(ready.Plan.Recipe);
    }

    [Fact]
    public void Version2Packet_RoundTripsExplicitNullTargetBesideQualifiedTarget()
    {
        var packet = new WorkspaceSharePacket(
            [
                new WorkspaceShareTab(
                    WorkspaceShareSourceKind.Package,
                    "Example",
                    "1.0.0",
                    "net10.0",
                    runtimeIdentifier: null),
                new WorkspaceShareTab(
                    WorkspaceShareSourceKind.Package,
                    "Example",
                    "1.0.0",
                    "net10.0",
                    "linux-x64"),
            ],
            [
                new WorkspaceShareContext([0]),
                new WorkspaceShareContext([1]),
            ],
            focusedTabIndex: 0,
            selectedContextIndex: 0,
            [
                new WorkspaceShareViewState(
                    tabIndex: null,
                    new PortableSubjectRequest.Workspace(),
                    context: null,
                    facet: null),
                new WorkspaceShareViewState(
                    0,
                    new PortableSubjectRequest.Package(),
                    new PortableRetainedSubjectContext.Package(),
                    "package.overview"),
                new WorkspaceShareViewState(
                    1,
                    subject: null,
                    context: null,
                    facet: null),
            ]);
        var authority = new TestIntentAuthority();

        var ready = Assert.IsType<CompleteRestorationPreparationResult.Ready>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                WorkspaceSharePacketCodec.Encode(packet),
                authority));

        var recipe = Assert.IsType<CompleteRestorationRecipe.Version2>(
            ready.Plan.Recipe);
        Assert.Null(ready.Plan.WorkspacePlan.Contexts[0].RuntimeIdentifier);
        Assert.Equal(
            "linux-x64",
            ready.Plan.WorkspacePlan.Contexts[1].RuntimeIdentifier);
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                recipe.Definitions,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            WorkspaceSharePacketCodec.Encode(packet),
            WorkspaceSharePacketCodec.Encode(
                Assert.IsType<WorkspaceSharePacket>(projection.Packet)));
    }

    [Fact]
    public void PacketCancellationAfterDecode_ReturnsTypedFailure()
    {
        InspectionDefinitionRegistry registry = Version2Registry();
        var prepared =
            Assert.IsType<InspectionDefinitionScenarioPreparationResult.Version2>(
                registry.PrepareScenario("scenario"));
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                prepared.Definitions,
                TestContext.Current.CancellationToken);
        string encoded = WorkspaceSharePacketCodec.Encode(
            Assert.IsType<WorkspaceSharePacket>(projection.Packet));
        using var cancellation = new CancellationTokenSource();
        var authority = new CancelOnSecondStatusReadAuthority(cancellation);

        var failed = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            WorkspaceDefinitionConsumer.PrepareRestorationWithCancellation(
                encoded,
                authority,
                cancellation.Token));

        Assert.IsType<CompleteRestorationFailure.Cancelled>(failed.Failure);
    }

    [Fact]
    public void Version1Definition_IsRejectedBeforeConstruction()
    {
        InspectionDefinitionRegistry registry = Version1Registry();
        var authority = new TestIntentAuthority();

        var failed = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "scenario",
                authority));

        Assert.Same(authority.Identity, failed.Intent);
        var unsupported =
            Assert.IsType<CompleteRestorationFailure.UnsupportedVersion>(
                failed.Failure);
        Assert.Equal(InspectionDefinitionSchema.Version1, unsupported.Version);
    }

    [Fact]
    public void Version1Packet_IsRejectedBeforeConstruction()
    {
        var packet = new WorkspaceSharePacket(
            [
                new WorkspaceShareTab(
                    WorkspaceShareSourceKind.Package,
                    "System.Text.Json",
                    "10.0.0",
                    "net10.0",
                    runtimeIdentifier: null),
            ],
            [new WorkspaceShareContext([0])],
            activeTabIndex: 0,
            selectedContextIndex: 0,
            lens: "overview",
            type: null,
            memberAnchor: null,
            memberSignature: null,
            section: null,
            libraries: []);
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        var authority = new TestIntentAuthority();

        var failed = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                encoded,
                authority));

        Assert.Same(authority.Identity, failed.Intent);
        var unsupported =
            Assert.IsType<CompleteRestorationFailure.UnsupportedVersion>(
                failed.Failure);
        Assert.Equal(WorkspaceSharePacketCodec.LegacyFormatVersion, unsupported.Version);
    }

    [Fact]
    public void WorkspaceFreeVersion2Definition_IsRejectedBeforeConstruction()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version2,
            "scenario",
            input: "System.Text.Json@10.0.0"));

        var failed = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry,
                "scenario",
                new TestIntentAuthority()));

        Assert.IsType<CompleteRestorationFailure.InvalidDefinitionSet>(
            failed.Failure);
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

    private static InspectionDefinitionRegistry Version1Registry()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(Workspace(InspectionDefinitionSchema.Version1));
        registry.Add(new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            "navigation",
            [new NavigationTabDefinition("package", coordinate: Package())],
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
        return registry;
    }

    private static InspectionDefinitionRegistry Version3Registry()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version3,
            "workspace",
            [],
            registrations:
            [
                new WorkspaceRegistration.PackagePrefix(
                    new PackagePrefixDeclaration(
                        "Microsoft.Extensions.")),
            ]));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version3,
            "navigation",
            [],
            focus: null));
        registry.Add(new CommittedViewDefinition(
            InspectionDefinitionSchema.Version3,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
            ]));
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version3,
            "scenario",
            workspace: "workspace",
            context: null,
            view: "view",
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

    private sealed class CancelOnSecondStatusReadAuthority(
        CancellationTokenSource cancellation) :
        ICompleteRestorationIntentAuthority
    {
        private int _reads;

        public CompleteRestorationIntentIdentity Identity { get; } = new();

        public CompleteRestorationIntentStatus Status
        {
            get
            {
                if (Interlocked.Increment(ref _reads) == 2)
                    cancellation.Cancel();
                return CompleteRestorationIntentStatus.Current;
            }
        }

        public CancellationToken Revocation => CancellationToken.None;
    }
}
