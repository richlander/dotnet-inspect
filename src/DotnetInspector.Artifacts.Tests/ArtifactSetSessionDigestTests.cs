using System.Text;

using DotnetInspector.Artifacts.Workspaces;

namespace DotnetInspector.Artifacts.Tests;

public sealed partial class ArtifactSetSessionTests
{
    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public async Task Digest_ChargesColdPassAndReusesOwnerValue(
        string text,
        string expectedHash)
    {
        byte[] content = Encoding.UTF8.GetBytes(text);
        await using var session = new ArtifactSetSession();
        ArtifactIdentity identity = await PublishDigestFixture(session, content);
        using ArtifactQueryLease lease =
            session.IssueLease(session.CreateQueryAuthorization());
        var charges = new List<long>();

        ArtifactContentDigest first = AccessedDigest(
            session.GetContentDigest(identity, lease, charges.Add, TestContext.Current.CancellationToken));
        using ArtifactQueryLease anotherLease =
            session.IssueLease(session.CreateQueryAuthorization());
        ArtifactContentDigest second = AccessedDigest(
            session.GetContentReference(identity, anotherLease)
                .GetContentDigest(charges.Add, TestContext.Current.CancellationToken));

        Assert.Equal("SHA-256", first.Algorithm);
        Assert.Equal(expectedHash, first.HexValue);
        Assert.Same(identity, first.Artifact);
        Assert.Same(session.Generation, first.Generation);
        Assert.Same(first, second);
        Assert.Equal([content.LongLength], charges);
    }

