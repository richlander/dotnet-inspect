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

        content.Dispose();
        await disposal.WaitAsync(cancellationToken);
        Assert.Equal(1, acquisition.DisposeCount);
        Assert.Throws<ObjectDisposedException>(
            () => content.WithContent(
                static (_, _) => 0,
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
}
