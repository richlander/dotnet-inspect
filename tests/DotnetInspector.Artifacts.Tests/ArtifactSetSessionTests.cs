using System.Runtime.CompilerServices;

using DotnetInspector.Artifacts.Workspaces;

namespace DotnetInspector.Artifacts.Tests;

public sealed partial class ArtifactSetSessionTests
{
    [Fact]
    public async Task ArtifactSetSession_ComposesArtifactsFromMultipleSources()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var firstLease = new TrackingLease();
        var secondLease = new TrackingLease();
        var session = new ArtifactSetSession();
        try
        {
            await session.AddRequiredAcquisitionAsync(
                (scope, _) => Acquired(
                    scope,
                    new Provenance("first"),
                    [1, 2],
                    firstLease),
                cancellationToken: cancellationToken);
            await session.AddRequiredAcquisitionAsync(
                (scope, _) => Acquired(
                    scope,
                    new Provenance("second"),
                    [3, 4],
                    secondLease),
                cancellationToken: cancellationToken);

            Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                await session.SealAsync(cancellationToken));

            ArtifactQueryAuthorization authorization =
                session.CreateQueryAuthorization();
            using ArtifactQueryLease lease =
                session.IssueLease(authorization);
            IReadOnlyList<ArtifactDescriptor> catalog =
                session.GetCatalog(lease);