    [Fact]
    public async Task Digest_UsesRetainedSnapshotWithoutReopeningSource()
    {
        byte[] source = "abc"u8.ToArray();
        int opens = 0;
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync((scope, _) =>
        {
            ArtifactContribution artifact = scope.Register(
                new Provenance("digest-source"),
                _ =>
                {
                    opens++;
                    return new MemoryStream(source, writable: false);
                });
            return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                new ArtifactAcquisitionOutcome.Acquired(
                    [artifact], ArtifactAcquisitionLeases.None));
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(TestContext.Current.CancellationToken));
        using ArtifactQueryLease lease =
            session.IssueLease(session.CreateQueryAuthorization());
        ArtifactIdentity identity = Assert.Single(session.GetCatalog(lease)).Identity;
        source[0] = (byte)'z';

        ArtifactContentDigest digest = AccessedDigest(
            session.GetContentDigest(identity, lease, _ => { }, TestContext.Current.CancellationToken));
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            digest.HexValue);
        Assert.Equal(1, opens);
    }

    [Fact]
    public async Task Digest_EqualBytesDoNotCoalesceArtifactIdentity()
    {
        await using var session = new ArtifactSetSession();
        foreach (string name in new[] { "first", "second" })
        {
            await session.AddRequiredAcquisitionAsync(
                (scope, _) => Acquired(
                    scope, new Provenance(name), [1], ArtifactAcquisitionLeases.None),
                cancellationToken: TestContext.Current.CancellationToken);
        }
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(TestContext.Current.CancellationToken));
        using ArtifactQueryLease lease =
            session.IssueLease(session.CreateQueryAuthorization());
        var charges = new List<long>();
        ArtifactContentDigest[] digests = session.GetCatalog(lease)
            .Select(item => AccessedDigest(
                session.GetContentDigest(item.Identity, lease, charges.Add, TestContext.Current.CancellationToken)))
            .ToArray();

        Assert.Equal(digests[0].HexValue, digests[1].HexValue);
        Assert.NotSame(digests[0], digests[1]);
        Assert.NotSame(digests[0].Artifact, digests[1].Artifact);
        Assert.Equal([1L, 1L], charges);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("disposed")]
    [InlineData("revoked")]
    [InlineData("replaced")]
    [InlineData("ended")]
    public async Task Digest_RejectsAuthorityEvenWhenCached(string mode)
    {
        await using var session = new ArtifactSetSession();
        ArtifactIdentity identity = await PublishDigestFixture(session, [1]);
        ArtifactQueryAuthorization authorization = session.CreateQueryAuthorization();
        using ArtifactQueryLease lease = session.IssueLease(authorization);
        AccessedDigest(session.GetContentDigest(identity, lease, _ => { }, TestContext.Current.CancellationToken));
        await using var foreign = new ArtifactSetSession();
        await PublishDigestFixture(foreign, [2]);
        using ArtifactQueryLease foreignLease =
            foreign.IssueLease(foreign.CreateQueryAuthorization());
        ArtifactQueryLease? attemptedLease = lease;
        switch (mode)
        {
            case "missing":
                attemptedLease = null;
                break;
            case "foreign":
                attemptedLease = foreignLease;
                break;
            case "disposed":
                lease.Dispose();
                break;
            case "revoked":
                session.Revoke(authorization);
                break;
            case "replaced":
                session.ReplaceQueryAuthorization(authorization);
                break;
            case "ended":
                await session.DisposeAsync();
                break;
        }
        int charges = 0;

        Assert.IsType<ArtifactContentAccessOutcome<ArtifactContentDigest>.Unauthorized>(
            session.GetContentDigest(identity, attemptedLease, _ => charges++, TestContext.Current.CancellationToken));
        Assert.Equal(0, charges);
    }

    [Fact]
    public async Task Digest_ReferenceRevalidatesAndAuthorizesBeforeLookup()
    {
        await using var session = new ArtifactSetSession();
        ArtifactIdentity identity = await PublishDigestFixture(session, [1]);
        ArtifactQueryAuthorization authorization = session.CreateQueryAuthorization();
        using ArtifactQueryLease lease = session.IssueLease(authorization);
        ArtifactContentReference reference = session.GetContentReference(identity, lease);
        AccessedDigest(reference.GetContentDigest(_ => { }, TestContext.Current.CancellationToken));
        await using var other = new ArtifactSetSession();
        ArtifactIdentity unknown = await PublishDigestFixture(other, [2]);

        Assert.Throws<KeyNotFoundException>(
            () => session.GetContentDigest(unknown, lease, _ => { }, TestContext.Current.CancellationToken));
        session.Revoke(authorization);
        Assert.IsType<ArtifactContentAccessOutcome<ArtifactContentDigest>.Unauthorized>(
            reference.GetContentDigest(_ => Assert.Fail("Unauthorized charge."), TestContext.Current.CancellationToken));
        Assert.IsType<ArtifactContentAccessOutcome<ArtifactContentDigest>.Unauthorized>(
            session.GetContentDigest(unknown, lease, _ => Assert.Fail("Unauthorized charge."), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Digest_ChargeFailurePropagatesWithoutPublishingValue()
    {
        await using var session = new ArtifactSetSession();
        ArtifactIdentity identity = await PublishDigestFixture(session, [1, 2]);
        using ArtifactQueryLease lease =
            session.IssueLease(session.CreateQueryAuthorization());
        var failure = new InvalidOperationException("Operation budget refused.");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(
            () => session.GetContentDigest(identity, lease, _ => throw failure, TestContext.Current.CancellationToken)));
        var charges = new List<long>();

        ArtifactContentDigest digest = AccessedDigest(
            session.GetContentDigest(identity, lease, charges.Add, TestContext.Current.CancellationToken));
        Assert.Same(digest, AccessedDigest(session.GetContentDigest(
            identity, lease, _ => throw new InvalidOperationException("Warm charge."), TestContext.Current.CancellationToken)));
        Assert.Equal([2L], charges);
    }

    [Fact]
    public async Task Digest_CancellationRemainsCancellationAndCompletedWorkIsMemoized()
    {
        await using var session = new ArtifactSetSession();
        ArtifactIdentity identity = await PublishDigestFixture(session, [1, 2]);
        using ArtifactQueryLease lease =
            session.IssueLease(session.CreateQueryAuthorization());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.GetContentDigest(
            identity, lease, _ => Assert.Fail("Cancelled charge."), cancellation.Token));
        using var duringPass = new CancellationTokenSource();
        int charges = 0;
        Assert.Throws<OperationCanceledException>(() => session.GetContentDigest(
            identity, lease, _ =>
            {
                charges++;
                duringPass.Cancel();
            }, duringPass.Token));

        AccessedDigest(session.GetContentDigest(
            identity, lease, _ => Assert.Fail("Completed work was charged twice."), TestContext.Current.CancellationToken));
        Assert.Equal(1, charges);
    }

    [Fact]
    public async Task Digest_ConcurrentRequestsShareOneColdPass()
    {
        await using var session = new ArtifactSetSession();
        ArtifactIdentity identity = await PublishDigestFixture(session, new byte[1024 * 1024]);
        using ArtifactQueryLease lease =
            session.IssueLease(session.CreateQueryAuthorization());
        int charges = 0;
        Task<ArtifactContentDigest>[] requests = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => AccessedDigest(
                session.GetContentDigest(identity, lease, _ => Interlocked.Increment(ref charges), TestContext.Current.CancellationToken)),
                TestContext.Current.CancellationToken))
            .ToArray();

        ArtifactContentDigest[] digests = await Task.WhenAll(requests);

        Assert.Equal(1, charges);
        Assert.All(digests, digest => Assert.Same(digests[0], digest));
    }

    [Fact]
    public async Task Digest_ActiveComputationPinsReleaseAndValueCanEscape()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var acquisitionLease = new TrackingLease();
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(scope, new Provenance("digest"), [1], acquisitionLease),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        using ArtifactQueryLease lease =
            session.IssueLease(session.CreateQueryAuthorization());
        ArtifactIdentity identity = Assert.Single(session.GetCatalog(lease)).Identity;
        Task<ArtifactContentDigest> request = Task.Run(() => AccessedDigest(
            session.GetContentDigest(identity, lease, _ =>
            {
                entered.TrySetResult();
                release.Wait(cancellationToken);
            }, cancellationToken)), cancellationToken);
        Task disposal;
        try
        {
            await entered.Task.WaitAsync(cancellationToken);
            disposal = session.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            Assert.Equal(0, acquisitionLease.DisposeCount);
            Assert.IsType<ArtifactContentAccessOutcome<ArtifactContentDigest>.Unauthorized>(
                session.GetContentDigest(identity, lease, _ => Assert.Fail("Ended charge."), cancellationToken));
        }
        finally
        {
            release.Set();
        }

        ArtifactContentDigest digest = await request;
        await disposal;
        Assert.Equal(1, acquisitionLease.DisposeCount);
        Assert.Same(identity, digest.Artifact);
        Assert.Equal("4bf5122f344554c53bde2ebb8cd2b7e3d1600ad631c385a5d7cce23c7785459a", digest.HexValue);
    }

    private static ArtifactContentDigest AccessedDigest(
        ArtifactContentAccessOutcome<ArtifactContentDigest> outcome) =>
        Assert.IsType<ArtifactContentAccessOutcome<ArtifactContentDigest>.Accessed>(outcome).Value;

    private static async Task<ArtifactIdentity> PublishDigestFixture(
        ArtifactSetSession session,
        byte[] content)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope, new Provenance("digest"), content, ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        using ArtifactQueryLease lease =
            session.IssueLease(session.CreateQueryAuthorization());
        return Assert.Single(session.GetCatalog(lease)).Identity;
    }
}
