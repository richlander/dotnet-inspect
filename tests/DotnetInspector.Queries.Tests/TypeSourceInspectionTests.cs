using System.Collections.Immutable;
using System.Net;

using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    // PR-fast: the completed desktop operation preserves immediate authored
    // source when the PDB is ready inside the initial window.
    [Fact]
    public async Task
        TypeSourceLatencyHedge_ImmediateAuthoredSourceRemainsPreferred()
    {
        TestAssembly assembly =
            TestAssembly.Create(
                fixture:
                    FixtureCatalog.SourceDiffV1);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourcePairBytes(
                FixtureCatalog.SourceDiffV1));
        var time = new ObservedFakeTimeProvider();
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<AssemblyTypeSourceEntry>
            inspection =
                await TypeSourceInspection
                    .ExecuteWithLatencyHedgeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest("Counter"),
                        host.Context,
                        new TypeSourceLatencyHedge(
                            TimeSpan.FromSeconds(1),
                            TimeSpan.FromMilliseconds(250),
                            time),
                        TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<
                AssemblyTypeSourceEntry.Available>(
                    inspection.Content);
        Assert.IsType<AssemblyTypeSource.Pdb>(
            available.Source);
        TypeSourceLatencyHedgeEvidence evidence =
            Assert.IsType<
                TypeSourceLatencyHedgeEvidence>(
                    available.LatencyHedgeEvidence);
        Assert.True(
            evidence
                .PdbReadyBeforeDecompilation);
        Assert.True(
            evidence.Selection
                is TypeSourceLatencyHedgeSelection
                    .AuthoredBeforeDecompilation
                or TypeSourceLatencyHedgeSelection
                    .AuthoredAfterDecompilation);
        Assert.Single(host.SourceRequests);
    }

    // PR-fast: a ready PDB contributes symbols to speculative decompilation,
    // and an authored-source stall is bounded by the grace window.
    [Fact]
    public async Task
        TypeSourceLatencyHedge_SourceStallPublishesPdbAssistedDecompilation()
    {
        TestAssembly assembly =
            TestAssembly.Create(
                fixture:
                    FixtureCatalog.SourceDiffV1);
        var sourceEntered =
            new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);
        var sourceRelease =
            new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);
        var sourceCancelled =
            new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourcePairBytes(
                FixtureCatalog.SourceDiffV1),
            beforeSourceResponse:
                async cancellationToken =>
                {
                    sourceEntered.TrySetResult();
                    try
                    {
                        await sourceRelease.Task
                            .WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        sourceCancelled.TrySetResult();
                        throw;
                    }
                });
        var time = new ObservedFakeTimeProvider();
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        Task<InspectionEnvelope<AssemblyTypeSourceEntry>>
            operation =
                TypeSourceInspection
                    .ExecuteWithLatencyHedgeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest("Counter"),
                        host.Context,
                        new TypeSourceLatencyHedge(
                            TimeSpan.FromSeconds(1),
                            TimeSpan.FromMilliseconds(250),
                            time),
                        TestContext.Current.CancellationToken);
        await sourceEntered.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        await time.WaitForTimerAsync(
            TimeSpan.FromMilliseconds(250),
            TestContext.Current.CancellationToken);
        time.Advance(
            TimeSpan.FromMilliseconds(250));
        await sourceCancelled.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        InspectionEnvelope<AssemblyTypeSourceEntry>
            inspection = await operation;

        var available =
            Assert.IsType<
                AssemblyTypeSourceEntry.Available>(
                    inspection.Content);
        var source =
            Assert.IsType<
                AssemblyTypeSource.Decompiled>(
                    available.Source);
        Assert.True(
            source.Decompilation.PdbSupplied);
        Assert.Equal(
            PdbTypeSourceOutcome
                .AuthoredSourcePreferenceWindowElapsed,
            source.PdbAttempt.Outcome);
        Assert.Equal(
            new TypeSourceLatencyHedgeEvidence(
                PdbReadyBeforeDecompilation:
                    true,
                DecompilationStarted: true,
                DecompilationUsedPdb: true,
                TypeSourceLatencyHedgeSelection
                    .DecompiledAfterPreferenceWindow),
            available.LatencyHedgeEvidence);
    }

    // PR-fast: when the initial PDB window elapses, no-PDB decompilation
    // begins while acquisition remains live; authored source may still win
    // the post-decompilation preference window.
    [Fact]
    public async Task
        TypeSourceLatencyHedge_LatePdbAuthoredSourceWinsGraceWindow()
    {
        TestAssembly assembly =
            TestAssembly.Create(
                fixture:
                    FixtureCatalog.SourceDiffV1);
        var symbolEntered =
            new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);
        var symbolRelease =
            new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourcePairBytes(
                FixtureCatalog.SourceDiffV1),
            beforeSymbolResponse:
                async cancellationToken =>
                {
                    symbolEntered.TrySetResult();
                    await symbolRelease.Task
                        .WaitAsync(cancellationToken);
                });
        var time = new ObservedFakeTimeProvider();
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        Task<InspectionEnvelope<AssemblyTypeSourceEntry>>
            operation =
                TypeSourceInspection
                    .ExecuteWithLatencyHedgeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest("Counter"),
                        host.Context,
                        new TypeSourceLatencyHedge(
                            TimeSpan.FromSeconds(1),
                            TimeSpan.FromMilliseconds(250),
                            time),
                        TestContext.Current.CancellationToken);
        await symbolEntered.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        await time.WaitForTimerAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        time.Advance(
            TimeSpan.FromSeconds(1));
        await time.WaitForTimerAsync(
            TimeSpan.FromMilliseconds(250),
            TestContext.Current.CancellationToken);
        symbolRelease.TrySetResult();

        InspectionEnvelope<AssemblyTypeSourceEntry>
            inspection = await operation;

        var available =
            Assert.IsType<
                AssemblyTypeSourceEntry.Available>(
                    inspection.Content);
        Assert.IsType<AssemblyTypeSource.Pdb>(
            available.Source);
        Assert.Equal(
            new TypeSourceLatencyHedgeEvidence(
                PdbReadyBeforeDecompilation:
                    false,
                DecompilationStarted: true,
                DecompilationUsedPdb: false,
                TypeSourceLatencyHedgeSelection
                    .AuthoredAfterDecompilation),
            available.LatencyHedgeEvidence);
        Assert.Single(host.SourceRequests);
    }

    // PR-fast: an upstream PDB stall cannot hold an available decompilation
    // beyond the initial and authored preference windows, but binding-policy
    // invalidation retains terminal precedence before publication.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        TypeSourceLatencyHedge_PdbStallPublishesNoPdbDecompilation(
            bool rotateBindingPolicy)
    {
        TestAssembly assembly =
            TestAssembly.Create(
                fixture:
                    FixtureCatalog.SourceDiffV1);
        var symbolEntered =
            new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);
        var symbolRelease =
            new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);
        var symbolCancelled =
            new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourcePairBytes(
                FixtureCatalog.SourceDiffV1),
            beforeSymbolResponse:
                async cancellationToken =>
                {
                    symbolEntered.TrySetResult();
                    try
                    {
                        await symbolRelease.Task
                            .WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        symbolCancelled.TrySetResult();
                        throw;
                    }
                });
        var time = new ObservedFakeTimeProvider();
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        Task<InspectionEnvelope<AssemblyTypeSourceEntry>>
            operation =
                TypeSourceInspection
                    .ExecuteWithLatencyHedgeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest("Counter"),
                        host.Context,
                        new TypeSourceLatencyHedge(
                            TimeSpan.FromSeconds(1),
                            TimeSpan.FromMilliseconds(250),
                            time),
                        TestContext.Current.CancellationToken);
        await symbolEntered.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        await time.WaitForTimerAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        time.Advance(
            TimeSpan.FromSeconds(1));
        await time.WaitForTimerAsync(
            TimeSpan.FromMilliseconds(250),
            TestContext.Current.CancellationToken);
        if (rotateBindingPolicy)
        {
            assembly.Policy.ChangeVersion();
        }
        time.Advance(
            TimeSpan.FromMilliseconds(250));
        await symbolCancelled.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        if (rotateBindingPolicy)
        {
            InspectionEnvelope<AssemblyTypeSourceEntry>
                invalidated = await operation;
            var unavailable =
                Assert.IsType<
                    AssemblyTypeSourceEntry.Unavailable>(
                        invalidated.Content);
            InvalidOperationException error =
                Assert.IsType<InvalidOperationException>(
                    unavailable.Failure.Error);
            Assert.Contains(
                "binding-policy snapshot changed",
                error.Message);
            Assert.Empty(host.SourceRequests);
            return;
        }

        InspectionEnvelope<AssemblyTypeSourceEntry>
            inspection = await operation;

        var available =
            Assert.IsType<
                AssemblyTypeSourceEntry.Available>(
                    inspection.Content);
        var source =
            Assert.IsType<
                AssemblyTypeSource.Decompiled>(
                    available.Source);
        Assert.False(
            source.Decompilation.PdbSupplied);
        Assert.Equal(
            PdbTypeSourceOutcome
                .AuthoredSourcePreferenceWindowElapsed,
            source.PdbAttempt.Outcome);
        Assert.Equal(
            new TypeSourceLatencyHedgeEvidence(
                PdbReadyBeforeDecompilation:
                    false,
                DecompilationStarted: true,
                DecompilationUsedPdb: false,
                TypeSourceLatencyHedgeSelection
                    .DecompiledAfterPreferenceWindow),
            available.LatencyHedgeEvidence);
        Assert.Empty(host.SourceRequests);
    }

    // PR-fast: independent authored and decompilation admissions retain both
    // terminal results when their distinct Library limits reject the assembly.
    [Fact]
    public async Task
        TypeSourceLatencyHedge_DualLibraryFailuresRemainDistinct()
    {
        TestAssembly assembly =
            TestAssembly.Create(
                fixture:
                    FixtureCatalog.SourceDiffV1);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourcePairBytes(
                FixtureCatalog.SourceDiffV1));
        SourceHouseLimits defaults =
            host.Context.TypeSourceLimits;
        var context =
            new AssemblyContextSourceQueryContext(
                host.Context.SymbolClient,
                host.Context.PdbStore,
                host.Context
                    .PackageSourceAuthorization,
                host.Context.SourceFetch)
            {
                TypeSourceLimits = new(
                    1,
                    1,
                    defaults.TargetBounds,
                    defaults.SourceLinkReadLimits,
                    defaults.MaximumDocuments,
                    defaults.MaximumTargetMappings,
                    defaults.MaximumCandidateAttempts,
                    defaults.MaximumSourceBytes,
                    defaults
                        .MaximumSourceTextCharacters),
                TypeDecompilationLimits = new(
                    2,
                    2,
                    defaults.TargetBounds,
                    defaults.SourceLinkReadLimits),
            };
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<AssemblyTypeSourceEntry>
            inspection =
                await TypeSourceInspection
                    .ExecuteWithLatencyHedgeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest("Counter"),
                        context,
                        new TypeSourceLatencyHedge(
                            TimeSpan.FromSeconds(1),
                            TimeSpan.FromMilliseconds(250)),
                        TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<
                AssemblyTypeSourceEntry.Unavailable>(
                    inspection.Content);
        TypeSourceLatencyHedgeEvidence evidence =
            Assert.IsType<TypeSourceLatencyHedgeEvidence>(
                unavailable.LatencyHedgeEvidence);
        var authored =
            Assert.IsType<
                AssemblyContextLibraryAdapterResult
                    .Incomplete>(
                        evidence.AuthoredLibraryFailure);
        var decompilation =
            Assert.IsType<
                AssemblyContextLibraryAdapterResult
                    .Incomplete>(
                        evidence
                            .DecompilationLibraryFailure);
        Assert.Equal(
            1,
            authored.MaxCapturedImageBytes);
        Assert.Equal(
            2,
            decompilation.MaxCapturedImageBytes);
        Assert.Same(
            decompilation,
            unavailable.LibraryFailure);
        Assert.Empty(host.SourceRequests);
    }

    // PR-fast: the authored path remains authoritative beyond the grace
    // window when decompilation cannot produce publishable source.
    [Fact]
    public async Task
        TypeSourceLatencyHedge_UnavailableDecompilationDoesNotTruncateAuthored()
    {
        TestAssembly assembly =
            TestAssembly.Create(
                fixture:
                    FixtureCatalog.SourceDiffV1);
        var sourceEntered =
            new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);
        var sourceRelease =
            new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourcePairBytes(
                FixtureCatalog.SourceDiffV1),
            maxDecompilerBodyProjections: 0,
            beforeSourceResponse:
                async cancellationToken =>
                {
                    sourceEntered.TrySetResult();
                    await sourceRelease.Task
                        .WaitAsync(cancellationToken);
                });
        var time = new ObservedFakeTimeProvider();
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        Task<InspectionEnvelope<AssemblyTypeSourceEntry>>
            operation =
                TypeSourceInspection
                    .ExecuteWithLatencyHedgeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest("Counter"),
                        host.Context,
                        new TypeSourceLatencyHedge(
                            TimeSpan.FromSeconds(1),
                            TimeSpan.FromMilliseconds(250),
                            time),
                        TestContext.Current.CancellationToken);
        await sourceEntered.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        time.Advance(
            TimeSpan.FromMilliseconds(250));
        await Task.Yield();
        Assert.False(operation.IsCompleted);
        sourceRelease.TrySetResult();

        InspectionEnvelope<AssemblyTypeSourceEntry>
            inspection = await operation;

        var available =
            Assert.IsType<
                AssemblyTypeSourceEntry.Available>(
                    inspection.Content);
        Assert.IsType<AssemblyTypeSource.Pdb>(
            available.Source);
        Assert.Equal(
            new TypeSourceLatencyHedgeEvidence(
                PdbReadyBeforeDecompilation:
                    true,
                DecompilationStarted: true,
                DecompilationUsedPdb: true,
                TypeSourceLatencyHedgeSelection
                    .AuthoredAfterDecompilation),
            available.LatencyHedgeEvidence);
    }

    sealed class ObservedFakeTimeProvider : TimeProvider
    {
        readonly object _gate = new();
        readonly List<ManualTimer> _active = [];
        readonly Dictionary<
            TimeSpan,
            TaskCompletionSource> _timers = [];
        DateTimeOffset _utcNow =
            DateTimeOffset.UnixEpoch;
        long _timestamp;

        public override DateTimeOffset GetUtcNow() =>
            _utcNow;

        public override long TimestampFrequency =>
            TimeSpan.TicksPerSecond;

        public override long GetTimestamp() =>
            _timestamp;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = new ManualTimer(
                this,
                callback,
                state);
            lock (_gate)
            {
                timer.ChangeCore(
                    dueTime,
                    period,
                    _utcNow);
                _active.Add(timer);
                if (_timers.TryGetValue(
                        dueTime,
                        out TaskCompletionSource? created))
                {
                    created.TrySetResult();
                }
                else
                {
                    _timers.Add(
                        dueTime,
                        CompletedSignal());
                }
            }

            return timer;
        }

        internal void Advance(TimeSpan delta)
        {
            List<(TimerCallback Callback, object? State)>
                callbacks = [];
            lock (_gate)
            {
                _utcNow += delta;
                _timestamp += delta.Ticks;
                foreach (ManualTimer timer in _active)
                {
                    if (timer.TryTakeCallback(
                            _utcNow,
                            out var callback))
                    {
                        callbacks.Add(callback);
                    }
                }
            }

            foreach (var callback in callbacks)
            {
                callback.Callback(
                    callback.State);
            }
        }

        internal Task WaitForTimerAsync(
            TimeSpan dueTime,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource signal;
            lock (_timers)
            {
                if (_timers.TryGetValue(
                        dueTime,
                        out TaskCompletionSource? existing))
                {
                    return existing.Task;
                }

                signal = new(
                    TaskCreationOptions
                        .RunContinuationsAsynchronously);
                _timers.Add(
                    dueTime,
                    signal);
            }

            return signal.Task.WaitAsync(
                cancellationToken);
        }

        sealed class ManualTimer(
            ObservedFakeTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            DateTimeOffset _dueAt;
            TimeSpan _period;
            bool _enabled;
            bool _disposed;

            public bool Change(
                TimeSpan dueTime,
                TimeSpan period)
            {
                lock (owner._gate)
                {
                    if (_disposed)
                        return false;

                    ChangeCore(
                        dueTime,
                        period,
                        owner._utcNow);
                    return true;
                }
            }

            internal void ChangeCore(
                TimeSpan dueTime,
                TimeSpan period,
                DateTimeOffset now)
            {
                _enabled =
                    dueTime != Timeout.InfiniteTimeSpan;
                _dueAt =
                    _enabled
                        ? now + dueTime
                        : DateTimeOffset.MaxValue;
                _period = period;
            }

            internal bool TryTakeCallback(
                DateTimeOffset now,
                out (TimerCallback Callback, object? State)
                    invocation)
            {
                if (_disposed
                    || !_enabled
                    || _dueAt > now)
                {
                    invocation = default;
                    return false;
                }

                invocation = (callback, state);
                if (_period == Timeout.InfiniteTimeSpan)
                {
                    _enabled = false;
                }
                else
                {
                    _dueAt = now + _period;
                }

                return true;
            }

            public void Dispose()
            {
                lock (owner._gate)
                {
                    _disposed = true;
                    _enabled = false;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }

        static TaskCompletionSource CompletedSignal()
        {
            var signal = new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);
            signal.SetResult();
            return signal;
        }
    }

    // PR-fast: the completed decompiled-only facade retains exact House evidence.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        TypeDecompilationInspection_PathlessTargetUsesOnlySuppliedEvidence(
            bool supplyPdb)
    {
        TestAssembly assembly =
            TestAssembly.Create(
                fixture:
                    FixtureCatalog.DecompilerUnsafeLegacy);
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest(
                "SelectedAutoPropertySamples");
        AssemblyContextLibraryPortablePdb? portablePdb =
            supplyPdb
                ? new(
                    ImmutableArray.CreateRange(
                        File.ReadAllBytes(
                            assembly.PdbPath)),
                    new AssemblySourcePdbProvenance(
                        assembly.Assembly.Registration,
                        Identity: null,
                        Location: assembly.PdbPath,
                        Path: assembly.PdbPath,
                        SymbolServer: null))
                : null;
        using var host = QueryHost.WithoutPdb();
        InspectionEnvelope<AssemblyTypeDecompilationEntry>
            inspection;
        await using (var workspace =
            new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    [assembly.Participant]);
            inspection =
                await TypeSourceInspection.DecompileAsync(
                    group,
                    assembly.Participant,
                    request,
                    host.Context,
                    portablePdb,
                    TestContext.Current.CancellationToken);
        }

        var settled =
            Assert.IsType<
                AssemblyTypeDecompilationEntry.Settled>(
                    inspection.Content);
        Assert.Equal(
            CSharpDecompilationStatus.Available,
            settled.Attempt.Status);
        Assert.Contains(
            "class SelectedAutoPropertySamples",
            settled.Attempt.Text);
        Assert.Equal(
            supplyPdb,
            settled.Attempt.PdbSupplied);
        var house =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                    settled.HouseOutcome);
        Assert.Same(
            settled.Attempt,
            house.Attempt);
        Assert.Equal(
            request.Type,
            Assert.IsType<SourceHouseTarget.TypeTarget>(
                house.Request.Target)
                .Type);
        Assert.Equal(
            supplyPdb
                ? SourceHousePdbContributionKind.SuppliedCompanion
                : SourceHousePdbContributionKind.Unavailable,
            house.PdbContribution.Kind);
        Assert.Equal(
            SourceHouseLibraryLeaseConsumer.SourceHouse,
            house.LeaseSettlement.Consumer);
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(
            "type-decompilation/share",
            Assert.IsType<InspectionPortableProjection.NonProjectable>(
                inspection.PortableProjection)
                .Path);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public async Task
        TypeDecompilationInspection_IgnoresFilteredRequestModelMembers()
    {
        TestAssembly assembly =
            TestAssembly.Create(
                fixture:
                    FixtureCatalog.DecompilerUnsafeLegacy);
        ApiType selected =
            assembly.TypeTarget(
                "SelectedAutoPropertySamples");
        selected.Members =
        [
            Assert.Single(
                selected.Members,
                member => member.Name == "Count"),
        ];
        AssemblyTypeSourceRequest request =
            AssemblyTypeSourceRequest.From(selected);
        using var host = QueryHost.WithoutPdb();
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<AssemblyTypeDecompilationEntry>
            inspection =
                await TypeSourceInspection.DecompileAsync(
                    group,
                    assembly.Participant,
                    request,
                    host.Context,
                    cancellationToken:
                        TestContext.Current.CancellationToken);

        var settled =
            Assert.IsType<
                AssemblyTypeDecompilationEntry.Settled>(
                    inspection.Content);
        Assert.Equal(
            CSharpDecompilationStatus.Available,
            settled.Attempt.Status);
        Assert.Contains(
            "public int Count { get; }",
            settled.Attempt.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "SharedCount",
            settled.Attempt.Text,
            StringComparison.Ordinal);
        Assert.Equal(
            request.Type,
            Assert.IsType<SourceHouseTarget.TypeTarget>(
                settled.HouseOutcome.Request.Target)
                .Type);
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public async Task
        TypeDecompilationInspection_TerminalLibraryAdmissionRetainsEvidence()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithoutPdb();
        SourceHouseDecompilationLimits defaults =
            host.Context.TypeDecompilationLimits;
        var context =
            new AssemblyContextSourceQueryContext(
                host.Context.SymbolClient,
                host.Context.PdbStore,
                host.Context.PackageSourceAuthorization,
                host.Context.SourceFetch)
            {
                TypeDecompilationLimits = new(
                    maximumAssemblyBytes: 1,
                    maximumPortablePdbBytes: 1,
                    defaults.TargetBounds,
                    defaults.EmbeddedPdbReadLimits),
            };
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<AssemblyTypeDecompilationEntry>
            inspection =
                await TypeSourceInspection.DecompileAsync(
                    group,
                    assembly.Participant,
                    assembly.TypeRequest(
                        typeof(SourceFixture).Name),
                    context,
                    cancellationToken:
                        TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<
                AssemblyTypeDecompilationEntry.Unavailable>(
                    inspection.Content);
        Assert.Equal(
            AssemblySourceFailureKind.InspectionFailed,
            unavailable.Failure.Kind);
        Assert.IsType<
            AssemblyContextLibraryAdapterResult.Incomplete>(
                unavailable.LibraryFailure);
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public async Task
        TypeDecompilationInspection_InvalidSuppliedPdbIsTerminal()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithoutPdb();
        var portablePdb =
            new AssemblyContextLibraryPortablePdb(
                [1, 2, 3, 4],
                new AssemblySourcePdbProvenance(
                    assembly.Assembly.Registration,
                    Identity: null,
                    Location: "invalid.pdb",
                    Path: null,
                    SymbolServer: null));
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<AssemblyTypeDecompilationEntry>
            inspection =
                await TypeSourceInspection.DecompileAsync(
                    group,
                    assembly.Participant,
                    assembly.TypeRequest(
                        typeof(SourceFixture).Name),
                    host.Context,
                    portablePdb,
                    TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<
                AssemblyTypeDecompilationEntry.Unavailable>(
                    inspection.Content);
        Assert.Equal(
            AssemblySourceFailureKind.InspectionFailed,
            unavailable.Failure.Kind);
        Assert.IsType<
            AssemblyContextLibraryAdapterResult.PortablePdbRejected>(
                unavailable.LibraryFailure);
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public async Task
        TypeDecompilationInspection_PreservesIncompleteNativeAttempt()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithoutPdb();
        var context =
            new AssemblyContextSourceQueryContext(
                host.Context.SymbolClient,
                host.Context.PdbStore,
                host.Context.PackageSourceAuthorization,
                host.Context.SourceFetch)
            {
                MaxDecompilerBodyProjections = 0,
            };
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<AssemblyTypeDecompilationEntry>
            inspection =
                await TypeSourceInspection.DecompileAsync(
                    group,
                    assembly.Participant,
                    assembly.TypeRequest(
                        typeof(SourceFixture).Name),
                    context,
                    cancellationToken:
                        TestContext.Current.CancellationToken);

        var settled =
            Assert.IsType<
                AssemblyTypeDecompilationEntry.Settled>(
                    inspection.Content);
        Assert.Equal(
            CSharpDecompilationStatus.Incomplete,
            settled.Attempt.Status);
        var house =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                    settled.HouseOutcome);
        Assert.Same(
            settled.Attempt,
            house.Attempt);
        Assert.Equal(
            0,
            house.Work.BodyProjectionsAttempted);
        Assert.Equal(
            SourceHouseLibraryLeaseConsumer.SourceHouse,
            house.LeaseSettlement.Consumer);
    }

    [Fact]
    public async Task
        TypeDecompilationInspection_RejectsAuthoredDocumentSelector()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithoutPdb();
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        await Assert.ThrowsAsync<ArgumentException>(
            () => TypeSourceInspection.DecompileAsync(
                group,
                assembly.Participant,
                AssemblyTypeSourceRequest.AuthoredDocument(
                    assembly.TypeRequest(
                        typeof(SourceFixture).Name)
                        .Type,
                    "SourceFixture.cs"),
                host.Context,
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public async Task
        TypeDecompilationInspection_BindingChangePreventsPublication()
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        assembly.Policy.BeforeSelection =
            assembly.Policy.ChangeVersion;
        using var host = QueryHost.WithoutPdb();
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<AssemblyTypeDecompilationEntry>
            inspection =
                await TypeSourceInspection.DecompileAsync(
                    group,
                    assembly.Participant,
                    assembly.TypeRequest(
                        typeof(SourceFixture).Name),
                    host.Context,
                    cancellationToken:
                        TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<
                AssemblyTypeDecompilationEntry.Unavailable>(
                    inspection.Content);
        Assert.IsType<InvalidOperationException>(
            unavailable.Failure.Error);
        Assert.True(
            assembly.Policy.SelectionCount > 0);
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
    }

    // PR-fast: bounded single-type requests over this repository's two-document type.
    [Theory]
    [InlineData("SourceLinkService.cs", PdbTypeSourceUnitScope.PrimaryTypeDocument)]
    [InlineData("SourceLinkService.SourceContent.cs", PdbTypeSourceUnitScope.AdditionalTypeDocument)]
    public async Task TypeSourceInspection_ExplicitDocumentIsSelectedVerifiedAndDetached(
        string fileName,
        PdbTypeSourceUnitScope scope)
    {
        string path = typeof(SourceLinkService).Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        TestAssembly assembly = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        AssemblyTypeSourceRequest ordinary = assembly.TypeRequest("SourceLinkService");
        SourceLinkResolver.TypeSourceInfo mapping;
        using (var reader = SourceLinkService.Open(path))
            mapping = Assert.IsType<SourceLinkResolver.TypeSourceInfo>(
                reader.ResolveTypeSource(ordinary.Type));
        SourceLinkResolver.TypeSourceDocument selected = Assert.Single(
            mapping.Documents, document => Path.GetFileName(document.FilePath) == fileName);
        byte[] bytes = File.ReadAllBytes(Path.Combine(
            FindRepositoryRoot(), "src", "ILInspector.SourceLink", fileName));
        using var host = QueryHost.WithPdb(pdbPath, bytes);
        var request = AssemblyTypeSourceRequest.AuthoredDocument(ordinary.Type, selected.FilePath);
        InspectionEnvelope<AssemblyTypeSourceEntry> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup([assembly.Participant]);
            inspection = await TypeSourceInspection.ExecuteAsync(
                group, assembly.Participant, request, host.Context,
                TestContext.Current.CancellationToken);
        }

        var available = Assert.IsType<AssemblyTypeSourceEntry.Available>(inspection.Content);
        var source = Assert.IsType<AssemblyTypeSource.Pdb>(available.Source);
        var house = Assert.IsType<SourceHouseOutcome.Available>(available.HouseOutcome);
        var native = Assert.IsType<SourceHouseAuthoredMapping.Type>(house.Source.Mapping);
        Assert.Same(request, available.Request);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(bytes), source.Text);
        Assert.Equal(selected.FilePath, source.Inspection.Document!.OriginalPath);
        Assert.Equal(scope, source.Inspection.Scope);
        Assert.Equal(SourceChecksumVerification.Exact, source.Inspection.ChecksumVerification);
        Assert.Same(native.SourceMapping, source.Inspection.Mapping);
        Assert.Equal(mapping.Documents.Length, source.Inspection.Mapping!.Documents.Length);
        Assert.Equal(selected.Checksum, Assert.Single(
            source.Inspection.Mapping.Documents,
            document => document.FilePath == selected.FilePath).Checksum);
        Assert.Equal(selected.SourceUrl, Assert.Single(host.SourceRequests).AbsoluteUri);
        Assert.Equal(SourceHouseLibraryLeaseConsumer.SourceHouse, house.Receipt.LeaseSettlement.Consumer);
        Assert.Equal(0, assembly.Policy.SelectionCount);
        Assert.IsType<InspectionPortableProjection.NonProjectable>(inspection.PortableProjection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypeSourceInspection_ExplicitUnavailableOrMismatchedDocumentNeverDecompiles(
        bool checksumFailure)
    {
        string path = checksumFailure
            ? FixtureCatalog.SourceDiffV1.AssemblyPath()
            : typeof(EmbeddedSourceFixture).Assembly.Location;
        TestAssembly assembly = checksumFailure
            ? TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1)
            : TestAssembly.Create(File.ReadAllBytes(path));
        AssemblyTypeSourceRequest ordinary =
            assembly.TypeRequest(checksumFailure ? "Counter" : "EmbeddedSourceFixture");
        string originalPath;
        using (var reader = SourceLinkService.Open(path))
            originalPath = Assert.IsType<SourceLinkResolver.TypeSourceInfo>(
                reader.ResolveTypeSource(ordinary.Type)).Documents[0].FilePath;
        using var host = checksumFailure
            ? QueryHost.WithPdb(assembly.PdbPath, "wrong source document"u8.ToArray())
            : QueryHost.WithUnavailableSource(HttpStatusCode.NotFound);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);

        var inspection = await TypeSourceInspection.ExecuteAsync(
            group, assembly.Participant,
            AssemblyTypeSourceRequest.AuthoredDocument(ordinary.Type, originalPath),
            host.Context, TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(inspection.Content);
        Assert.Equal(AssemblySourceFailureKind.AuthoredDocumentUnavailable, unavailable.Failure.Kind);
        Assert.Null(unavailable.DecompiledAttempt);
        Assert.Null(unavailable.DecompilationHouseOutcome);
        Assert.Equal(
            checksumFailure
                ? PdbTypeSourceOutcome.ChecksumMismatch
                : PdbTypeSourceOutcome.SourceAcquisitionUnavailable,
            unavailable.PdbAttempt!.Outcome);
        Assert.NotNull(unavailable.HouseOutcome);
        Assert.Single(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypeSourceInspection_ExplicitPathMustBelongToExactType(bool changeCase)
    {
        string path = typeof(SourceLinkService).Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        TestAssembly assembly = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        AssemblyTypeSourceRequest ordinary = assembly.TypeRequest("SourceLinkService");
        string originalPath;
        using (var reader = SourceLinkService.Open(path))
            originalPath = Assert.IsType<SourceLinkResolver.TypeSourceInfo>(
                reader.ResolveTypeSource(ordinary.Type)).Documents[0].FilePath;
        string requested = changeCase ? originalPath.ToUpperInvariant() : "/another-type.cs";
        Assert.NotEqual(originalPath, requested);
        using var host = QueryHost.WithPdb(pdbPath, "must not be fetched"u8.ToArray());
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);

        var inspection = await TypeSourceInspection.ExecuteAsync(
            group, assembly.Participant,
            AssemblyTypeSourceRequest.AuthoredDocument(ordinary.Type, requested),
            host.Context, TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(inspection.Content);
        Assert.Equal(AssemblySourceFailureKind.AuthoredDocumentUnavailable, unavailable.Failure.Kind);
        Assert.Null(unavailable.DecompiledAttempt);
        Assert.Null(unavailable.DecompilationHouseOutcome);
        Assert.Equal(PdbTypeSourceOutcome.SourceMappingUnavailable, unavailable.PdbAttempt!.Outcome);
        Assert.IsType<SourceHouseOutcome.Unavailable>(unavailable.HouseOutcome);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Fact]
    public async Task TypeSourceInspection_ExplicitDocumentDeadlineDoesNotDecompile()
    {
        string path = FixtureCatalog.SourceDiffV1.AssemblyPath();
        TestAssembly assembly = TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        AssemblyTypeSourceRequest ordinary = assembly.TypeRequest("Counter");
        string originalPath;
        using (var reader = SourceLinkService.Open(path))
            originalPath = Assert.IsType<SourceLinkResolver.TypeSourceInfo>(
                reader.ResolveTypeSource(ordinary.Type)).Documents[0].FilePath;
        using var host = QueryHost.WithPdb(assembly.PdbPath, SourcePairBytes(FixtureCatalog.SourceDiffV1));
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            TypeSourceTimeout = TimeSpan.FromTicks(1),
        };
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);

        var inspection = await TypeSourceInspection.ExecuteAsync(
            group, assembly.Participant,
            AssemblyTypeSourceRequest.AuthoredDocument(ordinary.Type, originalPath),
            context, TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(inspection.Content);
        Assert.Equal(AssemblySourceFailureKind.AuthoredDocumentUnavailable, unavailable.Failure.Kind);
        Assert.Null(unavailable.DecompiledAttempt);
        Assert.Null(unavailable.DecompilationHouseOutcome);
        Assert.Equal(PdbTypeSourceOutcome.SourceDeadlineExceeded, unavailable.PdbAttempt!.Outcome);
        Assert.Equal(SourceHouseIncompleteBoundary.Deadline,
            Assert.IsType<SourceHouseOutcome.Incomplete>(unavailable.HouseOutcome).Boundary);
        Assert.Empty(host.SourceRequests);
    }

    [Theory]
    [InlineData("package")]
    [InlineData("project")]
    [InlineData("platform")]
    public async Task TypeSourceInspection_ExplicitDocumentPreservesPdbAcquisitionAuthority(string origin)
    {
        string path = FixtureCatalog.SourceDiffV1.AssemblyPath();
        TestAssembly fixture = TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        AssemblyTypeSourceRequest ordinary = fixture.TypeRequest("Counter");
        string originalPath;
        using (var reader = SourceLinkService.Open(path))
            originalPath = Assert.IsType<SourceLinkResolver.TypeSourceInfo>(
                reader.ResolveTypeSource(ordinary.Type)).Documents[0].FilePath;
        byte[] bytes = File.ReadAllBytes(path);
        AssemblyResolutionProvenance provenance = origin switch
        {
            "package" => AssemblyResolutionProvenance.Package("Supplier.Source", "2.0.0", "net11.0", null),
            "project" => AssemblyResolutionProvenance.Project("project.csproj", "net11.0", null),
            "platform" => AssemblyResolutionProvenance.Platform("Microsoft.NETCore.App", "11.0.0", "test"),
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        var assembly = ResolvedAssemblyReference.Create(
            ReadIdentity(bytes), path: null, () => new MemoryStream(bytes, writable: false), provenance);
        var participant = new AssemblyContextParticipant(assembly, fixture.Policy);
        using var host = QueryHost.WithPdb(fixture.PdbPath, SourcePairBytes(FixtureCatalog.SourceDiffV1));
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            PdbFallbackPackage = new PackageCoordinate("Fallback.Source", "3.0.0"),
        };
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([participant]);

        var inspection = await TypeSourceInspection.ExecuteAsync(
            group, participant,
            AssemblyTypeSourceRequest.AuthoredDocument(ordinary.Type, originalPath),
            context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(host.SymbolRequests);
        if (origin == "platform")
        {
            Assert.DoesNotContain(host.SymbolRequests,
                uri => uri.AbsolutePath.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase));
            var unavailable = Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(inspection.Content);
            Assert.Null(unavailable.DecompiledAttempt);
            Assert.Empty(host.SourceRequests);
        }
        else
        {
            var available = Assert.IsType<AssemblyTypeSourceEntry.Available>(inspection.Content);
            Assert.IsType<AssemblyTypeSource.Pdb>(available.Source);
            string expected = origin == "package" ? "supplier.source.2.0.0.snupkg" : "fallback.source.3.0.0.snupkg";
            Assert.EndsWith(expected, Assert.Single(host.SymbolRequests,
                uri => uri.AbsolutePath.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)).AbsolutePath,
                StringComparison.OrdinalIgnoreCase);
            var house = Assert.IsType<SourceHouseOutcome.Available>(available.HouseOutcome);
            Assert.Same(assembly.Registration, Assert.IsType<AssemblySourcePdbProvenance>(
                house.PdbContribution.Content!.ArtifactReference.Provenance).SourceRegistration);
        }
    }

    [Fact]
    public async Task TypeSourceInspection_RealRepositoryAuthoredResultIsDetached()
    {
        string path = typeof(CSharpText.MemberSlicing.MemberTextSlicer).Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        TestAssembly assembly = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        using var host = QueryHost.WithPdb(pdbPath, File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "LibraryAdapter", "MemberTextSlicer.cs")),
            maxDecompilerBodyProjections: 0);
        InspectionEnvelope<AssemblyTypeSourceEntry> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup([assembly.Participant]);
            inspection = await TypeSourceInspection.ExecuteAsync(
                group, assembly.Participant, assembly.TypeRequest("MemberTextSlicer"),
                host.Context, TestContext.Current.CancellationToken);
        }

        var available = Assert.IsType<AssemblyTypeSourceEntry.Available>(inspection.Content);
        var pdb = Assert.IsType<AssemblyTypeSource.Pdb>(available.Source);
        var house = Assert.IsType<SourceHouseOutcome.Available>(available.HouseOutcome);
        Assert.Null(available.DecompilationHouseOutcome);
        Assert.Contains("public static class MemberTextSlicer", pdb.Text);
        Assert.Equal(pdb.Text, house.Source.Text);
        Assert.Equal(PdbTypeSourceOutcome.Complete, pdb.Inspection.Outcome);
        Assert.Equal(PdbTypeSourceUnitScope.PrimaryTypeDocument, pdb.Inspection.Scope);
        Assert.Equal(PdbTypeSourceMappingStrength.CorrelatedTypeDocument, pdb.Inspection.Strength);
        Assert.Equal(SourceChecksumVerification.Exact, pdb.Inspection.ChecksumVerification);
        Assert.IsType<SourceHouseTarget.TypeTarget>(house.Request.Target);
        var provenance = Assert.IsType<AssemblySourcePdbProvenance>(
            house.PdbContribution.Content!.ArtifactReference.Provenance);
        Assert.Same(assembly.Assembly.Registration, provenance.SourceRegistration);
        Assert.Equal(SourceHouseLibraryLeaseConsumer.SourceHouse, house.Receipt.LeaseSettlement.Consumer);
        Assert.Equal(0, assembly.Policy.SelectionCount);
        Assert.Equal("type-source/share",
            Assert.IsType<InspectionPortableProjection.NonProjectable>(inspection.PortableProjection).Path);
        Assert.Empty(inspection.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypeSourceInspection_MissingOrChecksumFailureRetainsSymbolsForFallback(
        bool checksumFailure)
    {
        TestAssembly assembly = checksumFailure
            ? TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1)
            : TestAssembly.Create(File.ReadAllBytes(typeof(EmbeddedSourceFixture).Assembly.Location));
        using var host = checksumFailure
            ? QueryHost.WithPdb(assembly.PdbPath, "not the checksum-verified source"u8.ToArray())
            : QueryHost.WithUnavailableSource(HttpStatusCode.NotFound);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);

        InspectionEnvelope<AssemblyTypeSourceEntry> inspection =
            await TypeSourceInspection.ExecuteAsync(
                group, assembly.Participant,
                assembly.TypeRequest(checksumFailure ? "Counter" : "EmbeddedSourceFixture"),
                host.Context, TestContext.Current.CancellationToken);

        var available = Assert.IsType<AssemblyTypeSourceEntry.Available>(inspection.Content);
        var source = Assert.IsType<AssemblyTypeSource.Decompiled>(available.Source);
        var decompilationHouse =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                available.DecompilationHouseOutcome);
        Assert.True(source.Decompilation.PdbSupplied);
        Assert.Same(source.Decompilation, decompilationHouse.Attempt);
        Assert.Equal(
            assembly.TypeRequest(
                checksumFailure ? "Counter" : "EmbeddedSourceFixture").Type,
            decompilationHouse.Request.Target.Type);
        Assert.Equal(
            checksumFailure
                ? SourceHousePdbContributionKind.SuppliedCompanion
                : SourceHousePdbContributionKind.Embedded,
            decompilationHouse.PdbContribution.Kind);
        Assert.Equal(
            SourceHouseLibraryLeaseConsumer.SourceHouse,
            decompilationHouse.LeaseSettlement.Consumer);
        Assert.Equal(
            checksumFailure
                ? PdbTypeSourceOutcome.ChecksumMismatch
                : PdbTypeSourceOutcome.SourceAcquisitionUnavailable,
            source.PdbAttempt.Outcome);
        if (checksumFailure)
        {
            Assert.IsType<FindingInspection<string>.Failed>(source.PdbAttempt.Lines.Value);
            Assert.IsType<SourceHouseOutcome.Failed>(available.HouseOutcome);
            Assert.Single(host.SymbolRequests, uri => uri.AbsolutePath.EndsWith(".snupkg"));
        }
        else
        {
            Assert.IsType<FindingInspection<string>.Absent>(source.PdbAttempt.Lines.Value);
            Assert.IsType<SourceHouseOutcome.Unavailable>(available.HouseOutcome);
            Assert.Empty(host.SymbolRequests);
        }
        Assert.Single(host.SourceRequests);
    }

    [Theory]
    [InlineData("deadline")]
    [InlineData("source-bytes")]
    [InlineData("assembly")]
    public async Task TypeSourceInspection_BoundsRetainFailureAndFallback(string boundary)
    {
        TestAssembly assembly = TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath, SourcePairBytes(FixtureCatalog.SourceDiffV1));
        SourceHouseLimits defaults = host.Context.TypeSourceLimits;
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            TypeSourceTimeout = boundary == "deadline"
                ? TimeSpan.FromTicks(1)
                : TimeSpan.FromMinutes(5),
            TypeSourceLimits = new(
                boundary == "assembly" ? 1 : defaults.MaximumAssemblyBytes,
                boundary == "assembly" ? 1 : defaults.MaximumPortablePdbBytes,
                defaults.TargetBounds, defaults.SourceLinkReadLimits,
                defaults.MaximumDocuments, defaults.MaximumTargetMappings,
                defaults.MaximumCandidateAttempts,
                boundary == "source-bytes" ? 1 : defaults.MaximumSourceBytes,
                defaults.MaximumSourceTextCharacters),
        };
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);

        InspectionEnvelope<AssemblyTypeSourceEntry> inspection =
            await TypeSourceInspection.ExecuteAsync(
                group, assembly.Participant, assembly.TypeRequest("Counter"),
                context, TestContext.Current.CancellationToken);

        var available = Assert.IsType<AssemblyTypeSourceEntry.Available>(
            inspection.Content);
        var source = Assert.IsType<AssemblyTypeSource.Decompiled>(
            available.Source);
        Assert.True(source.Decompilation.PdbSupplied);
        Assert.Equal(
            boundary == "deadline"
                ? PdbTypeSourceOutcome.SourceDeadlineExceeded
                : PdbTypeSourceOutcome.SourceLimitExceeded,
            source.PdbAttempt.Outcome);
        Assert.IsType<FindingInspection<string>.Failed>(
            source.PdbAttempt.Lines.Value);
        Assert.Equal(
            boundary == "deadline"
                ? SourceHouseIncompleteBoundary.Deadline
                : boundary == "source-bytes"
                    ? SourceHouseIncompleteBoundary.SourceBytes
                    : SourceHouseIncompleteBoundary.AssemblyBytes,
            Assert.IsType<SourceHouseOutcome.Incomplete>(
                available.HouseOutcome).Boundary);
        Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
            available.DecompilationHouseOutcome);
        Assert.Null(available.LibraryFailure);
    }

    [Fact]
    public async Task TypeSourceInspection_LibraryAdmissionLimitCannotBecomeDecompilerFallback()
    {
        TestAssembly assembly = TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath, SourcePairBytes(FixtureCatalog.SourceDiffV1));
        SourceHouseLimits defaults = host.Context.TypeSourceLimits;
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            TypeSourceLimits = new(
                1, 1, defaults.TargetBounds, defaults.SourceLinkReadLimits,
                defaults.MaximumDocuments, defaults.MaximumTargetMappings,
                defaults.MaximumCandidateAttempts, defaults.MaximumSourceBytes,
                defaults.MaximumSourceTextCharacters),
            TypeDecompilationLimits = new(
                1, 1, defaults.TargetBounds, defaults.SourceLinkReadLimits),
        };
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);

        var inspection = await TypeSourceInspection.ExecuteAsync(
            group, assembly.Participant, assembly.TypeRequest("Counter"),
            context, TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(inspection.Content);
        Assert.IsType<AssemblyContextLibraryAdapterResult.Incomplete>(unavailable.LibraryFailure);
        Assert.Equal(AssemblySourceFailureKind.InspectionFailed, unavailable.Failure.Kind);
        Assert.Contains("terminal Library admission", unavailable.Failure.Detail);
        Assert.Null(unavailable.HouseOutcome);
        Assert.Null(unavailable.DecompilationHouseOutcome);
        Assert.Null(unavailable.DecompiledAttempt);
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public async Task TypeSourceInspection_TypeBoundsAreIndependentFromMemberAndPair()
    {
        var (before, after) = SourcePairAssemblies();
        using var host = SourcePairHost(before, after);
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            TypeSourceTimeout = TimeSpan.FromTicks(1),
        };
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([before.Participant]);

        var type = await TypeSourceInspection.ExecuteAsync(
            group, before.Participant, before.TypeRequest("Counter"),
            context, TestContext.Current.CancellationToken);
        var member = await MemberSourceInspection.ExecuteAsync(
            group, before.Participant, before.MemberRequest("Value", "Counter"),
            context, TestContext.Current.CancellationToken);
        AssemblyMemberSourcePairResult pair = await ExecuteSourcePairAsync(
            before, after, "Value", host, sourceContext: context);

        var typeSource = Assert.IsType<AssemblyTypeSource.Decompiled>(
            Assert.IsType<AssemblyTypeSourceEntry.Available>(type.Content).Source);
        Assert.Equal(PdbTypeSourceOutcome.SourceDeadlineExceeded, typeSource.PdbAttempt.Outcome);
        Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
            Assert.IsType<AssemblyTypeSourceEntry.Available>(type.Content)
                .DecompilationHouseOutcome);
        Assert.IsType<AssemblyMemberSource.Pdb>(
            Assert.IsType<AssemblyMemberSourceEntry.Available>(member.Content).Source);
        Assert.Equal(AssemblyMemberSourcePairStatus.Compared, pair.Status);
    }

    [Fact]
    public async Task
        TypeSourceInspection_DecompilationBoundsAreIndependentFromMember()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithoutPdb();
        SourceHouseDecompilationLimits defaults =
            host.Context.TypeDecompilationLimits;
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient,
            host.Context.PdbStore,
            host.Context.PackageSourceAuthorization,
            host.Context.SourceFetch)
        {
            TypeDecompilationLimits = new(
                maximumAssemblyBytes: 1,
                defaults.MaximumPortablePdbBytes,
                defaults.TargetBounds,
                defaults.EmbeddedPdbReadLimits),
        };
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);

        InspectionEnvelope<AssemblyTypeSourceEntry> type =
            await TypeSourceInspection.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(typeof(SourceFixture).Name),
                context,
                TestContext.Current.CancellationToken);
        InspectionEnvelope<AssemblyMemberSourceEntry> member =
            await MemberSourceInspection.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(nameof(SourceFixture.Describe)),
                context,
                TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(
            type.Content);
        Assert.Equal(
            CSharpDecompilationStatus.Incomplete,
            unavailable.DecompiledAttempt!.Status);
        Assert.Equal(
            SourceHouseIncompleteBoundary.AssemblyBytes,
            Assert.IsType<SourceHouseDecompilationOutcome.Incomplete>(
                unavailable.DecompilationHouseOutcome)
                .Boundary);
        Assert.IsType<AssemblyMemberSource.Decompiled>(
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                member.Content)
                .Source);
    }

    [Fact]
    public async Task TypeSourceInspection_PreservesPrimaryPartialDocumentEvidence()
    {
        string path = typeof(SourceLinkService).Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        TestAssembly assembly = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        using var host = QueryHost.WithPdb(
            pdbPath,
            File.ReadAllBytes(Path.Combine(
                FindRepositoryRoot(), "src", "ILInspector.SourceLink", "SourceLinkService.cs")));
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);

        InspectionEnvelope<AssemblyTypeSourceEntry> inspection =
            await TypeSourceInspection.ExecuteAsync(
                group, assembly.Participant, assembly.TypeRequest("SourceLinkService"),
                host.Context, TestContext.Current.CancellationToken);

        var available = Assert.IsType<AssemblyTypeSourceEntry.Available>(inspection.Content);
        var source = Assert.IsType<AssemblyTypeSource.Pdb>(available.Source);
        var house = Assert.IsType<SourceHouseOutcome.Available>(available.HouseOutcome);
        var native = Assert.IsType<SourceHouseAuthoredMapping.Type>(house.Source.Mapping);
        SourceLinkResolver.TypeSourceInfo projected =
            Assert.IsType<SourceLinkResolver.TypeSourceInfo>(source.Inspection.Mapping);
        Assert.Equal(PdbTypeSourceUnitScope.PrimaryTypeDocument, source.Inspection.Scope);
        Assert.Equal(
            PdbTypeSourceMappingStrength.CorrelatedTypeDocument,
            source.Inspection.Strength);
        Assert.True(source.Inspection.IsPartial);
        Assert.Same(native.SourceMapping, projected);
        var primary = Assert.Single(projected.Documents,
            document => document.FilePath == native.Document.OriginalPath);
        Assert.Equal(
            SourceLinkResolver.SourceResolutionMethod.SourceLink,
            primary.ResolutionMethod);
        Assert.NotNull(primary.GitHubBrowseUrl);
        Assert.NotEmpty(primary.Checksum!);
        Assert.NotNull(primary.ChecksumAlgorithm);
        Assert.EndsWith("SourceLinkService.cs", source.Inspection.Document!.OriginalPath);
        Assert.Equal(native.Document, source.Inspection.Document);
        Assert.Equal(native.AdditionalDocuments.Count, source.Inspection.AdditionalDocuments.Count);
        Assert.Contains(
            source.Inspection.AdditionalDocuments,
            document => document.OriginalPath.EndsWith(
                "SourceLinkService.SourceContent.cs", StringComparison.Ordinal));
        SourceLinkResolver.TypeSourceDocument additional = Assert.Single(
            projected.Documents,
            document => document.FilePath.EndsWith(
                "SourceLinkService.SourceContent.cs", StringComparison.Ordinal));
        Assert.NotNull(additional.GitHubBrowseUrl);
        Assert.NotEmpty(additional.Checksum!);
        Assert.NotNull(additional.ChecksumAlgorithm);
    }
}
