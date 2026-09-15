using System.Reflection;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    [Fact]
    public async Task ResidentLocator_IsLazyAndReusesInventoriesAcrossPatternsAndVisibility()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(workspace,
            LocatorImage("Visibility", metadata =>
            {
                LocatorDefinition(metadata, "N", "Widget");
                LocatorDefinition(metadata, "N", "Hidden", TypeAttributes.NotPublic);
            }));
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator();
        Assert.False(locator.IsActive);
        Assert.Equal(0, locator.InventoryReadCount);
        Assert.True(locator.Maintenance.IsCompletedSuccessfully);
        Assert.Same(locator, workspace.GetDeclarationLocator());

        var first = await ResidentFind(locator);
        var again = await ResidentFind(locator);
        var all = Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            await locator.ExecuteAsync([new TypeDeclarationLocatorRequest.Pattern("*")],
                includeAll: true, TestContext.Current.CancellationToken));
        await locator.Maintenance;

        Assert.True(locator.IsActive);
        Assert.Equal(1, locator.InventoryReadCount);
        Assert.Equal(1, locator.RetainedInventoryCount);
        Assert.Same(first.Population.Identity, again.Population.Identity);
        Assert.Equal(["Hidden", "Widget"], all.Answers[0].Candidates.Select(candidate => candidate.Name.Segments[0]));
        var captured = Assert.IsType<WorkspaceDeclarationPopulationCapture.Captured>(
            workspace.CaptureObservedDeclarationPopulation()).Population;
        Assert.Same(first.Population.Identity, captured.Receipt.Identity);
        AssertEquivalent(Locate(captured, new TypeDeclarationLocatorRequest.Pattern("Widget")), first);

        _ = workspace.CreateAssemblyContextGroup(ContextLoaded(context).Group.Participants);
        locator.PopulationChanged();
        Assert.Same(first.Population.Identity, (await ResidentFind(locator)).Population.Identity);
        Assert.Equal(1, locator.InventoryReadCount);
    }

    [Fact]
    public async Task ResidentLocator_AppendDuringInitializationMaintainsWithoutAnotherFindAndPinsReceipt()
    {
        await using var workspace = new InspectionWorkspace();
        byte[] image = LocatorImage("Same", metadata => LocatorDefinition(metadata, "N", "Widget"));
        WorkspaceDeclarationContext firstContext = await LocatorContext(workspace, image);
        var pause = new LocatorYieldPause();
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator(null, pause.YieldAsync);
        Task<TypeDeclarationLocatorResult.Evaluated> first = ResidentFind(locator);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        try
        {
            _ = await LocatorContext(workspace, image);
            locator.PopulationChanged();
            locator.PopulationChanged();
            Assert.False(first.IsCompleted);
        }
        finally
        {
            pause.Resume.TrySetResult();
        }
        await locator.Maintenance.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, locator.InventoryReadCount);
        var p1 = await first;
        Assert.Single(p1.Population.Contexts);
        Assert.Single(p1.Answers[0].Candidates);
        AssertEquivalent(Locate(CaptureDeclarations(workspace, firstContext),
            new TypeDeclarationLocatorRequest.Pattern("Widget")), p1);

        var p2 = await ResidentFind(locator);
        Assert.Equal(2, p2.Population.Contexts.Length);
        Assert.NotSame(p1.Population.Identity, p2.Population.Identity);
        Assert.Equal(2, p2.Answers[0].Candidates.Length);
        Assert.NotSame(p2.Answers[0].Candidates[0].Observation.Occurrence,
            p2.Answers[0].Candidates[1].Observation.Occurrence);
        Assert.Equal(2, locator.InventoryReadCount);
        var current = Assert.IsType<WorkspaceDeclarationPopulationCapture.Captured>(
            workspace.CaptureObservedDeclarationPopulation()).Population;
        Assert.Same(p2.Population.Identity, current.Receipt.Identity);
        AssertEquivalent(Locate(current, new TypeDeclarationLocatorRequest.Pattern("Widget")), p2);
    }

    [Fact]
    public async Task ResidentLocator_LoadStartedBeforeActivationIsObservedAfterCommit()
    {
        byte[] image = LocatorImage("Late", metadata => LocatorDefinition(metadata, "N", "Widget"));
        await using var workspace = new InspectionWorkspace();
        using var handler = new PausedPopulationHandler(Archive(("lib/net10.0/Late.dll", image)));
        using var client = new HttpClient(handler);
        Task<WorkspaceDeclarationContext> loading = WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace, new() { Framework = Framework, Members = [PackageMember(Version)] },
            Options(client, new InMemoryPackageStore()), TestContext.Current.CancellationToken);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator();
        try
        {
            var empty = await ResidentFind(locator);
            Assert.Empty(empty.Population.Contexts);
            Assert.True(empty.Answers[0].IsComplete);
            Assert.Equal(0, locator.InventoryReadCount);
        }
        finally
        {
            handler.Resume.TrySetResult();
        }
        _ = await loading;
        await locator.Maintenance.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, locator.InventoryReadCount);
        Assert.Single((await ResidentFind(locator)).Answers[0].Candidates);
    }

    [Fact]
    public async Task ResidentLocator_LateEarlierContextDoesNotTreatReceiptGrowthAsAnOrdinalSuffix()
    {
        byte[] image = LocatorImage("Order", metadata => LocatorDefinition(metadata, "N", "Widget"));
        await using var workspace = new InspectionWorkspace();
        using var handler = new PausedPopulationHandler(Archive(("lib/net10.0/Order.dll", image)));
        using var client = new HttpClient(handler);
        Task<WorkspaceDeclarationContext> earlier = WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace, new() { Framework = Framework, Members = [PackageMember(Version)] },
            Options(client, new InMemoryPackageStore()), TestContext.Current.CancellationToken);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        WorkspaceDeclarationContext later = await LocatorContext(workspace, image);
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator();
        TypeDeclarationLocatorResult.Evaluated p1;
        try
        {
            p1 = await ResidentFind(locator);
            Assert.Same(later.Receipt, Assert.Single(p1.Population.Contexts));
        }
        finally
        {
            handler.Resume.TrySetResult();
        }
        WorkspaceDeclarationContext completedEarlier = await earlier;
        await locator.Maintenance.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, locator.InventoryReadCount);
        var p2 = await ResidentFind(locator);
        Assert.Equal([completedEarlier.Receipt, later.Receipt], p2.Population.Contexts);
        Assert.Same(p1.Answers[0].Candidates[0].Observation, p2.Answers[0].Candidates[1].Observation);
        Assert.Equal(2, locator.InventoryReadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResidentLocator_CallerCancellationDoesNotCancelSharedOrLaterMaintenance(bool anotherCaller)
    {
        await using var workspace = new InspectionWorkspace();
        byte[] image = LocatorImage("Shared", metadata => LocatorDefinition(metadata, "N", "Widget"));
        _ = await LocatorContext(workspace, image);
        var pause = new LocatorYieldPause();
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator(null, pause.YieldAsync);
        using var cancelled = new CancellationTokenSource();
        Task<TypeDeclarationLocatorResult> first = locator.ExecuteAsync(
            [new TypeDeclarationLocatorRequest.Pattern("Widget")], cancellationToken: cancelled.Token);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Task<TypeDeclarationLocatorResult.Evaluated>? second = anotherCaller ? ResidentFind(locator) : null;
        try
        {
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            if (second is not null)
                Assert.False(second.IsCompleted);
            Assert.False(locator.Maintenance.IsCompleted);
        }
        finally
        {
            pause.Resume.TrySetResult();
        }
        if (second is not null)
            Assert.Single((await second).Answers[0].Candidates);
        await locator.Maintenance;
        Assert.Equal(1, locator.InventoryReadCount);
        _ = await LocatorContext(workspace, image);
        await locator.Maintenance.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, locator.InventoryReadCount);
    }

    [Theory]
    [InlineData(1, 10, 1, 2, WorkspaceDeclarationInventoryBound.ReadAttempts)]
    [InlineData(10, 1, 1, 1, WorkspaceDeclarationInventoryBound.RetainedInventories)]
    [InlineData(0, 10, 0, 0, WorkspaceDeclarationInventoryBound.ReadAttempts)]
    [InlineData(10, 0, 0, 0, WorkspaceDeclarationInventoryBound.RetainedInventories)]
    public async Task ResidentLocator_BoundsStayVisibleAndExplicitRetryDoesNotRescanHealthyEntries(
        int reads, int retained, int firstCount, int retryCount, WorkspaceDeclarationInventoryBound bound)
    {
        await using var workspace = new InspectionWorkspace();
        _ = await LocatorContext(workspace,
            LocatorImage("First", metadata => LocatorDefinition(metadata, "N", "Widget")),
            LocatorImage("Second", metadata => LocatorDefinition(metadata, "N", "Widget")));
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator(new()
        {
            MaxInventoryReadsPerAttempt = reads,
            MaxRetainedInventories = retained,
        });
        Assert.Same(locator, workspace.GetDeclarationLocator());
        var first = await ResidentFind(locator);
        Assert.Equal(firstCount, first.Answers[0].Candidates.Length);
        Assert.False(first.Answers[0].IsComplete);
        Assert.Equal(bound, Assert.IsType<TypeDeclarationLocatorMemberOutcome.NotEvaluated>(first.Members[^1]).Bound);
        Assert.Equal(firstCount, locator.InventoryReadCount);
        var retry = await ResidentFind(locator);
        Assert.Equal(retryCount, retry.Answers[0].Candidates.Length);
        Assert.Equal(retryCount, locator.InventoryReadCount);
        Assert.Equal(retryCount == 2, retry.Answers[0].IsComplete);
        Assert.Same(first.Population.Identity, retry.Population.Identity);
    }

    [Fact]
    public async Task ResidentLocator_ImmutableRejectionsAreResidentAndUpstreamGapsRemainVisible()
    {
        await using var workspace = new InspectionWorkspace();
        _ = await LocatorContext(workspace,
            LocatorImage("Healthy", metadata => LocatorDefinition(metadata, "N", "Widget")),
            LocatorImage("Rejected", metadata =>
            {
                LocatorDefinition(metadata, "N", "Duplicate");
                LocatorDefinition(metadata, "N", "Duplicate");
            }));
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator();
        var first = await ResidentFind(locator);
        Assert.Single(first.Answers[0].Candidates);
        Assert.False(first.Answers[0].IsComplete);
        Assert.IsType<TypeDeclarationLocatorMemberOutcome.InventoryRejected>(first.Members[1]);
        _ = await ResidentFind(locator);
        Assert.Equal(2, locator.InventoryReadCount);
        Assert.Equal(2, locator.RetainedInventoryCount);

        using var client = new HttpClient(new NotFoundHandler());
        WorkspaceDeclarationContext failed = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace,
            new() { Framework = Framework, Members = [WorkspaceMemberCoordinate.Package("missing", Version)] },
            Options(client, new InMemoryPackageStore()), TestContext.Current.CancellationToken);
        Assert.IsType<WorkspaceContextLoadOutcome.Failed>(failed.ContextLoadOutcome);
        var incomplete = await ResidentFind(locator);
        Assert.Equal(2, incomplete.Population.Contexts.Length);
        Assert.False(incomplete.Answers[0].IsRealizationComplete);
        Assert.Single(incomplete.Answers[0].Candidates);
        Assert.Equal(2, locator.InventoryReadCount);
    }

    [Fact]
    public async Task ResidentLocator_UnsupportedCoordinateRemainsAnUnscannedObservation()
    {
        await using var workspace = new InspectionWorkspace();
        byte[] image = File.ReadAllBytes(EmbeddedPath);
        using var client = new HttpClient(new FailingHandler());
        _ = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace, new() { Members = [EmbeddedMember(image)] },
            Options(client, new InMemoryPackageStore(), new StubEmbeddedContent(image)),
            TestContext.Current.CancellationToken);
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator();
        var result = await ResidentFind(locator);
        Assert.IsType<TypeDeclarationLocatorMemberOutcome.CoordinateUnavailable>(Assert.Single(result.Members));
        Assert.False(result.Answers[0].IsComplete);
        Assert.Equal(0, locator.InventoryReadCount);
    }

    [Fact]
    public async Task ResidentLocator_CloseDrainsScheduledWorkAndPreservesDetachedAnswers()
    {
        var workspace = new InspectionWorkspace();
        byte[] image = LocatorImage("Close", metadata => LocatorDefinition(metadata, "N", "Widget"));
        WorkspaceDeclarationContext context = await LocatorContext(workspace, image);
        var pause = new LocatorYieldPause(pauseAt: 2);
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator(null, pause.YieldAsync);
        var first = await ResidentFind(locator);
        WorkspaceDeclarationContext added = await LocatorContext(workspace, image);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Task<TypeDeclarationLocatorResult.Evaluated> pending = ResidentFind(locator);
        Task<InspectionWorkspaceCloseReport> close = workspace.CloseAsync();
        try
        {
            Assert.False(close.IsCompleted);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        finally
        {
            pause.Resume.TrySetResult();
        }
        Assert.True((await close).Succeeded);
        Assert.Equal(1, locator.InventoryReadCount);
        Assert.Equal(0, locator.RetainedInventoryCount);
        Assert.False(locator.IsActive);
        Assert.Equal(0, ContextLoaded(context).Group.RetainedImageBytes);
        Assert.Equal(0, ContextLoaded(added).Group.RetainedImageBytes);
        Assert.Single(first.Answers[0].Candidates);
        var unavailable = Assert.IsType<TypeDeclarationLocatorResult.Rejected>(
            await locator.ExecuteAsync([new TypeDeclarationLocatorRequest.Pattern("Widget")],
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceDeclarationPopulationFailure.WorkspaceClosed, unavailable.PopulationFailure);
        locator.PopulationChanged();
        Assert.Equal(1, locator.InventoryReadCount);
    }

    [Fact]
    public async Task ResidentLocator_CloseWaitsForActualBorrowedInventoryAccess()
    {
        var workspace = new InspectionWorkspace();
        byte[] image = LocatorImage("Borrowed", metadata => LocatorDefinition(metadata, "N", "Widget"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        WorkspaceDeclarationContext context = await ControlledResidentContext(workspace, image, () =>
        {
            entered.TrySetResult();
            resume.Wait(TestContext.Current.CancellationToken);
            return new MemoryStream(image, writable: false);
        });
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator();
        Task<TypeDeclarationLocatorResult.Evaluated> query = ResidentFind(locator);
        Task<InspectionWorkspaceCloseReport>? close = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            close = workspace.CloseAsync();
            Assert.False(close.IsCompleted);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        }
        finally
        {
            resume.Set();
            await workspace.CloseAsync();
        }
        Assert.NotNull(close);
        Assert.True((await close).Succeeded);
        Assert.Equal(1, locator.InventoryReadCount);
        Assert.Equal(0, ContextLoaded(context).Group.RetainedImageBytes);
        Assert.Equal(0, locator.RetainedInventoryCount);
    }

    [Fact]
    public async Task ResidentLocator_OperationalRejectionIsRetriedWithoutRescanningHealthyInventory()
    {
        await using var workspace = new InspectionWorkspace();
        _ = await LocatorContext(workspace,
            LocatorImage("Healthy", metadata => LocatorDefinition(metadata, "N", "Widget")));
        _ = await ControlledResidentContext(workspace,
            LocatorImage("Unavailable", metadata => LocatorDefinition(metadata, "N", "Widget")),
            () => throw new IOException("Fixture source is unavailable."));
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator();
        var first = await ResidentFind(locator);
        Assert.IsType<TypeDeclarationLocatorMemberOutcome.AccessRejected>(first.Members[1]);
        Assert.False(first.Answers[0].IsComplete);
        Assert.Single(first.Answers[0].Candidates);
        Assert.Equal(2, locator.InventoryReadCount);
        Assert.Equal(1, locator.RetainedInventoryCount);
        var retry = await ResidentFind(locator);
        Assert.IsType<TypeDeclarationLocatorMemberOutcome.AccessRejected>(retry.Members[1]);
        Assert.Equal(3, locator.InventoryReadCount);
        Assert.Equal(1, locator.RetainedInventoryCount);
        Assert.Single(retry.Answers[0].Candidates);
    }

    [Fact]
    public async Task ResidentLocator_UnexpectedMaintenanceFailureStaysFaultedAndCloseStillReleases()
    {
        var workspace = new InspectionWorkspace();
        byte[] image = LocatorImage("Fault", metadata => LocatorDefinition(metadata, "N", "Widget"));
        WorkspaceDeclarationContext firstContext = await LocatorContext(workspace, image);
        var failure = new InvalidOperationException("Maintenance scheduling failed.");
        int yields = 0;
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator(null, () =>
            Interlocked.Increment(ref yields) == 1
                ? ValueTask.CompletedTask
                : ValueTask.FromException(failure));
        var first = await ResidentFind(locator);
        _ = await LocatorContext(workspace, image);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => locator.Maintenance));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => ResidentFind(locator)));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CloseAsync()));
        Assert.NotNull(workspace.CloseReport);
        Assert.Equal(0, ContextLoaded(firstContext).Group.RetainedImageBytes);
        Assert.Equal(0, locator.RetainedInventoryCount);
        Assert.Single(first.Answers[0].Candidates);
    }

    [Fact]
    public async Task ResidentLocator_InvalidRequestsDoNotActivateOrChangeFixedLimits()
    {
        await using var workspace = new InspectionWorkspace();
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.GetDeclarationLocator(
            new() { MaxRetainedInventories = -1 }));
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator();
        Assert.Throws<InvalidOperationException>(() => workspace.GetDeclarationLocator(
            new() { MaxRetainedInventories = 1 }));
        Assert.IsType<TypeDeclarationLocatorResult.Rejected>(await locator.ExecuteAsync(
            [], cancellationToken: TestContext.Current.CancellationToken));
        Assert.IsType<TypeDeclarationLocatorResult.Rejected>(
            await locator.ExecuteAsync([new TypeDeclarationLocatorRequest.Pattern(" ")],
                cancellationToken: TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => locator.ExecuteAsync(
            [new TypeDeclarationLocatorRequest.Pattern("*")], cancellationToken: cancelled.Token));
        Assert.False(locator.IsActive);
        Assert.Equal(0, locator.InventoryReadCount);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ResidentLocator_RealJsonAppendMaintainsDistinctOriginsAndForwarderChoices()
    {
        await using var workspace = new InspectionWorkspace();
        using var client = new HttpClient();
        var options = Options(client, new InMemoryPackageStore());
        _ = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace,
            new() { Framework = Framework, Members = [WorkspaceMemberCoordinate.Package("System.Text.Json", "10.0.0")] },
            options, TestContext.Current.CancellationToken);
        WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator();
        Assert.Equal(0, locator.InventoryReadCount);
        TypeDeclarationLocatorRequest[] requests =
        [
            new TypeDeclarationLocatorRequest.Pattern("System.Text.Json.JsonSerializer"),
            new TypeDeclarationLocatorRequest.Pattern("System.Object"),
        ];
        var p1 = await ResidentFind(locator, requests);
        Assert.Single(p1.Answers[0].Candidates);
        Assert.Equal(1, locator.InventoryReadCount);
        _ = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace, new()
            {
                Framework = Framework,
                Members =
                [
                    WorkspaceMemberCoordinate.Platform("runtime", "System.Text.Json", "10.0.10"),
                    WorkspaceMemberCoordinate.Platform("runtime", "netstandard", "10.0.10"),
                    WorkspaceMemberCoordinate.Platform("runtime", "System.Private.CoreLib", "10.0.10"),
                ],
            }, options, TestContext.Current.CancellationToken);
        await locator.Maintenance.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, locator.InventoryReadCount);
        var p2 = await ResidentFind(locator, requests);
        Assert.All(p2.Answers, answer => Assert.True(answer.IsComplete));
        Assert.Equal(2, p2.Answers[0].Candidates.Length);
        Assert.IsType<ExactLibrarySourceCoordinate.Package>(p2.Answers[0].Candidates[0].Coordinate);
        Assert.IsType<ExactLibrarySourceCoordinate.Platform>(p2.Answers[0].Candidates[1].Coordinate);
        Assert.Equal([AssemblyTypeDeclarationKind.Forwarder, AssemblyTypeDeclarationKind.Definition],
            p2.Answers[1].Candidates.Select(candidate => candidate.Kind));
        Assert.Equal(4, locator.InventoryReadCount);
        var current = Assert.IsType<WorkspaceDeclarationPopulationCapture.Captured>(
            workspace.CaptureObservedDeclarationPopulation()).Population;
        AssertEquivalent(Locate(current, requests), p2);
        await workspace.CloseAsync();
        Assert.Single(p1.Answers[0].Candidates);
        Assert.Equal(2, p2.Answers[0].Candidates.Length);
    }

    static async Task<TypeDeclarationLocatorResult.Evaluated> ResidentFind(
        WorkspaceDeclarationLocator locator, params TypeDeclarationLocatorRequest[] requests) =>
        Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(await locator.ExecuteAsync(
            requests.Length == 0 ? [new TypeDeclarationLocatorRequest.Pattern("Widget")] : [.. requests],
            cancellationToken: TestContext.Current.CancellationToken));

    static async Task<WorkspaceDeclarationContext> ControlledResidentContext(
        InspectionWorkspace workspace, byte[] image, Func<Stream> open)
    {
        int order = workspace.BeginDeclarationContext();
        await using var producer = new InspectionWorkspace();
        WorkspaceDeclarationContext acquired = await LocatorContext(producer, image);
        WorkspaceContextMember source = Assert.Single(ContextLoaded(acquired).Members);
        // Preserve loader-issued correspondence; defer the same fixture image to
        // the group opener so lifetime/failure gates exercise actual scoped access.
        var assembly = ResolvedAssemblyReference.Create(source.Participant.Assembly.Identity,
            path: null, open, source.Participant.Assembly.Provenance);
        var participant = new AssemblyContextParticipant(assembly, NoResolverAssemblyBindingPolicy.Instance);
        AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([participant]);
        return workspace.CompleteDeclarationContext(order, ContextRequest(acquired),
            new WorkspaceContextLoadOutcome.Loaded(
                group, [source with { Participant = participant }], [], Framework, runtimeIdentifier: null));
    }

    static void AssertEquivalent(
        TypeDeclarationLocatorResult.Evaluated expected, TypeDeclarationLocatorResult.Evaluated actual)
    {
        Assert.Equal(expected.Population.Contexts, actual.Population.Contexts);
        Assert.Equal(expected.IncludeAll, actual.IncludeAll);
        Assert.Equal(expected.Members, actual.Members);
        Assert.Equal(expected.Answers.Length, actual.Answers.Length);
        foreach (var (left, right) in expected.Answers.Zip(actual.Answers))
        {
            Assert.Equal(left.Request, right.Request);
            Assert.Equal(left.IsRealizationComplete, right.IsRealizationComplete);
            Assert.Equal(left.IsEvaluationComplete, right.IsEvaluationComplete);
            Assert.Equal(left.Candidates.Select(candidate =>
                    (candidate.Coordinate, candidate.Name, candidate.Kind, candidate.Observation)),
                right.Candidates.Select(candidate =>
                    (candidate.Coordinate, candidate.Name, candidate.Kind, candidate.Observation)));
        }
    }

    sealed class LocatorYieldPause(int pauseAt = 1)
    {
        int _calls;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async ValueTask YieldAsync()
        {
            if (Interlocked.Increment(ref _calls) == pauseAt)
            {
                Entered.TrySetResult();
                await Resume.Task;
            }
            else
            {
                await Task.Yield();
            }
        }
    }
}
