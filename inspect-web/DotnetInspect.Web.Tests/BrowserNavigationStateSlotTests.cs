using System.Collections.Immutable;
using System.IO.Compression;
using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserNavigationStateSlotTests
{
    [Fact]
    public async Task StateSlot_ExecutesOpaqueActionsAndSettlesAuthority()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        BrowserNavigationStateSlot slot = fixture.Slot;
        NavigationConsumerResult initialization = slot.Initialization;
        Assert.Same(fixture.Workspace.Identity, slot.Workspace);
        Assert.Equal(initialization.Authority!.Session, slot.Id);
        Assert.Equal(
            NavigationAuthorityResult.Accepted,
            slot.RecordConsumerPosting(initialization.Authority));
        Assert.Equal(
            NavigationAuthorityResult.Accepted,
            slot.Acknowledge(initialization.Authority));

        NavigationAction action = Assert.IsType<NavigationAction>(
            initialization.Snapshot.Types
                .First(type => type.Navigation.Action is not null)
                .Navigation.Action);
        NavigationConsumerResult selected = Assert.IsType<
            NavigationConsumerResult>(
                await slot.ExecuteAsync(
                    action,
                    fixture.Prepare,
                    TestContext.Current.CancellationToken));

        Assert.Equal(NavigationOutcomeKind.Applied, selected.Outcome.Kind);
        Assert.Equal(
            StructuralSubjectKind.Type,
            selected.Snapshot.ActiveSubject.Kind);
        Assert.True(slot.ValidateAuthority(selected.Authority!));
        Assert.Equal(
            NavigationAuthorityResult.Accepted,
            slot.RecordConsumerPosting(selected.Authority!));
        Assert.Equal(
            NavigationAuthorityResult.Accepted,
            slot.Acknowledge(selected.Authority!));

        NavigationConsumerResult synchronized = Assert.IsType<
            NavigationConsumerResult>(
                await slot.SynchronizeAsync(
                    fixture.Prepare,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            NavigationOperationKind.Synchronize,
            synchronized.Operation);
        Assert.Equal(
            NavigationOutcomeKind.Synchronized,
            synchronized.Outcome.Kind);
        Assert.Equal(
            NavigationSynchronizationDisposition.Current,
            synchronized.Synchronization);
    }

    [Fact]
    public async Task StateSlot_LatestIntentDiscardsDelayedCompletion()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        BrowserNavigationStateSlot slot = fixture.Slot;
        fixture.AcknowledgeInitialization();
        NavigationAction[] actions =
        [
            .. slot.Initialization.Snapshot.Types
                .Where(type => type.Navigation.Action is not null)
                .Take(2)
                .Select(type => type.Navigation.Action!),
        ];
        Assert.Equal(2, actions.Length);
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<NavigationPreparation> Delayed(
            NavigationEvaluationRequest request,
            CancellationToken cancellationToken)
        {
            firstStarted.TrySetResult();
            await releaseFirst.Task.WaitAsync(cancellationToken);
            return fixture.Ready(request);
        }

        Task<NavigationConsumerResult?> first =
            slot.ExecuteAsync(
                actions[0],
                Delayed,
                TestContext.Current.CancellationToken).AsTask();
        await firstStarted.Task;
        NavigationConsumerResult second = Assert.IsType<
            NavigationConsumerResult>(
                await slot.ExecuteAsync(
                    actions[1],
                    fixture.Prepare,
                    TestContext.Current.CancellationToken));
        releaseFirst.SetResult();

        Assert.Equal(NavigationOutcomeKind.Applied, second.Outcome.Kind);
        Assert.Null(await first);
        Assert.True(slot.ValidateAuthority(second.Authority!));
    }

    [Fact]
    public async Task StateSlot_RetirementProducesNoAbortedAuthority()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        BrowserNavigationStateSlot slot = fixture.Slot;
        fixture.AcknowledgeInitialization();
        NavigationAction action = Assert.IsType<NavigationAction>(
            slot.Initialization.Snapshot.Types
                .First(type => type.Navigation.Action is not null)
                .Navigation.Action);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<NavigationPreparation> WaitForRetirement(
            NavigationEvaluationRequest _,
            CancellationToken cancellationToken)
        {
            started.SetResult();
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException(
                "Retirement cancellation did not stop preparation.");
        }

        Task<NavigationConsumerResult?> operation =
            slot.ExecuteAsync(
                action,
                WaitForRetirement,
                TestContext.Current.CancellationToken).AsTask();
        await started.Task;

        slot.Retire();

        Assert.Null(await operation);
        Assert.False(slot.ValidateAuthority(slot.Initialization.Authority!));
    }

    [Fact]
    public async Task StateSlot_RetirementSettlesBlockedSynchronization()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        BrowserNavigationStateSlot slot = fixture.Slot;
        Task<NavigationConsumerResult?> synchronization =
            slot.SynchronizeAsync(
                fixture.Prepare,
                TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();
        Assert.False(synchronization.IsCompleted);

        slot.Retire();

        Assert.Null(await synchronization);
        Assert.False(slot.ValidateAuthority(slot.Initialization.Authority!));
    }

    [Fact]
    public async Task StateSlot_RetirementSettlesPendingBeforeSurfacingCancellationFailure()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        BrowserNavigationStateSlot slot = fixture.Slot;
        fixture.AcknowledgeInitialization();
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePreparation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<NavigationPreparation> ThrowDuringRetirement(
            NavigationEvaluationRequest _,
            CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    static () => throw new InvalidOperationException(
                        "retirement cleanup failed"));
            Task cancellation = Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            started.SetResult();
            try
            {
                await cancellation;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await releasePreparation.Task;
                throw;
            }
            throw new InvalidOperationException(
                "Retirement cancellation did not stop preparation.");
        }

        Task<NavigationConsumerResult?> maintenance =
            slot.RefreshAsync(
                ThrowDuringRetirement,
                TestContext.Current.CancellationToken).AsTask();
        await started.Task;

        try
        {
            Assert.ThrowsAny<Exception>(slot.Retire);
        }
        finally
        {
            releasePreparation.SetResult();
        }
        Assert.Null(await maintenance);
    }

    [Fact]
    public async Task StateSlot_CancelledMaintenanceSettlesOnlyItsRequest()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        BrowserNavigationStateSlot slot = fixture.Slot;
        using var cancellation = new CancellationTokenSource();
        Task<NavigationConsumerResult?> cancelled =
            slot.RefreshAsync(
                fixture.Prepare,
                cancellation.Token).AsTask();
        Task<NavigationConsumerResult?> next =
            slot.RefreshAsync(
                fixture.Prepare,
                TestContext.Current.CancellationToken).AsTask();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelled);
        Assert.False(next.IsCompleted);
        fixture.AcknowledgeInitialization();
        NavigationConsumerResult result = Assert.IsType<
            NavigationConsumerResult>(await next);
        Assert.Equal(
            NavigationOperationKind.Maintenance,
            result.Operation);
    }

    [Fact]
    public async Task StateSlot_CallerCancellationReturnsTypedAbort()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        BrowserNavigationStateSlot slot = fixture.Slot;
        fixture.AcknowledgeInitialization();
        NavigationAction action = Assert.IsType<NavigationAction>(
            slot.Initialization.Snapshot.Types
                .First(type => type.Navigation.Action is not null)
                .Navigation.Action);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        ValueTask<NavigationPreparation> ObserveCancellation(
            NavigationEvaluationRequest _,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "The cancelled prerequisite was evaluated.");
        }

        NavigationConsumerResult aborted = Assert.IsType<
            NavigationConsumerResult>(
                await slot.ExecuteAsync(
                action,
                ObserveCancellation,
                cancellation.Token));

        Assert.Equal(NavigationOutcomeKind.Aborted, aborted.Outcome.Kind);
        Assert.NotNull(aborted.Authority);
        Assert.True(slot.ValidateAuthority(aborted.Authority));
    }

    sealed class Fixture : IAsyncDisposable
    {
        readonly NavigationEvaluationFacts _facts;
        readonly PackageAssemblyContextRealization _realization;

        Fixture(
            InspectionWorkspace workspace,
            NavigationEvaluationFacts facts,
            PackageAssemblyContextRealization realization,
            BrowserNavigationStateSlot slot)
        {
            Workspace = workspace;
            _facts = facts;
            _realization = realization;
            Slot = slot;
        }

        internal InspectionWorkspace Workspace { get; }
        internal BrowserNavigationStateSlot Slot { get; }

        internal static async Task<Fixture> CreateAsync()
        {
            PackageRootBinding binding = Binding();
            var workspace = new InspectionWorkspace();
            WorkspaceScopeSnapshot scope = await ReplaceAsync(
                workspace,
                binding);
            PackageAssemblyContextRealization realization =
                workspace.RealizePackageAssemblyContextRoles(
                    [binding.Root]);
            ImmutableArray<NavigationLibraryEvaluation> libraries =
            [
                .. realization.SurfaceParticipants.Select(
                    participant => new NavigationLibraryEvaluation(
                        scope.Packages[0].Occurrence.Package.Coordinate,
                        participant)),
            ];
            AssemblyContextApiSurfaceResult surface =
                AssemblyContextApiSurfaceQuery.ExecuteBounded(
                    realization.SurfaceGroup,
                    ApiSurfaceScope.Public,
                    BrowserApiSurfacePolicy.Limits,
                    [
                        .. libraries.Select(
                            library => library.Library.Participant),
                    ]);
            var package = new NavigationPackageEvaluation(
                scope.Packages[0],
                binding,
                libraries,
                surface);
            ViewFacetRegistry registry =
                InspectionViewFacetCatalog.Registry;
            var availability = new ViewFacetAvailabilitySnapshot(
                registry.Descriptors.Select(
                    descriptor => new ViewFacetAvailabilityFact(
                        descriptor.Id,
                        ViewFacetAvailability.Available.Instance)));
            var facts = new NavigationEvaluationFacts(
                scope,
                package,
                (_, _) => availability);
            NavigationOperationInitialization initialization =
                NavigationTransitions.Initialize(
                    workspace.Identity,
                    facts,
                    registry);
            return new Fixture(
                workspace,
                facts,
                realization,
                new BrowserNavigationStateSlot(
                    initialization,
                    registry));
        }

        internal ValueTask<NavigationPreparation> Prepare(
            NavigationEvaluationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Ready(request));
        }

        internal NavigationPreparation Ready(
            NavigationEvaluationRequest request)
        {
            Assert.Same(Workspace.Identity, request.Workspace);
            Assert.Same(
                _facts.Package!.Occurrence.Occurrence,
                request.Occurrence);
            return new NavigationPreparation.Ready(_facts);
        }

        internal void AcknowledgeInitialization()
        {
            NavigationEffectAuthority authority =
                Slot.Initialization.Authority!;
            Assert.Equal(
                NavigationAuthorityResult.Accepted,
                Slot.RecordConsumerPosting(authority));
            Assert.Equal(
                NavigationAuthorityResult.Accepted,
                Slot.Acknowledge(authority));
        }

        public async ValueTask DisposeAsync()
        {
            Slot.Retire();
            _realization.Dispose();
            await Workspace.CloseAsync();
        }

        static async Task<WorkspaceScopeSnapshot> ReplaceAsync(
            InspectionWorkspace workspace,
            PackageRootBinding binding)
        {
            WorkspaceScopeReadResult read =
                await workspace.GetScopeSnapshotAsync();
            WorkspaceScopeSnapshot initial =
                Assert.IsType<WorkspaceScopeReadResult.Available>(
                    read).Snapshot;
            WorkspaceScopeOperationResult replaced =
                await workspace.ReplaceScopeAsync(
                    initial.Revision,
                    [binding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken);
            return Assert.IsType<
                WorkspaceScopeOperationResult.Committed>(
                    replaced).Snapshot;
        }

        static PackageRootBinding Binding()
        {
            byte[] assembly = File.ReadAllBytes(
                typeof(BrowserNavigationStateSlotTests).Assembly.Location);
            using var bytes = new MemoryStream();
            using (var archive = new ZipArchive(
                bytes,
                ZipArchiveMode.Create,
                leaveOpen: true))
            {
                using Stream entry = archive.CreateEntry(
                    "lib/net11.0/Browser.Navigation.dll").Open();
                entry.Write(assembly);
            }
            return PackageRootBinding.CreateFromSource(
                new AcquiredPackageSourcePayload(
                    PackageSourceCoordinate.Create(
                        "Browser.Navigation",
                        "1.0.0"),
                    new InMemoryPackageContent(
                        bytes.ToArray(),
                        fromCache: false,
                        producerKey: "fixture"),
                    "fixture",
                    PackagePayloadOrigin.Download),
                "net11.0");
        }
    }
}
