using DotnetInspector.Queries.Definitions;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceSharePacketV3TransposerTests
{
    private const string RegistrationOnlyJson =
        """{"f":3,"t":[],"g":[],"r":[["p","Microsoft.Extensions."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""";

    private const string CompositeJson =
        """{"f":3,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"r":[["l",["p","system.text.json","10.0.0",["System.Text.Json","10.0.0.0",null,"cc7b13ffcd2ddd51"]]],["l",["t","DotNetRuntime",["System.Runtime","11.0.0.0",null,"b03f5f7f11d50a3a"]]],["e","ecosystem.platform",["System"],["system.runtime"],[["l",["p","system.text.json","10.0.0",["System.Text.Json","10.0.0.0",null,"cc7b13ffcd2ddd51"]]],["t","AspNetCore"],["p","Microsoft.Extensions."]]]],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""";

    [Fact]
    public void CompleteWorkspaceCapture_AuthorsFormat3FromExactResolvedState()
    {
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToCompleteWorkspacePacket(
                ResolvedWorkspace(
                    version: "10.0.0",
                    framework: "net10.0"),
                TestContext.Current.CancellationToken);

        Assert.True(projection.Succeeded);
        WorkspaceSharePacket packet =
            Assert.IsType<WorkspaceSharePacket>(projection.Packet);
        Assert.Equal(
            WorkspaceSharePacketCodec.CurrentFormatVersion,
            packet.FormatVersion);
        Assert.Equal(2, packet.ViewStates.Count);
        Assert.IsType<PortableSubjectRequest.Workspace>(
            packet.ViewStates[0].Subject);
        Assert.IsType<PortableSubjectRequest.Package>(
            packet.ViewStates[1].Subject);
        Assert.Null(packet.FocusedTabIndex);
        Assert.Equal(0, packet.SelectedContextIndex);
    }

    [Theory]
    [InlineData(null, "net10.0")]
    [InlineData("10.0.0", null)]
    public void CompleteWorkspaceCapture_RejectsFloatingCoordinates(
        string? version,
        string? framework)
    {
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToCompleteWorkspacePacket(
                ResolvedWorkspace(version, framework),
                TestContext.Current.CancellationToken);

        Assert.False(projection.Succeeded);
        Assert.Equal(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            projection.Failure?.Kind);
        Assert.Equal(
            "navigation.tabs[0].coordinate",
            projection.Failure?.Path);
    }

    [Theory]
    [InlineData(RegistrationOnlyJson)]
    [InlineData(CompositeJson)]
    public void Format3_PacketRecordsPacket_RoundTripsByteForByte(
        string json)
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);

        CommittedScenarioDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                packet,
                TestContext.Current.CancellationToken);
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                definitions,
                TestContext.Current.CancellationToken);

        Assert.True(projection.Succeeded);
        Assert.All(
            definitions.Records,
            record => Assert.Equal(
                InspectionDefinitionSchema.Version3,
                record.SchemaVersion));
        Assert.Equal(
            json,
            WorkspaceSharePacketCodec.SerializeJson(
                Assert.IsType<WorkspaceSharePacket>(projection.Packet)));
    }

    [Fact]
    public void Format3_RegistrationOnly_ProducesExactPeerGraph()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            RegistrationOnlyJson,
            TestContext.Current.CancellationToken);

        CommittedScenarioDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                packet,
                TestContext.Current.CancellationToken);

        Assert.Empty(definitions.Workspace!.Contexts);
        Assert.Single(definitions.Workspace.Registrations);
        Assert.Empty(definitions.Navigation!.Tabs);
        Assert.Null(definitions.Navigation.Focus);
        Assert.Single(definitions.View!.States);
        Assert.Null(definitions.Scenario.Context);
    }

    [Fact]
    public void Format3_PinnedPlatform_LowersToExactRestorationPlan()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            """
            {"f":3,"t":[[":Platform","10.0.10","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}
            """,
            TestContext.Current.CancellationToken);
        CommittedScenarioDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                packet,
                TestContext.Current.CancellationToken);

        WorkspacePlan plan =
            InspectionDefinitionRegistry
                .CreateCompleteRestorationWorkspacePlan(
                    definitions.Workspace!);
        var platform = Assert.IsType<
            WorkspaceMemberCoordinate.PlatformMember>(
                Assert.Single(Assert.Single(plan.Contexts).Members));

        Assert.Equal("runtime", platform.Family);
        Assert.Equal("10.0.10", platform.Version);
        Assert.Equal("net10.0", platform.Framework);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Format3_NonPortableRegistrations_AreNonProjectable(
        bool scanner)
    {
        WorkspaceRegistration registration = scanner
            ? new WorkspaceRegistration.Ecosystem(
                new WorkspaceEcosystemRegistrationDeclaration(
                    WorkspaceEcosystemRegistrationId.Create(
                        "ecosystem.scanner"),
                    ["Scanner"],
                    [],
                    [],
                    EcosystemIntegrationScanner.AspireBinding))
            : new WorkspaceRegistration.ExactLibrary(
                new ExactLibrarySourceCoordinate.Local(
                    Library("Local.Library")));
        CommittedScenarioDefinitionSet definitions =
            Definitions([registration]);

        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                definitions,
                TestContext.Current.CancellationToken);

        Assert.False(projection.Succeeded);
        Assert.Equal(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            projection.Failure?.Kind);
        Assert.StartsWith(
            "workspace.registrations[0]",
            projection.Failure?.Path,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format3_RegistrationLimit_IsNonProjectable()
    {
        WorkspaceRegistration[] registrations =
        [
            .. Enumerable.Range(
                0,
                WorkspaceSharePacketCodec.MaxRegistrations + 1)
                .Select(index =>
                    (WorkspaceRegistration)
                        new WorkspaceRegistration.PackagePrefix(
                            new PackagePrefixDeclaration(
                                $"Package{index}."))),
        ];

        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                Definitions(registrations),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            projection.Failure?.Kind);
        Assert.Equal(
            "workspace.registrations",
            projection.Failure?.Path);
    }

    private static CommittedScenarioDefinitionSet Definitions(
        IReadOnlyList<WorkspaceRegistration> registrations)
    {
        var workspace = new WorkspaceDefinition(
            InspectionDefinitionSchema.Version3,
            WorkspaceSharePacketTransposer.WorkspaceId,
            [],
            registrations: registrations);
        var navigation = new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version3,
            WorkspaceSharePacketTransposer.NavigationId,
            [],
            focus: null);
        var view = new CommittedViewDefinition(
            InspectionDefinitionSchema.Version3,
            WorkspaceSharePacketTransposer.ViewId,
            [
                new CommittedViewStateDefinition(
                    navigation: null,
                    subject: new PortableSubjectRequest.Workspace()),
            ]);
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version3,
            WorkspaceSharePacketTransposer.ScenarioId,
            workspace: workspace.Id,
            context: null,
            view: view.Id,
            navigation: navigation.Id);
        var registry = new InspectionDefinitionRegistry();
        registry.Add(workspace);
        registry.Add(navigation);
        registry.Add(view);
        registry.Add(scenario);
        return Assert.IsType<
            InspectionDefinitionScenarioPreparationResult.Version3>(
                registry.PreparePacketScenario(scenario.Id)).Definitions;
    }

    private static WorkspaceSharePacketDefinitionSet ResolvedWorkspace(
        string? version,
        string? framework)
    {
        var package =
            new DefinitionMemberCoordinate.PackageCoordinate(
                "System.Text.Json",
                version,
                framework);
        var workspace = new WorkspaceDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.WorkspaceId,
            [
                new WorkspaceContextDefinition(
                    "g0",
                    framework,
                    members: [package]),
            ]);
        var navigation = new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.NavigationId,
            [new NavigationTabDefinition("t0", coordinate: package)],
            "t0");
        var view = new ViewDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.ViewId);
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.ScenarioId,
            workspace: workspace.Id,
            context: "g0",
            view: view.Id,
            navigation: navigation.Id);
        return new(workspace, navigation, view, scenario);
    }

    private static ManagedMetadataIdentity.Assembly Library(string name) =>
        new(
            new AssemblyReferenceIdentity(
                name,
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));
}
