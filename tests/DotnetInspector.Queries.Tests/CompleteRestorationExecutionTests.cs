using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class CompleteRestorationExecutionTests
{
    [Fact]
    public async Task WorkspaceDefinition_PreparesOneExactUnpublishedActivation()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        InspectionDefinitionRegistry registry = WorkspaceOnlyRegistry(assembly);
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    registry,
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, assembly),
                TestContext.Current.CancellationToken);

        var activated =
            Assert.IsType<
                CompleteRestorationResult<InspectionWorkspace>.Activated>(
                    result);
        Assert.Same(host.Workspace, activated.Activation);
        Assert.Same(
            preparation.Plan.WorkspacePlan,
            activated.Workspace.Snapshot.Definition.Plan);
        Assert.Same(
            activated.Activation.Identity,
            activated.Workspace.Workspace);
        Assert.Single(activated.Workspace.Snapshot.Contexts);
        Assert.Empty(activated.Workspace.Snapshot.Scope.Packages);
        Assert.IsType<CompleteRestorationProjection.NonProjectable>(
            activated.Workspace.Projection);
        Assert.Equal(
            StructuralSubjectKind.Workspace,
            activated.Workspace.Snapshot.Navigation.State.Snapshot
                .ActiveSubject.Kind);

        InspectionWorkspaceCloseReport close =
            await activated.Activation.CloseAsync();
        Assert.True(close.Succeeded);
    }

    [Fact]
    public async Task SupersededDuringConstruction_ClosesWorkspaceAndReturnsNoActivation()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    WorkspaceOnlyRegistry(assembly),
                    "scenario",
                    authority));
        var host = new TestHost
        {
            BeforePreparation = () =>
                authority.Status =
                    CompleteRestorationIntentStatus.Superseded,
        };
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, assembly),
                TestContext.Current.CancellationToken);

        Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Superseded>(
                result);
        Assert.NotNull(host.CloseReport);
        Assert.True(host.CloseReport.Succeeded);
    }

    [Fact]
    public async Task HostUsingDifferentPlan_FailsAndClosesWorkspace()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    WorkspaceOnlyRegistry(assembly),
                    "scenario",
                    authority));
        var host = new TestHost
        {
            OverridePlan = WorkspacePlan.Empty,
        };
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, assembly),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Failed>(result);
        Assert.IsType<
            CompleteRestorationFailure.WorkspaceConstructionFailed>(
                failed.Failure);
        Assert.NotNull(host.CloseReport);
        Assert.True(host.CloseReport.Succeeded);
    }

    [Fact]
    public async Task SystemTextJsonVersion2_RestoresExactPackageAndNavigation()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version2PackageRegistry(),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        var activated =
            Assert.IsType<
                CompleteRestorationResult<InspectionWorkspace>.Activated>(
                    result);
        var resolved =
            Assert.IsType<CompleteRestorationResolvedState.Version2>(
                activated.Workspace.Snapshot.Resolved);
        CompleteRestorationResolvedViewState packageState =
            resolved.States[resolved.ActiveStateIndex!.Value];
        Assert.Equal(
            StructuralSubjectKind.Package,
            packageState.Initialization!.Subject!.Kind);
        Assert.Equal(
            StructuralSubjectKind.Package,
            activated.Workspace.Snapshot.Navigation.State.Snapshot
                .ActiveSubject.Kind);
        Assert.NotEmpty(
            activated.Workspace.Snapshot.Navigation.State.Snapshot.Libraries);

        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task CompatiblePackageTarget_RestoresSelectedImplementation()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version2PackageRegistryForFramework("net10.0"),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        WorkspacePackageDescriptor restored =
            Assert.Single(
                activated.Workspace.Snapshot.Scope.Packages)
                .Occurrence.Package;
        Assert.Equal("net10.0", restored.Coordinate.Framework);
        Assert.Equal("net9.0", restored.SelectedTargetFramework);
        Assert.Equal(
            StructuralSubjectKind.Package,
            activated.Workspace.Snapshot.Navigation.State.Snapshot
                .ActiveSubject.Kind);

        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task CompatibleRidPackageTarget_RestoresSelectedImplementation()
    {
        PackageFixture package = await CompatibleRidPackageAsync();
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version2PackageRegistryForFramework(
                        "net10.0",
                        "linux-x64"),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        WorkspacePackageDescriptor restored =
            Assert.Single(
                activated.Workspace.Snapshot.Scope.Packages)
                .Occurrence.Package;
        Assert.Equal("net10.0", restored.Coordinate.Framework);
        Assert.Equal("linux-x64", restored.Coordinate.RuntimeIdentifier);
        Assert.Equal("net9.0", restored.SelectedTargetFramework);

        WorkspaceDeclarationContextReceipt context =
            Assert.Single(activated.Workspace.Snapshot.Contexts);
        var provenance = Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(
            Assert.Single(context.Members).Selection);
        Assert.Equal("net9.0", provenance.Tfm);
        Assert.Equal("linux-x64", provenance.Rid);
        Assert.Equal(
            StructuralSubjectKind.Package,
            activated.Workspace.Snapshot.Navigation.State.Snapshot
                .ActiveSubject.Kind);

        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task NormalizedPlatformTarget_RetainsPackageRootAssociation()
    {
        const string framework = "net8.0-windows10.0.19041.0";
        PackageFixture package = await SystemTextJsonPackageAsync();
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version2PackageRegistryForFramework(framework),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        WorkspacePackageDescriptor restored =
            Assert.Single(
                activated.Workspace.Snapshot.Scope.Packages)
                .Occurrence.Package;
        Assert.Equal(framework, restored.Coordinate.Framework);
        Assert.Equal(
            StructuralSubjectKind.Package,
            activated.Workspace.Snapshot.Navigation.State.Snapshot
                .ActiveSubject.Kind);

        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task PacketV1_RetainsCanonicalPacketAfterExactRestoration()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        InspectionDefinitionRegistry registry = Version1PackageRegistry(
            new ViewDefinition(
                InspectionDefinitionSchema.Version1,
                "view",
                lens: "overview"));
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
            WorkspaceSharePacketTransposer.ToPacket(
                definitions,
                TestContext.Current.CancellationToken).Packet!;
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    encoded,
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        var activated =
            Assert.IsType<
                CompleteRestorationResult<InspectionWorkspace>.Activated>(
                    result);
        var projection =
            Assert.IsType<CompleteRestorationProjection.Projectable>(
                activated.Workspace.Projection);
        Assert.Equal(encoded, projection.CanonicalPacket);
        Assert.IsType<CompleteRestorationResolvedState.Legacy>(
            activated.Workspace.Snapshot.Resolved);

        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task PacketV1FloatingVersion_SelectsItsAcquiredOccurrence()
    {
        const string packageId = "floating.restore.fixture";
        const string listedVersion = "2.0.0";
        const string pinnedVersion = "1.0.0";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] nupkg = Archive(
            ("lib/net9.0/DotnetInspector.Queries.Tests.dll", assembly));
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            packageId,
            pinnedVersion,
            NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json"),
            new MemoryStream(nupkg, writable: false),
            TestContext.Current.CancellationToken);
        var packet = new WorkspaceSharePacket(
            [
                new WorkspaceShareTab(
                    WorkspaceShareSourceKind.Package,
                    packageId,
                    version: null,
                    framework: "net9.0",
                    runtimeIdentifier: null),
                new WorkspaceShareTab(
                    WorkspaceShareSourceKind.Package,
                    packageId,
                    pinnedVersion,
                    "net9.0",
                    runtimeIdentifier: null),
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
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    encoded,
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(
            new FloatingPackageHandler(
                packageId,
                listedVersion,
                nupkg));

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, store),
                TestContext.Current.CancellationToken);

        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        Assert.Equal(2, activated.Workspace.Snapshot.Scope.Packages.Length);
        var resolved =
            Assert.IsType<CompleteRestorationResolvedState.Legacy>(
                activated.Workspace.Snapshot.Resolved);
        var packageSubject =
            Assert.IsType<StructuralSubjectIdentity.PackageSubject>(
                resolved.Initialization.Subject);
        Assert.Equal(
            listedVersion,
            packageSubject.Descriptor.PackageVersion);
        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task DefinitionV1_ResolvesPackageOverviewFacet()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        InspectionDefinitionRegistry registry = Version1PackageRegistry(
            new ViewDefinition(
                InspectionDefinitionSchema.Version1,
                "view",
                lens: "overview"));
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    registry,
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        var activated =
            Assert.IsType<
                CompleteRestorationResult<InspectionWorkspace>.Activated>(
                    result);
        var legacy =
            Assert.IsType<CompleteRestorationResolvedState.Legacy>(
                activated.Workspace.Snapshot.Resolved);
        Assert.Equal(
            StructuralSubjectKind.Package,
            legacy.Initialization.Subject!.Kind);
        Assert.Equal(
            "package.overview",
            legacy.Initialization.Lens!.Facet.Value);
        Assert.Equal(
            StructuralSubjectKind.Package,
            activated.Workspace.Snapshot.Navigation.State.Snapshot
                .ActiveSubject.Kind);

        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task DefinitionV1_RetainsCompleteOrderedNavigationState()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version1MultipleStateRegistry(assembly),
                    "scenario",
                    authority));
        var recipe =
            Assert.IsType<CompleteRestorationRecipe.LegacyDirectPackage>(
                preparation.Plan.Recipe);
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(
                    client,
                    package.Store,
                    new StubEmbeddedContent(assembly)),
                TestContext.Current.CancellationToken);

        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        var legacy =
            Assert.IsType<CompleteRestorationResolvedState.Legacy>(
                activated.Workspace.Snapshot.Resolved);
        Assert.Same(recipe.Definitions, legacy.Definitions);
        Assert.Equal(1, legacy.ActiveStateIndex);
        Assert.Equal(4, legacy.States.Length);

        CompleteRestorationResolvedViewState workspaceState = legacy.States[0];
        Assert.Null(workspaceState.NavigationId);
        Assert.Equal(
            StructuralSubjectKind.Workspace,
            workspaceState.Initialization!.Subject!.Kind);

        CompleteRestorationResolvedViewState activeState = legacy.States[1];
        Assert.Same(activeState, legacy.ActiveState);
        Assert.Equal("first", activeState.NavigationId);
        Assert.Equal(
            StructuralSubjectKind.Package,
            activeState.Initialization!.Subject!.Kind);
        Assert.Equal(
            "net9.0",
            activeState.Initialization.Context!.Package.Descriptor
                .Coordinate.Framework);
        Assert.Equal(
            "package.overview",
            activeState.Initialization.Lens!.Facet.Value);

        CompleteRestorationResolvedViewState inactiveState = legacy.States[2];
        Assert.Equal("second", inactiveState.NavigationId);
        Assert.Null(inactiveState.Initialization!.Subject);
        Assert.Equal(
            "net8.0",
            inactiveState.Initialization.Context!.Package.Descriptor
                .Coordinate.Framework);
        Assert.Null(inactiveState.Initialization.Lens);

        CompleteRestorationResolvedViewState dormantState = legacy.States[3];
        Assert.Equal("fixture", dormantState.NavigationId);
        Assert.Null(dormantState.Initialization);

        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task PacketV1_RetainsCompleteOrderedPackageState()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        var packet = new WorkspaceSharePacket(
            [
                new WorkspaceShareTab(
                    WorkspaceShareSourceKind.Package,
                    "System.Text.Json",
                    "9.0.4",
                    "net9.0",
                    runtimeIdentifier: null),
                new WorkspaceShareTab(
                    WorkspaceShareSourceKind.Package,
                    "System.Text.Json",
                    "9.0.4",
                    "net8.0",
                    runtimeIdentifier: null),
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
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    encoded,
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        var legacy =
            Assert.IsType<CompleteRestorationResolvedState.Legacy>(
                activated.Workspace.Snapshot.Resolved);
        Assert.Equal(1, legacy.ActiveStateIndex);
        Assert.Equal(
            new string?[] { null, "t0", "t1" },
            legacy.States.Select(static state => state.NavigationId));
        Assert.Equal(
            StructuralSubjectKind.Package,
            legacy.ActiveState.Initialization!.Subject!.Kind);
        Assert.Null(legacy.States[2].Initialization!.Subject);
        Assert.Equal(
            "net8.0",
            legacy.States[2].Initialization!.Context!.Package.Descriptor
                .Coordinate.Framework);
        Assert.Equal(
            encoded,
            Assert.IsType<CompleteRestorationProjection.Projectable>(
                activated.Workspace.Projection).CanonicalPacket);

        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task RepeatedPackageAcrossContexts_RestoresOneOccurrence()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version2PackageRegistry(duplicateContexts: true),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        if (result
            is CompleteRestorationResult<InspectionWorkspace>.Failed failed)
        {
            Assert.Fail(
                $"{failed.Failure.GetType().Name}: "
                    + failed.Failure.Message
                    + (failed.Failure
                            is CompleteRestorationFailure.NavigationFailed
                                navigation
                        ? $" {navigation.Outcome}"
                        : ""));
        }
        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        Assert.Single(activated.Workspace.Snapshot.Scope.Packages);
        Assert.Equal(2, activated.Workspace.Snapshot.Contexts.Length);
        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task InheritedNavigationTarget_SelectsExactOccurrence()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version2InheritedTargetRegistry(),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        Assert.Equal(2, activated.Workspace.Snapshot.Scope.Packages.Length);
        var resolved =
            Assert.IsType<CompleteRestorationResolvedState.Version2>(
                activated.Workspace.Snapshot.Resolved);
        var packageSubject =
            Assert.IsType<StructuralSubjectIdentity.PackageSubject>(
                resolved.States[1].Initialization!.Subject);
        Assert.Equal(
            "net9.0",
            packageSubject.Descriptor.Coordinate.Framework);
        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task InheritedLegacyNavigationTarget_SelectsExactOccurrence()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version1InheritedTargetRegistry(),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        Assert.Equal(2, activated.Workspace.Snapshot.Scope.Packages.Length);
        var resolved =
            Assert.IsType<CompleteRestorationResolvedState.Legacy>(
                activated.Workspace.Snapshot.Resolved);
        var packageSubject =
            Assert.IsType<StructuralSubjectIdentity.PackageSubject>(
                resolved.Initialization.Subject);
        Assert.Equal(
            "net9.0",
            packageSubject.Descriptor.Coordinate.Framework);
        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task SubjectlessPackageState_UsesCoordinateRecommendationBasis()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version2PackageRegistry(
                        new CommittedViewStateDefinition("package")),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        if (result
            is CompleteRestorationResult<InspectionWorkspace>.Failed failed)
        {
            Assert.Fail(
                $"{failed.Failure.GetType().Name}: "
                    + failed.Failure.Message);
        }
        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        Assert.Equal(
            StructuralSubjectKind.Library,
            activated.Workspace.Snapshot.Navigation.State.Snapshot
                .ActiveSubject.Kind);
        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task InactiveUnknownFacet_FailsBeforeActivation()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version2PackageRegistry(
                        new CommittedViewStateDefinition(
                            "package",
                            new PortableSubjectRequest.Package(),
                            new PortableRetainedSubjectContext.Package(),
                            facet: "package.unknown"),
                        focus: null),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Failed>(result);
        Assert.IsType<CompleteRestorationFailure.SelectorResolutionFailed>(
            failed.Failure);
        Assert.True(host.CloseReport!.Succeeded);
    }

    [Fact]
    public async Task ProjectionFailure_ClosesWorkspaceAndReturnsTypedFailure()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    WorkspaceOnlyRegistry(assembly),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());
        CompleteRestorationExecutionOptions options =
            Options(client, assembly) with
            {
                Projection = _ =>
                    new CompleteRestorationProjectionResult.Failed(
                        "Injected projection failure."),
            };

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                options,
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Failed>(result);
        Assert.IsType<CompleteRestorationFailure.ProjectionFailed>(
            failed.Failure);
        Assert.True(host.CloseReport!.Succeeded);
    }

    [Fact]
    public async Task StalePreparedFailure_ReturnsSuperseded()
    {
        var authority = new TestIntentAuthority();
        CompleteRestorationPreparationResult preparation =
            WorkspaceDefinitionConsumer.PrepareRestoration(
                "not-a-packet",
                authority);
        authority.Status = CompleteRestorationIntentStatus.Superseded;
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                new NeverConstructHost(),
                Options(client, []),
                TestContext.Current.CancellationToken);

        Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Superseded>(
                result);
    }

    [Fact]
    public void LegacyMemberKeyWithoutStableSelector_FailsBeforeConstruction()
    {
        InspectionDefinitionRegistry registry = Version1PackageRegistry(
            new ViewDefinition(
                InspectionDefinitionSchema.Version1,
                "view",
                lens: "api",
                type: "System.Text.Json.JsonSerializer",
                memberKey: "method:Serialize"));
        var authority = new TestIntentAuthority();
        var failed =
            Assert.IsType<CompleteRestorationPreparationResult.Failed>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    registry,
                    "scenario",
                    authority));

        Assert.IsType<CompleteRestorationFailure.LegacyLoweringFailed>(
            failed.Failure);
    }

    [Theory]
    [InlineData(CompleteRestorationIntentStatus.Cancelled)]
    [InlineData(CompleteRestorationIntentStatus.Expired)]
    [InlineData(CompleteRestorationIntentStatus.Revoked)]
    public async Task UnavailableIntentBeforeExecution_DoesNotConstruct(
        CompleteRestorationIntentStatus status)
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        CompleteRestorationPreparationResult preparation =
            WorkspaceDefinitionConsumer.PrepareRestoration(
                WorkspaceOnlyRegistry(assembly),
                "scenario",
                authority);
        authority.Status = status;
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                new NeverConstructHost(),
                Options(client, assembly),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Failed>(result);
        Assert.Equal(
            status,
            Assert.IsType<
                CompleteRestorationFailure.AuthorityUnavailable>(
                    failed.Failure).Status);
    }

    [Fact]
    public async Task RevocationBeforeExecution_DoesNotConstruct()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        CompleteRestorationPreparationResult preparation =
            WorkspaceDefinitionConsumer.PrepareRestoration(
                WorkspaceOnlyRegistry(assembly),
                "scenario",
                authority);
        authority.Revoke();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                new NeverConstructHost(),
                Options(client, assembly),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Failed>(result);
        Assert.IsType<CompleteRestorationFailure.Cancelled>(failed.Failure);
    }

    [Theory]
    [InlineData(CompleteRestorationIntentStatus.Expired)]
    [InlineData(CompleteRestorationIntentStatus.Revoked)]
    public async Task AuthorityLossDuringWork_PreservesExactStatus(
        CompleteRestorationIntentStatus status)
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    WorkspaceOnlyRegistry(assembly),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());
        CompleteRestorationExecutionOptions options =
            Options(client, assembly) with
            {
                Projection = _ =>
                {
                    authority.Status = status;
                    authority.Revoke();
                    throw new OperationCanceledException(authority.Revocation);
                },
            };

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                options,
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Failed>(result);
        Assert.Equal(
            status,
            Assert.IsType<
                CompleteRestorationFailure.AuthorityUnavailable>(
                    failed.Failure).Status);
        Assert.True(host.CloseReport!.Succeeded);
    }

    [Fact]
    public async Task CancellationDuringWork_RemainsCancelled()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    WorkspaceOnlyRegistry(assembly),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());
        CompleteRestorationExecutionOptions options =
            Options(client, assembly) with
            {
                Projection = _ => throw new OperationCanceledException(),
            };

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                options,
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Failed>(result);
        Assert.IsType<CompleteRestorationFailure.Cancelled>(failed.Failure);
        Assert.True(host.CloseReport!.Succeeded);
    }

    [Fact]
    public async Task ContextLoadFailure_ClosesWorkspace()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    WorkspaceOnlyRegistry(
                        assembly,
                        "Wrong.Assembly.Name"),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, assembly),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Failed>(result);
        Assert.IsType<CompleteRestorationFailure.ContextLoadFailed>(
            failed.Failure);
        Assert.True(host.CloseReport!.Succeeded);
    }

    [Fact]
    public async Task ScopeDeadlineFailure_ClosesWorkspace()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version2PackageRegistry(),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());
        CompleteRestorationExecutionOptions options =
            Options(client, package.Store) with
            {
                ScopeDeadline = DateTimeOffset.UtcNow.AddMinutes(-1),
            };

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                options,
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Failed>(result);
        Assert.IsType<CompleteRestorationFailure.ScopeMutationFailed>(
            failed.Failure);
        Assert.True(host.CloseReport!.Succeeded);
    }

    [Fact]
    public async Task SupersededDuringProjection_ClosesWorkspace()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    WorkspaceOnlyRegistry(assembly),
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());
        CompleteRestorationExecutionOptions options =
            Options(client, assembly) with
            {
                Projection = request =>
                {
                    authority.Status =
                        CompleteRestorationIntentStatus.Superseded;
                    return CompleteRestorationProjections.Classify(request);
                },
            };

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                options,
                TestContext.Current.CancellationToken);

        Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Superseded>(
                result);
        Assert.True(host.CloseReport!.Succeeded);
    }

    [Fact]
    public async Task ReconstructedHostActivation_IsRejected()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    WorkspaceOnlyRegistry(assembly),
                    "scenario",
                    authority));
        var host = new ReconstructingHost();
        using var client = new HttpClient(new RejectingHandler());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, assembly),
                TestContext.Current.CancellationToken));

        Assert.True(host.CloseReport!.Succeeded);
    }

    [Fact]
    public async Task EmptyVersion1View_UsesCoordinateRecommendation()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        InspectionDefinitionRegistry registry = Version1PackageRegistry(
            new ViewDefinition(
                InspectionDefinitionSchema.Version1,
                "view"));
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    registry,
                    "scenario",
                    authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        var legacy =
            Assert.IsType<CompleteRestorationResolvedState.Legacy>(
                activated.Workspace.Snapshot.Resolved);
        Assert.Null(legacy.Initialization.Subject);
        Assert.Equal(
            StructuralSubjectKind.Library,
            activated.Workspace.Snapshot.Navigation.State.Snapshot
                .ActiveSubject.Kind);
        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task HostReturn_IsActivationLinearizationPoint()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    WorkspaceOnlyRegistry(assembly),
                    "scenario",
                    authority));
        var host = new TestHost
        {
            BeforeActivatedReturn = () =>
                authority.Status =
                    CompleteRestorationIntentStatus.Superseded,
        };
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, assembly),
                TestContext.Current.CancellationToken);

        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task CleanupFailure_RemainsVisibleAfterSupersession()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    WorkspaceOnlyRegistry(assembly),
                    "scenario",
                    authority));
        var host = new CleanupFailureHost(authority);
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                host,
                Options(client, assembly),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Failed>(result);
        Assert.IsType<CompleteRestorationFailure.CleanupFailed>(
            failed.Failure);
        Assert.True(host.CloseReport!.Succeeded);
    }

    private static CompleteRestorationExecutionOptions Options(
        HttpClient client,
        byte[] assembly) =>
        Options(
            client,
            new InMemoryPackageStore(),
            new StubEmbeddedContent(assembly));

    private static CompleteRestorationExecutionOptions Options(
        HttpClient client,
        IPackageStore store,
        IEmbeddedContentProvider? embedded = null)
    {
        ViewFacetRegistry facets = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot available =
            NavigationSnapshotTestData.AllAvailable(facets);
        return new()
        {
            ContextLoad = new WorkspaceContextLoadOptions
            {
                HttpClient = client,
                SourceAuthorization =
                    new UniformPackageSourceAuthorization(
                        [new PackageSource(
                            "nuget.org",
                            "https://api.nuget.org/v3/index.json")]),
                PackageStore = store,
                EmbeddedContent = embedded,
            },
            ScopeDeadline = DateTimeOffset.UtcNow.AddMinutes(1),
            Facets = facets,
            FacetAvailability = (_, _) => available,
        };
    }

    private static InspectionDefinitionRegistry WorkspaceOnlyRegistry(
        byte[] assembly,
        string assemblyName = "DotnetInspector.Queries.Tests")
    {
        string digest = Convert.ToHexString(
            SHA256.HashData(assembly)).ToLowerInvariant();
        var embedded =
            new DefinitionMemberCoordinate.EmbeddedCoordinate(
                "queries-tests",
                digest,
                assemblyName);
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version2,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    members:
                    [
                        embedded,
                    ]),
            ]));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [new NavigationTabDefinition("fixture", coordinate: embedded)],
            focus: null));
        registry.Add(new CommittedViewDefinition(
            InspectionDefinitionSchema.Version2,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace(),
                    facet: "workspace.overview"),
                new CommittedViewStateDefinition("fixture"),
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

    private static InspectionDefinitionRegistry Version2PackageRegistry(
        CommittedViewStateDefinition? packageState = null,
        bool duplicateContexts = false,
        string? focus = "package")
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(PackageWorkspace(
            InspectionDefinitionSchema.Version2,
            duplicateContexts));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [new NavigationTabDefinition("package", coordinate: Package())],
            focus));
        registry.Add(new CommittedViewDefinition(
            InspectionDefinitionSchema.Version2,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                packageState
                    ?? new CommittedViewStateDefinition(
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

    private static InspectionDefinitionRegistry
        Version2PackageRegistryForFramework(
            string framework,
            string? runtimeIdentifier = null)
    {
        var package = new DefinitionMemberCoordinate.PackageCoordinate(
            "System.Text.Json",
            "9.0.4",
            framework,
            runtimeIdentifier);
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version2,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    framework,
                    runtimeIdentifier,
                    members: [package]),
            ]));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [
                new NavigationTabDefinition(
                    "package",
                    coordinate: package,
                    framework: framework,
                    runtimeIdentifier: runtimeIdentifier),
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

    private static InspectionDefinitionRegistry
        Version2InheritedTargetRegistry()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version2,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    framework: "net9.0",
                    members:
                    [
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "System.Text.Json",
                            "9.0.4"),
                    ]),
                new WorkspaceContextDefinition(
                    "second",
                    framework: "net8.0",
                    members:
                    [
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "System.Text.Json",
                            "9.0.4"),
                    ]),
            ]));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [
                new NavigationTabDefinition(
                    "package",
                    coordinate:
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "System.Text.Json",
                            "9.0.4"),
                    framework: "net9.0"),
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

    private static InspectionDefinitionRegistry
        Version1InheritedTargetRegistry()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version1,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    framework: "net9.0",
                    members:
                    [
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "System.Text.Json",
                            "9.0.4"),
                    ]),
                new WorkspaceContextDefinition(
                    "second",
                    framework: "net8.0",
                    members:
                    [
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "System.Text.Json",
                            "9.0.4"),
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
                            "System.Text.Json",
                            "9.0.4"),
                    framework: "net9.0"),
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
        return registry;
    }

    private static InspectionDefinitionRegistry Version1PackageRegistry(
        ViewDefinition view)
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(PackageWorkspace(
            InspectionDefinitionSchema.Version1));
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

    private static InspectionDefinitionRegistry Version1MultipleStateRegistry(
        byte[] assembly)
    {
        string digest = Convert.ToHexString(
            SHA256.HashData(assembly)).ToLowerInvariant();
        var embedded =
            new DefinitionMemberCoordinate.EmbeddedCoordinate(
                "queries-tests",
                digest,
                "DotnetInspector.Queries.Tests");
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version1,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "first-context",
                    framework: "net9.0",
                    members:
                    [
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "System.Text.Json",
                            "9.0.4"),
                    ]),
                new WorkspaceContextDefinition(
                    "second-context",
                    framework: "net8.0",
                    members:
                    [
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "System.Text.Json",
                            "9.0.4"),
                    ]),
                new WorkspaceContextDefinition(
                    "fixture-context",
                    members: [embedded]),
            ]));
        registry.Add(new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            "navigation",
            [
                new NavigationTabDefinition(
                    "first",
                    coordinate:
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "System.Text.Json",
                            "9.0.4"),
                    framework: "net9.0"),
                new NavigationTabDefinition(
                    "second",
                    coordinate:
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "System.Text.Json",
                            "9.0.4"),
                    framework: "net8.0"),
                new NavigationTabDefinition("fixture", coordinate: embedded),
            ],
            "first"));
        registry.Add(new ViewDefinition(
            InspectionDefinitionSchema.Version1,
            "view",
            lens: "overview"));
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            "scenario",
            workspace: "workspace",
            context: "first-context",
            view: "view",
            navigation: "navigation"));
        return registry;
    }

    private static WorkspaceDefinition PackageWorkspace(
        int schemaVersion,
        bool duplicateContexts = false) =>
        new(
            schemaVersion,
            "workspace",
            duplicateContexts
                ?
                [
                    new WorkspaceContextDefinition(
                        "context",
                        framework: "net9.0",
                        members: [Package()]),
                    new WorkspaceContextDefinition(
                        "second",
                        framework: "net9.0",
                        members: [Package()]),
                ]
                :
                [
                    new WorkspaceContextDefinition(
                        "context",
                        framework: "net9.0",
                        members: [Package()]),
                ]);

    private static DefinitionMemberCoordinate.PackageCoordinate Package() =>
        new("System.Text.Json", "9.0.4", "net9.0");

    private static async Task<PackageFixture> SystemTextJsonPackageAsync()
    {
        const string url = "https://api.nuget.org/v3/index.json";
        string path = Path.Combine(
            FindRepositoryRoot(),
            "fixtures",
            "services",
            "signatures",
            "system.text.json.9.0.4.nupkg");
        byte[] content = await File.ReadAllBytesAsync(
            path,
            TestContext.Current.CancellationToken);
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            "System.Text.Json",
            "9.0.4",
            NuGetCache.GetSourceKey(url),
            new MemoryStream(content, writable: false),
            TestContext.Current.CancellationToken);
        return new(store);
    }

    private static async Task<PackageFixture> CompatibleRidPackageAsync()
    {
        const string url = "https://api.nuget.org/v3/index.json";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        string assemblyName =
            Path.GetFileName(
                typeof(CompleteRestorationExecutionTests).Assembly.Location);
        byte[] content = Archive(
            ($"lib/net8.0/{assemblyName}", assembly),
            ($"ref/net9.0/{assemblyName}", assembly),
            ($"runtimes/linux-x64/lib/net9.0/{assemblyName}", assembly));
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            "System.Text.Json",
            "9.0.4",
            NuGetCache.GetSourceKey(url),
            new MemoryStream(content, writable: false),
            TestContext.Current.CancellationToken);
        return new(store);
    }

    private static byte[] Archive(
        params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(path).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(
                Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root.");
    }

    private sealed class TestHost :
        ICompleteRestorationHost<InspectionWorkspace>
    {
        public Action? BeforePreparation { get; init; }

        public Action? BeforeActivatedReturn { get; init; }

        public WorkspacePlan? OverridePlan { get; init; }

        public InspectionWorkspace? Workspace { get; private set; }

        public InspectionWorkspaceCloseReport? CloseReport { get; private set; }

        public async ValueTask<
            CompleteRestorationHostResult<InspectionWorkspace>>
            ConstructAsync(
                ICompleteRestorationIntentAuthority authority,
                CompleteRestorationPlan plan,
                CompleteWorkspacePreparationCallback prepare,
                CancellationToken cancellationToken = default)
        {
            Workspace = new InspectionWorkspace(
                OverridePlan ?? plan.WorkspacePlan);
            BeforePreparation?.Invoke();
            CompleteWorkspacePreparationResult result;
            try
            {
                result = await prepare(
                    Workspace,
                    authority.Revocation);
            }
            catch
            {
                CloseReport = await Workspace.CloseAsync();
                throw;
            }
            if (result
                is CompleteWorkspacePreparationResult.Prepared prepared
                && authority.Status
                    is CompleteRestorationIntentStatus.Current
                && !authority.Revocation.IsCancellationRequested)
            {
                BeforeActivatedReturn?.Invoke();
                return new CompleteRestorationHostResult<InspectionWorkspace>
                    .Activated(Workspace, prepared.Activation);
            }

            CloseReport = await Workspace.CloseAsync();
            if (!CloseReport.Succeeded)
            {
                return new CompleteRestorationHostResult<InspectionWorkspace>
                    .Failed(
                        new CompleteRestorationFailure.CleanupFailed(
                            "The test host could not close the unpublished "
                                + "Workspace."));
            }

            return result switch
            {
                CompleteWorkspacePreparationResult.Failed failed =>
                    new CompleteRestorationHostResult<InspectionWorkspace>
                        .Failed(failed.Failure),
                CompleteWorkspacePreparationResult.Superseded =>
                    new CompleteRestorationHostResult<InspectionWorkspace>
                        .Superseded(),
                CompleteWorkspacePreparationResult.Prepared =>
                    new CompleteRestorationHostResult<InspectionWorkspace>
                        .Superseded(),
                _ => throw new InvalidOperationException(
                    "Unknown Workspace preparation result."),
            };
        }
    }

    private sealed class FloatingPackageHandler(
        string packageId,
        string listedVersion,
        byte[] nupkg) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string id = packageId.ToLowerInvariant();
            string url = request.RequestUri!.ToString();
            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{id}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                return Json($$"""{"versions":["1.0.0","{{listedVersion}}"]}""");
            }
            if (url.Equals(
                $"https://api.nuget.org/v3/registration5-gz-semver2/{id}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                return Json(
                    $$$"""
                    {"items":[{"items":[
                      {"catalogEntry":{"version":"1.0.0","listed":true}},
                      {"catalogEntry":{"version":"{{{listedVersion}}}","listed":true}}
                    ]}]}
                    """);
            }
            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{id}/{listedVersion}/{id}.{listedVersion}.nupkg",
                StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nupkg),
                    });
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));

            static Task<HttpResponseMessage> Json(string body) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
        }
    }

    private sealed class TestIntentAuthority :
        ICompleteRestorationIntentAuthority
    {
        readonly CancellationTokenSource _revocation = new();

        public CompleteRestorationIntentIdentity Identity { get; } = new();

        public CompleteRestorationIntentStatus Status { get; set; } =
            CompleteRestorationIntentStatus.Current;

        public CancellationToken Revocation => _revocation.Token;

        public void Revoke() => _revocation.Cancel();
    }

    private sealed class NeverConstructHost :
        ICompleteRestorationHost<InspectionWorkspace>
    {
        public ValueTask<CompleteRestorationHostResult<InspectionWorkspace>>
            ConstructAsync(
                ICompleteRestorationIntentAuthority authority,
                CompleteRestorationPlan plan,
                CompleteWorkspacePreparationCallback prepare,
                CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Stale prepared results must not construct a Workspace.");
    }

    private sealed class ReconstructingHost :
        ICompleteRestorationHost<InspectionWorkspace>
    {
        public InspectionWorkspaceCloseReport? CloseReport { get; private set; }

        public async ValueTask<
            CompleteRestorationHostResult<InspectionWorkspace>>
            ConstructAsync(
                ICompleteRestorationIntentAuthority authority,
                CompleteRestorationPlan plan,
                CompleteWorkspacePreparationCallback prepare,
                CancellationToken cancellationToken = default)
        {
            var workspace = new InspectionWorkspace(plan.WorkspacePlan);
            CompleteWorkspacePreparationResult result =
                await prepare(workspace, authority.Revocation);
            var prepared =
                Assert.IsType<
                    CompleteWorkspacePreparationResult.Prepared>(result);
            var reconstructed = new CompleteWorkspaceActivation(
                prepared.Activation.Intent,
                prepared.Activation.Request,
                prepared.Activation.Workspace,
                prepared.Activation.Snapshot,
                prepared.Activation.Projection);
            CloseReport = await workspace.CloseAsync();
            return new CompleteRestorationHostResult<InspectionWorkspace>
                .Activated(workspace, reconstructed);
        }
    }

    private sealed class CleanupFailureHost(TestIntentAuthority authority) :
        ICompleteRestorationHost<InspectionWorkspace>
    {
        public InspectionWorkspaceCloseReport? CloseReport { get; private set; }

        public async ValueTask<
            CompleteRestorationHostResult<InspectionWorkspace>>
            ConstructAsync(
                ICompleteRestorationIntentAuthority ignoredAuthority,
                CompleteRestorationPlan plan,
                CompleteWorkspacePreparationCallback prepare,
                CancellationToken cancellationToken = default)
        {
            var workspace = new InspectionWorkspace(plan.WorkspacePlan);
            CompleteWorkspacePreparationResult result =
                await prepare(workspace, authority.Revocation);
            Assert.IsType<CompleteWorkspacePreparationResult.Prepared>(result);
            authority.Status =
                CompleteRestorationIntentStatus.Superseded;
            CloseReport = await workspace.CloseAsync();
            return new CompleteRestorationHostResult<InspectionWorkspace>
                .Failed(
                    new CompleteRestorationFailure.CleanupFailed(
                        "Injected cleanup settlement failure."));
        }
    }

    private sealed class StubEmbeddedContent(byte[] content)
        : IEmbeddedContentProvider
    {
        public bool TryOpenContent(
            string contentRef,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Stream? stream)
        {
            stream = new MemoryStream(content, writable: false);
            return true;
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The embedded fixture must not use the network.");
    }

    private sealed record PackageFixture(IPackageStore Store);
}
