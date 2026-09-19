using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    [Fact]
    public async Task PlatformAcquisition_ForwardsTransferPolicyForDeclaredAndRealizedCoordinates()
    {
        byte[] nupkg = RuntimePack();
        var declaredPolicy = new RecordingTransferPolicy();
        await using var declaredWorkspace = new InspectionWorkspace();
        using var declaredClient = new HttpClient(
            new PayloadHandler(
                nupkg,
                RuntimePackVersion,
                RuntimePackPackageId));

        var declared = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                declaredWorkspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            version: RuntimePackVersion),
                    ],
                },
                Options(
                    declaredClient,
                    new InMemoryPackageStore(),
                    packageTransferPolicy: declaredPolicy),
                TestContext.Current.CancellationToken));

        AssertTransferPolicy(
            declaredPolicy,
            RuntimePackPackageId,
            RuntimePackVersion);
        RealizedMemberCoordinate.Platform realized =
            Assert.IsType<RealizedMemberCoordinate.Platform>(
                declared.Members[0].Realized);

        var realizedPolicy = new RecordingTransferPolicy();
        await using var realizedWorkspace = new InspectionWorkspace();
        using var realizedClient = new HttpClient(
            new PayloadHandler(
                nupkg,
                RuntimePackVersion,
                RuntimePackPackageId));

        _ = Loaded(
            await WorkspaceContextLoader.LoadRealizedAsync(
                realizedWorkspace,
                [realized],
                Options(
                    realizedClient,
                    new InMemoryPackageStore(),
                    packageTransferPolicy: realizedPolicy),
                TestContext.Current.CancellationToken));

        AssertTransferPolicy(
            realizedPolicy,
            RuntimePackPackageId,
            RuntimePackVersion);
    }

    [Fact]
    public async Task PerPackageAuthorization_KeepsEachPackageOnItsOwnProducer()
    {
        var handler = new PerFeedHandler();
        handler.Serve(FeedA, "alpha.package", "1.0.0", CallerPackage());
        handler.Serve(FeedB, "bravo.package", "1.0.0", TargetPackage());
        using var client = new HttpClient(handler);
        await using var workspace = new InspectionWorkspace();

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Package(
                            "alpha.package",
                            "1.0.0"),
                        WorkspaceMemberCoordinate.Package(
                            "bravo.package",
                            "1.0.0"),
                    ],
                },
                Options(
                    client,
                    new InMemoryPackageStore(),
                    sourceAuthorization: new PerPackageAuthorization
                    {
                        ["alpha.package"] = [FeedA],
                        ["bravo.package"] = [FeedB],
                    }),
                TestContext.Current.CancellationToken));

        // Each package was realized from the one producer its own id
        // authorizes, and the realized coordinate names that producer.
        Assert.Equal(
            Producer(FeedA),
            Assert.IsType<RealizedMemberCoordinate.Package>(
                loaded.Members[0].Realized).Producer);
        Assert.Equal(
            Producer(FeedB),
            Assert.IsType<RealizedMemberCoordinate.Package>(
                loaded.Members[1].Realized).Producer);

        // A union of both members' sources would have let either feed answer
        // for either package. No request crosses.
        Assert.NotEmpty(handler.Requests);
        Assert.All(
            handler.Requests.Where(url =>
                url.Contains("a.test", StringComparison.Ordinal)),
            url => Assert.DoesNotContain(
                "bravo.package",
                url,
                StringComparison.Ordinal));
        Assert.All(
            handler.Requests.Where(url =>
                url.Contains("b.test", StringComparison.Ordinal)),
            url => Assert.DoesNotContain(
                "alpha.package",
                url,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task PerPackageAuthorization_RefusesAProducerAuthorizedForAnotherPackage()
    {
        byte[] nupkg = CallerPackage();
        var handler = new PerFeedHandler();

        // Only the feed this package is *not* authorized for can serve it, and
        // that feed's cache slot is already warm. A union of the context's
        // sources would succeed here; per-package authorization must not.
        handler.Serve(FeedB, "alpha.package", "1.0.0", nupkg);
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            "alpha.package",
            "1.0.0",
            Producer(FeedB),
            new MemoryStream(nupkg),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(handler);
        await using var workspace = new InspectionWorkspace();

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Package(
                            "alpha.package",
                            "1.0.0"),
                    ],
                },
                Options(
                    client,
                    store,
                    sourceAuthorization: new PerPackageAuthorization
                    {
                        ["alpha.package"] = [FeedA],
                        ["bravo.package"] = [FeedB],
                    }),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.PackageUnavailable,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
        Assert.All(
            handler.Requests,
            url => Assert.DoesNotContain(
                "b.test",
                url,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task PerPackageAuthorization_WithNoProducer_IsTypedUnavailable()
    {
        using var client = new HttpClient(new FailingHandler());
        var store = new CountingPackageStore(new InMemoryPackageStore());
        await using var workspace = new InspectionWorkspace();

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                Options(
                    client,
                    store,
                    sourceAuthorization: new PerPackageAuthorization()),
                TestContext.Current.CancellationToken);

        WorkspaceContextLoadFailure failure =
            Assert.Single(Failed(outcome).Failures);
        Assert.Equal(
            WorkspaceContextLoadFailureKind.PackageUnavailable,
            failure.Kind);
        Assert.Equal(0, GroupCount(workspace));

        // An empty authorization ends the member: no cache read, no download,
        // and no fallback to a default feed the throwing client would reveal.
        Assert.Equal(0, store.Interactions);
    }

    [Fact]
    public async Task RealizedCoordinate_NamesTheProducerThatServedTheBytes()
    {
        var handler = new PerFeedHandler();

        // One id, one version, one target — two feeds, two different payloads.
        handler.Serve(FeedA, PackageId, Version, LibraryPackage());
        handler.Serve(FeedB, PackageId, Version, TargetV2Package());
        using var client = new HttpClient(handler);

        RealizedMemberCoordinate.Package fromA = await RealizeAsync(FeedA);
        RealizedMemberCoordinate.Package fromB = await RealizeAsync(FeedB);

        Assert.Equal(fromA.PackageId, fromB.PackageId);
        Assert.Equal(fromA.Version, fromB.Version);
        Assert.Equal(fromA.Framework, fromB.Framework);
        Assert.Equal(fromA.RuntimeIdentifier, fromB.RuntimeIdentifier);
        Assert.Equal(Producer(FeedA), fromA.Producer);
        Assert.Equal(Producer(FeedB), fromB.Producer);
        Assert.NotEqual(fromA, fromB);

        async Task<RealizedMemberCoordinate.Package> RealizeAsync(
            PackageSource feed)
        {
            await using var workspace = new InspectionWorkspace();
            var loaded = Loaded(
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    new WorkspaceContextInput
                    {
                        Framework = Framework,
                        Members = [PackageMember(Version)],
                    },
                    Options(
                        client,
                        new InMemoryPackageStore(),
                        sourceAuthorization:
                            new UniformPackageSourceAuthorization([feed])),
                    TestContext.Current.CancellationToken));
            return Assert.IsType<RealizedMemberCoordinate.Package>(
                loaded.Members[0].Realized);
        }
    }

    /// <summary>
    /// The other half of the recorded producer. Both feeds are authorized and
    /// both serve this id and version, with different bytes; the realized
    /// coordinate names the second, and re-acquiring it must return the second
    /// feed's bytes rather than the first authorized feed's.
    /// </summary>
    [Fact]
    public async Task RealizedLoad_ReacquiresFromTheRecordedProducer()
    {
        var handler = new PerFeedHandler();
        handler.Serve(FeedA, PackageId, Version, TargetPackage());
        handler.Serve(FeedB, PackageId, Version, TargetV2Package());
        using var client = new HttpClient(handler);

        var pinned = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            PortableProducer(FeedB),
            Framework,
            runtimeIdentifier: null);

        await using var workspace = new InspectionWorkspace();
        var loaded = Loaded(
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [pinned],
                Options(
                    client,
                    new InMemoryPackageStore(),
                    sourceAuthorization:
                        new UniformPackageSourceAuthorization([FeedA, FeedB])),
                TestContext.Current.CancellationToken));

        // The realized coordinate round-trips by value, and the bytes are the
        // producer's own: the two feeds ship different assembly versions of one
        // id and version, and the second feed's is what came back.
        Assert.Equal(pinned, loaded.Members[0].Realized);
        Assert.Equal(
            IdentityVersion(TargetV2Path),
            Assert.Single(loaded.Group.Participants).Assembly.Identity.Version);

        // Exact selection, not preference: the first authorized feed was never
        // asked, although it is authorized and does serve this coordinate.
        Assert.NotEmpty(handler.Requests);
        Assert.All(
            handler.Requests,
            url => Assert.DoesNotContain(
                "a.test",
                url,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RealizedLoad_WithAnUnauthorizedProducer_FailsTyped()
    {
        var handler = new PerFeedHandler();
        handler.Serve(FeedA, PackageId, Version, TargetPackage());
        using var client = new HttpClient(handler);
        var store = new InMemoryPackageStore();

        // The coordinate was realized somewhere else, from a producer this host
        // does not authorize for this package. A coordinate confers nothing:
        // the host's own authorization still governs, and the answer is typed
        // rather than a quiet fallback to the producer it does authorize.
        var pinned = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            Producer(FeedB),
            Framework,
            runtimeIdentifier: null);

        await using var workspace = new InspectionWorkspace();
        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [pinned],
                Options(
                    client,
                    store,
                    sourceAuthorization:
                        new UniformPackageSourceAuthorization([FeedA])),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.PackageProducerUnavailable,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));

        // The intersection is empty, so the member ends before any discovery,
        // cache read, or download for it.
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// Pinning holds through a discovery failure. The recorded producer is
    /// authorized here, but its service index advertises no package resource,
    /// so the coordinate cannot be re-acquired from it. That is the producer
    /// failing, not the package being unavailable in general — and it is not an
    /// invitation to ask the other authorized producer, which does serve this
    /// coordinate.
    /// </summary>
    [Fact]
    public async Task RealizedLoad_WhenTheProducerCannotDiscoverTheResource_FailsTyped()
    {
        var handler = new PerFeedHandler();
        handler.Serve(FeedA, PackageId, Version, TargetPackage());
        handler.WithoutFlatContainer(FeedB);
        using var client = new HttpClient(handler);

        var pinned = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            Producer(FeedB),
            Framework,
            runtimeIdentifier: null);

        await using var workspace = new InspectionWorkspace();
        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [pinned],
                Options(
                    client,
                    new InMemoryPackageStore(),
                    sourceAuthorization:
                        new UniformPackageSourceAuthorization([FeedA, FeedB])),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.PackageProducerUnavailable,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));

        // No fallback producer was contacted, although one is authorized and
        // does serve this exact coordinate.
        Assert.NotEmpty(handler.Requests);
        Assert.All(
            handler.Requests,
            url => Assert.DoesNotContain(
                "a.test",
                url,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// The same shape one step later: the producer's resource is discoverable
    /// but it does not serve this coordinate's payload.
    /// </summary>
    [Fact]
    public async Task RealizedLoad_WhenTheProducerDoesNotServeThePayload_FailsTyped()
    {
        var handler = new PerFeedHandler();
        handler.Serve(FeedA, PackageId, Version, TargetPackage());
        using var client = new HttpClient(handler);

        var pinned = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            Producer(FeedB),
            Framework,
            runtimeIdentifier: null);

        await using var workspace = new InspectionWorkspace();
        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [pinned],
                Options(
                    client,
                    new InMemoryPackageStore(),
                    sourceAuthorization:
                        new UniformPackageSourceAuthorization([FeedA, FeedB])),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.PackageProducerUnavailable,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
        Assert.All(
            handler.Requests,
            url => Assert.DoesNotContain(
                "a.test",
                url,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Pinning reaches the cache too: a warm entry committed by another
    /// authorized producer is not this coordinate's bytes, so it is not served.
    /// </summary>
    [Fact]
    public async Task RealizedLoad_IgnoresACachedEntryFromAnotherProducer()
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            Producer(FeedA),
            new MemoryStream(TargetPackage()),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new NotFoundHandler());

        var pinned = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            Producer(FeedB),
            Framework,
            runtimeIdentifier: null);

        await using var workspace = new InspectionWorkspace();
        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [pinned],
                Options(
                    client,
                    store,
                    sourceAuthorization:
                        new UniformPackageSourceAuthorization([FeedA, FeedB])),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.PackageProducerUnavailable,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task RealizedLoad_WithACachedProducerEntry_AnswersWithoutNetworkWork()
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            Producer(FeedB),
            new MemoryStream(TargetPackage()),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());

        var pinned = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            Producer(FeedB),
            Framework,
            runtimeIdentifier: null);

        await using var workspace = new InspectionWorkspace();
        var loaded = Loaded(
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [pinned],
                Options(
                    client,
                    store,
                    sourceAuthorization:
                        new UniformPackageSourceAuthorization([FeedA, FeedB])),
                TestContext.Current.CancellationToken));

        Assert.Equal(pinned, loaded.Members[0].Realized);
    }

    /// <summary>
    /// A whole context round-trips: what <c>LoadAsync</c> realized is what
    /// <c>LoadRealizedAsync</c> re-acquires, member for member, including an
    /// embedded member whose bytes never came from a feed.
    /// </summary>
    [Fact]
    public async Task RealizedLoad_RoundTripsAWholeContext()
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        IPackageStore store = await CachedStoreAsync(Version, LibraryPackage());
        using var client = new HttpClient(new FailingHandler());
        var provider = new StubEmbeddedContent(embedded);

        await using var first = new InspectionWorkspace();
        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                first,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        PackageMember(Version),
                        EmbeddedMember(embedded),
                    ],
                },
                Options(client, store, provider),
                TestContext.Current.CancellationToken));

        // Exactly what a loaded context reports, with no caller-side
        // de-duplication: the package carries two assemblies, so its realized
        // coordinate appears twice in Members, and the boundary has to be the
        // one that collapses it.
        ImmutableArray<RealizedMemberCoordinate> realized =
        [
            .. loaded.Members.Select(member => member.Realized),
        ];
        Assert.Equal(3, realized.Length);
        Assert.Equal(2, realized.Distinct().Count());

        await using var second = new InspectionWorkspace();
        var reloaded = Loaded(
            await WorkspaceContextLoader.LoadRealizedAsync(
                second,
                realized,
                Options(client, store, provider),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            loaded.Group.Participants.Length,
            reloaded.Group.Participants.Length);
        Assert.Equal(
            loaded.Members.Select(member => member.Realized),
            reloaded.Members.Select(member => member.Realized));
        Assert.Equal(Framework, reloaded.Framework);

        string[] identities =
        [
            .. reloaded.Group.Participants
                .Select(participant => participant.Assembly.Identity.Name)
                .Order(StringComparer.Ordinal),
        ];
        Assert.Equal(
            loaded.Group.Participants
                .Select(participant => participant.Assembly.Identity.Name)
                .Order(StringComparer.Ordinal),
            identities);
        Assert.Equal(identities.Length, identities.Distinct(StringComparer.Ordinal).Count());

        // The repeated coordinate would have put one assembly in the group
        // twice, and every in-context reference to it would then bind to
        // several descriptors rather than one.
        AssemblyContextParticipant caller = Participant(reloaded, CallerPath);
        AssemblyContextParticipant target = Participant(reloaded, TargetPath);
        AssemblyBindingSelection selection = caller.BindingPolicy.Select(
            new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(target.Assembly.Identity),
                AssemblyBindingOrigin.FromAssembly(caller.Assembly),
                AssemblyResolutionScope.Any)).Selection;
        Assert.Same(
            target.Assembly,
            Assert.IsType<AssemblyBindingSelection.Selected>(selection).Assembly);
    }

    [Fact]
    public async Task RealizedLoad_WithConflictingTargets_CreatesNoGroup()
    {
        IPackageStore store = await CachedStoreAsync(Version, LibraryPackage());
        using var client = new HttpClient(new FailingHandler());
        await using var workspace = new InspectionWorkspace();

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [
                    new RealizedMemberCoordinate.Package(
                        PackageId,
                        Version,
                        Producer(NuGetOrg),
                        Framework,
                        runtimeIdentifier: null),
                    new RealizedMemberCoordinate.Package(
                        "other.package",
                        Version,
                        Producer(NuGetOrg),
                        "net8.0",
                        runtimeIdentifier: null),
                ],
                Options(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.ConflictingAcquisitionTarget,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task RealizedLoad_WithNoMembers_CreatesNoGroup()
    {
        using var client = new HttpClient(new FailingHandler());
        await using var workspace = new InspectionWorkspace();

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [],
                Options(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.EmptyContext,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }
}
