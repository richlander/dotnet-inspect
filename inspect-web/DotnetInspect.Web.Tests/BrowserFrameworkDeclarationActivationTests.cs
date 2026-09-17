using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserFrameworkDeclarationActivationTests
{
    const string Framework = "net10.0";
    const string PlatformVersionText = "10.0.10";
    const string PackageVersion = "10.0.0";
    const string ReferencePackage = "microsoft.netcore.app.ref";
    const string RuntimePackage =
        "microsoft.netcore.app.runtime.linux-x64";
    static readonly PackageSource NuGetOrg = PackageSource.NuGetOrg;

    [Fact]
    public async Task RealFrameworkTypeSettlesExactEffectBesidePackageNeighbor()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host);
        TypeDeclarationLocatorResult.Evaluated result =
            await LocateAsync(
                operation.Workspace,
                Name("System.Text.Json", "JsonSerializer"));
        TypeDeclarationLocatorCandidate framework =
            Assert.Single(
                Assert.Single(result.Answers).Candidates,
                candidate => candidate.Coordinate
                    is ExactLibrarySourceCoordinate.Platform);
        TypeDeclarationLocatorCandidate package =
            Assert.Single(
                Assert.Single(result.Answers).Candidates,
                candidate => candidate.Coordinate
                    is ExactLibrarySourceCoordinate.Package);
        BrowserSpotlightActivationBasis basis = Basis(operation, 1);

        BrowserFrameworkDeclarationPublication publication =
            await scenario.Activation.PublishAsync(
                Authority(scenario.Activation, 1),
                operation,
                basis,
                result,
                [
                    new BrowserFrameworkDeclarationDestination.Type(
                        framework),
                    new BrowserFrameworkDeclarationDestination.Type(
                        package),
                ],
                [scenario.PlatformContext, scenario.PackageContext]);
        BrowserFrameworkDeclarationActionPublication.Published published =
            Assert.IsType<
                BrowserFrameworkDeclarationActionPublication.Published>(
                    publication.Actions[0]);
        var packageBlocked = Assert.IsType<
            BrowserFrameworkDeclarationActionPublication.Blocked>(
                publication.Actions[1]);
        Assert.Equal(
            BrowserFrameworkDeclarationRefusedReason
                .NonPlatformObservation,
            Assert.IsType<
                BrowserFrameworkDeclarationActivationBlock.Refused>(
                    packageBlocked.Block).Reason);

        var settled = Assert.IsType<
            BrowserFrameworkDeclarationActivationResult.Settled>(
                await scenario.Activation.ActivateAsync(
                    published.Action,
                    TestContext.Current.CancellationToken));
        var effect = Assert.IsType<
            BrowserFrameworkDeclarationEffect.Type>(settled.Effect);

        Assert.Same(framework.Observation, effect.Observation);
        Assert.Equal(framework.Name, effect.Name);
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            effect.SelectedType.DefinitionId);
        Assert.Equal(
            "System.Text.Json",
            effect.SelectedType.AssemblyName);
        Assert.Equal(Framework, effect.Surface.ActiveFramework);
        Assert.Equal(PlatformVersionText, effect.Surface.Version);
        Assert.Equal(
            BrowserPlatformIdentity.PackageName,
            effect.Surface.Package);
        Assert.Single(effect.Surface.Assemblies);
        Assert.IsType<ExactLibrarySourceCoordinate.Platform>(
            effect.Coordinate);
    }

    [Fact]
    public async Task WholeFamilyPlatformContextSettlesExactType()
    {
        await using Scenario scenario =
            await CreateScenarioAsync(wholeFamily: true);
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host);
        TypeDeclarationLocatorResult.Evaluated result =
            await LocateAsync(
                operation.Workspace,
                Name("System.Text.Json", "JsonSerializer"));
        TypeDeclarationLocatorCandidate framework =
            Assert.Single(
                Assert.Single(result.Answers).Candidates,
                candidate => candidate.Coordinate
                    is ExactLibrarySourceCoordinate.Platform);
        var origin = Assert.IsType<WorkspaceDeclarationOrigin.ContextLoad>(
            framework.Observation.Origin);
        var realized = Assert.IsType<RealizedMemberCoordinate.Platform>(
            origin.Realized);
        Assert.Null(realized.Assembly);

        BrowserFrameworkDeclarationAction action =
            Assert.IsType<
                BrowserFrameworkDeclarationActionPublication.Published>(
                    Assert.Single(
                        (await scenario.Activation.PublishAsync(
                            Authority(scenario.Activation, 1),
                            operation,
                            Basis(operation, 1),
                            result,
                            [
                                new
                                    BrowserFrameworkDeclarationDestination
                                        .Type(framework),
                            ],
                            [scenario.PlatformContext])).Actions)).Action;

        var settled = Assert.IsType<
            BrowserFrameworkDeclarationActivationResult.Settled>(
                await scenario.Activation.ActivateAsync(
                    action,
                    TestContext.Current.CancellationToken));
        var effect = Assert.IsType<
            BrowserFrameworkDeclarationEffect.Type>(settled.Effect);
        Assert.Same(framework.Observation, effect.Observation);
        Assert.Equal(framework.Name, effect.Name);
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            effect.SelectedType.DefinitionId);
        Assert.Equal(
            "System.Text.Json",
            effect.SelectedType.AssemblyName);
    }

    [Fact]
    public async Task LibraryDestinationSettlesWithoutSelectingDefaultType()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host);
        TypeDeclarationLocatorResult.Evaluated result =
            await LocateAsync(
                operation.Workspace,
                Name("System.Text.Json", "JsonSerializer"));
        TypeDeclarationLocatorCandidate framework =
            Assert.Single(
                Assert.Single(result.Answers).Candidates,
                candidate => candidate.Coordinate
                    is ExactLibrarySourceCoordinate.Platform);

        BrowserFrameworkDeclarationPublication publication =
            await scenario.Activation.PublishAsync(
                Authority(scenario.Activation, 1),
                operation,
                Basis(operation, 1),
                result,
                [
                    new BrowserFrameworkDeclarationDestination.Library(
                        framework.Observation),
                ],
                [scenario.PlatformContext]);
        BrowserFrameworkDeclarationActionPublication.Published published =
            Assert.IsType<
                BrowserFrameworkDeclarationActionPublication.Published>(
                    Assert.Single(publication.Actions));

        var settled = Assert.IsType<
            BrowserFrameworkDeclarationActivationResult.Settled>(
                await scenario.Activation.ActivateAsync(
                    published.Action,
                    TestContext.Current.CancellationToken));
        var effect = Assert.IsType<
            BrowserFrameworkDeclarationEffect.Library>(settled.Effect);

        Assert.Same(framework.Observation, effect.Observation);
        Assert.Single(effect.Surface.Assemblies);
    }

    [Fact]
    public async Task NewPublicationMakesPriorActionStale()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host);
        TypeDeclarationLocatorResult.Evaluated result =
            await LocateAsync(
                operation.Workspace,
                Name("System.Text.Json", "JsonSerializer"));
        TypeDeclarationLocatorCandidate framework =
            Assert.Single(
                Assert.Single(result.Answers).Candidates,
                candidate => candidate.Coordinate
                    is ExactLibrarySourceCoordinate.Platform);
        var destination =
            new BrowserFrameworkDeclarationDestination.Type(framework);
        BrowserFrameworkDeclarationAction first =
            Assert.IsType<
                BrowserFrameworkDeclarationActionPublication.Published>(
                    Assert.Single(
                        (await scenario.Activation.PublishAsync(
                            Authority(scenario.Activation, 1),
                            operation,
                            Basis(operation, 1),
                            result,
                            [destination],
                            [scenario.PlatformContext])).Actions)).Action;

        BrowserFrameworkDeclarationPublication current =
            await scenario.Activation.PublishAsync(
                Authority(scenario.Activation, 2),
                operation,
                Basis(operation, 2),
                result,
                [destination],
                [scenario.PlatformContext]);

        var blocked = Assert.IsType<
            BrowserFrameworkDeclarationActivationResult.Blocked>(
                await scenario.Activation.ActivateAsync(
                    first,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            BrowserFrameworkDeclarationStaleReason.Result,
            Assert.IsType<
                BrowserFrameworkDeclarationActivationBlock.Stale>(
                    blocked.Block).Reason);
        BrowserFrameworkDeclarationAction currentAction =
            Assert.IsType<
                BrowserFrameworkDeclarationActionPublication.Published>(
                    Assert.Single(current.Actions)).Action;
        Assert.IsType<BrowserFrameworkDeclarationActivationResult.Settled>(
            await scenario.Activation.ActivateAsync(
                currentAction,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NewResultPreventsOlderGenerationPublication()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host);
        TypeDeclarationLocatorResult.Evaluated result =
            await LocateAsync(
                operation.Workspace,
                Name("System.Text.Json", "JsonSerializer"));
        TypeDeclarationLocatorCandidate framework =
            Assert.Single(
                Assert.Single(result.Answers).Candidates,
                candidate => candidate.Coordinate
                    is ExactLibrarySourceCoordinate.Platform);
        BrowserFrameworkDeclarationResultAuthority older =
            Authority(scenario.Activation, 1);
        _ = Authority(scenario.Activation, 2);

        BrowserFrameworkDeclarationPublication publication =
            await scenario.Activation.PublishAsync(
                older,
                operation,
                Basis(operation, 1),
                result,
                [
                    new BrowserFrameworkDeclarationDestination.Type(
                        framework),
                ],
                [scenario.PlatformContext]);

        var blocked = Assert.IsType<
            BrowserFrameworkDeclarationActionPublication.Blocked>(
                Assert.Single(publication.Actions));
        Assert.Equal(
            BrowserFrameworkDeclarationStaleReason.Result,
            Assert.IsType<
                BrowserFrameworkDeclarationActivationBlock.Stale>(
                    blocked.Block).Reason);
    }

    [Fact]
    public async Task EmptyNewResultInvalidatesPriorActions()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        BrowserFrameworkDeclarationAction action;
        using (WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host))
        {
            TypeDeclarationLocatorResult.Evaluated result =
                await LocateAsync(
                    operation.Workspace,
                    Name("System.Text.Json", "JsonSerializer"));
            TypeDeclarationLocatorCandidate framework =
                Assert.Single(
                    Assert.Single(result.Answers).Candidates,
                    candidate => candidate.Coordinate
                        is ExactLibrarySourceCoordinate.Platform);
            action = Assert.IsType<
                BrowserFrameworkDeclarationActionPublication.Published>(
                    Assert.Single(
                        (await scenario.Activation.PublishAsync(
                            Authority(scenario.Activation, 1),
                            operation,
                            Basis(operation, 1),
                            result,
                            [
                                new
                                    BrowserFrameworkDeclarationDestination
                                        .Type(framework),
                            ],
                            [scenario.PlatformContext])).Actions)).Action;
        }

        _ = Authority(scenario.Activation, 2);

        var blocked = Assert.IsType<
            BrowserFrameworkDeclarationActivationResult.Blocked>(
                await scenario.Activation.ActivateAsync(
                    action,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            BrowserFrameworkDeclarationStaleReason.Result,
            Assert.IsType<
                BrowserFrameworkDeclarationActivationBlock.Stale>(
                    blocked.Block).Reason);
    }

    [Fact]
    public async Task CanceledActivationReturnsTypedBlock()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        BrowserFrameworkDeclarationAction action;
        using (WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host))
        {
            TypeDeclarationLocatorResult.Evaluated result =
                await LocateAsync(
                    operation.Workspace,
                    Name("System.Text.Json", "JsonSerializer"));
            TypeDeclarationLocatorCandidate framework =
                Assert.Single(
                    Assert.Single(result.Answers).Candidates,
                    candidate => candidate.Coordinate
                        is ExactLibrarySourceCoordinate.Platform);
            action = Assert.IsType<
                BrowserFrameworkDeclarationActionPublication.Published>(
                    Assert.Single(
                        (await scenario.Activation.PublishAsync(
                            Authority(scenario.Activation, 1),
                            operation,
                            Basis(operation, 1),
                            result,
                            [
                                new
                                    BrowserFrameworkDeclarationDestination
                                        .Type(framework),
                            ],
                            [scenario.PlatformContext])).Actions)).Action;
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var blocked = Assert.IsType<
            BrowserFrameworkDeclarationActivationResult.Blocked>(
                await scenario.Activation.ActivateAsync(
                    action,
                    cancellation.Token));
        Assert.IsType<
            BrowserFrameworkDeclarationActivationBlock.Canceled>(
                blocked.Block);
    }

    [Fact]
    public async Task RealizationReplacementRejectsExactPriorAction()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        BrowserFrameworkDeclarationAction action;
        using (WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host))
        {
            TypeDeclarationLocatorResult.Evaluated result =
                await LocateAsync(
                    operation.Workspace,
                    Name("System.Text.Json", "JsonSerializer"));
            TypeDeclarationLocatorCandidate framework =
                Assert.Single(
                    Assert.Single(result.Answers).Candidates,
                    candidate => candidate.Coordinate
                        is ExactLibrarySourceCoordinate.Platform);
            action = Assert.IsType<
                BrowserFrameworkDeclarationActionPublication.Published>(
                    Assert.Single(
                        (await scenario.Activation.PublishAsync(
                            Authority(scenario.Activation, 1),
                            operation,
                            Basis(operation, 1),
                            result,
                            [
                                new
                                    BrowserFrameworkDeclarationDestination
                                        .Type(framework),
                            ],
                            [scenario.PlatformContext])).Actions)).Action;
        }

        _ = await ActivateEmptyAsync(scenario.Host);

        var blocked = Assert.IsType<
            BrowserFrameworkDeclarationActivationResult.Blocked>(
                await scenario.Activation.ActivateAsync(
                    action,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            BrowserFrameworkDeclarationStaleReason.Realization,
            Assert.IsType<
                BrowserFrameworkDeclarationActivationBlock.Stale>(
                    blocked.Block).Reason);
    }

    [Fact]
    public async Task ScopeMovementRejectsActionBeforeProjection()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        BrowserFrameworkDeclarationAction action;
        WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host);
        try
        {
            TypeDeclarationLocatorResult.Evaluated result =
                await LocateAsync(
                    operation.Workspace,
                    Name("System.Text.Json", "JsonSerializer"));
            TypeDeclarationLocatorCandidate framework =
                Assert.Single(
                    Assert.Single(result.Answers).Candidates,
                    candidate => candidate.Coordinate
                        is ExactLibrarySourceCoordinate.Platform);
            action = Assert.IsType<
                BrowserFrameworkDeclarationActionPublication.Published>(
                    Assert.Single(
                        (await scenario.Activation.PublishAsync(
                            Authority(scenario.Activation, 1),
                            operation,
                            Basis(operation, 1),
                            result,
                            [
                                new
                                    BrowserFrameworkDeclarationDestination
                                        .Type(framework),
                            ],
                            [scenario.PlatformContext])).Actions)).Action;

            WorkspaceScopeOperationResult replaced =
                await operation.Workspace.ReplaceScopeAsync(
                    operation.Scope.Revision,
                    [],
                    DateTimeOffset.UtcNow.AddSeconds(30),
                    TestContext.Current.CancellationToken);
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                replaced);
        }
        finally
        {
            operation.Dispose();
        }

        var blocked = Assert.IsType<
            BrowserFrameworkDeclarationActivationResult.Blocked>(
                await scenario.Activation.ActivateAsync(
                    action,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            BrowserFrameworkDeclarationStaleReason.ScopeRevision,
            Assert.IsType<
                BrowserFrameworkDeclarationActivationBlock.Stale>(
                    blocked.Block).Reason);
    }

    [Fact]
    public async Task ReleasedContextBlocksPublication()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host);
        TypeDeclarationLocatorResult.Evaluated result =
            await LocateAsync(
                operation.Workspace,
                Name("System.Text.Json", "JsonSerializer"));
        TypeDeclarationLocatorCandidate framework =
            Assert.Single(
                Assert.Single(result.Answers).Candidates,
                candidate => candidate.Coordinate
                    is ExactLibrarySourceCoordinate.Platform);
        Assert.IsType<AssemblyContextGroup>(
            scenario.PlatformContext.Group).Dispose();

        BrowserFrameworkDeclarationPublication publication =
            await scenario.Activation.PublishAsync(
                Authority(scenario.Activation, 1),
                operation,
                Basis(operation, 1),
                result,
                [
                    new BrowserFrameworkDeclarationDestination.Type(
                        framework),
                ],
                [scenario.PlatformContext]);

        var blocked = Assert.IsType<
            BrowserFrameworkDeclarationActionPublication.Blocked>(
                Assert.Single(publication.Actions));
        Assert.Equal(
            BrowserFrameworkDeclarationUnavailableReason.ContextUnavailable,
            Assert.IsType<
                BrowserFrameworkDeclarationActivationBlock.Unavailable>(
                    blocked.Block).Reason);
    }

    [Fact]
    public async Task ReleasedContextBlocksActivation()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        BrowserFrameworkDeclarationAction action;
        using (WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host))
        {
            TypeDeclarationLocatorResult.Evaluated result =
                await LocateAsync(
                    operation.Workspace,
                    Name("System.Text.Json", "JsonSerializer"));
            TypeDeclarationLocatorCandidate framework =
                Assert.Single(
                    Assert.Single(result.Answers).Candidates,
                    candidate => candidate.Coordinate
                        is ExactLibrarySourceCoordinate.Platform);
            action = Assert.IsType<
                BrowserFrameworkDeclarationActionPublication.Published>(
                    Assert.Single(
                        (await scenario.Activation.PublishAsync(
                            Authority(scenario.Activation, 1),
                            operation,
                            Basis(operation, 1),
                            result,
                            [
                                new
                                    BrowserFrameworkDeclarationDestination
                                        .Type(framework),
                            ],
                            [scenario.PlatformContext])).Actions)).Action;
        }
        Assert.IsType<AssemblyContextGroup>(
            scenario.PlatformContext.Group).Dispose();

        var blocked = Assert.IsType<
            BrowserFrameworkDeclarationActivationResult.Blocked>(
                await scenario.Activation.ActivateAsync(
                    action,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            BrowserFrameworkDeclarationUnavailableReason.ContextUnavailable,
            Assert.IsType<
                BrowserFrameworkDeclarationActivationBlock.Unavailable>(
                    blocked.Block).Reason);
    }

    [Fact]
    public async Task DuplicateLiveContextAssociationIsAmbiguous()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host);
        TypeDeclarationLocatorResult.Evaluated result =
            await LocateAsync(
                operation.Workspace,
                Name("System.Text.Json", "JsonSerializer"));
        TypeDeclarationLocatorCandidate framework =
            Assert.Single(
                Assert.Single(result.Answers).Candidates,
                candidate => candidate.Coordinate
                    is ExactLibrarySourceCoordinate.Platform);

        BrowserFrameworkDeclarationPublication publication =
            await scenario.Activation.PublishAsync(
                Authority(scenario.Activation, 1),
                operation,
                Basis(operation, 1),
                result,
                [
                    new BrowserFrameworkDeclarationDestination.Type(
                        framework),
                ],
                [
                    scenario.PlatformContext,
                    scenario.PlatformContext,
                ]);

        var blocked = Assert.IsType<
            BrowserFrameworkDeclarationActionPublication.Blocked>(
                Assert.Single(publication.Actions));
        Assert.Equal(
            BrowserFrameworkDeclarationAmbiguousReason.Context,
            Assert.IsType<
                BrowserFrameworkDeclarationActivationBlock.Ambiguous>(
                    blocked.Block).Reason);
    }

    [Fact]
    public async Task RuntimeForwarderIsRefusedWithoutTargetAction()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host);
        TypeDeclarationLocatorResult.Evaluated result =
            await LocateAsync(
                operation.Workspace,
                Name("System", "Object"));
        TypeDeclarationLocatorCandidate forwarder =
            Assert.Single(
                Assert.Single(result.Answers).Candidates,
                candidate =>
                    candidate.Kind
                        == AssemblyTypeDeclarationKind.Forwarder
                    && candidate.Coordinate
                        is ExactLibrarySourceCoordinate.Platform platform
                    && string.Equals(
                        platform.LibraryIdentity.Identity.Name,
                        "netstandard",
                        StringComparison.OrdinalIgnoreCase));

        BrowserFrameworkDeclarationPublication publication =
            await scenario.Activation.PublishAsync(
                Authority(scenario.Activation, 1),
                operation,
                Basis(operation, 1),
                result,
                [
                    new BrowserFrameworkDeclarationDestination.Type(
                        forwarder),
                ],
                [scenario.PlatformContext]);

        var blocked = Assert.IsType<
            BrowserFrameworkDeclarationActionPublication.Blocked>(
                Assert.Single(publication.Actions));
        Assert.Equal(
            BrowserFrameworkDeclarationRefusedReason.Forwarder,
            Assert.IsType<
                BrowserFrameworkDeclarationActivationBlock.Refused>(
                    blocked.Block).Reason);
    }

    [Fact]
    public async Task ForeignIssuerActionIsRefused()
    {
        await using Scenario scenario = await CreateScenarioAsync();
        using var other =
            new BrowserFrameworkDeclarationActivation(scenario.Host);
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host);
        TypeDeclarationLocatorResult.Evaluated result =
            await LocateAsync(
                operation.Workspace,
                Name("System.Text.Json", "JsonSerializer"));
        TypeDeclarationLocatorCandidate framework =
            Assert.Single(
                Assert.Single(result.Answers).Candidates,
                candidate => candidate.Coordinate
                    is ExactLibrarySourceCoordinate.Platform);
        BrowserFrameworkDeclarationAction action =
            Assert.IsType<
                BrowserFrameworkDeclarationActionPublication.Published>(
                    Assert.Single(
                        (await scenario.Activation.PublishAsync(
                            Authority(scenario.Activation, 1),
                            operation,
                            Basis(operation, 1),
                            result,
                            [
                                new
                                    BrowserFrameworkDeclarationDestination
                                        .Type(framework),
                            ],
                            [scenario.PlatformContext])).Actions)).Action;

        var blocked = Assert.IsType<
            BrowserFrameworkDeclarationActivationResult.Blocked>(
                await other.ActivateAsync(
                    action,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            BrowserFrameworkDeclarationRefusedReason.ForeignAction,
            Assert.IsType<
                BrowserFrameworkDeclarationActivationBlock.Refused>(
                    blocked.Block).Reason);
    }

    [Fact]
    public async Task RetainedActionDoesNotRetainRetiredMetadataAuthority()
    {
        DetachmentSpecimen specimen =
            await CreateDetachmentSpecimenAsync();
        try
        {
            for (int attempt = 0;
                attempt < 10
                && specimen.LiveAuthority.Any(
                    reference => reference.IsAlive);
                attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Yield();
            }

            Assert.All(
                specimen.LiveAuthority,
                reference => Assert.False(reference.IsAlive));
            GC.KeepAlive(specimen.Action);
            GC.KeepAlive(specimen.Activation);
        }
        finally
        {
            specimen.Activation.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task<DetachmentSpecimen>
        CreateDetachmentSpecimenAsync()
    {
        Scenario scenario = await CreateScenarioAsync();
        BrowserFrameworkDeclarationAction action;
        WeakReference[] authority;
        using (WorkspaceRealizationOperationLease operation =
            await EnterAsync(scenario.Host))
        {
            TypeDeclarationLocatorResult.Evaluated result =
                await LocateAsync(
                    operation.Workspace,
                    Name("System.Text.Json", "JsonSerializer"));
            TypeDeclarationLocatorCandidate framework =
                Assert.Single(
                    Assert.Single(result.Answers).Candidates,
                    candidate => candidate.Coordinate
                        is ExactLibrarySourceCoordinate.Platform);
            action =
                Assert.IsType<
                    BrowserFrameworkDeclarationActionPublication.Published>(
                        Assert.Single(
                            (await scenario.Activation.PublishAsync(
                                Authority(scenario.Activation, 1),
                                operation,
                                Basis(operation, 1),
                                result,
                                [
                                    new
                                        BrowserFrameworkDeclarationDestination
                                            .Type(framework),
                                ],
                                [scenario.PlatformContext])).Actions)).Action;
            AssemblyContextGroup group =
                Assert.IsType<AssemblyContextGroup>(
                    scenario.PlatformContext.Group);
            authority =
            [
                new(scenario.PlatformContext),
                new(group),
                new(group.Participants[0].Assembly.Registration),
            ];
        }

        await scenario.Host.DisposeAsync();
        return new(action, scenario.Activation, authority);
    }

    static async Task<Scenario> CreateScenarioAsync(
        bool wholeFamily = false)
    {
        var host = new BrowserWorkspaceRealizationHost();
        var activation =
            new BrowserFrameworkDeclarationActivation(host);
        try
        {
            PackageSourceAuthorization authorization =
                PackageSourceAuthorization.Authorize([NuGetOrg]);
            using IPackageSourceClient sourceClient =
                PackageSourceClientFactory.CreateGallery(
                    authorization.Authorities[0].Association,
                    new FailingHandler());
            var store = new InMemoryPackageStore();
            await store.CommitAsync(
                ReferencePackage,
                PlatformVersionText,
                sourceClient.Source.Producer.Key,
                File.OpenRead(ReferencePackageAsset()),
                TestContext.Current.CancellationToken);
            if (wholeFamily)
            {
                await store.CommitAsync(
                    RuntimePackage,
                    PlatformVersionText,
                    NuGetCache.GetSourceKey(NuGetOrg.Url),
                    File.OpenRead(RuntimePackageAsset()),
                    TestContext.Current.CancellationToken);
            }
            await store.CommitAsync(
                "System.Text.Json",
                PackageVersion,
                NuGetCache.GetSourceKey(NuGetOrg.Url),
                File.OpenRead(PackageAsset()),
                TestContext.Current.CancellationToken);
            using var client = new HttpClient(new FailingHandler());
            var options = new WorkspaceContextLoadOptions
            {
                HttpClient = client,
                SourceAuthorization =
                    new UniformPackageSourceAuthorization([NuGetOrg]),
                PackageStore = store,
            };
            await using PackageSourceSettlementLease sourceRoot =
                PackageSourceSettlementService.IssueLease(
                    _ => sourceClient);
            var referenceSource = new PackagePlatformSource(
                new FixedPackageSourceAuthorization(authorization),
                new PackagePayloadAcquisitionPlan((_, _) => store));
            var referenceCoordinate =
                new PackageReferencePackCoordinate(
                    new PlatformFamilyTarget(
                        PlatformFamily.DotNetRuntime,
                        PlatformTargetFramework.Parse(Framework),
                        DotnetInspector.Platforms.PlatformVersion.Parse(
                            PlatformVersionText)));

            BrowserWorkspaceRealizationCandidateStartResult.Prepared
                prepared = Assert.IsType<
                    BrowserWorkspaceRealizationCandidateStartResult.Prepared>(
                        await host.BeginCandidateAsync(
                            WorkspacePlan.Empty,
                            TestContext.Current.CancellationToken));
            WorkspaceDeclarationContext platform;
            WorkspaceDeclarationContext package;
            using (WorkspaceRealizationConstructionLease construction =
                prepared.Candidate.EnterConstruction())
            {
                platform = wholeFamily
                    ? await WorkspaceContextLoader
                        .LoadDeclarationContextAsync(
                            construction.Workspace,
                            new WorkspaceContextInput
                            {
                                Framework = Framework,
                                RuntimeIdentifier = "linux-x64",
                                Members =
                                [
                                    WorkspaceMemberCoordinate.Platform(
                                        "runtime",
                                        version: PlatformVersionText,
                                        framework: Framework),
                                ],
                            },
                            options,
                            TestContext.Current.CancellationToken)
                    : await WorkspaceReferenceDeclarationLoader.LoadAsync(
                            construction.Workspace,
                            referenceSource,
                            referenceCoordinate,
                            new PackageReferencePopulationDemand
                                .CompletePopulation(),
                            new PackageReferenceWorkBudget(
                                maxAssemblies: 4096,
                                maxBytes: 512L * 1024 * 1024),
                            sourceRoot.IssueOperationLease(
                                TestContext.Current.CancellationToken,
                                requestTimeout: TimeSpan.FromMinutes(2),
                                operationTimeout:
                                    TimeSpan.FromMinutes(2)));
                package =
                    await WorkspaceContextLoader
                        .LoadDeclarationContextAsync(
                            construction.Workspace,
                            new WorkspaceContextInput
                            {
                                Framework = Framework,
                                Members =
                                [
                                    WorkspaceMemberCoordinate.Package(
                                        "System.Text.Json",
                                        PackageVersion),
                                ],
                            },
                            options,
                            TestContext.Current.CancellationToken);
            }
            Assert.True(platform.Receipt.IsRealized);
            Assert.NotNull(platform.Group);
            Assert.IsType<WorkspaceContextLoadOutcome.Loaded>(
                package.ContextLoadOutcome);
            Assert.IsType<
                WorkspaceRealizationCandidateCompletionResult.Ready>(
                    await host.CompleteCandidateAsync(
                        prepared.Candidate,
                        TestContext.Current.CancellationToken));
            Assert.IsType<
                BrowserWorkspaceRealizationCutoverResult.Activated>(
                    host.CutOver(prepared.Candidate));
            return new Scenario(
                host,
                activation,
                platform,
                package);
        }
        catch
        {
            activation.Dispose();
            await host.DisposeAsync();
            throw;
        }
    }

    static async Task<WorkspaceRealization> ActivateEmptyAsync(
        BrowserWorkspaceRealizationHost host)
    {
        BrowserWorkspaceRealizationCandidateStartResult.Prepared prepared =
            Assert.IsType<
                BrowserWorkspaceRealizationCandidateStartResult.Prepared>(
                    await host.BeginCandidateAsync(
                        WorkspacePlan.Empty,
                        TestContext.Current.CancellationToken));
        Assert.IsType<WorkspaceRealizationCandidateCompletionResult.Ready>(
            await host.CompleteCandidateAsync(
                prepared.Candidate,
                TestContext.Current.CancellationToken));
        return Assert.IsType<
            BrowserWorkspaceRealizationCutoverResult.Activated>(
                host.CutOver(prepared.Candidate)).Realization;
    }

    static async ValueTask<WorkspaceRealizationOperationLease> EnterAsync(
        BrowserWorkspaceRealizationHost host)
    {
        WorkspaceRealizationOperationAdmission admission =
            await host.EnterOperationAsync(
                TestContext.Current.CancellationToken);
        return Assert.IsType<
            WorkspaceRealizationOperationAdmission.Admitted>(
                admission).Lease;
    }

    static async Task<TypeDeclarationLocatorResult.Evaluated> LocateAsync(
        InspectionWorkspace workspace,
        MetadataTypeDefinitionName name) =>
        Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            await workspace.GetDeclarationLocator().ExecuteAsync(
                [new TypeDeclarationLocatorRequest.Exact(name)],
                cancellationToken:
                    TestContext.Current.CancellationToken));

    static BrowserSpotlightActivationBasis Basis(
        WorkspaceRealizationOperationLease operation,
        long generation) =>
        new(
            generation,
            operation.Scope,
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                operation.Workspace.GetRegistrationSnapshot()).Revision);

    static BrowserFrameworkDeclarationResultAuthority Authority(
        BrowserFrameworkDeclarationActivation activation,
        long generation) =>
        Assert.IsType<
            BrowserFrameworkDeclarationResultAdmission.Admitted>(
                activation.BeginResult(generation)).Authority;

    static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [.. segments])).Name;

    static string ReferencePackageAsset() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "FrameworkActivation",
            "microsoft.netcore.app.ref.10.0.10.nupkg");

    static string PackageAsset() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "FrameworkActivation",
            "system.text.json.10.0.0.nupkg");

    static string RuntimePackageAsset() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "FrameworkActivation",
            "microsoft.netcore.app.runtime.linux-x64.10.0.10.nupkg");

    sealed class Scenario(
        BrowserWorkspaceRealizationHost host,
        BrowserFrameworkDeclarationActivation activation,
        WorkspaceDeclarationContext platformContext,
        WorkspaceDeclarationContext packageContext) : IAsyncDisposable
    {
        internal BrowserWorkspaceRealizationHost Host { get; } = host;

        internal BrowserFrameworkDeclarationActivation Activation { get; } =
            activation;

        internal WorkspaceDeclarationContext PlatformContext { get; } =
            platformContext;

        internal WorkspaceDeclarationContext PackageContext { get; } =
            packageContext;

        public async ValueTask DisposeAsync()
        {
            Activation.Dispose();
            await Host.DisposeAsync();
        }
    }

    sealed record DetachmentSpecimen(
        BrowserFrameworkDeclarationAction Action,
        BrowserFrameworkDeclarationActivation Activation,
        WeakReference[] LiveAuthority);

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected HTTP request to {request.RequestUri}.");
    }

    sealed class FixedPackageSourceAuthorization(
        PackageSourceAuthorization authorization) :
        IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId) =>
            authorization;
    }
}
