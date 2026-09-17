using System.Reflection;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using Inspector.Resources;
using NuGetFetch;

namespace DotnetInspector.Libraries.Tests;

public sealed partial class LibraryReferenceTests
{
    [Fact]
    public async Task
        ContentOwner_SnapshotsExactContentAfterQueryPolicyReplacement()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(4);
        LibraryReference library = PlatformLibrary(
            artifacts,
            "System.Text.Json",
            fourPartVersion: new Version(11, 0, 0, 0));
        ArtifactContentLease[] children =
            IssueChildren(artifacts, library.Contents.Count);
        await using var owner =
            new LibraryContentOwner(library, children);
        artifacts.ReplaceQueryPolicy();
        using LibraryOperationLease operation =
            Issued(owner, library);

        int api = operation.Snapshot(
            library.ApiAssembly,
            static (view, _) =>
            {
                Assert.True(
                    view.Reference.HasRole(
                        LibraryContentRole.ApiAssembly));
                return view.Content[0];
            },
            cancellationToken);
        LibraryContentReference pdb =
            Assert.Single(
                library.Contents,
                content => content.HasRole(
                    LibraryContentRole.PortablePdb));
        int pair = operation.SnapshotPair(
            library.ImplementationAssembly!,
            pdb,
            static (view, _) =>
            {
                Assert.True(
                    view.First.Reference.HasRole(
                        LibraryContentRole.ImplementationAssembly));
                Assert.True(
                    view.Second.Reference.HasRole(
                        LibraryContentRole.PortablePdb));
                Assert.Same(
                    view.First.Reference,
                    view.Second.Reference.AssociatedAssembly);
                return view.First.Content[0]
                    + view.Second.Content[0];
            },
            cancellationToken);