            Assert.Equal([0L, 1L], catalog.Select(
                descriptor => descriptor.Identity.Ordinal));
            Assert.Equal(
                ["first", "second"],
                catalog.Select(descriptor =>
                    Assert.IsType<Provenance>(
                        session.GetProvenance(
                            descriptor.Identity,
                            lease)).Source));
            using Stream firstContent =
                session.OpenRead(
                    catalog[0].Identity,
                    lease);
            Assert.Equal(
                [1, 2],
                ReadAll(firstContent));
            using Stream secondContent =
                session.OpenRead(
                    catalog[1].Identity,
                    lease);
            Assert.Equal(
                [3, 4],
                ReadAll(secondContent));
        }
        finally
        {
            await session.DisposeAsync();
        }

        Assert.Equal(1, firstLease.DisposeCount);
        Assert.Equal(1, secondLease.DisposeCount);
    }

    [Fact]
    public async Task ArtifactSetSession_DisposesEveryContributingLease()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var first = new TrackingLease();
        var second = new TrackingLease();
        var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("first"),
                [1],
                first),
            cancellationToken: cancellationToken);
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("second"),
                [2],
                second),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));

        await session.DisposeAsync();

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public async Task ArtifactSetSession_ReleasesLeasesOnlyAfterOpenArtifactStreamsQuiesce()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var acquisitionLease = new TrackingLease();
        var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("query-stream"),
                [1, 2],
                acquisitionLease),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        ArtifactQueryAuthorization authorization =
            session.CreateQueryAuthorization();
        using ArtifactQueryLease lease =
            session.IssueLease(authorization);
        ArtifactIdentity identity =
            Assert.Single(session.GetCatalog(lease)).Identity;
        Stream opened = session.OpenRead(identity, lease);

        Task disposal = session.DisposeAsync().AsTask();

        Assert.False(disposal.IsCompleted);
        Assert.Equal(0, acquisitionLease.DisposeCount);
        Assert.Equal(1, opened.ReadByte());

        opened.Dispose();
        await disposal;
        Assert.Equal(1, acquisitionLease.DisposeCount);
    }

    [Fact]
    public async Task ArtifactSetSession_CancellationCallbackFailureDoesNotSkipLeaseCleanup()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var acquisitionLease = new TrackingLease();
        var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution contribution = scope.Register(
                    new Provenance("throwing-cancellation"),
                    ownerCancellation =>
                    {
                        ownerCancellation.Register(
                            static () =>
                                throw new IOException(
                                    "owner cancellation failed"));
                        entered.TrySetResult();
                        ownerCancellation.WaitHandle.WaitOne();
                        ownerCancellation.ThrowIfCancellationRequested();
                        throw new InvalidOperationException(
                            "The owner did not cancel the opener.");
                    });
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [contribution],
                        acquisitionLease));
            },
            cancellationToken: cancellationToken);

        Task<ArtifactSetPublicationOutcome> publication =
            Task.Run(
                async () =>
                    await session.SealAsync(cancellationToken),
                cancellationToken);
        await entered.Task.WaitAsync(cancellationToken);
        Task firstDisposal = session.DisposeAsync().AsTask();

        await firstDisposal;
        Assert.Equal(1, acquisitionLease.DisposeCount);
        AggregateException failure =
            Assert.IsType<AggregateException>(
                Assert.Single(session.CleanupFailures));
        Assert.IsType<IOException>(
            Assert.Single(
                failure.Flatten().InnerExceptions));
        ObjectDisposedException disposed =
            await Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await publication);
        IReadOnlyList<Exception> attached =
            Assert.IsAssignableFrom<IReadOnlyList<Exception>>(
                disposed.Data[
                    "DotnetInspector.Artifacts.Workspaces.CleanupFailures"]);
        Assert.Same(
            failure,
            Assert.Single(attached));

        await session.DisposeAsync();
        Assert.Equal(1, acquisitionLease.DisposeCount);
    }

    [Fact]
    public async Task ArtifactSetSession_DisposalCancelsInFlightMaterialization()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var acquisitionLease = new TrackingLease();
        var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution contribution = scope.Register(
                    new Provenance("owner-cancelled-read"),
                    _ => new OwnerCancellationReadStream(
                        entered,
                        cancellationObserved));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [contribution],
                        acquisitionLease));
            },
            cancellationToken: cancellationToken);

        Task<ArtifactSetPublicationOutcome> publication =
            session.SealAsync(cancellationToken).AsTask();
        await entered.Task.WaitAsync(cancellationToken);
        Task disposal = session.DisposeAsync().AsTask();

        await cancellationObserved.Task.WaitAsync(cancellationToken);
        await disposal;
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await publication);
        Assert.Equal(1, acquisitionLease.DisposeCount);
    }

    [Fact]
    public async Task ArtifactSetSession_DisposalReleasesOwnerHeldState()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        (
            ArtifactSetSession session,
            WeakReference<object> provenanceMarker) =
            await CreateDisposedSessionWithTrackedProvenance(
                cancellationToken);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(provenanceMarker.TryGetTarget(out _));
        GC.KeepAlive(session);
    }

    [Fact]
    public async Task ArtifactSetSession_ConcurrentTerminationWaitsForCleanup()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var lease = new BlockingThrowingLease();
        var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("blocking-cleanup"),
                [1],
                lease),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));

        Task first = session.DisposeAsync().AsTask();
        await lease.Entered.WaitAsync(cancellationToken);
        Task second = session.DisposeAsync().AsTask();

        Assert.False(second.IsCompleted);
        Assert.Empty(session.CleanupFailures);
        lease.Release();
        await Task.WhenAll(first, second);
        Assert.IsType<IOException>(
            Assert.Single(session.CleanupFailures));
    }

    [Fact]
    public async Task ArtifactSetSession_ConcurrentAbortAndDisposalShareCleanup()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var lease = new BlockingThrowingLease();
        var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("blocking-cleanup"),
                [1],
                lease),
            cancellationToken: cancellationToken);

        Task acquisition = session.AddRequiredAcquisitionAsync(
            static (_, _) =>
                ValueTask.FromException<ArtifactAcquisitionOutcome>(
                    new InvalidDataException("primary failure")),
            cancellationToken: cancellationToken).AsTask();
        await lease.Entered.WaitAsync(cancellationToken);
        Task disposal = session.DisposeAsync().AsTask();

        Assert.False(disposal.IsCompleted);
        lease.Release();
        InvalidDataException primary =
            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await acquisition);
        await disposal;

        Assert.Equal("primary failure", primary.Message);
        IReadOnlyList<Exception> attached =
            Assert.IsAssignableFrom<IReadOnlyList<Exception>>(
                primary.Data[
                    "DotnetInspector.Artifacts.Workspaces.CleanupFailures"]);
        Assert.Same(
            Assert.Single(attached),
            Assert.Single(session.CleanupFailures));
    }

    [Fact]
    public async Task ArtifactSetSession_DisposalDuringAcquisitionDisposesLateLease()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var acquisitionLease = new ThrowingLease();
        var session = new ArtifactSetSession();

        Task acquisition = session.AddRequiredAcquisitionAsync(
            async (scope, token) =>
            {
                ArtifactContribution contribution = scope.Register(
                    new Provenance("late"),
                    _ => new MemoryStream(
                        [1],
                        writable: false));
                entered.SetResult();
                await release.Task.WaitAsync(token);
                return new ArtifactAcquisitionOutcome.Acquired(
                    [contribution],
                    acquisitionLease);
            },
            cancellationToken: cancellationToken).AsTask();

        await entered.Task.WaitAsync(cancellationToken);
        await session.DisposeAsync();
        release.SetResult();

        ObjectDisposedException disposed =
            await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await acquisition);
        IReadOnlyList<Exception> attached =
            Assert.IsAssignableFrom<IReadOnlyList<Exception>>(
                disposed.Data[
                    "DotnetInspector.Artifacts.Workspaces.CleanupFailures"]);
        Assert.IsType<IOException>(Assert.Single(attached));
        Assert.Same(
            Assert.Single(attached),
            Assert.Single(session.CleanupFailures));
    }

    [Fact]
    public async Task ArtifactSetSession_SealRejectsAcquisitionInProgress()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new ArtifactSetSession();

        Task acquisition = session.AddRequiredAcquisitionAsync(
            async (scope, token) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
                return await Acquired(
                    scope,
                    new Provenance("only"),
                    [1],
                    ArtifactAcquisitionLeases.None);
            },
            cancellationToken: cancellationToken).AsTask();

        await entered.Task.WaitAsync(cancellationToken);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await session.SealAsync(cancellationToken));
        }
        finally
        {
            release.TrySetResult();
        }

        await acquisition;
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
    }

    [Fact]
    public async Task ArtifactSetSession_DisposalDuringSealCannotPublish()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution gated = scope.Register(
                    new Provenance("gated"),
                    _ => new GatedReadStream(
                        [1],
                        entered,
                        release));
                ArtifactContribution unopened = scope.Register(
                    new Provenance("unopened"),
                    _ => new MemoryStream(
                        [2],
                        writable: false));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [gated, unopened],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: cancellationToken);

        Task<ArtifactSetPublicationOutcome> publication =
            session.SealAsync(cancellationToken).AsTask();
        await entered.Task.WaitAsync(cancellationToken);
        await session.DisposeAsync();
        release.SetResult();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await publication);
        Assert.Throws<ObjectDisposedException>(
            session.CreateQueryAuthorization);
    }

    [Fact]
    public async Task ArtifactSetSession_SealedGenerationCannotMutate()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("only"),
                [1],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await session.AddRequiredAcquisitionAsync(
                (scope, _) => Acquired(
                    scope,
                    new Provenance("late"),
                    [2],
                    ArtifactAcquisitionLeases.None),
                cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task ArtifactSetSession_SealingRequiresMaterializedBoundedContent()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var acquisitionLease = new TrackingLease();
        var session = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = 1,
                MaxArtifactBytes = 2,
                MaxRetainedBytes = 2,
            });
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("oversize"),
                [1, 2, 3],
                acquisitionLease),
            cancellationToken: cancellationToken);

        var rejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await session.SealAsync(cancellationToken));
        ArtifactSetAdmissionFailure failure =
            Assert.Single(rejected.Failures);

        Assert.Equal(
            ArtifactSetAdmissionFailureKind.Rejected,
            failure.Kind);
        Assert.Equal(
            "artifact.session.artifact-byte-limit",
            failure.Diagnostic.Code);
        Assert.Empty(rejected.CleanupFailures);
        Assert.Equal(1, acquisitionLease.DisposeCount);
        Assert.Throws<ObjectDisposedException>(
            session.CreateQueryAuthorization);
    }

    [Fact]
    public async Task ArtifactSetSession_SourceObjectDisposalIsMaterializationFailure()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution contribution = scope.Register(
                    new Provenance("disposed-source"),
                    _ => throw new ObjectDisposedException(
                        "source stream"));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [contribution],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: cancellationToken);

        var rejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await session.SealAsync(cancellationToken));

        ArtifactSetAdmissionFailure failure =
            Assert.Single(rejected.Failures);
        Assert.Equal(
            ArtifactSetAdmissionFailureKind.Failed,
            failure.Kind);
        Assert.Equal(
            "artifact.session.materialization-failed",
            failure.Diagnostic.Code);
    }

    [Fact]
    public async Task ArtifactSetSession_PreservesPrimaryFailureWhenCleanupFails()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("first"),
                [1],
                new ThrowingLease()),
            cancellationToken: cancellationToken);
        await session.AddRequiredAcquisitionAsync(
            static (_, _) =>
                ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Unavailable(
                        new Diagnostic(
                            "source.missing",
                            "Required source is missing."))),
            cancellationToken: cancellationToken);

        var rejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await session.SealAsync(cancellationToken));

        ArtifactSetAdmissionFailure failure =
            Assert.Single(rejected.Failures);
        Assert.Equal(
            ArtifactSetAdmissionFailureKind.Unavailable,
            failure.Kind);
        Assert.Equal("source.missing", failure.Diagnostic.Code);
        Assert.IsType<IOException>(
            Assert.Single(rejected.CleanupFailures));

        var disposalSession = new ArtifactSetSession();
        await disposalSession.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("dispose"),
                [1],
                new ThrowingLease()),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await disposalSession.SealAsync(cancellationToken));

        InvalidDataException primary =
            await Assert.ThrowsAsync<InvalidDataException>(
                async () =>
                {
                    await using (disposalSession)
                    {
                        throw new InvalidDataException(
                            "primary failure");
                    }
                });

        Assert.Equal("primary failure", primary.Message);
        Assert.IsType<IOException>(
            Assert.Single(disposalSession.CleanupFailures));
    }

    [Fact]
    public async Task SupplementalAcquisition_RequiredCheckpointPreservesSealOutcome()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await VerifyEquivalentAsync(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = 1,
            },
            scope =>
            {
                ArtifactContribution first = scope.Register(
                    new Provenance("first"),
                    _ => new MemoryStream([1], writable: false));
                ArtifactContribution second = scope.Register(
                    new Provenance("second"),
                    _ => new MemoryStream([2], writable: false));
                return new ArtifactAcquisitionOutcome.Acquired(
                    [first, second],
                    ArtifactAcquisitionLeases.None);
            });
        await VerifyEquivalentAsync(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = 1,
                MaxArtifactBytes = 2,
                MaxRetainedBytes = 1,
            },
            scope => AcquiredOutcome(
                scope,
                new Provenance("oversize"),
                [1, 2, 3],
                ArtifactAcquisitionLeases.None));
        await VerifyEquivalentAsync(
            new ArtifactSetSessionLimits(),
            scope =>
            {
                ArtifactContribution duplicate = scope.Register(
                    new Provenance("duplicate"),
                    _ => new MemoryStream([1], writable: false));
                return new ArtifactAcquisitionOutcome.Acquired(
                    [duplicate, duplicate],
                    ArtifactAcquisitionLeases.None);
            });

        var requiredDiagnostic =
            new Diagnostic(
                "required.unavailable",
                "The required source is unavailable.");
        var failedSession = new ArtifactSetSession();
        await failedSession.AddRequiredAcquisitionAsync(
            (_, _) =>
                ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Unavailable(
                        requiredDiagnostic)),
            cancellationToken: cancellationToken);
        await failedSession.AddSupplementalAcquisitionAsync(
            static (_, _, _) =>
                throw new InvalidOperationException(
                    "The callback must not run."),
            cancellationToken: cancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await failedSession.AddRequiredAcquisitionAsync(
                (scope, _) => Acquired(
                    scope,
                    new Provenance("late"),
                    [1],
                    ArtifactAcquisitionLeases.None),
                cancellationToken: cancellationToken));
        var requiredRejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await failedSession.SealAsync(cancellationToken));
        ArtifactSetAdmissionFailure requiredFailure =
            Assert.Single(requiredRejected.Failures);
        Assert.Equal(
            ArtifactSetAdmissionFailureKind.Unavailable,
            requiredFailure.Kind);
        Assert.Same(
            requiredDiagnostic,
            requiredFailure.Diagnostic);

        async Task VerifyEquivalentAsync(
            ArtifactSetSessionLimits limits,
            Func<
                ArtifactContributionScope,
                ArtifactAcquisitionOutcome> create)
        {
            ArtifactSetAdmissionFailure direct =
                await RunAsync(useCheckpoint: false);
            ArtifactSetAdmissionFailure checkpointed =
                await RunAsync(useCheckpoint: true);
            Assert.Equal(direct.Kind, checkpointed.Kind);
            Assert.Equal(
                direct.Diagnostic.Code,
                checkpointed.Diagnostic.Code);

            async Task<ArtifactSetAdmissionFailure> RunAsync(
                bool useCheckpoint)
            {
                var session = new ArtifactSetSession(limits);
                await session.AddRequiredAcquisitionAsync(
                    (scope, _) =>
                        ValueTask.FromResult(create(scope)),
                    cancellationToken: cancellationToken);
                if (useCheckpoint)
                {
                    bool invoked = false;
                    await session.AddSupplementalAcquisitionAsync(
                        (_, _, _) =>
                        {
                            invoked = true;
                            return ValueTask.FromResult<
                                ArtifactAcquisitionOutcome>(
                                    new ArtifactAcquisitionOutcome.Acquired(
                                        [],
                                        ArtifactAcquisitionLeases.None));
                        },
                        cancellationToken: cancellationToken);
                    Assert.False(invoked);
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        async () =>
                            await session
                                .AddRequiredAcquisitionAsync(
                                    (scope, _) => Acquired(
                                        scope,
                                        new Provenance("late"),
                                        [1],
                                        ArtifactAcquisitionLeases.None),
                                    cancellationToken:
                                        cancellationToken));
                }

                var rejected =
                    Assert.IsType<
                        ArtifactSetPublicationOutcome.NotPublished>(
                            await session.SealAsync(
                                cancellationToken));
                return Assert.Single(rejected.Failures);
            }
        }
    }

    [Fact]
    public async Task SupplementalAcquisition_SealUsesCheckpointedSnapshots()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] source = [1, 2, 3];
        int openCount = 0;
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution contribution = scope.Register(
                    new Provenance("required"),
                    _ =>
                    {
                        openCount++;
                        return new MemoryStream(
                            source,
                            writable: false);
                    });
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [contribution],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: cancellationToken);
        await session.AddSupplementalAcquisitionAsync(
            static (_, _, _) =>
                ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [],
                        ArtifactAcquisitionLeases.None)),
            cancellationToken: cancellationToken);

        Assert.Equal(1, openCount);
        source[0] = 9;
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        Assert.Equal(1, openCount);

        ArtifactQueryAuthorization authorization =
            session.CreateQueryAuthorization();
        using ArtifactQueryLease lease =
            session.IssueLease(authorization);
        ArtifactDescriptor artifact =
            Assert.Single(session.GetCatalog(lease));
        using Stream opened =
            session.OpenRead(artifact.Identity, lease);
        Assert.Equal(
            [1, 2, 3],
            ReadAll(opened));
    }

    [Fact]
    public async Task SupplementalAcquisition_EmptyBatchPublishesNoArtifactsAndOwnsItsLease()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var emptyLease = new ThrowingLease();
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("required"),
                [1],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        await session.AddSupplementalAcquisitionAsync(
            (_, capacity, _) =>
            {
                Assert.True(capacity.MaxArtifacts > 0);
                Assert.True(capacity.MaxArtifactBytes > 0);
                Assert.True(capacity.MaxRetainedBytes > 0);
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [],
                        emptyLease));
            },
            [ArtifactWorkspaceRole.CallerDesignated],
            cancellationToken);

        Assert.IsType<IOException>(
            Assert.Single(session.CleanupFailures));
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        ArtifactQueryAuthorization authorization =
            session.CreateQueryAuthorization();
        using ArtifactQueryLease lease =
            session.IssueLease(authorization);
        ArtifactDescriptor required =
            Assert.Single(session.GetCatalog(lease));
        Assert.False(
            session.HasRole(
                required.Identity,
                ArtifactWorkspaceRole.CallerDesignated,
                lease));
    }

    [Fact]
    public async Task SupplementalAcquisition_ReservesBeforeAdapterAndCannotOverrunAtSeal()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using var session = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = 3,
                MaxArtifactBytes = 3,
                MaxRetainedBytes = 5,
            });
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("required"),
                [1, 2],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        await session.AddSupplementalAcquisitionAsync(
            (scope, capacity, _) =>
            {
                Assert.Equal(2, capacity.MaxArtifacts);
                Assert.Equal(3, capacity.MaxArtifactBytes);
                Assert.Equal(3, capacity.MaxRetainedBytes);
                return Acquired(
                    scope,
                    new Provenance("first-supplemental"),
                    [3],
                    ArtifactAcquisitionLeases.None);
            },
            cancellationToken: cancellationToken);
        await session.AddSupplementalAcquisitionAsync(
            (scope, capacity, _) =>
            {
                Assert.Equal(1, capacity.MaxArtifacts);
                Assert.Equal(2, capacity.MaxArtifactBytes);
                Assert.Equal(2, capacity.MaxRetainedBytes);
                return Acquired(
                    scope,
                    new Provenance("second-supplemental"),
                    [4, 5],
                    ArtifactAcquisitionLeases.None);
            },
            cancellationToken: cancellationToken);

        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        ArtifactQueryAuthorization authorization =
            session.CreateQueryAuthorization();
        using ArtifactQueryLease lease =
            session.IssueLease(authorization);
        Assert.Equal(3, session.GetCatalog(lease).Count);

        var rejectedLease = new TrackingLease();
        int opened = 0;
        var overrun = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = 2,
            });
        await overrun.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("required"),
                [1],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        await overrun.AddSupplementalAcquisitionAsync(
            (scope, capacity, _) =>
            {
                Assert.Equal(1, capacity.MaxArtifacts);
                ArtifactContribution first = scope.Register(
                    new Provenance("first"),
                    _ =>
                    {
                        opened++;
                        return new MemoryStream([2], writable: false);
                    });
                ArtifactContribution second = scope.Register(
                    new Provenance("second"),
                    _ =>
                    {
                        opened++;
                        return new MemoryStream([3], writable: false);
                    });
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [first, second],
                        rejectedLease));
            },
            cancellationToken: cancellationToken);

        Assert.Equal(0, opened);
        Assert.Equal(1, rejectedLease.DisposeCount);
        var rejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await overrun.SealAsync(cancellationToken));
        Assert.Equal(
            "artifact.supplemental.count-limit",
            Assert.Single(rejected.Failures).Diagnostic.Code);

        var artifactByteSession = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifactBytes = 2,
                MaxRetainedBytes = 3,
            });
        await artifactByteSession.AddSupplementalAcquisitionAsync(
            (scope, _, _) => Acquired(
                scope,
                new Provenance("oversize"),
                [1, 2, 3, 4],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        var artifactByteRejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await artifactByteSession.SealAsync(
                    cancellationToken));
        Assert.Equal(
            "artifact.supplemental.artifact-byte-limit",
            Assert.Single(
                artifactByteRejected.Failures).Diagnostic.Code);

        var retainedByteSession = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = 2,
                MaxArtifactBytes = 3,
                MaxRetainedBytes = 3,
            });
        await retainedByteSession.AddSupplementalAcquisitionAsync(
            (scope, _, _) =>
            {
                ArtifactContribution first = scope.Register(
                    new Provenance("first"),
                    _ => new MemoryStream([1, 2], writable: false));
                ArtifactContribution second = scope.Register(
                    new Provenance("second"),
                    _ => new MemoryStream([3, 4], writable: false));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [first, second],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: cancellationToken);
        var retainedByteRejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await retainedByteSession.SealAsync(
                    cancellationToken));
        Assert.Equal(
            "artifact.supplemental.byte-limit",
            Assert.Single(
                retainedByteRejected.Failures).Diagnostic.Code);

        bool capacityCallbackInvoked = false;
        var exhaustedSession = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = 1,
            });
        await exhaustedSession.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("required"),
                [1],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        await exhaustedSession.AddSupplementalAcquisitionAsync(
            (_, _, _) =>
            {
                capacityCallbackInvoked = true;
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: cancellationToken);
        Assert.False(capacityCallbackInvoked);
        var capacityRejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await exhaustedSession.SealAsync(cancellationToken));
        Assert.Equal(
            "artifact.supplemental.capacity-exhausted",
            Assert.Single(capacityRejected.Failures).Diagnostic.Code);
    }

    [Fact]
    public async Task SupplementalAcquisition_PreservesAdapterOutcomeKindAndDiagnostic()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;

        await VerifyAsync(
            ArtifactSetAdmissionFailureKind.Unavailable,
            diagnostic => new ArtifactAcquisitionOutcome.Unavailable(
                diagnostic));
        await VerifyAsync(
            ArtifactSetAdmissionFailureKind.Rejected,
            diagnostic => new ArtifactAcquisitionOutcome.Rejected(
                diagnostic));
        await VerifyAsync(
            ArtifactSetAdmissionFailureKind.Failed,
            diagnostic => new ArtifactAcquisitionOutcome.Failed(
                diagnostic));

        async Task VerifyAsync(
            ArtifactSetAdmissionFailureKind expectedKind,
            Func<Diagnostic, ArtifactAcquisitionOutcome> create)
        {
            var diagnostic =
                new Diagnostic(
                    $"adapter.{expectedKind}",
                    "Adapter diagnostic.");
            var session = new ArtifactSetSession();
            await session.AddRequiredAcquisitionAsync(
                (scope, _) => Acquired(
                    scope,
                    new Provenance("required"),
                    [1],
                    ArtifactAcquisitionLeases.None),
                cancellationToken: cancellationToken);
            await session.AddSupplementalAcquisitionAsync(
                (_, _, _) =>
                    ValueTask.FromResult(create(diagnostic)),
                cancellationToken: cancellationToken);

            var rejected =
                Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                    await session.SealAsync(cancellationToken));
            ArtifactSetAdmissionFailure failure =
                Assert.Single(rejected.Failures);
            Assert.Equal(expectedKind, failure.Kind);
            Assert.Same(diagnostic, failure.Diagnostic);
        }
    }

    [Fact]
    public async Task SupplementalAcquisition_NonEmptyBatchPreservesScopeAndRoleChecks()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("required"),
                [1],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        await session.AddSupplementalAcquisitionAsync(
            (scope, _, _) => Acquired(
                scope,
                new Provenance("supplemental"),
                [2],
                ArtifactAcquisitionLeases.None),
            [ArtifactWorkspaceRole.CallerDesignated],
            cancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await session.AddRequiredAcquisitionAsync(
                (scope, _) => Acquired(
                    scope,
                    new Provenance("late"),
                    [3],
                    ArtifactAcquisitionLeases.None),
                cancellationToken: cancellationToken));
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        ArtifactQueryAuthorization authorization =
            session.CreateQueryAuthorization();
        using ArtifactQueryLease lease =
            session.IssueLease(authorization);
        IReadOnlyList<ArtifactDescriptor> catalog =
            session.GetCatalog(lease);
        Assert.False(
            session.HasRole(
                catalog[0].Identity,
                ArtifactWorkspaceRole.CallerDesignated,
                lease));
        Assert.True(
            session.HasRole(
                catalog[1].Identity,
                ArtifactWorkspaceRole.CallerDesignated,
                lease));

        ArtifactContribution? foreign = null;
        await using var foreignSession = new ArtifactSetSession();
        await foreignSession.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                foreign = scope.Register(
                    new Provenance("foreign"),
                    _ => new MemoryStream([4], writable: false));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [foreign],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: cancellationToken);
        var rejectedLease = new TrackingLease();
        var target = new ArtifactSetSession();
        await target.AddSupplementalAcquisitionAsync(
            (_, _, _) =>
                ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [foreign!],
                        rejectedLease)),
            cancellationToken: cancellationToken);

        Assert.Equal(1, rejectedLease.DisposeCount);
        var rejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await target.SealAsync(cancellationToken));
        Assert.Equal(
            "artifact.supplemental.foreign",
            Assert.Single(rejected.Failures).Diagnostic.Code);

        await using var supplementalOnly = new ArtifactSetSession();
        await supplementalOnly.AddSupplementalAcquisitionAsync(
            (scope, _, _) => Acquired(
                scope,
                new Provenance("only"),
                [5],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await supplementalOnly.SealAsync(cancellationToken));

        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var overlap = new ArtifactSetSession();
        Task requiredAcquisition =
            overlap.AddRequiredAcquisitionAsync(
                async (scope, token) =>
                {
                    ArtifactContribution contribution =
                        scope.Register(
                            new Provenance("first"),
                            _ => new MemoryStream(
                                [1],
                                writable: false));
                    entered.SetResult();
                    await release.Task.WaitAsync(token);
                    return new ArtifactAcquisitionOutcome.Acquired(
                        [contribution],
                        ArtifactAcquisitionLeases.None);
                },
                cancellationToken: cancellationToken).AsTask();
        await entered.Task.WaitAsync(cancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await overlap.AddSupplementalAcquisitionAsync(
                static (_, _, _) =>
                    ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                        new ArtifactAcquisitionOutcome.Acquired(
                            [],
                            ArtifactAcquisitionLeases.None)),
                cancellationToken: cancellationToken));
        release.SetResult();
        await requiredAcquisition;
        await overlap.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("second"),
                [2],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await overlap.SealAsync(cancellationToken));
    }

    [Fact]
    public async Task SupplementalAcquisition_IdentityAndMaterializationAreAtomic()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        int collisionOpens = 0;
        var collisionLease = new TrackingLease();
        var collisionSession = new ArtifactSetSession();
        await collisionSession.AddSupplementalAcquisitionAsync(
            (scope, _, _) =>
            {
                ArtifactContribution duplicate = scope.Register(
                    new Provenance("duplicate"),
                    _ =>
                    {
                        collisionOpens++;
                        return new MemoryStream([1], writable: false);
                    });
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [duplicate, duplicate],
                        collisionLease));
            },
            cancellationToken: cancellationToken);

        Assert.Equal(0, collisionOpens);
        Assert.Equal(1, collisionLease.DisposeCount);
        var collisionRejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await collisionSession.SealAsync(cancellationToken));
        Assert.Equal(
            "artifact.supplemental.identity-collision",
            Assert.Single(collisionRejected.Failures).Diagnostic.Code);

        int firstOpens = 0;
        var materializationLease = new TrackingLease();
        var materializationSession = new ArtifactSetSession();
        await materializationSession.AddSupplementalAcquisitionAsync(
            (scope, _, _) =>
            {
                ArtifactContribution first = scope.Register(
                    new Provenance("first"),
                    _ =>
                    {
                        firstOpens++;
                        return new MemoryStream([1], writable: false);
                    });
                ArtifactContribution second = scope.Register(
                    new Provenance("second"),
                    _ => throw new IOException("read failed"));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [first, second],
                        materializationLease));
            },
            cancellationToken: cancellationToken);

        Assert.Equal(1, firstOpens);
        Assert.Equal(1, materializationLease.DisposeCount);
        var materializationRejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await materializationSession.SealAsync(
                    cancellationToken));
        Assert.Equal(
            "artifact.supplemental.materialization-failed",
            Assert.Single(
                materializationRejected.Failures).Diagnostic.Code);

        var unexpectedLease = new TrackingLease();
        var unexpectedSession = new ArtifactSetSession();
        FormatException unexpected =
            await Assert.ThrowsAsync<FormatException>(
                async () =>
                    await unexpectedSession
                        .AddSupplementalAcquisitionAsync(
                            (scope, _, _) =>
                            {
                                ArtifactContribution contribution =
                                    scope.Register(
                                        new Provenance("unexpected"),
                                        _ => throw
                                            new FormatException(
                                                "unexpected"));
                                return ValueTask.FromResult<
                                    ArtifactAcquisitionOutcome>(
                                        new ArtifactAcquisitionOutcome.Acquired(
                                            [contribution],
                                            unexpectedLease));
                            },
                            cancellationToken: cancellationToken));
        Assert.Equal("unexpected", unexpected.Message);
        Assert.Equal(1, unexpectedLease.DisposeCount);
        Assert.Throws<ObjectDisposedException>(
            unexpectedSession.CreateQueryAuthorization);
    }

    [Fact]
    public async Task SupplementalAcquisition_RejectedAcquiredBatchCleansLeaseWithoutMaskingFailure()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var session = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = 1,
            });
        await session.AddSupplementalAcquisitionAsync(
            (scope, _, _) =>
            {
                ArtifactContribution first = scope.Register(
                    new Provenance("first"),
                    _ => new MemoryStream([1], writable: false));
                ArtifactContribution second = scope.Register(
                    new Provenance("second"),
                    _ => new MemoryStream([2], writable: false));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [first, second],
                        new ThrowingLease()));
            },
            cancellationToken: cancellationToken);

        Assert.IsType<IOException>(
            Assert.Single(session.CleanupFailures));
        var rejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await session.SealAsync(cancellationToken));
        ArtifactSetAdmissionFailure failure =
            Assert.Single(rejected.Failures);
        Assert.Equal(
            ArtifactSetAdmissionFailureKind.Rejected,
            failure.Kind);
        Assert.Equal(
            "artifact.supplemental.count-limit",
            failure.Diagnostic.Code);
        Assert.IsType<IOException>(
            Assert.Single(rejected.CleanupFailures));
    }

    [Fact]
    public async Task SupplementalAcquisition_ConcurrentTerminationDisposesLateOutcomeAndReservation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lateLease = new ThrowingLease();
        var session = new ArtifactSetSession();

        Task acquisition = session.AddSupplementalAcquisitionAsync(
            async (scope, capacity, token) =>
            {
                Assert.Equal(
                    ArtifactSetSessionLimits.DefaultMaxArtifacts,
                    capacity.MaxArtifacts);
                ArtifactContribution contribution = scope.Register(
                    new Provenance("late"),
                    _ => new MemoryStream([1], writable: false));
                entered.SetResult();
                await release.Task.WaitAsync(token);
                return new ArtifactAcquisitionOutcome.Acquired(
                    [contribution],
                    lateLease);
            },
            cancellationToken: cancellationToken).AsTask();

        await entered.Task.WaitAsync(cancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await session.AddSupplementalAcquisitionAsync(
                static (_, _, _) =>
                    ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                        new ArtifactAcquisitionOutcome.Acquired(
                            [],
                            ArtifactAcquisitionLeases.None)),
                cancellationToken: cancellationToken));
        await session.DisposeAsync();
        release.SetResult();

        ObjectDisposedException disposed =
            await Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await acquisition);
        IReadOnlyList<Exception> attached =
            Assert.IsAssignableFrom<IReadOnlyList<Exception>>(
                disposed.Data[
                    "DotnetInspector.Artifacts.Workspaces.CleanupFailures"]);
        Assert.IsType<IOException>(Assert.Single(attached));
        Assert.Same(
            Assert.Single(attached),
            Assert.Single(session.CleanupFailures));
        Assert.False(
            disposed.Data.Contains(
                "DotnetInspector.Artifacts.Workspaces.AdmissionFailures"));
    }

    [Fact]
    public async Task SupplementalAcquisition_LateDiagnosticRemainsVisibleOnTermination()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostic =
            new Diagnostic(
                "adapter.rejected",
                "The adapter rejected the source.");
        var session = new ArtifactSetSession();

        Task acquisition = session.AddSupplementalAcquisitionAsync(
            async (_, _, token) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
                return new ArtifactAcquisitionOutcome.Rejected(
                    diagnostic);
            },
            cancellationToken: cancellationToken).AsTask();

        await entered.Task.WaitAsync(cancellationToken);
        await session.DisposeAsync();
        release.SetResult();

        ObjectDisposedException disposed =
            await Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await acquisition);
        IReadOnlyList<ArtifactSetAdmissionFailure> attached =
            Assert.IsAssignableFrom<
                IReadOnlyList<ArtifactSetAdmissionFailure>>(
                    disposed.Data[
                        "DotnetInspector.Artifacts.Workspaces.AdmissionFailures"]);
        ArtifactSetAdmissionFailure failure =
            Assert.Single(attached);
        Assert.Equal(
            ArtifactSetAdmissionFailureKind.Rejected,
            failure.Kind);
        Assert.Same(diagnostic, failure.Diagnostic);
    }

    [Fact]
    public async Task SupplementalAcquisition_CancellationRemainsCancellation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new TrackingLease();
        var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("required"),
                [1],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);

        Task acquisition = session.AddSupplementalAcquisitionAsync(
            (scope, _, _) =>
            {
                ArtifactContribution contribution = scope.Register(
                    new Provenance("supplemental"),
                    _ => new GatedReadStream(
                        [2],
                        entered,
                        never));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [contribution],
                        lease));
            },
            cancellationToken: cancellation.Token).AsTask();

        await entered.Task.WaitAsync(cancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await acquisition);
        Assert.Equal(1, lease.DisposeCount);
        Assert.Throws<ObjectDisposedException>(
            session.CreateQueryAuthorization);

        using var checkpointCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        var checkpointEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var checkpointNever = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var checkpointSession = new ArtifactSetSession();
        await checkpointSession.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution contribution = scope.Register(
                    new Provenance("required"),
                    _ => new GatedReadStream(
                        [1],
                        checkpointEntered,
                        checkpointNever));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [contribution],
                        new ThrowingLease()));
            },
            cancellationToken: cancellationToken);
        bool callbackInvoked = false;
        Task checkpoint =
            checkpointSession.AddSupplementalAcquisitionAsync(
                (_, _, _) =>
                {
                    callbackInvoked = true;
                    return ValueTask.FromResult<
                        ArtifactAcquisitionOutcome>(
                            new ArtifactAcquisitionOutcome.Acquired(
                                [],
                                ArtifactAcquisitionLeases.None));
                },
                cancellationToken:
                    checkpointCancellation.Token).AsTask();
        await checkpointEntered.Task.WaitAsync(cancellationToken);
        checkpointCancellation.Cancel();
        OperationCanceledException checkpointCanceled =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await checkpoint);
        Assert.False(callbackInvoked);
        IReadOnlyList<Exception> checkpointCleanup =
            Assert.IsAssignableFrom<IReadOnlyList<Exception>>(
                checkpointCanceled.Data[
                    "DotnetInspector.Artifacts.Workspaces.CleanupFailures"]);
        Assert.IsType<IOException>(
            Assert.Single(checkpointCleanup));

        using var cancellationFirst =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var permitCancellation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationFirstSession = new ArtifactSetSession();
        Task cancellationFirstTask =
            cancellationFirstSession.AddSupplementalAcquisitionAsync(
                async (_, _, token) =>
                {
                    try
                    {
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            token);
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationObserved.SetResult();
                        await permitCancellation.Task;
                        throw;
                    }

                    throw new InvalidOperationException(
                        "Cancellation was not observed.");
                },
                cancellationToken: cancellationFirst.Token).AsTask();
        cancellationFirst.Cancel();
        await cancellationObserved.Task.WaitAsync(cancellationToken);
        await cancellationFirstSession.DisposeAsync();
        permitCancellation.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await cancellationFirstTask);

        using var disposalFirstCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        var adapterEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterNever = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disposalFirstSession = new ArtifactSetSession();
        Task disposalFirst =
            disposalFirstSession.AddSupplementalAcquisitionAsync(
                async (_, _, token) =>
                {
                    adapterEntered.SetResult();
                    await adapterNever.Task.WaitAsync(token);
                    return new ArtifactAcquisitionOutcome.Acquired(
                        [],
                        ArtifactAcquisitionLeases.None);
                },
                cancellationToken:
                    disposalFirstCancellation.Token).AsTask();
        await adapterEntered.Task.WaitAsync(cancellationToken);
        await disposalFirstSession.DisposeAsync();
        disposalFirstCancellation.Cancel();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await disposalFirst);
    }

    [Fact]
    public async Task ArtifactOpen_RejectsContentSubstitutionAfterAdmission()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] source = [1, 2, 3];
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution contribution = scope.Register(
                    new Provenance("mutable"),
                    _ => new MemoryStream(
                        source,
                        writable: false));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [contribution],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        source[0] = 9;

        ArtifactQueryAuthorization authorization =
            session.CreateQueryAuthorization();
        using ArtifactQueryLease lease =
            session.IssueLease(authorization);
        ArtifactIdentity identity =
            Assert.Single(session.GetCatalog(lease)).Identity;
        using Stream first = session.OpenRead(identity, lease);

        Assert.False(first.CanWrite);
        Assert.IsNotType<MemoryStream>(first);
        Assert.Throws<NotSupportedException>(
            () => first.WriteByte(7));
        Assert.Equal([1, 2, 3], ReadAll(first));

        using Stream second = session.OpenRead(identity, lease);
        Assert.Equal([1, 2, 3], ReadAll(second));
    }

    [Fact]
    public async Task CallerDesignation_IsAssignedByAdmissionRatherThanProvenance()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("designated"),
                [1],
                ArtifactAcquisitionLeases.None),
            [ArtifactWorkspaceRole.CallerDesignated],
            cancellationToken);
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("ordinary"),
                [2],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));

        ArtifactQueryAuthorization authorization =
            session.CreateQueryAuthorization();
        using ArtifactQueryLease lease =
            session.IssueLease(authorization);
        IReadOnlyList<ArtifactDescriptor> catalog =
            session.GetCatalog(lease);

        Assert.True(
            session.HasRole(
                catalog[0].Identity,
                ArtifactWorkspaceRole.CallerDesignated,
                lease));
        Assert.False(
            session.HasRole(
                catalog[1].Identity,
                ArtifactWorkspaceRole.CallerDesignated,
                lease));
        Assert.IsNotType<ArtifactWorkspaceRole>(
            session.GetProvenance(
                catalog[0].Identity,
                lease));
    }

    [Fact]
    public async Task ArtifactContentReference_BindsIdentityRegistrationRoleAndContent()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var firstProvenance = new Provenance("first");
        var secondProvenance = new Provenance("second");
        await using var firstSession = new ArtifactSetSession();
        await firstSession.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                firstProvenance,
                [1],
                ArtifactAcquisitionLeases.None),
            [ArtifactWorkspaceRole.CallerDesignated],
            cancellationToken);
        await firstSession.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                secondProvenance,
                [2],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await firstSession.SealAsync(cancellationToken));

        ArtifactQueryAuthorization authorization =
            firstSession.CreateQueryAuthorization();
        using var lease = firstSession.IssueLease(authorization);
        IReadOnlyList<ArtifactDescriptor> catalog =
            firstSession.GetCatalog(lease);
        ArtifactContentReference first =
            firstSession.GetContentReference(
                catalog[0].Identity,
                lease);
        ArtifactContentReference second =
            firstSession.GetContentReference(
                catalog[1].Identity,
                lease);

        Assert.Same(catalog[0], first.Descriptor);
        Assert.Same(
            first.Descriptor.Identity,
            first.Registration.Artifact);
        Assert.Same(firstProvenance, first.Registration.Provenance);
        Assert.True(first.HasRole(
            ArtifactWorkspaceRole.CallerDesignated));
        using Stream firstContent = first.OpenRead();
        Assert.Equal([1], ReadAll(firstContent));

        Assert.Same(catalog[1], second.Descriptor);
        Assert.Same(
            second.Descriptor.Identity,
            second.Registration.Artifact);
        Assert.Same(secondProvenance, second.Registration.Provenance);
        Assert.False(second.HasRole(
            ArtifactWorkspaceRole.CallerDesignated));
        using Stream secondContent = second.OpenRead();
        Assert.Equal([2], ReadAll(secondContent));

        await using var secondSession = new ArtifactSetSession();
        await secondSession.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("foreign"),
                [3],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await secondSession.SealAsync(cancellationToken));
        ArtifactQueryAuthorization secondAuthorization =
            secondSession.CreateQueryAuthorization();
        using ArtifactQueryLease secondLease =
            secondSession.IssueLease(secondAuthorization);
        ArtifactDescriptor foreign =
            Assert.Single(secondSession.GetCatalog(secondLease));
        Assert.Throws<KeyNotFoundException>(
            () => firstSession.GetContentReference(
                foreign.Identity,
                lease));

        firstSession.Revoke(authorization);
        Assert.Throws<UnauthorizedAccessException>(
            () => _ = first.Registration);
        Assert.Throws<UnauthorizedAccessException>(
            () => first.HasRole(
                ArtifactWorkspaceRole.CallerDesignated));
        Assert.Throws<UnauthorizedAccessException>(
            () => first.OpenRead());

        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => _ = first.Registration);
        Assert.Throws<ObjectDisposedException>(
            () => first.HasRole(
                ArtifactWorkspaceRole.CallerDesignated));
        Assert.Throws<ObjectDisposedException>(
            () => first.OpenRead());
        Assert.Empty(
            typeof(ArtifactContentReference).GetConstructors());
    }

    [Fact]
    public async Task SessionAccess_RejectsReplacedQueryAuthorization()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("only"),
                [1],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        ArtifactQueryAuthorization authorization =
            session.CreateQueryAuthorization();
        using ArtifactQueryLease lease =
            session.IssueLease(authorization);
        ArtifactIdentity identity =
            Assert.Single(session.GetCatalog(lease)).Identity;

        ArtifactQueryAuthorization replacement =
            session.ReplaceQueryAuthorization(authorization);

        Assert.Throws<UnauthorizedAccessException>(
            () => session.OpenRead(identity, lease));
        using ArtifactQueryLease replacementLease =
            session.IssueLease(replacement);
        using Stream opened =
            session.OpenRead(identity, replacementLease);
        Assert.Equal(1, opened.ReadByte());
    }

    private static ValueTask<ArtifactAcquisitionOutcome> Acquired(
        ArtifactContributionScope scope,
        IArtifactProvenance provenance,
        byte[] content,
        IArtifactAcquisitionLease lease)
    {
        return ValueTask.FromResult(
            AcquiredOutcome(
                scope,
                provenance,
                content,
                lease));
    }

    private static ArtifactAcquisitionOutcome AcquiredOutcome(
        ArtifactContributionScope scope,
        IArtifactProvenance provenance,
        byte[] content,
        IArtifactAcquisitionLease lease)
    {
        ArtifactContribution contribution = scope.Register(
            provenance,
            _ => new MemoryStream(
                content,
                writable: false));
        return new ArtifactAcquisitionOutcome.Acquired(
            [contribution],
            lease);
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var destination = new MemoryStream();
        stream.CopyTo(destination);
        return destination.ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(
        ArtifactSetSession Session,
        WeakReference<object> ProvenanceMarker)>
        CreateDisposedSessionWithTrackedProvenance(
            CancellationToken cancellationToken)
    {
        var session = new ArtifactSetSession();
        WeakReference<object>? markerReference = null;
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                var marker = new object();
                markerReference = new WeakReference<object>(marker);
                return Acquired(
                    scope,
                    new TrackedProvenance(marker),
                    [1],
                    ArtifactAcquisitionLeases.None);
            },
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        await session.DisposeAsync();
        return (session, markerReference!);
    }

    private sealed record Provenance(string Source) :
        IArtifactProvenance;

    private sealed record TrackedProvenance(object Marker) :
        IArtifactProvenance;

    private sealed record Diagnostic(
        string Code,
        string Summary) :
        IArtifactAcquisitionDiagnostic;

    private sealed class TrackingLease :
        IArtifactAcquisitionLease
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingLease :
        IArtifactAcquisitionLease
    {
        public ValueTask DisposeAsync() =>
            ValueTask.FromException(
                new IOException("cleanup failed"));
    }

    private sealed class BlockingThrowingLease :
        IArtifactAcquisitionLease
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Release() => _release.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            _entered.TrySetResult();
            await _release.Task;
            throw new IOException("cleanup failed");
        }
    }

    private sealed class GatedReadStream(
        byte[] content,
        TaskCompletionSource entered,
        TaskCompletionSource release) :
        MemoryStream(content, writable: false)
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(
                buffer,
                cancellationToken);
        }
    }

    private sealed class OwnerCancellationReadStream(
        TaskCompletionSource entered,
        TaskCompletionSource cancellationObserved) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length =>
            throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                throw;
            }

            throw new InvalidOperationException(
                "The owner did not cancel the materialization read.");
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }
}
