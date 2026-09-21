using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class CompleteRestorationExecutionTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task RegistrationOnly_RestoresWithoutAcquisition(int version)
    {
        string json =
            $$$"""{"f":{{{version}}},"t":[],"g":[],"r":[["p","Microsoft.Extensions."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""";
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CompleteRestorationExecutionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
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
                Options(client, assembly),
                TestContext.Current.CancellationToken);

        var activated =
            Assert.IsType<
                CompleteRestorationResult<InspectionWorkspace>.Activated>(
                    result);
        Assert.Empty(activated.Workspace.Snapshot.Contexts);
        Assert.Empty(activated.Workspace.Snapshot.Scope.Packages);
        Assert.Single(
            activated.Workspace.Snapshot.Definition.Plan.Registrations);
        if (version == InspectionDefinitionSchema.Version3)
            Assert.IsType<CompleteRestorationResolvedState.Version3>(
                activated.Workspace.Snapshot.Resolved);
        else
            Assert.IsType<CompleteRestorationResolvedState.Version4>(
                activated.Workspace.Snapshot.Resolved);
        var projectable =
            Assert.IsType<CompleteRestorationProjection.Projectable>(
                activated.Workspace.Projection);
        Assert.Equal(encoded, projectable.CanonicalPacket);
        Assert.Equal(
            StructuralSubjectKind.Workspace,
            activated.Workspace.Snapshot.Navigation.State.Snapshot
                .ActiveSubject.Kind);

        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

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
        Assert.Single(activated.Workspace.Contexts);
        Assert.Same(
            activated.Workspace.Contexts[0],
            activated.Workspace.SelectedContext);
        Assert.Same(
            activated.Workspace.Snapshot.Contexts[0],
            activated.Workspace.Contexts[0].Receipt);
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
        var projectable =
            Assert.IsType<CompleteRestorationProjection.Projectable>(
                activated.Workspace.Projection);
        Assert.Equal(
            WorkspaceSharePacketCodec.Format2Version,
            WorkspaceSharePacketCodec.Decode(
                projectable.CanonicalPacket,
                TestContext.Current.CancellationToken).FormatVersion);

        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task ProjectionCallback_ReceivesExactReadyPackageInProductOrder()
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
        CompleteWorkspaceActivation? callbackActivation = null;
        CompleteRestorationReadyProjection? callbackProjection = null;
        int callbackCount = 0;

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreWithProjectionAsync(
                preparation,
                authority,
                host,
                Options(client, package.Store),
                (activation, projection, _) =>
                {
                    callbackCount++;
                    callbackActivation = activation;
                    callbackProjection = projection;
                    return ValueTask.CompletedTask;
                },
                TestContext.Current.CancellationToken);

        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        Assert.Equal(1, callbackCount);
        Assert.Same(activated.Workspace, callbackActivation);
        CompleteRestorationReadyPackage projected =
            Assert.Single(callbackProjection!.Packages);
        Assert.Equal("package", projected.NavigationId);
        Assert.Equal(
            Assert.Single(
                activated.Workspace.Snapshot.Navigation.Result.Consumer
                    .Snapshot.Packages).Subject.Id,
            projected.ConsumerPackageSubjectId);
        Assert.Same(
            Assert.Single(activated.Workspace.Snapshot.Scope.Packages),
            projected.Evaluation.Occurrence);
        Assert.Equal(
            projected.Binding.Coordinate,
            projected.Evaluation.Occurrence.Occurrence.Package.Coordinate);
        Assert.True((await activated.Activation.CloseAsync()).Succeeded);
    }

    [Fact]
    public async Task PacketV2_RetainsCanonicalPacketAfterExactRestoration()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        InspectionDefinitionRegistry registry = Version2PackageRegistry();
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
        var projectable =
            Assert.IsType<CompleteRestorationProjection.Projectable>(
                activated.Workspace.Projection);
        Assert.Equal(encoded, projectable.CanonicalPacket);
        Assert.IsType<CompleteRestorationResolvedState.Version2>(
            activated.Workspace.Snapshot.Resolved);
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
    public async Task CompatibleImplementationTarget_RetainsRequestedAssociation()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    Version2CompatibleTargetRegistry(),
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
        WorkspacePackageDescriptor packageDescriptor =
            Assert.Single(
                activated.Workspace.Snapshot.Scope.Packages).Occurrence.Package;
        Assert.Equal("net10.0", packageDescriptor.Coordinate.Framework);
        Assert.Equal("net10.0", packageDescriptor.RequestedTargetFramework);
        Assert.Equal("net9.0", packageDescriptor.SelectedTargetFramework);
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

    [Theory]
    [InlineData(InspectionDefinitionSchema.Version2, "package.unknown")]
    [InlineData(InspectionDefinitionSchema.Version2, "workspace.overview")]
    [InlineData(InspectionDefinitionSchema.Version3, "package.unknown")]
    [InlineData(InspectionDefinitionSchema.Version3, "workspace.overview")]
    public async Task InactiveInvalidFacet_FailsBeforeConstruction(
        int schemaVersion,
        string facet)
    {
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    PackageRegistry(
                        schemaVersion,
                        new CommittedViewStateDefinition(
                            "package",
                            new PortableSubjectRequest.Package(),
                            new PortableRetainedSubjectContext.Package(),
                            facet),
                        focus: null),
                    "scenario",
                    authority));
        using var client = new HttpClient(new RejectingHandler());

        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                new NeverConstructHost(),
                Options(client, []),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Failed>(result);
        var selectorFailure =
            Assert.IsType<
                CompleteRestorationFailure.SelectorResolutionFailed>(
                    failed.Failure);
        Assert.Equal(
            CommittedSelectorResolutionFailureKind.InvalidFacet,
            selectorFailure.Failure.Kind);
        Assert.Equal(1, selectorFailure.Failure.StateIndex);
        Assert.Equal("package", selectorFailure.Failure.NavigationId);
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
        var scopeFailure =
            Assert.IsType<CompleteRestorationFailure.ScopeMutationFailed>(
            failed.Failure);
        Assert.Equal(
            WorkspaceScopeOperationKind.Add,
            scopeFailure.Outcome.Association.Kind);
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
        string? focus = "package") =>
        PackageRegistry(
            InspectionDefinitionSchema.Version2,
            packageState,
            duplicateContexts,
            focus);

    private static InspectionDefinitionRegistry PackageRegistry(
        int schemaVersion,
        CommittedViewStateDefinition? packageState = null,
        bool duplicateContexts = false,
        string? focus = "package")
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(PackageWorkspace(
            schemaVersion,
            duplicateContexts));
        registry.Add(new CommittedNavigationDefinition(
            schemaVersion,
            "navigation",
            [new NavigationTabDefinition("package", coordinate: Package())],
            focus));
        registry.Add(new CommittedViewDefinition(
            schemaVersion,
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
            schemaVersion,
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
        Version2CompatibleTargetRegistry()
    {
        var package =
            new DefinitionMemberCoordinate.PackageCoordinate(
                "System.Text.Json",
                "9.0.4",
                "net10.0");
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version2,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    framework: "net10.0",
                    members: [package]),
            ]));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [new NavigationTabDefinition("package", coordinate: package)],
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
                prepared.Activation.Contexts,
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
