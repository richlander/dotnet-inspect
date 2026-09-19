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

/// <summary>
/// Realizing one workspace context into exactly one binding-consistent
/// assembly context group. The package members carry real compiled fixture
/// assemblies inside in-memory archives, so acquisition, asset selection,
/// descriptor construction, and image access are exercised against real PE
/// images rather than synthetic bytes.
/// </summary>
public sealed partial class WorkspaceContextLoaderTests
{
    const string Framework = "net10.0";
    const string PackageId = "workspace.sample";
    const string Version = "1.0.0";
    const string RuntimePackPackageId =
        "microsoft.netcore.app.runtime.linux-x64";
    const string AspNetCorePackPackageId =
        "microsoft.aspnetcore.app.runtime.linux-x64";
    const string RuntimePackVersion = "10.0.2";

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
        await using var workspace = new InspectionWorkspace();
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
                Options(client, store) with
                {
                    IncludePackageRootBindings = true,
                },
                TestContext.Current.CancellationToken);

        var loaded = Loaded(outcome);
        Assert.Equal(1, GroupCount(workspace));
        Assert.Equal(Framework, loaded.Framework);
        Assert.Null(loaded.RuntimeIdentifier);
        Assert.Equal(2, loaded.Group.Participants.Length);
        PackageRootBinding packageRoot =
            Assert.Single(loaded.PackageRoots);
        Assert.Equal(PackageId, packageRoot.Root.PackageId);
        Assert.Equal(Version, packageRoot.Root.PackageVersion);
        Assert.Equal(
            Framework,
            packageRoot.Root.AssetSelection.TargetFramework);
        Assert.Equal(
            Assert.IsType<RealizedMemberCoordinate.Package>(
                loaded.Members[0].Realized),
            packageRoot.Coordinate);
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
            Assert.Equal(
                $"lib/{Framework}/{participant.Assembly.Identity.Name}.dll",
                provenance.AssetPath);

            AssemblyImageAccessResult<int> image =
                loaded.Group.UseAssemblyImage(
                    participant.Assembly,
                    static view => view.Content.Length);
            Assert.IsType<AssemblyImageAccessResult<int>.Available>(image);
        }
    }

    [Fact]
    public async Task PackageBoundary_ProjectsLoadedPackageAsGroupAndNode()
    {
        await using var workspace = new InspectionWorkspace();
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

        InspectionGraphPackageBoundary boundary =
            InspectionGraphPackageBoundary.Create(loaded);
        InspectionGraphDocument document = boundary.Project(
            InspectionGraphPackageBoundaryLens.Mixed);

        Assert.Equal(3, document.Nodes.Length);
        InspectionGraphGroup packageGroup =
            Assert.Single(document.Groups);
        Assert.Same(document.Nodes[0].Subject, packageGroup.Subject);
        Assert.All(
            document.Nodes.Skip(1),
            node => Assert.Equal([packageGroup.Id], node.GroupIds));
        Assert.All(
            loaded.Members,
            member =>
            {
                Assert.True(
                    boundary.TryGetPackageSubject(
                        member.Participant.Assembly.Registration,
                        out InspectionGraphSubject.PackageSubject? owner));
                Assert.Same(packageGroup.Subject, owner);
            });
    }

    [Fact]
    public async Task PackageBoundary_KeepsEffectiveTargetAcrossAssetFallback()
    {
        const string RequestedFramework = "net11.0";
        await using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            Archive(
                ($"lib/{Framework}/{Path.GetFileName(TargetPath)}",
                    File.ReadAllBytes(TargetPath))));
        using var client = new HttpClient(new FailingHandler());

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = RequestedFramework,
                    Members = [PackageMember(Version)],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        WorkspaceContextMember member = Assert.Single(loaded.Members);
        var package =
            Assert.IsType<RealizedMemberCoordinate.Package>(
                member.Realized);
        var provenance =
            Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(
                member.Participant.Assembly.Provenance);
        Assert.Equal(RequestedFramework, package.Framework);
        Assert.Equal(Framework, provenance.Tfm);

        InspectionGraphDocument document =
            InspectionGraphPackageBoundary.Create(loaded)
                .Project(InspectionGraphPackageBoundaryLens.PackageNodes);
        var subject =
            Assert.IsType<InspectionGraphSubject.PackageSubject>(
                Assert.Single(document.Nodes).Subject);
        Assert.Equal(
            package,
            Assert.IsType<InspectionGraphPackageIdentity.Realized>(
                subject.Identity).Package);
    }

    [Fact]
    public async Task Group_BindsAnInContextReferenceToItsOwnDescriptor()
    {
        await using var workspace = new InspectionWorkspace();
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

        Assert.IsAssignableFrom<IAcquisitionFreeAssemblyBindingPolicy>(caller.BindingPolicy);
        AssemblyBindingSelection selection = caller.BindingPolicy.Select(
            new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(target.Assembly.Identity),
                AssemblyBindingOrigin.FromAssembly(caller.Assembly),
                AssemblyResolutionScope.Any)).Selection;

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
                AssemblyResolutionScope.Any)).Selection;
        Assert.IsType<AssemblyBindingSelection.Missing>(outside);
    }

    [Fact]
    public async Task Group_IntrinsicSelectionDoesNotReopenPackageContent()
    {
        var inner = new InMemoryPackageStore();
        await inner.CommitAsync(
            PackageId,
            Version,
            Producer(NuGetOrg),
            new MemoryStream(LibraryPackage()),
            TestContext.Current.CancellationToken);
        var store = new EntryCountingPackageStore(inner);
        await using var workspace = new InspectionWorkspace();
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
        int opensAfterLoad = store.EntryOpens;
        Assert.True(opensAfterLoad > 0);

        AssemblyContextParticipant caller =
            Participant(loaded, CallerPath);
        _ = caller.BindingPolicy.Select(
            new AssemblyBindingRequest(
                AssemblyBindingTarget.CoreLibrary(),
                AssemblyBindingOrigin.FromAssembly(caller.Assembly),
                AssemblyResolutionScope.Any));

        Assert.Equal(opensAfterLoad, store.EntryOpens);
    }

    [Fact]
    public async Task Group_DisposalRevokesItsSnapshotBackedDescriptors()
    {
        InspectionWorkspace workspace =
            new InspectionWorkspace();
        try
        {
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
            ResolvedAssemblyReference assembly =
                Participant(loaded, CallerPath).Assembly;

            using Stream activeStream = assembly.OpenRead();
            loaded.Group.Dispose();

            Assert.Throws<ObjectDisposedException>(
                () => assembly.OpenRead());
            Assert.NotEqual(-1, activeStream.ReadByte());
        }
        finally
        {
            await workspace.CloseAsync();
        }
    }

    [Fact]
    public async Task Group_RetentionBudgetFailureCreatesNoGroup()
    {
        await using var workspace = new InspectionWorkspace();
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
                Options(client, store) with
                {
                    MaxRetainedImageBytes = 1,
                },
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind
                .ImageRetentionBudgetExceeded,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task MixedPackageAndEmbeddedMembers_ShareOneGroup()
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        var transferPolicy = new RecordingTransferPolicy();
        await using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new PayloadHandler(nupkg, Version));

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
                    store,
                    packageTransferPolicy: transferPolicy),
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
        Assert.Equal(PackageId, transferPolicy.Transfer?.Coordinate.PackageId);
        Assert.Equal(Version, transferPolicy.Transfer?.Coordinate.Version);
        Assert.True(transferPolicy.Completed);
        Assert.True(transferPolicy.Disposed);
    }
}