        Assert.Equal(0, api);
        Assert.Equal(4, pair);
        LibraryContentReference xml =
            Assert.Single(
                library.Contents,
                content => content.HasRole(
                    LibraryContentRole.CompiledXmlDocumentation));
        int xmlPair = operation.SnapshotPair(
            library.ApiAssembly,
            xml,
            static (view, _) =>
            {
                Assert.True(
                    view.First.Reference.HasRole(
                        LibraryContentRole.ApiAssembly));
                Assert.True(
                    view.Second.Reference.HasRole(
                        LibraryContentRole.CompiledXmlDocumentation));
                Assert.Same(
                    view.First.Reference,
                    view.Second.Reference.AssociatedAssembly);
                return view.First.Content[0]
                    + view.Second.Content[0];
            },
            cancellationToken);
        Assert.Equal(2, xmlPair);
        Assert.Equal(
            1,
            SnapshotWithRefLikeState(
                operation,
                library.ApiAssembly,
                cancellationToken));
        Assert.Equal(LibraryContentOwnerState.Active, owner.State);
    }

    [Fact]
    public async Task
        ContentOwner_ConstructionRejectsInvalidChildrenWithoutConsumingThem()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(4);
        LibraryReference library = PlatformLibrary(
            artifacts,
            "System.Text.Json",
            fourPartVersion: new Version(11, 0, 0, 0));
        ArtifactContentLease first = artifacts.IssueContentLease(0);
        ArtifactContentLease second = artifacts.IssueContentLease(1);
        ArtifactContentLease third = artifacts.IssueContentLease(2);
        ArtifactContentLease fourth = artifacts.IssueContentLease(3);
        try
        {
            Assert.Throws<ArgumentException>(
                () => new LibraryContentOwner(
                    library,
                    [first, second, third]));
            Assert.Throws<ArgumentException>(
                () => new LibraryContentOwner(
                    library,
                    [first, second, third, third]));
            Assert.Throws<ArgumentException>(
                () => new LibraryContentOwner(
                    library,
                    [second, first, third, fourth]));

            Assert.Equal(0, ReadByte(first));
            Assert.Equal(1, ReadByte(second));
            Assert.Equal(2, ReadByte(third));
            Assert.Equal(3, ReadByte(fourth));
        }
        finally
        {
            fourth.Dispose();
            third.Dispose();
            second.Dispose();
            first.Dispose();
        }
    }

    [Fact]
    public async Task
        ContentOwner_ConstructionRejectsReleasedAndForeignChildren()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(2);
        await using ArtifactFixture foreign =
            await ArtifactFixture.CreateAsync(1);
        ManagedMetadataIdentity.Assembly identity =
            Identity(
                "Microsoft.Azure.SignalR",
                new Version(1, 33, 1, 0));
        LibraryReference library = LibraryReference.CreateDirect(
            new LibraryAssemblyCorrespondence(
                artifacts[0],
                identity,
                artifacts[1],
                identity));
        ArtifactContentLease first = artifacts.IssueContentLease(0);
        ArtifactContentLease second = artifacts.IssueContentLease(1);
        ArtifactContentLease foreignChild =
            foreign.IssueContentLease(0);
        try
        {
            Assert.Throws<ArgumentException>(
                () => new LibraryContentOwner(
                    library,
                    [first, foreignChild]));
            Assert.Equal(0, ReadByte(first));
            Assert.Equal(1, ReadByte(second));

            second.Dispose();
            Assert.Throws<ObjectDisposedException>(
                () => new LibraryContentOwner(
                    library,
                    [first, second]));
            Assert.Equal(0, ReadByte(first));
        }
        finally
        {
            foreignChild.Dispose();
            second.Dispose();
            first.Dispose();
        }
    }

    [Fact]
    public async Task
        ContentOwner_RetirementDrainsIssuedOperationsWithoutBlocking()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(1);
        LibraryReference library = DirectLibrary(
            artifacts[0],
            "Contoso.Direct");
        var owner = new LibraryContentOwner(
            library,
            [artifacts.IssueContentLease(0)]);
        LibraryOperationLease operation =
            Issued(owner, library);
        Task artifactRetirement =
            artifacts.BeginSessionRetirement();
        Task firstRetirement = owner.DisposeAsync().AsTask();
        Task secondRetirement = owner.DisposeAsync().AsTask();

        Assert.Same(firstRetirement, secondRetirement);
        Assert.Equal(
            LibraryContentOwnerState.Retiring,
            owner.State);
        Assert.False(firstRetirement.IsCompleted);
        Assert.False(artifactRetirement.IsCompleted);
        Assert.IsType<
            LibraryOperationLeaseIssueOutcome.OwnerRetiring>(
                owner.IssueOperationLease(library));
        Assert.Equal(
            0,
            operation.Snapshot(
                library.ApiAssembly,
                static (view, _) => view.Content[0],
                cancellationToken));

        operation.Dispose();
        await firstRetirement.WaitAsync(
            TestContext.Current.CancellationToken);
        await artifactRetirement.WaitAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            LibraryContentOwnerState.Released,
            owner.State);
        Assert.Empty(owner.CleanupFailures);
        var released =
            Assert.IsType<
                LibraryOperationLeaseIssueOutcome.OwnerReleased>(
                    owner.IssueOperationLease(library));
        Assert.Equal(
            LibraryContentOwnerState.Released,
            released.State);
        Assert.Throws<ObjectDisposedException>(
            () => operation.Snapshot(
                library.ApiAssembly,
                static (_, _) => 0,
                cancellationToken));
    }

    [Fact]
    public async Task
        ContentOwner_RejectsForeignLibraryAndContentReferences()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using ArtifactFixture packageArtifacts =
            await ArtifactFixture.CreateAsync(1);
        await using ArtifactFixture platformArtifacts =
            await ArtifactFixture.CreateAsync(1);
        ManagedMetadataIdentity.Assembly identity =
            Identity(
                "System.Text.Json",
                new Version(11, 0, 0, 0));
        LibraryReference package = LibraryReference.CreateFromSource(
            new ExactLibrarySourceCoordinate.Package(
                PackageSourceCoordinate.Create(
                    "System.Text.Json",
                    "11.0.0"),
                identity),
            new(packageArtifacts[0], identity));
        LibraryReference platform = LibraryReference.CreateFromSource(
            new ExactLibrarySourceCoordinate.Platform(
                new(PlatformFamily.DotNetRuntime),
                identity),
            new(platformArtifacts[0], identity));
        await using var owner = new LibraryContentOwner(
            package,
            [packageArtifacts.IssueContentLease(0)]);

        var mismatch =
            Assert.IsType<
                LibraryOperationLeaseIssueOutcome.ReferenceMismatch>(
                    owner.IssueOperationLease(platform));
        Assert.Same(package, mismatch.OwnerReference);
        Assert.Same(platform, mismatch.RequestedReference);
        using LibraryOperationLease operation =
            Issued(owner, package);
        Assert.Throws<ArgumentException>(
            () => operation.Snapshot(
                platform.ApiAssembly,
                static (_, _) => 0,
                cancellationToken));
    }

    [Fact]
    public async Task
        ContentOwner_PreservesMultiLibraryPackageAssociations()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(2);
        PackageSourceCoordinate package =
            PackageSourceCoordinate.Create(
                "Microsoft.Azure.SignalR",
                "1.33.1");
        ManagedMetadataIdentity.Assembly primaryIdentity =
            Identity(
                "Microsoft.Azure.SignalR",
                new Version(1, 0, 0, 0));
        ManagedMetadataIdentity.Assembly commonIdentity =
            Identity(
                "Microsoft.Azure.SignalR.Common",
                new Version(1, 0, 0, 0));
        LibraryReference primary = LibraryReference.CreateFromSource(
            new ExactLibrarySourceCoordinate.Package(
                package,
                primaryIdentity),
            new(artifacts[0], primaryIdentity));
        LibraryReference common = LibraryReference.CreateFromSource(
            new ExactLibrarySourceCoordinate.Package(
                package,
                commonIdentity),
            new(artifacts[1], commonIdentity));
        await using var primaryOwner = new LibraryContentOwner(
            primary,
            [artifacts.IssueContentLease(0)]);
        await using var commonOwner = new LibraryContentOwner(
            common,
            [artifacts.IssueContentLease(1)]);

        Assert.IsType<
            LibraryOperationLeaseIssueOutcome.ReferenceMismatch>(
                primaryOwner.IssueOperationLease(common));
        using LibraryOperationLease primaryOperation =
            Issued(primaryOwner, primary);
        using LibraryOperationLease commonOperation =
            Issued(commonOwner, common);
        Assert.Throws<ArgumentException>(
            () => primaryOperation.Snapshot(
                common.ApiAssembly,
                static (_, _) => 0,
                cancellationToken));
        Assert.Equal(
            0,
            primaryOperation.Snapshot(
                primary.ApiAssembly,
                static (view, _) => view.Content[0],
                cancellationToken));
        Assert.Equal(
            1,
            commonOperation.Snapshot(
                common.ApiAssembly,
                static (view, _) => view.Content[0],
                cancellationToken));
    }

    [Fact]
    public async Task
        ContentOwner_PairValidationAndCallbackFailureUnwindBorrows()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(2);
        ManagedMetadataIdentity.Assembly identity =
            Identity(
                "Microsoft.Azure.SignalR",
                new Version(1, 33, 1, 0));
        LibraryReference library = LibraryReference.CreateDirect(
            new LibraryAssemblyCorrespondence(
                artifacts[0],
                identity,
                artifacts[1],
                identity));
        ArtifactContentLease[] children =
            IssueChildren(artifacts, library.Contents.Count);
        await using var owner =
            new LibraryContentOwner(library, children);
        LibraryOperationLease operation =
            Issued(owner, library);
        int callbacks = 0;
        Task? retirement = null;

        Assert.Throws<ArgumentException>(
            () => operation.SnapshotPair(
                library.ApiAssembly,
                library.ApiAssembly,
                (_, _) =>
                {
                    callbacks++;
                    return 0;
                },
                cancellationToken));
        Assert.Equal(0, callbacks);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => operation.Snapshot(
                library.ApiAssembly,
                (_, _) =>
                {
                    callbacks++;
                    return 0;
                },
                cancelled.Token));
        Assert.Equal(0, callbacks);

        var singleFailure =
            new InvalidOperationException("Single callback failed.");
        Assert.Same(
            singleFailure,
            Assert.Throws<InvalidOperationException>(
                () => operation.Snapshot<int>(
                    library.ApiAssembly,
                    (_, _) => throw singleFailure,
                    cancellationToken)));
        Assert.Equal(
            0,
            operation.Snapshot(
                library.ApiAssembly,
                static (view, _) => view.Content[0],
                cancellationToken));

        var failure =
            new InvalidOperationException("Pair callback failed.");
        Assert.Same(
            failure,
            Assert.Throws<InvalidOperationException>(
                () => operation.SnapshotPair<int>(
                    library.ApiAssembly,
                    library.ImplementationAssembly!,
                    (view, _) =>
                    {
                        retirement = owner.DisposeAsync().AsTask();
                        Assert.False(retirement.IsCompleted);
                        Assert.Throws<InvalidOperationException>(
                            operation.Dispose);
                        Assert.Throws<InvalidOperationException>(
                            children[0].Dispose);
                        Assert.Throws<InvalidOperationException>(
                            children[1].Dispose);
                        Assert.Equal(0, view.First.Content[0]);
                        Assert.Equal(1, view.Second.Content[0]);
                        throw failure;
                    },
                    cancellationToken)));

        Assert.NotNull(retirement);
        Assert.False(retirement.IsCompleted);
        Assert.Equal(
            1,
            operation.Snapshot(
                library.ImplementationAssembly!,
                static (view, _) => view.Content[0],
                cancellationToken));
        operation.Dispose();
        await retirement.WaitAsync(cancellationToken);
        Assert.Equal(
            LibraryContentOwnerState.Released,
            owner.State);
    }

    [Fact]
    public async Task
        ContentOwner_IssuanceAndRetirementRaceHasOneWinner()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(1);
        LibraryReference library = DirectLibrary(
            artifacts[0],
            "Contoso.Race");

        for (int iteration = 0; iteration < 64; iteration++)
        {
            var owner = new LibraryContentOwner(
                library,
                [artifacts.IssueContentLease(0)]);
            var start =
                new TaskCompletionSource(
                    TaskCreationOptions
                        .RunContinuationsAsynchronously);
            Task<LibraryOperationLeaseIssueOutcome> issuance =
                Task.Run(
                    async () =>
                    {
                        await start.Task.WaitAsync(cancellationToken);
                        return owner.IssueOperationLease(library);
                    },
                    cancellationToken);
            Task retirement =
                Task.Run(
                    async () =>
                    {
                        await start.Task.WaitAsync(cancellationToken);
                        await owner.DisposeAsync();
                    },
                    cancellationToken);

            start.SetResult();
            LibraryOperationLeaseIssueOutcome outcome =
                await issuance.WaitAsync(cancellationToken);
            switch (outcome)
            {
                case LibraryOperationLeaseIssueOutcome.Issued issued:
                    Assert.False(retirement.IsCompleted);
                    issued.Lease.Dispose();
                    break;
                case LibraryOperationLeaseIssueOutcome.OwnerRetiring:
                case LibraryOperationLeaseIssueOutcome.OwnerReleased:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected issuance outcome {outcome.GetType().Name}.");
            }

            await retirement.WaitAsync(cancellationToken);
            Assert.Equal(
                LibraryContentOwnerState.Released,
                owner.State);
        }
    }

    [Fact]
    public async Task
        ContentOwner_ReleaseFailurePreservesEveryChildFailure()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(2);
        ManagedMetadataIdentity.Assembly identity =
            Identity(
                "Contoso.Cleanup",
                new Version(1, 0, 0, 0));
        LibraryReference library = LibraryReference.CreateDirect(
            new LibraryAssemblyCorrespondence(
                artifacts[0],
                identity,
                artifacts[1],
                identity));
        ArtifactContentLease[] children =
            IssueChildren(artifacts, library.Contents.Count);
        var owner = new LibraryContentOwner(library, children);
        Task? retirement = null;

        _ = Assert.IsType<
            ArtifactContentAccessOutcome<int>.Accessed>(
                children[0].WithContent(
                    (_, _) =>
                        Assert.IsType<
                            ArtifactContentAccessOutcome<int>.Accessed>(
                                children[1].WithContent(
                                    (_, _) =>
                                    {
                                        retirement =
                                            owner.DisposeAsync().AsTask();
                                        return 0;
                                    },
                                    cancellationToken))
                            .Value,
                    cancellationToken));

        Assert.NotNull(retirement);
        AggregateException failure =
            await Assert.ThrowsAsync<AggregateException>(
                async () => await retirement.WaitAsync(
                    cancellationToken));
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Equal(
            failure.InnerExceptions,
            owner.CleanupFailures);
        Assert.Collection(
            owner.ReleaseFailures,
            second =>
            {
                Assert.Same(library.Contents[1], second.Content);
                Assert.Same(
                    failure.InnerExceptions[0],
                    second.Failure);
            },
            first =>
            {
                Assert.Same(library.Contents[0], first.Content);
                Assert.Same(
                    failure.InnerExceptions[1],
                    first.Failure);
            });
        Assert.Equal(
            LibraryContentOwnerState.ReleaseFailed,
            owner.State);
        var released =
            Assert.IsType<
                LibraryOperationLeaseIssueOutcome.OwnerReleased>(
                    owner.IssueOperationLease(library));
        Assert.Equal(
            LibraryContentOwnerState.ReleaseFailed,
            released.State);
        Assert.Same(retirement, owner.DisposeAsync().AsTask());

        children[1].Dispose();
        children[0].Dispose();
    }

    [Fact]
    public void ContentOwnerContracts_DeclareOnlyLiveResourceTypes()
    {
        Assert.NotNull(
            typeof(LibraryContentOwner)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(LibraryOperationLease)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.True(typeof(LibraryContentView).IsByRefLike);
        Assert.True(typeof(LibraryContentPairView).IsByRefLike);
        Assert.False(
            typeof(IDisposable).IsAssignableFrom(
                typeof(LibraryContentReference)));
        Assert.False(
            typeof(IAsyncDisposable).IsAssignableFrom(
                typeof(LibraryContentReference)));
    }

    private static LibraryReference PlatformLibrary(
        ArtifactFixture artifacts,
        string name,
        Version fourPartVersion)
    {
        ManagedMetadataIdentity.Assembly identity =
            Identity(name, fourPartVersion);
        return LibraryReference.CreateFromSource(
            new ExactLibrarySourceCoordinate.Platform(
                new(PlatformFamily.DotNetRuntime),
                identity),
            new LibraryAssemblyCorrespondence(
                artifacts[0],
                identity,
                artifacts[1],
                identity),
            [
                new(
                    artifacts[2],
                    LibraryContentRole.CompiledXmlDocumentation,
                    artifacts[0]),
                new(
                    artifacts[3],
                    LibraryContentRole.PortablePdb,
                    artifacts[1]),
            ]);
    }

    private static LibraryReference DirectLibrary(
        ArtifactContentReference artifact,
        string name)
    {
        ManagedMetadataIdentity.Assembly identity =
            Identity(name, new Version(1, 0, 0, 0));
        return LibraryReference.CreateDirect(
            new LibraryAssemblyCorrespondence(
                artifact,
                identity,
                artifact,
                identity));
    }

    private static ArtifactContentLease[] IssueChildren(
        ArtifactFixture artifacts,
        int count) =>
        Enumerable.Range(0, count)
            .Select(artifacts.IssueContentLease)
            .ToArray();

    private static LibraryOperationLease Issued(
        LibraryContentOwner owner,
        LibraryReference reference) =>
        Assert.IsType<
            LibraryOperationLeaseIssueOutcome.Issued>(
                owner.IssueOperationLease(reference))
            .Lease;

    private static int SnapshotWithRefLikeState(
        LibraryOperationLease operation,
        LibraryContentReference content,
        CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> expected = stackalloc byte[] { 0 };
        return operation.Snapshot(
            content,
            expected,
            static (view, expected, _) =>
            {
                Assert.True(view.Content.SequenceEqual(expected));
                return view.Content.Length;
            },
            cancellationToken);
    }

    private static int ReadByte(ArtifactContentLease lease) =>
        Assert.IsType<
            ArtifactContentAccessOutcome<int>.Accessed>(
                lease.WithContent(
                    static (view, _) => (int)view.Content[0],
                    TestContext.Current.CancellationToken))
            .Value;
}
