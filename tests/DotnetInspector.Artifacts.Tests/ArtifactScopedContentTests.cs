using System.Collections.Immutable;
using DotnetInspector.Artifacts.Workspaces;

namespace DotnetInspector.Artifacts.Tests;

public sealed class ArtifactScopedContentTests
{
    [Fact]
    public void ScopedContent_AdmissionAndQueryKeepExactIdentityAndBytes()
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission = owner.CreateAdmissionAuthorization();
        using ArtifactAdmissionLease admissionLease = owner.IssueLease(admission);
        (ArtifactContribution contribution, RetainedArtifactContent content) =
            Retain(owner, admission);
        var marker = new object();

        var projected = Assert.IsType<ArtifactContentAccessOutcome<object>.Accessed>(
            content.WithAdmissionContent(admissionLease, (view, _) =>
            {
                Assert.Same(contribution.Registration.Artifact, view.Artifact);
                Assert.Same(owner.Generation, view.Generation);
                Assert.True(view.Content.SequenceEqual<byte>([1, 2, 3]));
                return marker;
            }, TestContext.Current.CancellationToken));
        Assert.Same(marker, projected.Value);
        owner.CompleteAdmission(admission);
        using ArtifactQueryLease query = owner.IssueLease(owner.CreateQueryAuthorization());
        Assert.IsType<ArtifactContentAccessOutcome<int>.Accessed>(
            content.WithQueryContent(query, (view, _) =>
            {
                Assert.Same(contribution.Registration.Artifact, view.Artifact);
                Assert.Same(owner.Generation, view.Generation);
                Assert.True(view.Content.SequenceEqual<byte>([1, 2, 3]));
                return view.Content.Length;
            }, TestContext.Current.CancellationToken));
        using Stream stream = content.OpenRead(query);
        Assert.False(stream.CanWrite);
        Assert.Equal(1, stream.ReadByte());
        owner.EndGeneration();
    }

    [Theory]
    [InlineData(false, "missing")]
    [InlineData(false, "foreign")]
    [InlineData(false, "disposed")]
    [InlineData(false, "revoked")]
    [InlineData(false, "ended")]
    [InlineData(true, "missing")]
    [InlineData(true, "foreign")]
    [InlineData(true, "disposed")]
    [InlineData(true, "revoked")]
    [InlineData(true, "replaced")]
    [InlineData(true, "ended")]
    public void ScopedContent_RejectsAuthorityBeforeInvocation(bool query, string state)
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission = owner.CreateAdmissionAuthorization();
        using ArtifactAdmissionLease admissionLease = owner.IssueLease(admission);
        (_, RetainedArtifactContent content) = Retain(owner, admission);
        var foreign = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization foreignAdmission = foreign.CreateAdmissionAuthorization();
        using ArtifactAdmissionLease foreignAdmissionLease = foreign.IssueLease(foreignAdmission);
        ArtifactQueryLease? foreignQueryLease = null;
        if (query)
        {
            foreign.CompleteAdmission(foreignAdmission);
            foreignQueryLease = foreign.IssueLease(foreign.CreateQueryAuthorization());
        }
        int calls = 0;
        ArtifactContentAccessOutcome<int> outcome;
        if (query)
        {
            owner.CompleteAdmission(admission);
            ArtifactQueryAuthorization authorization = owner.CreateQueryAuthorization();
            using ArtifactQueryLease lease = owner.IssueLease(authorization);
            if (state == "disposed")
                lease.Dispose();
            if (state == "revoked")
                owner.Revoke(authorization);
            if (state == "replaced")
                owner.ReplaceQueryAuthorization(authorization);
            if (state == "ended")
                owner.EndGeneration();
            outcome = content.WithQueryContent(
                state == "missing" ? null : state == "foreign" ? foreignQueryLease : lease,
                (_, _) => ++calls, TestContext.Current.CancellationToken);
        }
        else
        {
            if (state == "disposed")
                admissionLease.Dispose();
            if (state == "revoked")
                owner.CompleteAdmission(admission);
            if (state == "ended")
                owner.EndGeneration();
            outcome = content.WithAdmissionContent(
                state == "missing" ? null : state == "foreign" ? foreignAdmissionLease : admissionLease,
                (_, _) => ++calls, TestContext.Current.CancellationToken);
        }

        Assert.IsType<ArtifactContentAccessOutcome<int>.Unauthorized>(outcome);
        Assert.Equal(0, calls);
        foreignQueryLease?.Dispose();
        owner.EndGeneration();
        foreign.EndGeneration();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedContent_ConsumerExceptionsAreNotAuthorizationFailures(bool query)
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission = owner.CreateAdmissionAuthorization();
        using ArtifactAdmissionLease admissionLease = owner.IssueLease(admission);
        (_, RetainedArtifactContent content) = Retain(owner, admission);
        Exception[] failures =
        [
            new UnauthorizedAccessException("consumer"),
            new ObjectDisposedException("consumer"),
            new IOException("consumer"),
            new InvalidOperationException("consumer"),
        ];
        if (query)
        {
            owner.CompleteAdmission(admission);
            using ArtifactQueryLease lease = owner.IssueLease(owner.CreateQueryAuthorization());
            foreach (Exception failure in failures)
            {
                Assert.Same(failure, Record.Exception(() =>
                    content.WithQueryContent<int>(
                        lease, (_, _) => throw failure, TestContext.Current.CancellationToken)));
            }
        }
        else
        {
            foreach (Exception failure in failures)
            {
                Assert.Same(failure, Record.Exception(() =>
                    content.WithAdmissionContent<int>(
                        admissionLease, (_, _) => throw failure, TestContext.Current.CancellationToken)));
            }
        }
        Assert.True(owner.EndGenerationAsync().IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedContent_CancellationRemainsCancellation(bool query)
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission = owner.CreateAdmissionAuthorization();
        using ArtifactAdmissionLease admissionLease = owner.IssueLease(admission);
        (_, RetainedArtifactContent content) = Retain(owner, admission);
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        int Cancel(CancellationToken token)
        {
            Assert.Equal(cancellation.Token, token);
            calls++;
            cancellation.Cancel();
            return 1;
        }

        OperationCanceledException failure;
        if (query)
        {
            owner.CompleteAdmission(admission);
            using ArtifactQueryLease lease = owner.IssueLease(owner.CreateQueryAuthorization());
            failure = Assert.Throws<OperationCanceledException>(() =>
                content.WithQueryContent(lease, (_, token) => Cancel(token), cancellation.Token));
            Assert.Throws<OperationCanceledException>(() =>
                content.WithQueryContent(lease, (_, _) => ++calls, cancellation.Token));
        }
        else
        {
            failure = Assert.Throws<OperationCanceledException>(() =>
                content.WithAdmissionContent(admissionLease, (_, token) => Cancel(token), cancellation.Token));
            Assert.Throws<OperationCanceledException>(() =>
                content.WithAdmissionContent(admissionLease, (_, _) => ++calls, cancellation.Token));
        }
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(1, calls);
        Assert.True(owner.EndGenerationAsync().IsCompletedSuccessfully);
    }

    [Fact]
    public void ScopedContent_RequiresImmutableSnapshot()
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission = owner.CreateAdmissionAuthorization();
        using ArtifactAdmissionLease lease = owner.IssueLease(admission);
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope = owner.BeginContribution(admission))
            contribution = scope.Register(new Provenance(), _ => new MemoryStream([1]));
        int opens = 0;
        RetainedArtifactContent content = owner.CreateRetainedContent(
            contribution.Registration, _ =>
            {
                opens++;
                return new MemoryStream([1]);
            });
        Assert.Throws<InvalidOperationException>(() =>
            content.WithAdmissionContent(lease, (_, _) => 0, TestContext.Current.CancellationToken));
        Assert.Equal(0, opens);
        using (Stream stream = content.OpenRead(lease))
            Assert.Equal(1, stream.ReadByte());
        Assert.Equal(1, opens);
        owner.EndGeneration();
    }

    [Fact]
    public void ScopedContent_RepeatedQueriesDoNotAllocateFullImage()
    {
        const int imageSize = 1024 * 1024;
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission = owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope = owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance(), _ => throw new InvalidOperationException("Source must not reopen."));
        }
        RetainedArtifactContent content = owner.CreateRetainedContent(
            contribution.Registration, ImmutableArray.Create(new byte[imageSize]));
        owner.CompleteAdmission(admission);
        using ArtifactQueryLease lease = owner.IssueLease(owner.CreateQueryAuthorization());
        ArtifactQueryContentCallback<int> callback = static (view, _) => view.Content.Length;
        CancellationToken token = TestContext.Current.CancellationToken;
        content.WithQueryContent(lease, callback, token);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 16; index++)
            content.WithQueryContent(lease, callback, token);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < imageSize, $"Scoped query allocations: {allocated} bytes.");
        owner.EndGeneration();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopedContent_ActiveCallbackPinsRelease(bool query)
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission = owner.CreateAdmissionAuthorization();
        using ArtifactAdmissionLease admissionLease = owner.IssueLease(admission);
        (_, RetainedArtifactContent content) = Retain(owner, admission);
        Task? ending = null;
        int End()
        {
            ending = owner.EndGenerationAsync().AsTask();
            Assert.False(ending.IsCompleted);
            return 3;
        }
        if (query)
        {
            owner.CompleteAdmission(admission);
            using ArtifactQueryLease lease = owner.IssueLease(owner.CreateQueryAuthorization());
            content.WithQueryContent(lease, (view, _) =>
            {
                int result = End();
                Assert.True(view.Content.SequenceEqual<byte>([1, 2, 3]));
                return result;
            }, TestContext.Current.CancellationToken);
        }
        else
        {
            content.WithAdmissionContent(admissionLease, (view, _) =>
            {
                int result = End();
                Assert.True(view.Content.SequenceEqual<byte>([1, 2, 3]));
                return result;
            }, TestContext.Current.CancellationToken);
        }
        Assert.NotNull(ending);
        await ending.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArtifactProjection_PrecedesPublicationAndReusesSnapshot(bool checkpointed)
    {
        await using var session = new ArtifactSetSession();
        byte[] source = [1, 2, 3];
        int opens = 0;
        var sourceLease = new TrackingLease();
        ArtifactIdentity? acquiredIdentity = null;
        await session.AddRequiredAcquisitionAsync((scope, _) =>
        {
            ArtifactContribution contribution = scope.Register(new Provenance(), _ =>
            {
                opens++;
                return new MemoryStream(source, writable: false);
            });
            acquiredIdentity = contribution.Descriptor.Identity;
            return Acquired(contribution, sourceLease);
        }, cancellationToken: TestContext.Current.CancellationToken);
        if (checkpointed)
        {
            await session.AddSupplementalAcquisitionAsync(
                (_, _, _) => ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired([], new TrackingLease())),
                cancellationToken: TestContext.Current.CancellationToken);
        }
        int projections = 0;
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealWithProjectionAsync((view, _) =>
            {
                projections++;
                Assert.Same(acquiredIdentity, view.Artifact);
                Assert.Same(session.Generation, view.Generation);
                Assert.True(view.Content.SequenceEqual<byte>([1, 2, 3]));
                Assert.Throws<InvalidOperationException>(() => session.CreateQueryAuthorization());
                source[0] = 99;
                return null;
            }, TestContext.Current.CancellationToken));
        using ArtifactQueryLease query = session.IssueLease(session.CreateQueryAuthorization());
        var result = Assert.IsType<ArtifactContentAccessOutcome<int>.Accessed>(
            session.WithQueryContent(acquiredIdentity!, query, (view, _) =>
            {
                Assert.Same(acquiredIdentity, view.Artifact);
                Assert.True(view.Content.SequenceEqual<byte>([1, 2, 3]));
                return view.Content.Length;
            }, TestContext.Current.CancellationToken));
        Assert.Equal(3, result.Value);
        Assert.Equal(1, projections);
        Assert.Equal(1, opens);
        await session.DisposeAsync();
        Assert.Equal(1, sourceLease.Disposals);
    }

    [Fact]
    public async Task ArtifactProjection_RejectsAtomicallyAndRetainsDiagnostic()
    {
        await using var session = new ArtifactSetSession();
        var lease = new TrackingLease();
        await Add(session, lease);
        await Add(session, new TrackingLease());
        var failure = new ArtifactSetAdmissionFailure(
            ArtifactSetAdmissionFailureKind.Rejected, new Diagnostic());
        int calls = 0;
        var result = Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
            await session.SealWithProjectionAsync((_, _) =>
            {
                calls++;
                return failure;
            }, TestContext.Current.CancellationToken));
        Assert.Same(failure, Assert.Single(result.Failures));
        Assert.Equal(1, calls);
        Assert.Equal(1, lease.Disposals);
        Assert.Throws<ObjectDisposedException>(() => session.CreateQueryAuthorization());
    }

    [Fact]
    public async Task ArtifactProjection_MaterializationFailureDoesNotInvokeConsumer()
    {
        await using var session = new ArtifactSetSession(
            new ArtifactSetSessionLimits { MaxArtifactBytes = 2 });
        var lease = new TrackingLease();
        await Add(session, lease);
        int calls = 0;
        var result = Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
            await session.SealWithProjectionAsync((_, _) =>
            {
                calls++;
                return null;
            }, TestContext.Current.CancellationToken));
        Assert.Equal("artifact.session.artifact-byte-limit",
            Assert.Single(result.Failures).Diagnostic.Code);
        Assert.Equal(0, calls);
        Assert.Equal(1, lease.Disposals);
    }

    [Theory]
    [InlineData("unauthorized")]
    [InlineData("disposed")]
    [InlineData("io")]
    [InlineData("invalid")]
    [InlineData("cancelled")]
    public async Task ArtifactProjection_ExceptionAbortsWithoutReclassification(string kind)
    {
        await using var session = new ArtifactSetSession();
        var cleanup = new IOException("cleanup");
        var lease = new TrackingLease(cleanup);
        await Add(session, lease);
        using var cancellation = new CancellationTokenSource();
        Exception primary = kind switch
        {
            "unauthorized" => new UnauthorizedAccessException("consumer"),
            "disposed" => new ObjectDisposedException("consumer"),
            "io" => new IOException("consumer"),
            "invalid" => new InvalidOperationException("consumer"),
            _ => new OperationCanceledException(cancellation.Token),
        };
        Exception? actual = await Record.ExceptionAsync(async () =>
            await session.SealWithProjectionAsync(
                (_, _) => throw primary, TestContext.Current.CancellationToken));
        Assert.Same(primary, actual);
        Assert.Equal(1, lease.Disposals);
        Assert.Same(cleanup, Assert.Single(session.CleanupFailures));
        var evidence = Assert.IsAssignableFrom<IReadOnlyList<Exception>>(
            primary.Data["DotnetInspector.Artifacts.Workspaces.CleanupFailures"]);
        Assert.Same(cleanup, Assert.Single(evidence));
        Assert.Throws<ObjectDisposedException>(() => session.CreateQueryAuthorization());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArtifactProjection_CancellationOrTerminationPreventsPublication(bool dispose)
    {
        await using var session = new ArtifactSetSession();
        var cleanup = new IOException("cleanup");
        var lease = new TrackingLease(cleanup);
        await Add(session, lease);
        using var cancellation = new CancellationTokenSource();
        Task? disposal = null;
        Exception? actual = await Record.ExceptionAsync(async () =>
            await session.SealWithProjectionAsync((view, _) =>
            {
                if (dispose)
                {
                    disposal = session.DisposeAsync().AsTask();
                    Assert.False(disposal.IsCompleted);
                    Assert.Equal(0, lease.Disposals);
                    Assert.True(view.Content.SequenceEqual<byte>([1, 2, 3]));
                }
                else
                {
                    cancellation.Cancel();
                }
                return null;
            }, cancellation.Token));
        if (dispose)
        {
            Assert.IsType<ObjectDisposedException>(actual);
            await disposal!.WaitAsync(TestContext.Current.CancellationToken);
        }
        else
        {
            Assert.Equal(cancellation.Token,
                Assert.IsType<OperationCanceledException>(actual).CancellationToken);
        }
        Assert.Equal(1, lease.Disposals);
        var evidence = Assert.IsAssignableFrom<IReadOnlyList<Exception>>(
            actual!.Data["DotnetInspector.Artifacts.Workspaces.CleanupFailures"]);
        Assert.Same(cleanup, Assert.Single(evidence));
    }

    [Fact]
    public async Task ArtifactProjection_QueryRejectsBeforeSelectionAndPinsRelease()
    {
        await using var session = new ArtifactSetSession();
        var lease = new TrackingLease();
        await Add(session, lease);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(TestContext.Current.CancellationToken));
        ArtifactQueryAuthorization authorization = session.CreateQueryAuthorization();
        using ArtifactQueryLease query = session.IssueLease(authorization);
        ArtifactIdentity identity = Assert.Single(session.GetCatalog(query)).Identity;
        var foreign = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization foreignAdmission = foreign.CreateAdmissionAuthorization();
        (ArtifactContribution foreignContribution, _) = Retain(foreign, foreignAdmission);
        session.Revoke(authorization);
        int calls = 0;
        Assert.IsType<ArtifactContentAccessOutcome<int>.Unauthorized>(
            session.WithQueryContent(foreignContribution.Descriptor.Identity, query,
                (_, _) => ++calls, TestContext.Current.CancellationToken));
        Assert.Equal(0, calls);
        using ArtifactQueryLease current = session.IssueLease(session.CreateQueryAuthorization());
        Assert.Throws<KeyNotFoundException>(() =>
            session.WithQueryContent(foreignContribution.Descriptor.Identity, current,
                (_, _) => ++calls, TestContext.Current.CancellationToken));
        Task? disposal = null;
        session.WithQueryContent(identity, current, (view, _) =>
        {
            disposal = session.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            Assert.Equal(0, lease.Disposals);
            Assert.True(view.Content.SequenceEqual<byte>([1, 2, 3]));
            return 1;
        }, TestContext.Current.CancellationToken);
        await disposal!.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, lease.Disposals);
        Assert.IsType<ArtifactContentAccessOutcome<int>.Unauthorized>(
            session.WithQueryContent(identity, current,
                (_, _) => ++calls, TestContext.Current.CancellationToken));
        Assert.Equal(0, calls);
        foreign.EndGeneration();
    }

    private static (ArtifactContribution, RetainedArtifactContent) Retain(
        ArtifactGenerationAuthority owner, ArtifactAdmissionAuthorization admission)
    {
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope = owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance(), _ => throw new InvalidOperationException("Source must not reopen."));
        }
        return (contribution, owner.CreateRetainedContent(
            contribution.Registration, ImmutableArray.Create<byte>(1, 2, 3)));
    }

    private static ValueTask Add(ArtifactSetSession session, TrackingLease lease) =>
        session.AddRequiredAcquisitionAsync((scope, _) =>
            Acquired(scope.Register(new Provenance(), _ => new MemoryStream([1, 2, 3])), lease),
            cancellationToken: TestContext.Current.CancellationToken);

    private static ValueTask<ArtifactAcquisitionOutcome> Acquired(
        ArtifactContribution contribution, TrackingLease lease) =>
        ValueTask.FromResult<ArtifactAcquisitionOutcome>(
            new ArtifactAcquisitionOutcome.Acquired([contribution], lease));

    private sealed record Provenance : IArtifactProvenance;

    private sealed record Diagnostic : IArtifactAcquisitionDiagnostic
    {
        public string Code => "test.projection.rejected";
        public string Summary => "The consumer rejected the artifact.";
    }

    private sealed class TrackingLease(Exception? failure = null) : IArtifactAcquisitionLease
    {
        public int Disposals { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return failure is null ? ValueTask.CompletedTask : ValueTask.FromException(failure);
        }
    }
}
