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

    private const string QueryOnlyJson =
        """{"f":3,"t":[],"g":[],"r":[],"a":null,"x":null,"q":[["package-query/v1",{"t":[["depends","eq","Microsoft.Extensions.DependencyInjection"],["prefix","eq","Microsoft.Extensions."],["prerelease","eq","stable"]],"b":[["candidates",200]]}]],"v":[{"t":null,"u":{"k":"workspace"},"q":[0]}]}""";

    [Fact]
    public void CompleteWorkspaceCapture_AuthorsFormat3FromExactResolvedState()
    {
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToCompleteWorkspacePacket(
                ResolvedWorkspace(
                    version: "10.0.0",
                    framework: "net10.0",
                    runtimeIdentifier: "linux-x64",
                    platformSubscription: ":Platform@10.0.10",
                    includeInactivePackage: true,
                    focusId: "t1",
                    selectedContextName: "g2"),
                TestContext.Current.CancellationToken);

        Assert.True(projection.Succeeded);
        WorkspaceSharePacket packet =
            Assert.IsType<WorkspaceSharePacket>(projection.Packet);
        Assert.Equal(
            WorkspaceSharePacketCodec.CurrentFormatVersion,
            packet.FormatVersion);
        Assert.Equal(4, packet.ViewStates.Count);
        Assert.IsType<PortableSubjectRequest.Workspace>(
            packet.ViewStates[0].Subject);
        Assert.Null(packet.ViewStates[1].Subject);
        Assert.IsType<PortableSubjectRequest.Workspace>(
            packet.ViewStates[2].Subject);
        Assert.Null(packet.ViewStates[2].Context);
        Assert.IsType<PortableSubjectRequest.Package>(
            packet.ViewStates[3].Subject);
        Assert.IsType<PortableRetainedSubjectContext.Package>(
            packet.ViewStates[3].Context);
        Assert.Equal(1, packet.FocusedTabIndex);
        Assert.Equal(2, packet.SelectedContextIndex);

        WorkspaceShareTab platform = packet.Tabs[0];
        Assert.Equal(WorkspaceShareSourceKind.Group, platform.SourceKind);
        Assert.Equal(":Platform", platform.Source);
        Assert.Equal("10.0.10", platform.Version);
        Assert.Equal("net10.0", platform.Framework);
        Assert.Equal("linux-x64", platform.RuntimeIdentifier);

        WorkspaceShareTab package = packet.Tabs[1];
        Assert.Equal(WorkspaceShareSourceKind.Package, package.SourceKind);
        Assert.Equal("System.Text.Json", package.Source);
        Assert.Equal("10.0.0", package.Version);
        Assert.Equal("net10.0", package.Framework);
        Assert.Equal("linux-x64", package.RuntimeIdentifier);
        Assert.Equal([1], packet.Contexts[1].TabIndexes);

        WorkspaceShareTab inactivePackage = packet.Tabs[2];
        Assert.Equal(
            "Microsoft.Extensions.Logging.Abstractions",
            inactivePackage.Source);
        Assert.Equal("10.0.0", inactivePackage.Version);
        Assert.Equal("net10.0", inactivePackage.Framework);
        Assert.Equal("linux-x64", inactivePackage.RuntimeIdentifier);
        Assert.Equal([2], packet.Contexts[2].TabIndexes);
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

    [Fact]
    public void CompleteWorkspaceCapture_RejectsFloatingGroup()
    {
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToCompleteWorkspacePacket(
                ResolvedWorkspace(
                    version: "10.0.0",
                    framework: "net10.0",
                    platformSubscription: ":Platform"),
                TestContext.Current.CancellationToken);

        Assert.False(projection.Succeeded);
        Assert.Equal(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            projection.Failure?.Kind);
        Assert.Equal(
            "navigation.tabs[0].subscribe",
            projection.Failure?.Path);
    }

    [Fact]
    public void CompleteWorkspaceCapture_RejectsNonRootView()
    {
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToCompleteWorkspacePacket(
                ResolvedWorkspace(
                    version: "10.0.0",
                    framework: "net10.0",
                    view: new ViewDefinition(
                        InspectionDefinitionSchema.Version1,
                        WorkspaceSharePacketTransposer.ViewId,
                        type: "System.Text.Json.JsonSerializer")),
                TestContext.Current.CancellationToken);

        Assert.False(projection.Succeeded);
        Assert.Equal(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            projection.Failure?.Kind);
        Assert.Equal("view", projection.Failure?.Path);
    }

    [Fact]
    public void CompleteWorkspaceCapture_RejectsNonPackageFocus()
    {
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToCompleteWorkspacePacket(
                ResolvedWorkspace(
                    version: "10.0.0",
                    framework: "net10.0",
                    platformSubscription: ":Platform@10.0.10",
                    focusId: "t0"),
                TestContext.Current.CancellationToken);

        Assert.False(projection.Succeeded);
        Assert.Equal(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            projection.Failure?.Kind);
        Assert.Equal("navigation.focus", projection.Failure?.Path);
    }

    [Theory]
    [InlineData(RegistrationOnlyJson)]
    [InlineData(CompositeJson)]
    [InlineData(QueryOnlyJson)]
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
    public void Format3_QueryOnly_BindsTypedPackageQueryPlan()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            QueryOnlyJson,
            TestContext.Current.CancellationToken);

        CommittedScenarioDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                packet,
                TestContext.Current.CancellationToken);

        CommittedQueryDefinition query = Assert.Single(definitions.Queries);
        Assert.Equal("q0", query.Id);
        Assert.Equal(PackageQuery.VocabularyIdentity, query.QueryId);
        BoundCommittedQuery<PackageQueryPlan> binding =
            Assert.IsType<BoundCommittedQuery<PackageQueryPlan>>(
                Assert.Single(definitions.QueryBindings));
        Assert.Same(query, binding.Definition);
        Assert.Null(binding.Attachment.SubjectKind);
        Assert.Null(binding.Attachment.FacetId);
        Assert.Null(binding.Attachment.StateCoordinate);
        Assert.Null(binding.Attachment.SelectedContext);
        Assert.Empty(binding.Attachment.StateLibraryScope);
        Assert.Equal("Microsoft.Extensions.", binding.Plan.Prefix.ToString());
        Assert.Equal(200, binding.Plan.MaximumCandidates);
        Assert.Contains(
            binding.Plan.Terms,
            term => term.Key == PackageQuery.DependsTermKey);
    }

    [Fact]
    public void Format3_QueryOnly_UnknownVocabularyFailsAfterPacketDecode()
    {
        const string json =
            """{"f":3,"t":[],"g":[],"r":[],"a":null,"x":null,"q":[["unknown-query/v1",{}]],"v":[{"t":null,"u":{"k":"workspace"},"q":[0]}]}""";
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);

        InspectionDefinitionException exception =
            Assert.Throws<InspectionDefinitionException>(
                () => WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                    packet,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "uses unknown vocabulary 'unknown-query/v1'",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        """{"f":3,"t":[],"g":[],"r":[],"a":null,"x":null,"q":[["package-query/v1",{"t":[["prefix","eq","P."],["prerelease","eq","stable"]],"b":[["candidates",10]]}]],"v":[{"t":null,"u":{"k":"workspace"},"f":"workspace.overview","q":[0]}]}""")]
    [InlineData(
        """{"f":3,"t":[],"g":[],"r":[],"a":null,"x":null,"q":[["package-query/v1",{"t":[["prefix","eq","P."],["prerelease","eq","stable"]],"b":[["candidates",10]]}],["package-query/v1",{"t":[["prefix","eq","Q."],["prerelease","eq","stable"]],"b":[["candidates",10]]}]],"v":[{"t":null,"u":{"k":"workspace"},"q":[0,1]}]}""")]
    public void Format3_QueryOnly_RejectsForbiddenMixtures(string json)
    {
        WorkspaceSharePacketException packetFailure =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    json,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceSharePacketFailureKind.InvalidShape,
            packetFailure.Kind);
    }

    [Fact]
    public void Format3_CoordinateFreeQuery_CannotMixWithRegistration()
    {
        const string json =
            """{"f":3,"t":[],"g":[],"r":[["p","P."]],"a":null,"x":null,"q":[["package-query/v1",{"t":[["prefix","eq","P."],["prerelease","eq","stable"]],"b":[["candidates",10]]}]],"v":[{"t":null,"u":{"k":"workspace"},"q":[0]}]}""";
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);

        InspectionDefinitionException exception =
            Assert.Throws<InspectionDefinitionException>(
                () => WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                    packet,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "cannot be mixed with Workspace context or registration state",
            exception.Message,
            StringComparison.Ordinal);
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
        string? framework,
        string? runtimeIdentifier = null,
        string? platformSubscription = null,
        ViewDefinition? view = null,
        bool includeInactivePackage = false,
        string? focusId = null,
        string? selectedContextName = null)
    {
        var package =
            new DefinitionMemberCoordinate.PackageCoordinate(
                "System.Text.Json",
                version,
                framework,
                runtimeIdentifier);
        var contexts = new List<WorkspaceContextDefinition>();
        var tabs = new List<NavigationTabDefinition>();
        if (platformSubscription is not null)
        {
            contexts.Add(
                new WorkspaceContextDefinition(
                    $"g{contexts.Count}",
                    framework,
                    runtimeIdentifier,
                    subscribe: platformSubscription));
            tabs.Add(
                new NavigationTabDefinition(
                    $"t{tabs.Count}",
                    subscribe: platformSubscription,
                    framework: framework,
                    runtimeIdentifier: runtimeIdentifier));
        }

        string packageContext = $"g{contexts.Count}";
        string packageTab = $"t{tabs.Count}";
        contexts.Add(
            new WorkspaceContextDefinition(
                packageContext,
                framework,
                runtimeIdentifier,
                members: [package]));
        tabs.Add(
            new NavigationTabDefinition(
                packageTab,
                coordinate: package));

        if (includeInactivePackage)
        {
            var inactivePackage =
                new DefinitionMemberCoordinate.PackageCoordinate(
                    "Microsoft.Extensions.Logging.Abstractions",
                    "10.0.0",
                    framework,
                    runtimeIdentifier);
            contexts.Add(
                new WorkspaceContextDefinition(
                    $"g{contexts.Count}",
                    framework,
                    runtimeIdentifier,
                    members: [inactivePackage]));
            tabs.Add(
                new NavigationTabDefinition(
                    $"t{tabs.Count}",
                    coordinate: inactivePackage));
        }

        var workspace = new WorkspaceDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.WorkspaceId,
            contexts);
        var navigation = new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.NavigationId,
            tabs,
            focusId ?? packageTab);
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.ScenarioId,
            workspace: workspace.Id,
            context: selectedContextName ?? packageContext,
            view: WorkspaceSharePacketTransposer.ViewId,
            navigation: navigation.Id);
        return new(
            workspace,
            navigation,
            view
                ?? new ViewDefinition(
                    InspectionDefinitionSchema.Version1,
                    WorkspaceSharePacketTransposer.ViewId),
            scenario);
    }

    private static ManagedMetadataIdentity.Assembly Library(string name) =>
        new(
            new AssemblyReferenceIdentity(
                name,
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));
}
