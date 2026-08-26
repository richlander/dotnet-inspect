using System.Runtime.CompilerServices;

using DotnetInspector.Artifacts.Workspaces;

namespace DotnetInspector.Artifacts.Tests;

public sealed class ArtifactSetSessionTests
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
            Assert.Equal(
                [1, 2],
                ReadAll(session.OpenRead(
                    catalog[0].Identity,
                    lease)));
            Assert.Equal(
                [3, 4],
                ReadAll(session.OpenRead(
                    catalog[1].Identity,
                    lease)));
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
    public async Task ArtifactSetSession_DisposalDuringAcquisitionDisposesLateLease()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var acquisitionLease = new TrackingLease();
        var session = new ArtifactSetSession();

        Task acquisition = session.AddRequiredAcquisitionAsync(
            async (scope, token) =>
            {
                ArtifactContribution contribution = scope.Register(
                    new Provenance("late"),
                    () => new MemoryStream(
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

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await acquisition);
        Assert.Equal(1, acquisitionLease.DisposeCount);
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
                ArtifactContribution contribution = scope.Register(
                    new Provenance("gated"),
                    () => new GatedReadStream(
                        [1],
                        entered,
                        release));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [contribution],
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
                    () => throw new ObjectDisposedException(
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
                    () => new MemoryStream(
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
        Assert.False(
            Assert.IsType<MemoryStream>(first)
                .TryGetBuffer(out _));
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
        ArtifactContribution contribution = scope.Register(
            provenance,
            () => new MemoryStream(
                content,
                writable: false));
        return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
            new ArtifactAcquisitionOutcome.Acquired(
                [contribution],
                lease));
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
}
