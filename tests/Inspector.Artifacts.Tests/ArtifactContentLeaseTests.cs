using Inspector.Artifacts.Workspaces;

namespace Inspector.Artifacts.Tests;

public sealed partial class ArtifactSetSessionTests
{
    [Fact]
    public async Task ArtifactContentLease_SurvivesQueryReplacementAndPinsRetirement()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var acquisition = new TrackingLease();
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("content"),
                [1, 2, 3],
                acquisition),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));

        ArtifactQueryAuthorization authorization =
            session.CreateQueryAuthorization();
        using ArtifactQueryLease query =
            session.IssueLease(authorization);
        ArtifactContentReference reference =
            session.GetContentReference(
                Assert.Single(session.GetCatalog(query)).Identity,
                query);
        using ArtifactContentLease content =
            session.IssueContentLease(reference, query);

        ArtifactQueryAuthorization replacement =
            session.ReplaceQueryAuthorization(authorization);
        query.Dispose();
        using ArtifactQueryLease current =
            session.IssueLease(replacement);
        Task disposal = session.DisposeAsync().AsTask();

        Assert.False(disposal.IsCompleted);
        Assert.Equal(0, acquisition.DisposeCount);
        Assert.Throws<ObjectDisposedException>(
            () => session.IssueContentLease(reference, current));
        var accessed =
            Assert.IsType<
                ArtifactContentAccessOutcome<int>.Accessed>(
                    content.WithContent(
                        (view, _) =>
                        {
                            Assert.Same(reference, content.Reference);
                            Assert.Same(reference, view.Reference);
                            Assert.Same(
                                reference.Descriptor.Identity,
                                view.Artifact);
                            Assert.Same(
                                session.Generation,
                                view.Generation);
                            Assert.True(
                                view.Content.SequenceEqual<byte>(
                                    [1, 2, 3]));
                            return view.Content.Length;
                        },
                        cancellationToken));
        Assert.Equal(3, accessed.Value);
        ArtifactContentDigest digest =
            Assert.IsType<
                ArtifactContentAccessOutcome<
                    ArtifactContentDigest>.Accessed>(
                        content.GetContentDigest(
                            _ => { },
                            cancellationToken)).Value;
        Assert.Same(reference.Artifact, digest.Artifact);

        content.Dispose();
        await disposal.WaitAsync(cancellationToken);
        Assert.Equal(1, acquisition.DisposeCount);
        Assert.Throws<ObjectDisposedException>(
            () => content.WithContent(
                static (_, _) => 0,
                cancellationToken));
        Assert.Throws<ObjectDisposedException>(
            () => content.GetContentDigest(
                _ => { },
                cancellationToken));
    }

    [Fact]
    public async Task ArtifactContentLease_IssuanceRequiresCurrentQueryAndExactReference()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("local"),
                [1],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        ArtifactQueryAuthorization authorization =
            session.CreateQueryAuthorization();
        using ArtifactQueryLease query =
            session.IssueLease(authorization);
        ArtifactContentReference reference =
            session.GetContentReference(
                Assert.Single(session.GetCatalog(query)).Identity,
                query);

        await using var foreignSession = new ArtifactSetSession();
        await foreignSession.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("foreign"),
                [2],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await foreignSession.SealAsync(cancellationToken));
        ArtifactQueryAuthorization foreignAuthorization =
            foreignSession.CreateQueryAuthorization();
        using ArtifactQueryLease foreignQuery =
            foreignSession.IssueLease(foreignAuthorization);
        ArtifactContentReference foreignReference =
            foreignSession.GetContentReference(
                Assert.Single(
                    foreignSession.GetCatalog(foreignQuery)).Identity,
                foreignQuery);

        Assert.Throws<ArgumentException>(
            () => session.IssueContentLease(
                foreignReference,
                query));
        Assert.Throws<UnauthorizedAccessException>(
            () => session.IssueContentLease(
                reference,
                foreignQuery));

        ArtifactQueryAuthorization replacement =
            session.ReplaceQueryAuthorization(authorization);
        Assert.Throws<UnauthorizedAccessException>(
            () => session.IssueContentLease(reference, query));
        using ArtifactQueryLease current =
            session.IssueLease(replacement);
        using ArtifactContentLease content =
            session.IssueContentLease(reference, current);
        Assert.Same(reference, content.Reference);
        Assert.Same(reference.Descriptor.Identity, content.Artifact);
        current.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => session.IssueContentLease(reference, current));
    }

    [Fact]
    public async Task ArtifactContentLease_ActiveBorrowPreventsReleaseAndPinsRetirement()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var acquisition = new TrackingLease();
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("content"),
                [4, 5],
                acquisition),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        using ArtifactQueryLease query =
            session.IssueLease(
                session.CreateQueryAuthorization());
        ArtifactContentReference reference =
            session.GetContentReference(
                Assert.Single(session.GetCatalog(query)).Identity,
                query);
        ArtifactContentLease content =
            session.IssueContentLease(reference, query);

        Task? disposal = null;
        var accessed =
            Assert.IsType<
                ArtifactContentAccessOutcome<int>.Accessed>(
                    content.WithContent(
                        (view, _) =>
                        {
                            disposal =
                                session.DisposeAsync().AsTask();
                            Assert.False(disposal.IsCompleted);
                            Assert.Equal(
                                0,
                                acquisition.DisposeCount);
                            Assert.Throws<InvalidOperationException>(
                                content.Dispose);
                            Assert.True(
                                view.Content.SequenceEqual<byte>(
                                    [4, 5]));
                            return view.Content.Length;
                        },
                        cancellationToken));
        Assert.Equal(2, accessed.Value);
        Assert.NotNull(disposal);
        Assert.False(disposal.IsCompleted);

        content.Dispose();
        await disposal.WaitAsync(cancellationToken);
        Assert.Equal(1, acquisition.DisposeCount);
    }

    [Fact]
    public async Task ArtifactContentLease_NormalReturnWinsLaterCancellation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("content"),
                [7],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        using ArtifactQueryLease query =
            session.IssueLease(
                session.CreateQueryAuthorization());
        ArtifactContentReference reference =
            session.GetContentReference(
                Assert.Single(session.GetCatalog(query)).Identity,
                query);
        using ArtifactContentLease content =
            session.IssueContentLease(reference, query);
        using var cancellation = new CancellationTokenSource();
        var marker = new object();
        var failure =
            new InvalidOperationException("Callback failed.");
        int calls = 0;

        Assert.Same(
            failure,
            Assert.Throws<InvalidOperationException>(
                () => content.WithContent<object>(
                    (_, _) => throw failure,
                    cancellationToken)));
        var accessed =
            Assert.IsType<
                ArtifactContentAccessOutcome<object>.Accessed>(
                    content.WithContent(
                        (view, token) =>
                        {
                            Assert.Equal(
                                cancellation.Token,
                                token);
                            Assert.Equal(7, view.Content[0]);
                            calls++;
                            cancellation.Cancel();
                            return marker;
                        },
                        cancellation.Token));

        Assert.Same(marker, accessed.Value);
        Assert.Equal(1, calls);
        Assert.Throws<OperationCanceledException>(
            () => content.WithContent(
                (_, _) =>
                {
                    calls++;
                    return marker;
                },
                cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ArtifactContentLease_StatefulBorrowSupportsRefLikeState()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) => Acquired(
                scope,
                new Provenance("stateful"),
                [8, 9],
                ArtifactAcquisitionLeases.None),
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        using ArtifactQueryLease query =
            session.IssueLease(
                session.CreateQueryAuthorization());
        ArtifactContentReference reference =
            session.GetContentReference(
                Assert.Single(session.GetCatalog(query)).Identity,
                query);
        using ArtifactContentLease content =
            session.IssueContentLease(reference, query);
        using var cancellation = new CancellationTokenSource();
        ReadOnlySpan<byte> expected = stackalloc byte[] { 8, 9 };
        var state = new StatefulBorrowState(
            expected,
            content,
            cancellation);

        var accessed =
            Assert.IsType<
                ArtifactContentAccessOutcome<int>.Accessed>(
                    content.WithContent(
                        state,
                        static (view, state, token) =>
                        {
                            Assert.Equal(
                                state.Cancellation.Token,
                                token);
                            Assert.Same(
                                state.Lease.Reference,
                                view.Reference);
                            Assert.True(
                                view.Content.SequenceEqual(
                                    state.Expected));
                            Assert.Throws<InvalidOperationException>(
                                state.Lease.Dispose);
                            state.Cancellation.Cancel();
                            return view.Content.Length;
                        },
                        cancellation.Token));

        Assert.Equal(2, accessed.Value);
    }

    [Fact]
    public async Task ArtifactContentLease_StatefulBorrowComposesNestedExactBorrows()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using var session = new ArtifactSetSession();
        ArtifactIdentity? firstIdentity = null;
        ArtifactIdentity? secondIdentity = null;
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution contribution = scope.Register(
                    new Provenance("first"),
                    _ => new MemoryStream([1], writable: false));
                firstIdentity = contribution.Descriptor.Identity;
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [contribution],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: cancellationToken);
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution contribution = scope.Register(
                    new Provenance("second"),
                    _ => new MemoryStream([2], writable: false));
                secondIdentity = contribution.Descriptor.Identity;
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [contribution],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(cancellationToken));
        using ArtifactQueryLease query =
            session.IssueLease(
                session.CreateQueryAuthorization());
        ArtifactContentReference firstReference =
            session.GetContentReference(
                firstIdentity
                ?? throw new InvalidOperationException(
                    "The first Artifact identity was not captured."),
                query);
        ArtifactContentReference secondReference =
            session.GetContentReference(
                secondIdentity
                ?? throw new InvalidOperationException(
                    "The second Artifact identity was not captured."),
                query);
        using ArtifactContentLease first =
            session.IssueContentLease(firstReference, query);
        using ArtifactContentLease second =
            session.IssueContentLease(secondReference, query);
        var failure =
            new InvalidOperationException("Nested callback failed.");

        Assert.Same(
            failure,
            Assert.Throws<InvalidOperationException>(
                () => first.WithContent(
                    new NestedBorrowRequest(
                        first,
                        second,
                        failure),
                    BorrowNested,
                    cancellationToken)));
        var accessed =
            Assert.IsType<
                ArtifactContentAccessOutcome<int>.Accessed>(
                    first.WithContent(
                        new NestedBorrowRequest(
                            first,
                            second,
                            Failure: null),
                        BorrowNested,
                        cancellationToken));

        Assert.Equal(3, accessed.Value);
    }

    private static int BorrowNested(
        scoped ArtifactContentView firstView,
        NestedBorrowRequest request,
        CancellationToken cancellationToken)
    {
        var state = new NestedBorrowState(
            firstView,
            request);
        return Assert.IsType<
            ArtifactContentAccessOutcome<int>.Accessed>(
                request.Second.WithContent(
                    state,
                    BorrowNestedPair,
                    cancellationToken)).Value;
    }

    private static int BorrowNestedPair(
        scoped ArtifactContentView secondView,
        scoped NestedBorrowState state,
        CancellationToken _)
    {
        Assert.Same(
            state.Request.First.Reference,
            state.FirstView.Reference);
        Assert.Same(
            state.Request.Second.Reference,
            secondView.Reference);
        Assert.Equal(1, state.FirstView.Content[0]);
        Assert.Equal(2, secondView.Content[0]);
        Assert.Throws<InvalidOperationException>(
            state.Request.First.Dispose);
        Assert.Throws<InvalidOperationException>(
            state.Request.Second.Dispose);
        if (state.Request.Failure is { } failure)
            throw failure;

        return state.FirstView.Content[0]
            + secondView.Content[0];
    }

    private readonly ref struct StatefulBorrowState(
        ReadOnlySpan<byte> expected,
        ArtifactContentLease lease,
        CancellationTokenSource cancellation)
    {
        public ReadOnlySpan<byte> Expected { get; } = expected;
        public ArtifactContentLease Lease { get; } = lease;
        public CancellationTokenSource Cancellation { get; } =
            cancellation;
    }

    private readonly record struct NestedBorrowRequest(
        ArtifactContentLease First,
        ArtifactContentLease Second,
        Exception? Failure);

    private readonly ref struct NestedBorrowState(
        ArtifactContentView firstView,
        NestedBorrowRequest request)
    {
        public ArtifactContentView FirstView { get; } =
            firstView;
        public NestedBorrowRequest Request { get; } =
            request;
    }
}
