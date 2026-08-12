using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;

using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Realizing one workspace context into exactly one binding-consistent
/// assembly context group. The package members carry real compiled fixture
/// assemblies inside in-memory archives, so acquisition, asset selection,
/// descriptor construction, and image access are exercised against real PE
/// images rather than synthetic bytes.
/// </summary>
public sealed class WorkspaceContextLoaderTests
{
    const string Framework = "net10.0";
    const string PackageId = "workspace.sample";
    const string Version = "1.0.0";

    static readonly PackageSource NuGetOrg = PackageSource.NuGetOrg;
    static readonly PackageSource Private =
        new("private", "https://private.test/v3/index.json");
    static readonly PackageSource FeedA =
        new("feed-a", "https://a.test/v3/index.json");
    static readonly PackageSource FeedB =
        new("feed-b", "https://b.test/v3/index.json");
    static readonly string CallerPath =
        FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
    static readonly string TargetPath =
        FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
    static readonly string EmbeddedPath =
        FixtureCatalog.AnalysisCallerGraphLookalikeCaller.AssemblyPath();
    static readonly string TargetV2Path =
        FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath();

    [Fact]
    public async Task PackageMember_RealizesEveryManagedAssemblyInOneGroup()
    {
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            LibraryPackage());
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                Options(client, store),
                TestContext.Current.CancellationToken);

        var loaded = Loaded(outcome);
        Assert.Equal(1, GroupCount(workspace));
        Assert.Equal(Framework, loaded.Framework);
        Assert.Null(loaded.RuntimeIdentifier);
        Assert.Equal(2, loaded.Group.Participants.Length);
        Assert.Equal(
            [
                Path.GetFileNameWithoutExtension(CallerPath),
                Path.GetFileNameWithoutExtension(TargetPath),
            ],
            loaded.Group.Participants
                .Select(participant => participant.Assembly.Identity.Name)
                .Order(StringComparer.Ordinal));

        foreach (AssemblyContextParticipant participant
            in loaded.Group.Participants)
        {
            // One binding-policy snapshot for the whole context, and no
            // filesystem path: the descriptor is served by the package store.
            Assert.Same(
                loaded.Group.BindingPolicyVersion,
                participant.BindingPolicy.Version);
            Assert.Null(participant.Assembly.Path);

            var provenance = Assert.IsType<
                AssemblyResolutionProvenance.PackageAsset>(
                participant.Assembly.Provenance);
            Assert.Equal(PackageId, provenance.PackageId);
            Assert.Equal(Version, provenance.PackageVersion);
            Assert.Equal(Framework, provenance.Tfm);
            Assert.Null(provenance.Rid);

            AssemblyImageAccessResult<int> image =
                loaded.Group.UseAssemblyImage(
                    participant.Assembly,
                    static view => view.Content.Length);
            Assert.IsType<AssemblyImageAccessResult<int>.Available>(image);
        }
    }

    [Fact]
    public async Task Group_BindsAnInContextReferenceToItsOwnDescriptor()
    {
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            LibraryPackage());
        using var client = new HttpClient(new FailingHandler());

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        AssemblyContextParticipant caller = Participant(loaded, CallerPath);
        AssemblyContextParticipant target = Participant(loaded, TargetPath);

        AssemblyBindingSelection selection = caller.BindingPolicy.Select(
            new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(target.Assembly.Identity),
                AssemblyBindingOrigin.FromAssembly(caller.Assembly),
                AssemblyResolutionScope.Any));

        Assert.Same(
            target.Assembly,
            Assert.IsType<AssemblyBindingSelection.Selected>(selection)
                .Assembly);

        // A reference outside the context has no resolver behind it, so it is
        // a typed non-selection rather than a filesystem probe.
        AssemblyBindingSelection outside = caller.BindingPolicy.Select(
            new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(
                    new AssemblyReferenceIdentity(
                        "Absent.Library",
                        new Version(1, 0, 0, 0),
                        null,
                        null)),
                AssemblyBindingOrigin.FromAssembly(caller.Assembly),
                AssemblyResolutionScope.Any));
        Assert.IsType<AssemblyBindingSelection.Missing>(outside);
    }

    [Fact]
    public async Task MixedPackageAndEmbeddedMembers_ShareOneGroup()
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            LibraryPackage());
        using var client = new HttpClient(new FailingHandler());

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        PackageMember(Version),
                        EmbeddedMember(embedded),
                    ],
                },
                Options(client, store, new StubEmbeddedContent(embedded)),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, GroupCount(workspace));
        Assert.Equal(3, loaded.Group.Participants.Length);
        Assert.Equal(3, loaded.Members.Length);
        Assert.Equal(
            2,
            loaded.Members.Count(member =>
                member.Declared
                    is WorkspaceMemberCoordinate.PackageMember));

        AssemblyContextParticipant embeddedParticipant =
            Participant(loaded, EmbeddedPath);
        var provenance = Assert.IsType<
            AssemblyResolutionProvenance.EmbeddedAsset>(
            embeddedParticipant.Assembly.Provenance);
        Assert.Equal("bundle/lookalike.dll", provenance.ContentRef);
        Assert.Equal(Digest(embedded), provenance.Digest);
        Assert.Equal(
            Path.GetFileNameWithoutExtension(EmbeddedPath),
            provenance.DeclaredName);
        Assert.Same(
            loaded.Group.BindingPolicyVersion,
            embeddedParticipant.BindingPolicy.Version);
    }

    [Fact]
    public async Task MemberTarget_IsInheritedFromTheContext()
    {
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            RuntimeSpecificPackage());
        using var client = new HttpClient(new FailingHandler());

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    RuntimeIdentifier = "browser-wasm",
                    Members = [PackageMember(Version)],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal("browser-wasm", loaded.RuntimeIdentifier);
        AssemblyContextParticipant participant =
            Assert.Single(loaded.Group.Participants);
        var provenance = Assert.IsType<
            AssemblyResolutionProvenance.PackageAsset>(
            participant.Assembly.Provenance);
        Assert.Equal("browser-wasm", provenance.Rid);

        // The runtime-specific asset replaced the runtime-neutral one at the
        // same relative path. The fixtures share an assembly name and differ
        // only by version, so the identity says which folder was used.
        Assert.NotEqual(
            IdentityVersion(TargetPath),
            IdentityVersion(TargetV2Path));
        Assert.Equal(
            IdentityVersion(TargetPath),
            participant.Assembly.Identity.Version);
    }

    [Fact]
    public async Task MemberTarget_MayRestateTheContextTarget()
    {
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            LibraryPackage());
        using var client = new HttpClient(new FailingHandler());

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Members =
                    [
                        WorkspaceMemberCoordinate.Package(
                            PackageId,
                            Version,
                            Framework),
                    ],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(Framework, loaded.Framework);
    }

    [Fact]
    public async Task ConflictingTargets_CreateNoGroup()
    {
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            LibraryPackage());
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Package(
                            PackageId,
                            Version,
                            "net8.0"),
                    ],
                },
                Options(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.ConflictingAcquisitionTarget,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task PackageMemberWithoutAFramework_ReportsAMissingTarget()
    {
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            LibraryPackage());
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Members = [PackageMember(Version)],
                },
                Options(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.MissingAcquisitionTarget,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task EmptyContext_CreatesNoGroup()
    {
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput { Framework = Framework },
                Options(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.EmptyContext,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task FloatingMember_UsesTheListingAwareVersionPolicy()
    {
        byte[] nupkg = LibraryPackage();
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(
            new ListingHandler(nupkg, listedVersion: "1.5.0"));

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Package(PackageId),
                    ],
                },
                Options(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken));

        // 2.0.0 is the highest published version and is unlisted, so a
        // floating member resolves to the listed 1.5.0 instead.
        var provenance = Assert.IsType<
            AssemblyResolutionProvenance.PackageAsset>(
            Participant(loaded, TargetPath).Assembly.Provenance);
        Assert.Equal("1.5.0", provenance.PackageVersion);
        var realized = Assert.IsType<RealizedMemberCoordinate.Package>(
            loaded.Members[0].Realized);
        Assert.Equal(PackageId, realized.PackageId);
        Assert.Equal("1.5.0", realized.Version);
        Assert.Equal(Framework, realized.Framework);
        Assert.All(
            loaded.Members,
            member => Assert.Equal(realized, member.Realized));
    }

    [Fact]
    public async Task ExactPin_SelectsAnUnlistedVersionWithoutDiscovery()
    {
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            "2.0.0",
            LibraryPackage());
        using var client = new HttpClient(new FailingHandler());

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember("2.0.0")],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        var provenance = Assert.IsType<
            AssemblyResolutionProvenance.PackageAsset>(
            Participant(loaded, TargetPath).Assembly.Provenance);
        Assert.Equal("2.0.0", provenance.PackageVersion);
        Assert.Equal(
            new RealizedMemberCoordinate.Package(
                PackageId,
                "2.0.0",
                Producer(NuGetOrg),
                Framework,
                runtimeIdentifier: null),
            loaded.Members[0].Realized);
    }

    [Fact]
    public async Task BrowserNeutralAcquisition_DownloadsAndRealizesInMemory()
    {
        byte[] nupkg = LibraryPackage();
        var store = new InMemoryPackageStore();
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new PayloadHandler(nupkg, Version));

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        // Nothing in this path names a filesystem location: the payload came
        // from the host's HTTP client into an in-memory store, and every
        // descriptor is stream-backed.
        Assert.Equal(1, GroupCount(workspace));
        Assert.All(
            loaded.Group.Participants,
            participant => Assert.Null(participant.Assembly.Path));
        Assert.NotNull(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
        Assert.All(
            loaded.Group.Participants,
            participant => Assert.IsType<
                AssemblyImageAccessResult<int>.Available>(
                loaded.Group.UseAssemblyImage(
                    participant.Assembly,
                    static view => view.Content.Length)));
    }

    [Fact]
    public async Task PerPackageAuthorization_KeepsEachPackageOnItsOwnProducer()
    {
        var handler = new PerFeedHandler();
        handler.Serve(FeedA, "alpha.package", "1.0.0", CallerPackage());
        handler.Serve(FeedB, "bravo.package", "1.0.0", TargetPackage());
        using var client = new HttpClient(handler);
        using var workspace = new InspectionWorkspace();

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
        using var workspace = new InspectionWorkspace();

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
        using var workspace = new InspectionWorkspace();

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
            using var workspace = new InspectionWorkspace();
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
            Producer(FeedB),
            Framework,
            runtimeIdentifier: null);

        using var workspace = new InspectionWorkspace();
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

        using var workspace = new InspectionWorkspace();
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

        using var workspace = new InspectionWorkspace();
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

        using var workspace = new InspectionWorkspace();
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

        using var workspace = new InspectionWorkspace();
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

        using var workspace = new InspectionWorkspace();
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

        using var first = new InspectionWorkspace();
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

        ImmutableArray<RealizedMemberCoordinate> realized =
        [
            .. loaded.Members
                .Select(member => member.Realized)
                .Distinct(),
        ];

        using var second = new InspectionWorkspace();
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
        Assert.Equal(
            loaded.Group.Participants
                .Select(participant => participant.Assembly.Identity.Name)
                .Order(StringComparer.Ordinal),
            reloaded.Group.Participants
                .Select(participant => participant.Assembly.Identity.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RealizedLoad_WithConflictingTargets_CreatesNoGroup()
    {
        IPackageStore store = await CachedStoreAsync(Version, LibraryPackage());
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

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
        using var workspace = new InspectionWorkspace();

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

    [Theory]
    [InlineData("net10.0\u0007", null, null, null)]
    [InlineData(null, "browser-wasm\u0007", null, null)]
    [InlineData("net10.0", null, "net10.0\u200b\u0000", null)]
    [InlineData("net10.0", null, null, "browser\u0001wasm")]
    public async Task InvalidTargetText_IsRejectedBeforeAnyAcquisition(
        string? contextFramework,
        string? contextRid,
        string? memberFramework,
        string? memberRid)
    {
        using var client = new HttpClient(new FailingHandler());
        var store = new CountingPackageStore(new InMemoryPackageStore());
        using var workspace = new InspectionWorkspace();

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = contextFramework ?? Framework,
                    RuntimeIdentifier = contextRid,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Package(
                            PackageId,
                            Version,
                            memberFramework,
                            memberRid),
                    ],
                },
                Options(client, store),
                TestContext.Current.CancellationToken);

        Assert.Contains(
            Failed(outcome).Failures,
            failure => failure.Kind
                == WorkspaceContextLoadFailureKind.InvalidCoordinate);
        Assert.Equal(0, GroupCount(workspace));
        Assert.Equal(0, store.Interactions);
    }

    [Theory]
    [InlineData("../../admin")]
    [InlineData("sample?version=1")]
    [InlineData("sample#fragment")]
    [InlineData("sample/nested")]
    [InlineData("sample\\nested")]
    [InlineData("sample\u0007package")]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData("sample..package")]
    [InlineData("https://feed.test/sample")]
    public async Task InvalidPackageId_IsRejectedBeforeAnyAcquisition(
        string packageId)
    {
        foreach (IPackageStore inner in
            new IPackageStore[]
            {
                new InMemoryPackageStore(),
                new FileSystemPackageStore(),
            })
        {
            using var client = new HttpClient(new FailingHandler());
            var store = new CountingPackageStore(inner);
            using var workspace = new InspectionWorkspace();

            WorkspaceContextLoadOutcome outcome =
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    new WorkspaceContextInput
                    {
                        Framework = Framework,
                        Members =
                        [
                            WorkspaceMemberCoordinate.Package(
                                packageId,
                                Version),
                        ],
                    },
                    Options(client, store),
                    TestContext.Current.CancellationToken);

            // Both store kinds see the same typed rejection, because neither
            // is reached: the grammar decides before any source, cache, or
            // network step.
            WorkspaceContextLoadFailure failure =
                Assert.Single(Failed(outcome).Failures);
            Assert.Equal(
                WorkspaceContextLoadFailureKind.InvalidCoordinate,
                failure.Kind);
            Assert.DoesNotContain(
                packageId,
                failure.Message,
                StringComparison.Ordinal);
            Assert.Equal(0, GroupCount(workspace));
            Assert.Equal(0, store.Interactions);
        }
    }

    [Fact]
    public async Task OversizedPackagePayload_CreatesNoGroupAndDoesNotCache()
    {
        byte[] nupkg = LibraryPackage();
        var store = new InMemoryPackageStore();
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new PayloadHandler(nupkg, Version));

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
                    payloadLimits: new PackagePayloadLimits
                    {
                        MaxArchiveBytes = 1024,
                    }),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.PackageUnavailable,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
        Assert.Null(
            store.TryGetCached(PackageId, Version, [Producer(NuGetOrg)]));
    }

    [Fact]
    public async Task UnavailablePackage_CreatesNoGroup()
    {
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new NotFoundHandler());

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                Options(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.PackageUnavailable,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task PackageWithoutApplicableAssets_CreatesNoGroup()
    {
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            Archive(("lib/net481/Sample.dll", File.ReadAllBytes(TargetPath))));
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                Options(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.PackageAssetUnavailable,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task PackageAssetWithoutManagedMetadata_IsNotAnAssembly()
    {
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            Archive(
                ("lib/net10.0/Native.dll", "not a portable executable"u8.ToArray()),
                ($"lib/net10.0/{Path.GetFileName(TargetPath)}",
                    File.ReadAllBytes(TargetPath))));
        using var client = new HttpClient(new FailingHandler());

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            Path.GetFileNameWithoutExtension(TargetPath),
            Assert.Single(loaded.Group.Participants)
                .Assembly.Identity.Name);
    }

    [Fact]
    public async Task EmbeddedMemberWithoutAHostProvider_IsUnavailable()
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Members = [EmbeddedMember(embedded)],
                },
                Options(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.HostCapabilityUnavailable,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task MissingEmbeddedContent_CreatesNoGroup()
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Members = [EmbeddedMember(embedded)],
                },
                Options(
                    client,
                    new InMemoryPackageStore(),
                    new StubEmbeddedContent(content: null)),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.EmbeddedContentUnavailable,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task EmbeddedDigestMismatch_CreatesNoGroup()
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        byte[] tampered = [.. embedded];
        tampered[^1] ^= 0xFF;
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Members = [EmbeddedMember(embedded)],
                },
                Options(
                    client,
                    new InMemoryPackageStore(),
                    new StubEmbeddedContent(tampered)),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.EmbeddedDigestMismatch,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task EmbeddedNameMismatch_CreatesNoGroup()
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Members =
                    [
                        WorkspaceMemberCoordinate.Embedded(
                            "bundle/lookalike.dll",
                            Digest(embedded),
                            "Some.Other.Assembly"),
                    ],
                },
                Options(
                    client,
                    new InMemoryPackageStore(),
                    new StubEmbeddedContent(embedded)),
                TestContext.Current.CancellationToken);

        WorkspaceContextLoadFailure failure =
            Assert.Single(Failed(outcome).Failures);
        Assert.Equal(
            WorkspaceContextLoadFailureKind.EmbeddedNameMismatch,
            failure.Kind);
        Assert.DoesNotContain(
            Path.GetFileNameWithoutExtension(EmbeddedPath),
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task MalformedEmbeddedContent_CreatesNoGroup()
    {
        byte[] malformed = "not a portable executable"u8.ToArray();
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Members = [EmbeddedMember(malformed)],
                },
                Options(
                    client,
                    new InMemoryPackageStore(),
                    new StubEmbeddedContent(malformed)),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.InvalidImage,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task OversizedEmbeddedContent_CreatesNoGroup()
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Members = [EmbeddedMember(embedded)],
                },
                Options(
                    client,
                    new InMemoryPackageStore(),
                    new StubEmbeddedContent(embedded)) with
                {
                    MaxEmbeddedContentBytes = 16,
                },
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.EmbeddedContentUnavailable,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Theory]
    [InlineData("", "Sample")]
    [InlineData("not-hex", "Sample")]
    [InlineData("0123456789abcdef", "Sample")]
    [InlineData("valid", "")]
    [InlineData("valid", "Bad/Name")]
    public async Task MalformedEmbeddedCoordinate_IsRejectedBeforeAcquisition(
        string digest,
        string declaredName)
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());
        var provider = new StubEmbeddedContent(embedded);

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Members =
                    [
                        WorkspaceMemberCoordinate.Embedded(
                            "bundle/lookalike.dll",
                            digest == "valid" ? Digest(embedded) : digest,
                            declaredName),
                    ],
                },
                Options(client, new InMemoryPackageStore(), provider),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.InvalidCoordinate,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, provider.OpenCount);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Theory]
    [InlineData("/bundle/sample.dll")]
    [InlineData("bundle/sample.dll/")]
    [InlineData("bundle//sample.dll")]
    [InlineData("bundle/./sample.dll")]
    [InlineData("bundle/../sample.dll")]
    [InlineData("bundle\\sample.dll")]
    [InlineData(" bundle/sample.dll")]
    public async Task NonCanonicalEmbeddedContentRef_IsRejected(
        string contentRef)
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());
        var provider = new StubEmbeddedContent(embedded);

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Members =
                    [
                        WorkspaceMemberCoordinate.Embedded(
                            contentRef,
                            Digest(embedded),
                            "Sample"),
                    ],
                },
                Options(client, new InMemoryPackageStore(), provider),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.InvalidCoordinate,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, provider.OpenCount);
    }

    [Fact]
    public async Task UppercaseEmbeddedDigest_IsRejected()
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());
        var provider = new StubEmbeddedContent(embedded);

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Members =
                    [
                        WorkspaceMemberCoordinate.Embedded(
                            "bundle/sample.dll",
                            Digest(embedded).ToUpperInvariant(),
                            "Sample"),
                    ],
                },
                Options(client, new InMemoryPackageStore(), provider),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.InvalidCoordinate,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, provider.OpenCount);
    }

    [Fact]
    public void RealizedCoordinate_IsCanonicalAndStructurallyEquatable()
    {
        var first = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            Producer(NuGetOrg),
            Framework,
            "browser-wasm");
        var second = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            Producer(NuGetOrg),
            Framework,
            "browser-wasm");

        Assert.Equal(first, second);

        // The producer is part of the identity: the same id, version, target,
        // and runtime identifier served by another feed is another coordinate,
        // because it is not the same bytes.
        Assert.NotEqual(
            first,
            new RealizedMemberCoordinate.Package(
                PackageId,
                Version,
                Producer(Private),
                Framework,
                "browser-wasm"));

        Assert.Throws<ArgumentException>(
            () => new RealizedMemberCoordinate.Package(
                PackageId,
                "1.0",
                Producer(NuGetOrg),
                Framework,
                runtimeIdentifier: null));
        Assert.Throws<ArgumentException>(
            () => new RealizedMemberCoordinate.Package(
                "Workspace.Sample",
                Version,
                Producer(NuGetOrg),
                Framework,
                runtimeIdentifier: null));
        Assert.Throws<ArgumentException>(
            () => new RealizedMemberCoordinate.Package(
                "../../admin",
                Version,
                Producer(NuGetOrg),
                Framework,
                runtimeIdentifier: null));
        Assert.Throws<ArgumentException>(
            () => new RealizedMemberCoordinate.Package(
                PackageId,
                Version,
                Framework,
                Framework,
                runtimeIdentifier: null));
        Assert.Throws<ArgumentException>(
            () => new RealizedMemberCoordinate.Package(
                PackageId,
                Version,
                "https://user:secret@feed.test/v3/index.json",
                Framework,
                runtimeIdentifier: null));
        Assert.Throws<ArgumentException>(
            () => new RealizedMemberCoordinate.Package(
                PackageId,
                Version,
                Producer(NuGetOrg),
                "net10.0\u0007",
                runtimeIdentifier: null));
    }

    [Fact]
    public async Task Load_ObservesCancellation()
    {
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                Options(client, new InMemoryPackageStore()),
                cancellation.Token));
    }

    static WorkspaceContextLoadOptions Options(
        HttpClient client,
        IPackageStore store,
        IEmbeddedContentProvider? embeddedContent = null,
        IPackageSourceAuthorization? sourceAuthorization = null,
        PackagePayloadLimits? payloadLimits = null) =>
        new()
        {
            HttpClient = client,
            SourceAuthorization = sourceAuthorization
                ?? new UniformPackageSourceAuthorization([NuGetOrg]),
            PackageStore = store,
            EmbeddedContent = embeddedContent,
            PayloadLimits = payloadLimits ?? PackagePayloadLimits.Default,
        };

    static string Producer(PackageSource source) =>
        NuGetCache.GetSourceKey(source.Url);

    static WorkspaceMemberCoordinate PackageMember(string? version) =>
        WorkspaceMemberCoordinate.Package(PackageId, version);

    static WorkspaceMemberCoordinate EmbeddedMember(byte[] content) =>
        WorkspaceMemberCoordinate.Embedded(
            "bundle/lookalike.dll",
            Digest(content),
            Path.GetFileNameWithoutExtension(EmbeddedPath));

    static string Digest(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    static async Task<IPackageStore> CachedStoreAsync(
        string version,
        byte[] nupkg)
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            new MemoryStream(nupkg),
            TestContext.Current.CancellationToken);
        return store;
    }

    static byte[] LibraryPackage() =>
        Archive(
            ($"lib/{Framework}/{Path.GetFileName(CallerPath)}",
                File.ReadAllBytes(CallerPath)),
            ($"lib/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetPath)),
            ($"lib/{Framework}/de/{Path.GetFileNameWithoutExtension(TargetPath)}.resources.dll",
                File.ReadAllBytes(TargetPath)),
            ("build/Sample.props", "<Project />"u8.ToArray()));

    static byte[] CallerPackage() =>
        Archive(
            ($"lib/{Framework}/{Path.GetFileName(CallerPath)}",
                File.ReadAllBytes(CallerPath)));

    static byte[] TargetPackage() =>
        Archive(
            ($"lib/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetPath)));

    static byte[] TargetV2Package() =>
        Archive(
            ($"lib/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetV2Path)));

    static byte[] RuntimeSpecificPackage() =>
        Archive(
            ($"lib/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetV2Path)),
            ($"runtimes/browser-wasm/lib/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetPath)));

    static Version? IdentityVersion(string assemblyPath) =>
        ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local("fixture identity"))
            .Identity.Version;

    static byte[] Archive(params (string EntryPath, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string entryPath, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(entryPath).Open();
                stream.Write(content, 0, content.Length);
            }
        }

        return buffer.ToArray();
    }

    static WorkspaceContextLoadOutcome.Loaded Loaded(
        WorkspaceContextLoadOutcome outcome)
        => Assert.IsType<WorkspaceContextLoadOutcome.Loaded>(outcome);

    static WorkspaceContextLoadOutcome.Failed Failed(
        WorkspaceContextLoadOutcome outcome)
        => Assert.IsType<WorkspaceContextLoadOutcome.Failed>(outcome);

    static AssemblyContextParticipant Participant(
        WorkspaceContextLoadOutcome.Loaded loaded,
        string assemblyPath)
        => loaded.Group.Participants.Single(participant =>
            string.Equals(
                participant.Assembly.Identity.Name,
                Path.GetFileNameWithoutExtension(assemblyPath),
                StringComparison.Ordinal));

    /// <summary>
    /// Counts the groups a workspace owns. The workspace publishes no group
    /// census, and these tests assert that a rejected context creates no
    /// partial group, which is only observable from the inside.
    /// </summary>
    static int GroupCount(InspectionWorkspace workspace)
    {
        FieldInfo field =
            typeof(InspectionWorkspace).GetField(
                "_groups",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "InspectionWorkspace._groups was not found.");
        return ((System.Collections.ICollection)field.GetValue(workspace)!)
            .Count;
    }

    sealed class StubEmbeddedContent(byte[]? content)
        : IEmbeddedContentProvider
    {
        int _openCount;

        internal int OpenCount => _openCount;

        public bool TryOpenContent(
            string contentRef,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Stream? stream)
        {
            _openCount++;
            if (content is null)
            {
                stream = null;
                return false;
            }

            stream = new MemoryStream(content, writable: false);
            return true;
        }
    }

    sealed class PayloadHandler(byte[] nupkg, string version)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(
                request.RequestUri!.ToString().Equals(
                    $"https://api.nuget.org/v3-flatcontainer/{PackageId}/{version}/{PackageId}.{version}.nupkg",
                    StringComparison.OrdinalIgnoreCase)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nupkg),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    sealed class ListingHandler(byte[] nupkg, string listedVersion)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{PackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                return Json($$"""{"versions":["{{listedVersion}}","2.0.0"]}""");
            }

            if (url.Equals(
                $"https://api.nuget.org/v3/registration5-gz-semver2/{PackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                return Json(
                    $$$"""
                    {"items":[{"items":[
                      {"catalogEntry":{"version":"{{{listedVersion}}}","listed":true}},
                      {"catalogEntry":{"version":"2.0.0","listed":false}}
                    ]}]}
                    """);
            }

            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{PackageId}/{listedVersion}/{PackageId}.{listedVersion}.nupkg",
                StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nupkg),
                    });
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));

            static Task<HttpResponseMessage> Json(string body) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
        }
    }

    sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// Two independent private feeds, each with its own service index and flat
    /// container, that serve only the payloads registered for them. Every
    /// request URL is recorded, so a test can assert which feed was asked about
    /// which package rather than only which answer came back.
    /// </summary>
    sealed class PerFeedHandler : HttpMessageHandler
    {
        readonly Dictionary<string, byte[]> _payloads =
            new(StringComparer.Ordinal);
        readonly HashSet<string> _withoutFlatContainer =
            new(StringComparer.Ordinal);
        readonly List<string> _requests = [];

        internal IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                    return [.. _requests];
            }
        }

        internal void Serve(
            PackageSource feed,
            string packageId,
            string version,
            byte[] nupkg) =>
            _payloads[NupkgUrl(feed, packageId, version)] = nupkg;

        /// <summary>
        /// Answers this feed's service index without a flat-container resource,
        /// so its package resources cannot be discovered at all.
        /// </summary>
        internal void WithoutFlatContainer(PackageSource feed) =>
            _withoutFlatContainer.Add(feed.Url);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            lock (_requests)
                _requests.Add(url);

            foreach (PackageSource feed in new[] { FeedA, FeedB })
            {
                if (url.Equals(feed.Url, StringComparison.Ordinal))
                {
                    string resources = _withoutFlatContainer.Contains(feed.Url)
                        ? ""
                        : $$"""{"@id":"{{FlatContainer(feed)}}","@type":"PackageBaseAddress/3.0.0"}""";
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                $$"""
                                {"resources":[{{resources}}]}
                                """),
                        });
                }
            }

            return Task.FromResult(
                _payloads.TryGetValue(url, out byte[]? nupkg)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nupkg),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        static string FlatContainer(PackageSource feed) =>
            $"{new Uri(feed.Url).GetLeftPart(UriPartial.Authority)}/flat/";

        static string NupkgUrl(
            PackageSource feed,
            string packageId,
            string version) =>
            $"{FlatContainer(feed)}{packageId}/{version}/{packageId}.{version}.nupkg";
    }

    /// <summary>
    /// A host policy that authorizes a different producer set for each package
    /// id, the shape NuGet package source mapping produces. An id it never
    /// heard of is authorized for nothing.
    /// </summary>
    sealed class PerPackageAuthorization
        : Dictionary<string, PackageSource[]>, IPackageSourceAuthorization
    {
        internal PerPackageAuthorization()
            : base(StringComparer.Ordinal)
        {
        }

        public PackageSourceAuthorization AuthorizeSourcesFor(string packageId)
            => TryGetValue(packageId, out PackageSource[]? sources)
                ? PackageSourceAuthorization.Authorize(sources)
                : PackageSourceAuthorization.Deny(
                    "No source is authorized for this package.");
    }

    /// <summary>
    /// Counts every call that reaches a store, so a test can prove a rejection
    /// happened before any cache read or commit rather than only that it
    /// happened.
    /// </summary>
    sealed class CountingPackageStore(IPackageStore inner) : IPackageStore
    {
        int _interactions;

        internal int Interactions => Volatile.Read(ref _interactions);

        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null)
        {
            Interlocked.Increment(ref _interactions);
            return inner.TryGetCached(
                packageName,
                version,
                allowedSourceKeys,
                log);
        }

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _interactions);
            return inner.CommitAsync(
                packageName,
                version,
                sourceKey,
                nupkg,
                cancellationToken);
        }
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }
}
