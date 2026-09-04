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
public sealed class WorkspaceContextLoaderTests
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

            AssemblyImageAccessResult<int> image =
                loaded.Group.UseAssemblyImage(
                    participant.Assembly,
                    static view => view.Content.Length);
            Assert.IsType<AssemblyImageAccessResult<int>.Available>(image);
        }
    }

    [Fact]
    public async Task PlatformMember_ResolvesFrameworkMatchedVersionAndRealizesContentParticipants()
    {
        using var workspace = new InspectionWorkspace();
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(RuntimePack()),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(
            new PlatformListingHandler(
                "9.0.9",
                "10.0.0",
                RuntimePackVersion,
                "11.0.0"));

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform("runtime"),
                    ],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(Framework, loaded.Framework);
        Assert.Null(loaded.RuntimeIdentifier);
        Assert.Equal(2, loaded.Group.Participants.Length);
        Assert.All(
            loaded.Members,
            member =>
            {
                Assert.Null(member.Participant.Assembly.Path);
                var declared = Assert.IsType<
                    WorkspaceMemberCoordinate.PlatformMember>(
                    member.Declared);
                Assert.Equal("runtime", declared.Family);
                var realized = Assert.IsType<
                    RealizedMemberCoordinate.Platform>(
                    member.Realized);
                Assert.Equal("runtime", realized.Family);
                Assert.Equal(RuntimePackVersion, realized.Version);
                Assert.Equal(Producer(NuGetOrg), realized.Producer);
                Assert.Equal(Framework, realized.Framework);
                Assert.Null(realized.Assembly);

                var provenance = Assert.IsType<
                    AssemblyResolutionProvenance.PlatformAsset>(
                    member.Participant.Assembly.Provenance);
                Assert.Equal("runtime", provenance.Framework);
                Assert.Equal(
                    RuntimePackVersion,
                    provenance.FrameworkVersion);
            });

        AssemblyContextParticipant caller =
            Participant(loaded, CallerPath);
        AssemblyContextParticipant target =
            Participant(loaded, TargetPath);
        AssemblyBindingSelection selection = caller.BindingPolicy.Select(
            new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(target.Assembly.Identity),
                AssemblyBindingOrigin.FromAssembly(caller.Assembly),
                AssemblyResolutionScope.Platform)).Selection;
        Assert.Same(
            target.Assembly,
            Assert.IsType<AssemblyBindingSelection.Selected>(selection)
                .Assembly);

        InspectionGraphPackageBoundary boundary =
            InspectionGraphPackageBoundary.Create(loaded);
        Assert.All(
            loaded.Members,
            member => Assert.False(
                boundary.TryGetPackageSubject(
                    member.Participant.Assembly.Registration,
                    out _)));
    }

    [Fact]
    public async Task PlatformMember_AssemblyFilterUsesMetadataIdentity()
    {
        string assemblyName = Path.GetFileNameWithoutExtension(CallerPath);
        using var workspace = new InspectionWorkspace();
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(
                Archive(
                    ($"runtimes/linux-x64/lib/{Framework}/Misleading.dll",
                        File.ReadAllBytes(CallerPath)),
                    ("runtimes/linux-x64/lib/net9.0/Unrelated.dll",
                        File.ReadAllBytes(TargetPath)))),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            assemblyName,
                            RuntimePackVersion),
                    ],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        WorkspaceContextMember member = Assert.Single(loaded.Members);
        Assert.Equal(
            assemblyName,
            member.Participant.Assembly.Identity.Name);
        Assert.Equal(
            assemblyName,
            Assert.IsType<RealizedMemberCoordinate.Platform>(
                member.Realized).Assembly);
        Assert.Equal(
            assemblyName,
            Assert.Single(loaded.AvailablePlatformAssemblies).Assembly);
        _ = InspectionGraphPackageBoundary.Create(loaded);
    }

    [Fact]
    public async Task PlatformMember_DuplicateSimpleNameFailsTyped()
    {
        string assemblyName = Path.GetFileNameWithoutExtension(TargetPath);
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(
                Archive(
                    ($"runtimes/linux-x64/lib/{Framework}/v1.dll",
                        File.ReadAllBytes(TargetPath)),
                    ($"runtimes/linux-x64/lib/{Framework}/v2.dll",
                        File.ReadAllBytes(TargetV2Path)))),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var failed = Failed(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            assemblyName,
                            RuntimePackVersion),
                    ],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        WorkspaceContextLoadFailure failure =
            Assert.Single(failed.Failures);
        Assert.Equal(
            WorkspaceContextLoadFailureKind.PlatformAssemblyAmbiguous,
            failure.Kind);
        Assert.Equal(
            assemblyName,
            Assert.IsType<WorkspaceMemberCoordinate.PlatformMember>(
                failure.Member).Assembly);
    }

    [Fact]
    public async Task PlatformMembers_SameFamilyAndVersionSelectDifferentAssemblies()
    {
        string caller = Path.GetFileNameWithoutExtension(CallerPath);
        string target = Path.GetFileNameWithoutExtension(TargetPath);
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(RuntimePack()),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            caller,
                            RuntimePackVersion),
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            target,
                            RuntimePackVersion),
                    ],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            [caller, target],
            loaded.Group.Participants
                .Select(participant => participant.Assembly.Identity.Name)
                .Order(StringComparer.Ordinal));
        Assert.All(
            loaded.Members,
            member => Assert.Equal(
                RuntimePackVersion,
                Assert.IsType<RealizedMemberCoordinate.Platform>(
                    member.Realized).Version));
    }

    [Fact]
    public async Task PlatformMembers_SameFamilyAtDifferentVersionsFailBeforeHostCapabilities()
    {
        var authorization = new RecordingAuthorization();
        var store = new CountingPackageStore(
            new InMemoryPackageStore());
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var failed = Failed(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            Path.GetFileNameWithoutExtension(CallerPath),
                            RuntimePackVersion),
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            Path.GetFileNameWithoutExtension(TargetPath),
                            "10.0.3"),
                    ],
                },
                Options(
                    client,
                    store,
                    sourceAuthorization: authorization),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceContextLoadFailureKind.InvalidCoordinate,
            Assert.Single(failed.Failures).Kind);
        Assert.Equal(0, authorization.Requests);
        Assert.Equal(0, store.Interactions);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task FloatingPlatformMembers_SameFamilyCannotDriftAcrossListings()
    {
        const string nextVersion = "10.0.3";
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(RuntimePack()),
            TestContext.Current.CancellationToken);
        await store.CommitAsync(
            RuntimePackPackageId,
            nextVersion,
            Producer(NuGetOrg),
            new MemoryStream(RuntimePack()),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(
            new AlternatingPlatformListingHandler(
                RuntimePackVersion,
                nextVersion));
        using var workspace = new InspectionWorkspace();

        var failed = Failed(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            Path.GetFileNameWithoutExtension(CallerPath)),
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            Path.GetFileNameWithoutExtension(TargetPath)),
                    ],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceContextLoadFailureKind.InvalidCoordinate,
            Assert.Single(failed.Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task PlatformMembers_AllAndSelectedAsteriskFailBeforeHostCapabilities()
    {
        var authorization = new RecordingAuthorization();
        var store = new CountingPackageStore(
            new InMemoryPackageStore());
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var failed = Failed(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            version: RuntimePackVersion),
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            assembly: "*",
                            version: RuntimePackVersion),
                    ],
                },
                Options(
                    client,
                    store,
                    sourceAuthorization: authorization),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceContextLoadFailureKind.InvalidCoordinate,
            Assert.Single(failed.Failures).Kind);
        Assert.Equal(0, authorization.Requests);
        Assert.Equal(0, store.Interactions);
    }

    [Fact]
    public async Task PlatformMember_PlatformQualifiedTargetUsesBaseReleaseLine()
    {
        const string platformFramework = "net10.0-browser";
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(RuntimePack()),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = platformFramework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            version: RuntimePackVersion),
                    ],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(platformFramework, loaded.Framework);
        Assert.All(
            loaded.Members,
            member => Assert.Equal(
                platformFramework,
                Assert.IsType<RealizedMemberCoordinate.Platform>(
                    member.Realized).Framework));
    }

    [Fact]
    public async Task FloatingPlatformMember_AcquiresOnlyFromVersionReporters()
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(FeedB),
            new MemoryStream(RuntimePack()),
            TestContext.Current.CancellationToken);
        var handler = new PerFeedHandler();
        handler.List(
            FeedA,
            RuntimePackPackageId,
            RuntimePackVersion);
        handler.List(
            FeedB,
            RuntimePackPackageId,
            "9.0.9");
        using var client = new HttpClient(handler);
        using var workspace = new InspectionWorkspace();
        var authorization = new PerPackageAuthorization
        {
            [RuntimePackPackageId] = [FeedA, FeedB],
        };

        var failed = Failed(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform("runtime"),
                    ],
                },
                Options(
                    client,
                    store,
                    sourceAuthorization: authorization),
                TestContext.Current.CancellationToken));

        WorkspaceContextLoadFailure failure =
            Assert.Single(failed.Failures);
        Assert.Equal(
            WorkspaceContextLoadFailureKind.PlatformPackUnavailable,
            failure.Kind);
        Assert.DoesNotContain(
            RuntimePackPackageId,
            failure.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "package",
            failure.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FloatingPlatformMember_HttpSourceFailureIsUnavailable(
        bool serviceIndexFailure)
    {
        var handler = new PerFeedHandler();
        handler.List(
            FeedA,
            RuntimePackPackageId,
            RuntimePackVersion);
        if (serviceIndexFailure)
            handler.FailServiceIndex(FeedB);
        else
            handler.FailListing(FeedB, RuntimePackPackageId);
        using var client = new HttpClient(handler);
        var store = new CountingPackageStore(
            new InMemoryPackageStore());
        using var workspace = new InspectionWorkspace();
        var authorization = new PerPackageAuthorization
        {
            [RuntimePackPackageId] = [FeedA, FeedB],
        };

        var failed = Failed(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform("runtime"),
                    ],
                },
                Options(
                    client,
                    store,
                    sourceAuthorization: authorization),
                TestContext.Current.CancellationToken));

        WorkspaceContextLoadFailure failure =
            Assert.Single(failed.Failures);
        Assert.Equal(
            WorkspaceContextLoadFailureKind.PlatformPackUnavailable,
            failure.Kind);
        Assert.DoesNotContain(
            "package",
            failure.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.Interactions);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task FloatingPlatformMember_MixedMalformedCriticalResourceIsUnavailable()
    {
        var handler = new PerFeedHandler();
        handler.List(
            FeedA,
            RuntimePackPackageId,
            RuntimePackVersion);
        handler.AddMalformedFlatContainerSibling(FeedB);
        using var client = new HttpClient(handler);
        var store = new CountingPackageStore(
            new InMemoryPackageStore());
        using var workspace = new InspectionWorkspace();
        var authorization = new PerPackageAuthorization
        {
            [RuntimePackPackageId] = [FeedA, FeedB],
        };

        var failed = Failed(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform("runtime"),
                    ],
                },
                Options(
                    client,
                    store,
                    sourceAuthorization: authorization),
                TestContext.Current.CancellationToken));

        WorkspaceContextLoadFailure failure =
            Assert.Single(failed.Failures);
        Assert.Equal(
            WorkspaceContextLoadFailureKind.PlatformPackUnavailable,
            failure.Kind);
        Assert.Equal(0, store.Interactions);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task FloatingPlatformMember_AuthoritativeAbsenceDoesNotHideReporter()
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(FeedA),
            new MemoryStream(RuntimePack()),
            TestContext.Current.CancellationToken);
        var handler = new PerFeedHandler();
        handler.List(
            FeedA,
            RuntimePackPackageId,
            RuntimePackVersion);
        using var client = new HttpClient(handler);
        using var workspace = new InspectionWorkspace();
        var authorization = new PerPackageAuthorization
        {
            [RuntimePackPackageId] = [FeedA, FeedB],
        };

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform("runtime"),
                    ],
                },
                Options(
                    client,
                    store,
                    sourceAuthorization: authorization),
                TestContext.Current.CancellationToken));

        Assert.All(
            loaded.Members,
            member =>
            {
                var realized =
                    Assert.IsType<RealizedMemberCoordinate.Platform>(
                        member.Realized);
                Assert.Equal(RuntimePackVersion, realized.Version);
                Assert.Equal(Producer(FeedA), realized.Producer);
            });
    }

    [Fact]
    public async Task PlatformFamilies_FormOneBindingConsistentGroup()
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(RuntimePack()),
            TestContext.Current.CancellationToken);
        await store.CommitAsync(
            AspNetCorePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(
                Archive(
                    ($"runtimes/linux-x64/lib/{Framework}/{Path.GetFileName(EmbeddedPath)}",
                        File.ReadAllBytes(EmbeddedPath)))),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            version: RuntimePackVersion),
                        WorkspaceMemberCoordinate.Platform(
                            "aspnetcore",
                            version: RuntimePackVersion),
                    ],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(3, loaded.Group.Participants.Length);
        Assert.All(
            loaded.Group.Participants,
            participant => Assert.Same(
                loaded.Group.BindingPolicyVersion,
                participant.BindingPolicy.Version));
        Assert.Equal(
            ["aspnetcore", "runtime"],
            loaded.Members
                .Select(member =>
                    Assert.IsType<
                        AssemblyResolutionProvenance.PlatformAsset>(
                            member.Participant.Assembly.Provenance)
                        .Framework)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task PlatformMember_MismatchedExactVersionFailsBeforeHostCapabilities()
    {
        var authorization = new RecordingAuthorization();
        var store = new CountingPackageStore(
            new InMemoryPackageStore());
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var failed = Failed(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            version: "9.0.9"),
                    ],
                },
                Options(
                    client,
                    store,
                    sourceAuthorization: authorization),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceContextLoadFailureKind.PlatformPackUnavailable,
            Assert.Single(failed.Failures).Kind);
        Assert.Equal(0, authorization.Requests);
        Assert.Equal(0, store.Interactions);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task PlatformMember_WithNoVersionOnTargetLineFailsTyped()
    {
        using var client = new HttpClient(
            new PlatformListingHandler("9.0.9", "11.0.0"));
        using var workspace = new InspectionWorkspace();

        var failed = Failed(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform("runtime"),
                    ],
                },
                Options(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceContextLoadFailureKind.PlatformPackUnavailable,
            Assert.Single(failed.Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task RealizedPlatformCoordinate_ReacquiresRecordedProducer()
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(RuntimePack()),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        using var firstWorkspace = new InspectionWorkspace();
        var first = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                firstWorkspace,
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
                Options(client, store),
                TestContext.Current.CancellationToken));
        RealizedMemberCoordinate[] coordinates =
        [
            .. first.Members.Select(member => member.Realized),
        ];

        using var secondWorkspace = new InspectionWorkspace();
        var second = Loaded(
            await WorkspaceContextLoader.LoadRealizedAsync(
                secondWorkspace,
                coordinates,
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(2, second.Group.Participants.Length);
        Assert.All(
            second.Members,
            member => Assert.IsType<
                AssemblyResolutionProvenance.PlatformAsset>(
                    member.Participant.Assembly.Provenance));
        Assert.Equal(
            coordinates[0],
            second.Members[0].Realized);
    }

    [Fact]
    public async Task RealizedPlatformCoordinates_ScanSharedPackOnce()
    {
        var inner = new InMemoryPackageStore();
        await inner.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(RuntimePack()),
            TestContext.Current.CancellationToken);
        var store = new EntryCountingPackageStore(inner);
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [
                    new RealizedMemberCoordinate.Platform(
                        "runtime",
                        RuntimePackVersion,
                        Producer(NuGetOrg),
                        Framework,
                        Path.GetFileNameWithoutExtension(CallerPath)),
                    new RealizedMemberCoordinate.Platform(
                        "runtime",
                        RuntimePackVersion,
                        Producer(NuGetOrg),
                        Framework,
                        Path.GetFileNameWithoutExtension(TargetPath)),
                ],
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(2, loaded.Members.Length);
        Assert.All(
            loaded.Members,
            member => Assert.Equal(
                Assert.IsType<RealizedMemberCoordinate.Platform>(
                    member.Realized).Assembly,
                member.Participant.Assembly.Identity.Name));
        Assert.Equal(2, store.EntryOpens);
    }

    [Fact]
    public async Task RealizedPlatformCoordinates_ReportTheMissingSelectedAssembly()
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(RuntimePack()),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var failed = Failed(
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [
                    new RealizedMemberCoordinate.Platform(
                        "runtime",
                        RuntimePackVersion,
                        Producer(NuGetOrg),
                        Framework,
                        Path.GetFileNameWithoutExtension(CallerPath)),
                    new RealizedMemberCoordinate.Platform(
                        "runtime",
                        RuntimePackVersion,
                        Producer(NuGetOrg),
                        Framework,
                        "Missing.Platform.Assembly"),
                ],
                Options(client, store),
                TestContext.Current.CancellationToken));

        WorkspaceContextLoadFailure failure =
            Assert.Single(failed.Failures);
        Assert.Equal(
            WorkspaceContextLoadFailureKind.PlatformAssemblyUnavailable,
            failure.Kind);
        Assert.Equal(
            "Missing.Platform.Assembly",
            Assert.IsType<WorkspaceMemberCoordinate.PlatformMember>(
                failure.Member).Assembly);
        Assert.Contains("Missing.Platform.Assembly", failure.Message);
    }

    [Fact]
    public async Task RealizedPlatformCoordinate_WithUnauthorizedProducerFailsTyped()
    {
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var failed = Failed(
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [
                    new RealizedMemberCoordinate.Platform(
                        "runtime",
                        RuntimePackVersion,
                        Producer(NuGetOrg),
                        Framework,
                        assembly: null),
                ],
                Options(
                    client,
                    new InMemoryPackageStore(),
                    sourceAuthorization: new PerPackageAuthorization()),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceContextLoadFailureKind.PlatformProducerUnavailable,
            Assert.Single(failed.Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task RealizedPlatformCoordinates_SameFamilyAtDifferentVersionsFailBeforeHostCapabilities()
    {
        var authorization = new RecordingAuthorization();
        var store = new CountingPackageStore(
            new InMemoryPackageStore());
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var failed = Failed(
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [
                    new RealizedMemberCoordinate.Platform(
                        "runtime",
                        RuntimePackVersion,
                        Producer(NuGetOrg),
                        Framework,
                        Path.GetFileNameWithoutExtension(CallerPath)),
                    new RealizedMemberCoordinate.Platform(
                        "runtime",
                        "10.0.3",
                        Producer(NuGetOrg),
                        Framework,
                        Path.GetFileNameWithoutExtension(TargetPath)),
                ],
                Options(
                    client,
                    store,
                    sourceAuthorization: authorization),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceContextLoadFailureKind.InvalidCoordinate,
            Assert.Single(failed.Failures).Kind);
        Assert.Equal(0, authorization.Requests);
        Assert.Equal(0, store.Interactions);
    }

    [Fact]
    public async Task PlatformMember_UnsupportedTargetFailsBeforeHostCapabilities()
    {
        var authorization = new RecordingAuthorization();
        var store = new CountingPackageStore(
            new InMemoryPackageStore());
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var failed = Failed(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = "netstandard2.1",
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform("runtime"),
                    ],
                },
                Options(
                    client,
                    store,
                    sourceAuthorization: authorization),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceContextLoadFailureKind.InvalidCoordinate,
            Assert.Single(failed.Failures).Kind);
        Assert.Equal(0, authorization.Requests);
        Assert.Equal(0, store.Interactions);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task PackageBoundary_ProjectsLoadedPackageAsGroupAndNode()
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
        using var workspace = new InspectionWorkspace();
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
        var transferPolicy = new RecordingTransferPolicy();
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

    [Fact]
    public async Task PlatformAcquisition_ForwardsTransferPolicyForDeclaredAndRealizedCoordinates()
    {
        byte[] nupkg = RuntimePack();
        var declaredPolicy = new RecordingTransferPolicy();
        using var declaredWorkspace = new InspectionWorkspace();
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
        using var realizedWorkspace = new InspectionWorkspace();
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
    [InlineData("10.*", null, "version")]
    [InlineData(null, "net10..0", "target framework")]
    public async Task InvalidPlatformCoordinate_UsesPlatformDiagnostic(
        string? version,
        string? memberFramework,
        string expectedRule)
    {
        var logs = new List<string>();
        var authorization = new RecordingAuthorization();
        var store = new CountingPackageStore(
            new InMemoryPackageStore());
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var failed = Failed(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            version: version,
                            framework: memberFramework),
                    ],
                },
                Options(
                    client,
                    store,
                    sourceAuthorization: authorization,
                    log: logs.Add),
                TestContext.Current.CancellationToken));

        WorkspaceContextLoadFailure failure = Assert.Single(
            failed.Failures.Where(candidate =>
                candidate.Kind
                    == WorkspaceContextLoadFailureKind.InvalidCoordinate
                && candidate.Message.Contains(
                    "platform member",
                    StringComparison.Ordinal)));
        Assert.Contains(
            expectedRule,
            failure.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "package",
            failure.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            logs,
            message => message.Contains(
                "package coordinate",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, authorization.Requests);
        Assert.Equal(0, store.Interactions);
        Assert.Equal(0, GroupCount(workspace));
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
                ("lib/net10.0/Native.dll", CreateNoMetadataImage()),
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
    public async Task UnsupportedPackageAsset_FailsTheMemberBesideHealthyAssets()
    {
        // A workspace member is not a scan and may not present partial rows,
        // so a rejected asset denies the whole member even when a healthy
        // assembly sits beside it. Pinning the blast radius in both directions
        // is the point: the single-asset gates cannot distinguish scoping from
        // non-scoping.
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            Archive(
                ("lib/net10.0/Unsupported.dll",
                    CreateUnsupportedMetadataImage()),
                ($"lib/net10.0/{Path.GetFileName(TargetPath)}",
                    File.ReadAllBytes(TargetPath))));
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadFailure failure = Assert.Single(
            Failed(
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    new WorkspaceContextInput
                    {
                        Framework = Framework,
                        Members = [PackageMember(Version)],
                    },
                    Options(client, store),
                    TestContext.Current.CancellationToken))
                .Failures);

        Assert.Equal(
            "UnsupportedMetadataFormat",
            failure.Kind.ToString());
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task MalformedPackageAsset_FailsTheMemberBesideHealthyAssets()
    {
        // The base swallowed this inside CreateFromStreamIfManaged and loaded
        // the member as though the package were intact. That success-shaped
        // skip is what this contract removes, so the change is pinned here.
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            Archive(
                ("lib/net10.0/Malformed.dll",
                    CreateMalformedMetadataRootImage()),
                ($"lib/net10.0/{Path.GetFileName(TargetPath)}",
                    File.ReadAllBytes(TargetPath))));
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadFailure failure = Assert.Single(
            Failed(
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    new WorkspaceContextInput
                    {
                        Framework = Framework,
                        Members = [PackageMember(Version)],
                    },
                    Options(client, store),
                    TestContext.Current.CancellationToken))
                .Failures);

        Assert.Equal(
            "MalformedMetadataRoot",
            failure.Kind.ToString());
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task UnsupportedPackageAsset_CreatesTypedFailure()
    {
        byte[] unsupported = CreateUnsupportedMetadataImage();
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            Archive(("lib/net10.0/Unsupported.dll", unsupported)));
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadFailure failure = Assert.Single(
            Failed(
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    new WorkspaceContextInput
                    {
                        Framework = Framework,
                        Members = [PackageMember(Version)],
                    },
                    Options(client, store),
                    TestContext.Current.CancellationToken))
                .Failures);

        Assert.Equal(
            "UnsupportedMetadataFormat",
            failure.Kind.ToString());
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task MalformedPackageAsset_PreservesExactReason()
    {
        byte[] malformed = CreateMalformedMetadataRootImage();
        using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(
            Version,
            Archive(("lib/net10.0/Malformed.dll", malformed)));
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadFailure failure = Assert.Single(
            Failed(
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    new WorkspaceContextInput
                    {
                        Framework = Framework,
                        Members = [PackageMember(Version)],
                    },
                    Options(client, store),
                    TestContext.Current.CancellationToken))
                .Failures);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.MalformedMetadataRoot,
            failure.Kind);
        Assert.Equal(
            MetadataRootMalformedReason.UnmappableMetadataDirectory,
            failure.MetadataRootReason);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task UnsupportedPlatformAsset_CreatesTypedFailure()
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(
                Archive(
                    ("runtimes/linux-x64/lib/net10.0/Unsupported.dll",
                        CreateUnsupportedMetadataImage()))),
            TestContext.Current.CancellationToken);
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(
            new PlatformListingHandler(RuntimePackVersion));

        WorkspaceContextLoadFailure failure = Assert.Single(
            Failed(
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    new WorkspaceContextInput
                    {
                        Framework = Framework,
                        Members =
                        [
                            WorkspaceMemberCoordinate.Platform("runtime"),
                        ],
                    },
                    Options(client, store),
                    TestContext.Current.CancellationToken))
                .Failures);

        Assert.Equal(
            "UnsupportedMetadataFormat",
            failure.Kind.ToString());
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task MalformedPlatformAsset_PreservesExactReason()
    {
        byte[] malformed = CreateMalformedMetadataRootImage();
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            RuntimePackPackageId,
            RuntimePackVersion,
            Producer(NuGetOrg),
            new MemoryStream(
                Archive(
                    ("runtimes/linux-x64/lib/net10.0/Malformed.dll",
                        malformed))),
            TestContext.Current.CancellationToken);
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(
            new PlatformListingHandler(RuntimePackVersion));

        WorkspaceContextLoadFailure failure = Assert.Single(
            Failed(
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    new WorkspaceContextInput
                    {
                        Framework = Framework,
                        Members =
                        [
                            WorkspaceMemberCoordinate.Platform("runtime"),
                        ],
                    },
                    Options(client, store),
                    TestContext.Current.CancellationToken))
                .Failures);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.MalformedMetadataRoot,
            failure.Kind);
        Assert.Equal(
            MetadataRootMalformedReason.UnmappableMetadataDirectory,
            failure.MetadataRootReason);
        Assert.Equal(0, GroupCount(workspace));
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
        byte[] malformed = CreateMalformedMetadataRootImage();
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

        WorkspaceContextLoadFailure failure =
            Assert.Single(Failed(outcome).Failures);
        Assert.Equal(
            WorkspaceContextLoadFailureKind.MalformedMetadataRoot,
            failure.Kind);
        Assert.Equal(
            MetadataRootMalformedReason.UnmappableMetadataDirectory,
            failure.MetadataRootReason);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task UnsupportedEmbeddedContent_CreatesTypedFailure()
    {
        byte[] unsupported = CreateUnsupportedMetadataImage();
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());

        WorkspaceContextLoadFailure failure = Assert.Single(
            Failed(
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    new WorkspaceContextInput
                    {
                        Members =
                        [
                            WorkspaceMemberCoordinate.Embedded(
                                "bundle/unsupported.dll",
                                Digest(unsupported),
                                "Unsupported"),
                        ],
                    },
                    Options(
                        client,
                        new InMemoryPackageStore(),
                        new StubEmbeddedContent(unsupported)),
                    TestContext.Current.CancellationToken))
                .Failures);

        Assert.Equal(
            "UnsupportedMetadataFormat",
            failure.Kind.ToString());
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

    /// <summary>
    /// The realized coordinate is composed after acquisition, from an id and
    /// version the resolver produced. Holding those to the framework and
    /// runtime-identifier grammar rejected real package ids — every id with an
    /// underscore — after the payload had already been committed.
    /// </summary>
    [Fact]
    public async Task PackageMember_WithAnUnderscoreId_RealizesAfterAcquisition()
    {
        const string underscoreId = "sqlitepclraw.bundle_e_sqlite3";
        var handler = new PerFeedHandler();
        handler.Serve(FeedA, underscoreId, Version, TargetPackage());
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
                            underscoreId,
                            Version),
                    ],
                },
                Options(
                    client,
                    new InMemoryPackageStore(),
                    sourceAuthorization:
                        new UniformPackageSourceAuthorization([FeedA])),
                TestContext.Current.CancellationToken));

        var realized = Assert.IsType<RealizedMemberCoordinate.Package>(
            loaded.Members[0].Realized);
        Assert.Equal(underscoreId, realized.PackageId);
        Assert.Equal(Version, realized.Version);
    }

    /// <summary>
    /// A prerelease label may begin, end, or consist of hyphens. A feed can
    /// select such a version for a floating member, so the realized coordinate
    /// has to be able to name it — the moniker grammar could not, and threw
    /// after the payload was acquired and committed.
    /// </summary>
    [Theory]
    [InlineData("1.0.0--beta")]
    [InlineData("1.0.0-beta-")]
    [InlineData("1.0.0---")]
    public async Task FloatingMember_SelectingAHyphenRichPrerelease_Realizes(
        string selected)
    {
        byte[] nupkg = TargetPackage();
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(
            new ListingHandler(nupkg, listedVersion: selected));

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
                Options(client, new InMemoryPackageStore()) with
                {
                    IncludePrerelease = true,
                },
                TestContext.Current.CancellationToken));

        var realized = Assert.IsType<RealizedMemberCoordinate.Package>(
            loaded.Members[0].Realized);
        Assert.Equal(selected, realized.Version);
        Assert.Single(loaded.Group.Participants);
    }

    [Fact]
    public void RealizedCoordinate_AcceptsRealPackageIdentitiesAndVersions()
    {
        Assert.True(
            RealizedMemberCoordinate.IsCanonicalPackageIdentity(
                "sqlitepclraw.bundle_e_sqlite3"));
        Assert.True(
            RealizedMemberCoordinate.IsCanonicalPackageIdentity("a_b-c.d"));
        Assert.False(
            RealizedMemberCoordinate.IsCanonicalPackageIdentity(
                "SQLitePCLRaw.bundle_e_sqlite3"));
        Assert.False(
            RealizedMemberCoordinate.IsCanonicalPackageIdentity("../../admin"));

        foreach (string version in
            new[] { "1.0.0--beta", "1.0.0-beta-", "1.0.0---", "1.0.0-rc.1" })
        {
            Assert.True(
                RealizedMemberCoordinate.IsCanonicalPackageVersion(version),
                version);
        }

        foreach (string version in
            new[] { "1.0", "1.0.0.0", "1.0.0-BETA", "1.0.0+build", "latest", "" })
        {
            Assert.False(
                RealizedMemberCoordinate.IsCanonicalPackageVersion(version),
                version);
        }
    }

    /// <summary>
    /// Two casings of one framework are one target, so they must realize one
    /// coordinate. Carrying the declared spelling forward made them transport
    /// as different identities and handed the asset selector a moniker its
    /// ordinal framework parser does not recognize.
    /// </summary>
    [Fact]
    public async Task EquivalentFrameworkCasing_RealizesEqualCoordinates()
    {
        IPackageStore store = await CachedStoreAsync(Version, LibraryPackage());
        using var client = new HttpClient(new FailingHandler());

        RealizedMemberCoordinate lower = await RealizeAsync("net10.0");
        RealizedMemberCoordinate upper = await RealizeAsync("NET10.0");

        Assert.Equal(lower, upper);
        Assert.Equal(
            "net10.0",
            Assert.IsType<RealizedMemberCoordinate.Package>(upper).Framework);

        async Task<RealizedMemberCoordinate> RealizeAsync(string framework)
        {
            using var workspace = new InspectionWorkspace();
            var loaded = Loaded(
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    new WorkspaceContextInput
                    {
                        Framework = framework,
                        Members = [PackageMember(Version)],
                    },
                    Options(client, store),
                    TestContext.Current.CancellationToken));
            Assert.Equal("net10.0", loaded.Framework);
            return loaded.Members[0].Realized;
        }
    }

    /// <summary>
    /// A runtime identifier is matched ordinally against a package's own
    /// folder names, so a non-canonical casing is refused rather than folded —
    /// and refused before any authorization, source, store, or network work,
    /// not after acquiring a payload that then matches nothing.
    /// </summary>
    [Theory]
    [InlineData("Browser-Wasm", null)]
    [InlineData(null, "LINUX-X64")]
    public async Task NonCanonicalRuntimeIdentifier_IsRejectedBeforeAnyAcquisition(
        string? contextRid,
        string? memberRid)
    {
        using var client = new HttpClient(new FailingHandler());
        var store = new CountingPackageStore(new InMemoryPackageStore());
        var authorization = new RecordingAuthorization();
        using var workspace = new InspectionWorkspace();

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    RuntimeIdentifier = contextRid,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Package(
                            PackageId,
                            Version,
                            runtimeIdentifier: memberRid),
                    ],
                },
                Options(client, store, sourceAuthorization: authorization),
                TestContext.Current.CancellationToken);

        Assert.Contains(
            Failed(outcome).Failures,
            failure => failure.Kind
                == WorkspaceContextLoadFailureKind.InvalidCoordinate);
        Assert.Equal(0, GroupCount(workspace));
        Assert.Equal(0, store.Interactions);
        Assert.Equal(0, authorization.Requests);
    }

    /// <summary>
    /// A bidirectional override is not a control character, so a
    /// control-only grammar admitted it into an embedded coordinate and then
    /// into the typed failure that reports the coordinate as unusable.
    /// </summary>
    [Theory]
    [InlineData("assets/\u202eevil", "Sample")]
    [InlineData("assets/\u200bhidden", "Sample")]
    [InlineData("assets/ok.dll", "Sam\u202eple")]
    [InlineData("assets/ok.dll", "Sam\u200dple")]
    public async Task EmbeddedCoordinateWithANonGraphicScalar_IsRejectedBeforeProviderAccess(
        string contentRef,
        string declaredName)
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        var provider = new StubEmbeddedContent(embedded);
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Embedded(
                            contentRef,
                            Digest(embedded),
                            declaredName),
                    ],
                },
                Options(client, new InMemoryPackageStore(), provider),
                TestContext.Current.CancellationToken);

        WorkspaceContextLoadFailure failure =
            Assert.Single(Failed(outcome).Failures);
        Assert.Equal(
            WorkspaceContextLoadFailureKind.InvalidCoordinate,
            failure.Kind);
        Assert.Equal(0, provider.OpenCount);
        Assert.Equal(0, GroupCount(workspace));
        Assert.DoesNotContain('\u202e', failure.Message);
        Assert.DoesNotContain('\u200b', failure.Message);
        Assert.DoesNotContain('\u200d', failure.Message);
    }

    [Fact]
    public void EmbeddedGrammars_KeepLegitimateSpellings()
    {
        Assert.True(
            RealizedMemberCoordinate.IsCanonicalContentRef(
                "bundle/lib/net10.0/Sample.dll"));
        Assert.True(
            RealizedMemberCoordinate.IsAssemblySimpleName("System.Text.Json"));
        Assert.True(
            RealizedMemberCoordinate.IsAssemblySimpleName("Ünïcødé.Løbrary"));
        Assert.False(
            RealizedMemberCoordinate.IsCanonicalContentRef("assets/\u202eevil"));
        Assert.False(
            RealizedMemberCoordinate.IsAssemblySimpleName("Sam\u202eple"));
    }

    /// <summary>
    /// Two identical declared members name one acquisition, so realizing both
    /// would put each of the package's assemblies in the group twice and make
    /// every in-context reference bind ambiguously.
    /// </summary>
    [Fact]
    public async Task DuplicateDeclaredMembers_RealizeOneGroup()
    {
        IPackageStore store = await CachedStoreAsync(Version, LibraryPackage());
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version), PackageMember(Version)],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(2, loaded.Group.Participants.Length);
        Assert.Equal(
            2,
            loaded.Group.Participants
                .Select(participant => participant.Assembly.Identity.Name)
                .Distinct(StringComparer.Ordinal)
                .Count());

        AssemblyContextParticipant caller = Participant(loaded, CallerPath);
        AssemblyContextParticipant target = Participant(loaded, TargetPath);
        Assert.IsType<AssemblyBindingSelection.Selected>(
            caller.BindingPolicy.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(target.Assembly.Identity),
                    AssemblyBindingOrigin.FromAssembly(caller.Assembly),
                    AssemblyResolutionScope.Any)).Selection);
    }

    [Fact]
    public async Task ConflictingDuplicateMembers_CreateNoGroup()
    {
        IPackageStore store = await CachedStoreAsync(Version, LibraryPackage());
        using var client = new HttpClient(new FailingHandler());
        var counting = new CountingPackageStore(store);
        using var workspace = new InspectionWorkspace();

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version), PackageMember("2.0.0")],
                },
                Options(client, counting),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.InvalidCoordinate,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
        Assert.Equal(0, counting.Interactions);
    }

    /// <summary>
    /// One acquisition named twice, in two spellings: a different id casing, an
    /// unnormalized version, and a target the first member inherits while the
    /// second repeats it in another casing. Comparing coordinate records
    /// rejected this as a conflict, which is the opposite of what it says.
    /// </summary>
    [Theory]
    [InlineData("1.0.0", null, null)]
    [InlineData("1.0", null, null)]
    [InlineData("1.0.0", "NET10.0", null)]
    [InlineData("1.0", "net10.0", null)]
    [InlineData("1.0.0.0", "NET10.0", null)]
    public async Task EquivalentDuplicateMembers_CollapseToOneAcquisition(
        string secondVersion,
        string? secondFramework,
        string? secondRid)
    {
        IPackageStore store = await CachedStoreAsync(Version, LibraryPackage());
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Package(PackageId, Version),
                        WorkspaceMemberCoordinate.Package(
                            PackageId.ToUpperInvariant(),
                            secondVersion,
                            secondFramework,
                            secondRid),
                    ],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(2, loaded.Group.Participants.Length);
        Assert.Single(
            loaded.Members
                .Select(member => member.Realized)
                .Distinct());
    }

    [Fact]
    public async Task EquivalentFloatingDuplicates_CollapseToOneAcquisition()
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
                        WorkspaceMemberCoordinate.Package(
                            PackageId.ToUpperInvariant(),
                            version: null,
                            framework: "NET10.0"),
                    ],
                },
                Options(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken));

        Assert.Equal(2, loaded.Group.Participants.Length);
        Assert.Single(
            loaded.Members.Select(member => member.Realized).Distinct());
    }

    /// <summary>
    /// The close negatives: coordinates that name genuinely different
    /// acquisitions of one subject still cannot both be realized.
    /// </summary>
    [Theory]
    [InlineData("2.0.0", null)]
    [InlineData(null, null)]
    [InlineData("1.0.0", "net8.0")]
    public async Task DifferentAcquisitionsOfOneSubject_CreateNoGroup(
        string? secondVersion,
        string? secondFramework)
    {
        IPackageStore store = await CachedStoreAsync(Version, LibraryPackage());
        using var client = new HttpClient(new FailingHandler());
        var counting = new CountingPackageStore(store);
        using var workspace = new InspectionWorkspace();

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Package(PackageId, Version),
                        WorkspaceMemberCoordinate.Package(
                            PackageId,
                            secondVersion,
                            secondFramework),
                    ],
                },
                Options(client, counting),
                TestContext.Current.CancellationToken);

        Assert.NotEmpty(Failed(outcome).Failures);
        Assert.Equal(0, GroupCount(workspace));
        Assert.Equal(0, counting.Interactions);
    }

    [Fact]
    public async Task RealizedDuplicatesFromDifferentProducers_CreateNoGroup()
    {
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadRealizedAsync(
                workspace,
                [
                    new RealizedMemberCoordinate.Package(
                        PackageId,
                        Version,
                        Producer(FeedA),
                        Framework,
                        runtimeIdentifier: null),
                    new RealizedMemberCoordinate.Package(
                        PackageId,
                        Version,
                        Producer(FeedB),
                        Framework,
                        runtimeIdentifier: null),
                ],
                Options(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.InvalidCoordinate,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task EmbeddedDuplicatesWithDifferentDigests_CreateNoGroup()
    {
        byte[] embedded = File.ReadAllBytes(EmbeddedPath);
        var provider = new StubEmbeddedContent(embedded);
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members =
                    [
                        EmbeddedMember(embedded),
                        WorkspaceMemberCoordinate.Embedded(
                            "bundle/lookalike.dll",
                            Digest(File.ReadAllBytes(TargetPath)),
                            Path.GetFileNameWithoutExtension(EmbeddedPath)),
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

    /// <summary>
    /// Two images can carry one identity without either coordinate being
    /// duplicated. The binding policy answers such a group with
    /// <c>Multiple</c> for every in-context reference, so the context is not
    /// loadable and no group is created.
    /// </summary>
    [Fact]
    public async Task DuplicateAssemblyIdentityInOnePackage_CreatesNoGroup()
    {
        byte[] target = File.ReadAllBytes(TargetPath);
        IPackageStore store = await CachedStoreAsync(
            Version,
            Archive(
                ($"lib/{Framework}/{Path.GetFileName(TargetPath)}", target),
                ($"lib/{Framework}/copies/{Path.GetFileName(TargetPath)}", target)));
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

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

        WorkspaceContextLoadFailure failure =
            Assert.Single(Failed(outcome).Failures);
        Assert.Equal(
            WorkspaceContextLoadFailureKind.ConflictingAssemblyIdentity,
            failure.Kind);
        Assert.Equal(0, GroupCount(workspace));
        Assert.DoesNotContain(
            Path.GetFileNameWithoutExtension(TargetPath),
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindingEquivalentAssemblyIdentityInOnePackage_CreatesNoGroup()
    {
        byte[] target = File.ReadAllBytes(TargetPath);
        string assemblyName =
            Path.GetFileNameWithoutExtension(TargetPath);
        byte[] equivalent = ReplaceAscii(
            target,
            assemblyName,
            assemblyName.ToUpperInvariant());
        IPackageStore store = await CachedStoreAsync(
            Version,
            Archive(
                ($"lib/{Framework}/original.dll", target),
                ($"lib/{Framework}/equivalent.dll", equivalent)));
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

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
            WorkspaceContextLoadFailureKind.ConflictingAssemblyIdentity,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task DuplicateAssemblyIdentityAcrossProducers_CreatesNoGroup()
    {
        var handler = new PerFeedHandler();
        handler.Serve(FeedA, "alpha.package", Version, TargetPackage());
        handler.Serve(FeedB, "bravo.package", Version, TargetPackage());
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
                        WorkspaceMemberCoordinate.Package("alpha.package", Version),
                        WorkspaceMemberCoordinate.Package("bravo.package", Version),
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
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.ConflictingAssemblyIdentity,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    /// <summary>
    /// The close positive: two versions of one library are two identities and
    /// coexist, and an exact reference binds to one descriptor.
    /// </summary>
    [Fact]
    public async Task DistinctAssemblyVersions_LoadAndBindExactly()
    {
        IPackageStore store = await CachedStoreAsync(
            Version,
            Archive(
                ($"lib/{Framework}/{Path.GetFileName(TargetPath)}",
                    File.ReadAllBytes(TargetPath)),
                ($"lib/{Framework}/v2/{Path.GetFileName(TargetPath)}",
                    File.ReadAllBytes(TargetV2Path))));
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

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

        Assert.Equal(2, loaded.Group.Participants.Length);
        Assert.Equal(
            [IdentityVersion(TargetPath), IdentityVersion(TargetV2Path)],
            loaded.Group.Participants
                .Select(participant => participant.Assembly.Identity.Version)
                .Order());

        AssemblyContextParticipant first = loaded.Group.Participants[0];
        foreach (AssemblyContextParticipant participant
            in loaded.Group.Participants)
        {
            AssemblyBindingSelection selection = first.BindingPolicy.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(
                        participant.Assembly.Identity),
                    AssemblyBindingOrigin.FromAssembly(first.Assembly),
                    AssemblyResolutionScope.Any)).Selection;
            Assert.Same(
                participant.Assembly,
                Assert.IsType<AssemblyBindingSelection.Selected>(selection)
                    .Assembly);
        }
    }

    /// <summary>
    /// A hostile asset folder whose framework text carries a non-ASCII sign
    /// parsed as a negative version and threw out of the loader, after the
    /// package had been committed. It is now an ordinary unusable folder.
    /// </summary>
    [Fact]
    public async Task PackageWithASignBearingFrameworkFolder_IsTypedUnavailable()
    {
        IPackageStore store = await CachedStoreAsync(
            Version,
            Archive(
                ($"lib/netstandard\u22121.0/{Path.GetFileName(TargetPath)}",
                    File.ReadAllBytes(TargetPath)),
                ($"lib/net-1.0/{Path.GetFileName(TargetPath)}",
                    File.ReadAllBytes(TargetPath))));
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

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

    /// <summary>
    /// The workspace contract end to end: a feed publishing only previews is
    /// resolvable by the CLI, whose shared version policy falls back to them,
    /// and is not resolvable by a context that did not ask for prereleases.
    /// </summary>
    [Fact]
    public async Task FloatingMember_WithOnlyPrereleases_CreatesNoGroup()
    {
        byte[] nupkg = TargetPackage();
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(
            new ListingHandler(nupkg, listedVersion: "9.0.0-preview.2"));

        WorkspaceContextLoadOutcome outcome =
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
                TestContext.Current.CancellationToken);

        Assert.Equal(
            WorkspaceContextLoadFailureKind.PackageUnavailable,
            Assert.Single(Failed(outcome).Failures).Kind);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task FloatingMember_WithOnlyPrereleases_LoadsWhenIncluded()
    {
        byte[] nupkg = TargetPackage();
        using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(
            new ListingHandler(nupkg, listedVersion: "9.0.0-preview.2"));

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
                Options(client, new InMemoryPackageStore()) with
                {
                    IncludePrerelease = true,
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(
            "9.0.0-preview.2",
            Assert.IsType<RealizedMemberCoordinate.Package>(
                loaded.Members[0].Realized).Version);
    }

    /// <summary>
    /// An exact pin names whatever version it names: the stable-only rule
    /// governs floating discovery, not pinning.
    /// </summary>
    [Fact]
    public async Task ExactPrereleasePin_LoadsWithoutTheFlag()
    {
        IPackageStore store = await CachedStoreAsync(
            "9.0.0-preview.2",
            TargetPackage());
        using var client = new HttpClient(new FailingHandler());
        using var workspace = new InspectionWorkspace();

        var loaded = Loaded(
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember("9.0.0-preview.2")],
                },
                Options(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            "9.0.0-preview.2",
            Assert.IsType<RealizedMemberCoordinate.Package>(
                loaded.Members[0].Realized).Version);
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

        var platform = new RealizedMemberCoordinate.Platform(
            "runtime",
            RuntimePackVersion,
            Producer(NuGetOrg),
            Framework,
            assembly: null);
        Assert.Equal(
            platform,
            new RealizedMemberCoordinate.Platform(
                "runtime",
                RuntimePackVersion,
                Producer(NuGetOrg),
                Framework,
                assembly: null));
        Assert.NotEqual(
            platform,
            new RealizedMemberCoordinate.Platform(
                "runtime",
                RuntimePackVersion,
                Producer(Private),
                Framework,
                assembly: null));
        Assert.Throws<ArgumentException>(
            () => new RealizedMemberCoordinate.Platform(
                "Runtime",
                RuntimePackVersion,
                Producer(NuGetOrg),
                Framework,
                assembly: null));
        Assert.Throws<ArgumentException>(
            () => new RealizedMemberCoordinate.Platform(
                "runtime",
                "9.0.9",
                Producer(NuGetOrg),
                Framework,
                assembly: null));
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
        PackagePayloadLimits? payloadLimits = null,
        Action<string>? log = null,
        IPackagePayloadTransferPolicy? packageTransferPolicy = null) =>
        new()
        {
            HttpClient = client,
            SourceAuthorization = sourceAuthorization
                ?? new UniformPackageSourceAuthorization([NuGetOrg]),
            PackageStore = store,
            PackageTransferPolicy = packageTransferPolicy,
            EmbeddedContent = embeddedContent,
            PayloadLimits = payloadLimits ?? PackagePayloadLimits.Default,
            Log = log,
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

    static byte[] RuntimePack() =>
        Archive(
            ($"runtimes/linux-x64/lib/{Framework}/{Path.GetFileName(CallerPath)}",
                File.ReadAllBytes(CallerPath)),
            ($"runtimes/linux-x64/lib/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetPath)));

    static byte[] CreateMalformedMetadataRootImage()
    {
        // A real PE image whose CLI metadata directory size is zeroed, so the
        // metadata root cannot be mapped. A non-PE byte string is not a
        // substitute: it has no metadata root at all and is classified as a
        // descriptor-less image well before admission runs.
        byte[] image = File.ReadAllBytes(TargetPath);
        int corHeaderStart;
        using (var peReader = new PEReader(ImmutableArray.Create(image)))
        {
            corHeaderStart = peReader.PEHeaders.CorHeaderStartOffset;
        }

        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(corHeaderStart + 12, sizeof(int)),
            0);
        return image;
    }

    static byte[] CreateNoMetadataImage()
    {
        byte[] image = File.ReadAllBytes(TargetPath);
        using var peReader = new PEReader(ImmutableArray.Create(image));
        PEHeader peHeader = peReader.PEHeaders.PEHeader!;
        int directoryBase =
            peReader.PEHeaders.PEHeaderStartOffset
            + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        image.AsSpan(directoryBase + (14 * 8), 8).Clear();
        return image;
    }

    static byte[] CreateUnsupportedMetadataImage()
    {
        const int fixedMetadataRootPrefixLength = 16;
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Unsupported.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Unsupported"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                "WindowsRuntime 1.4;CLR v4.0.30319",
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var imageBuilder = new BlobBuilder();
        peBuilder.Serialize(imageBuilder);
        byte[] image = imageBuilder.ToArray();
        using var peReader = new PEReader(ImmutableArray.Create(image));
        int metadataStart = peReader.PEHeaders.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(
                peReader.PEHeaders.CorHeaderStartOffset + 12,
                sizeof(int)),
            fixedMetadataRootPrefixLength + versionLength);
        return image;
    }

    static Version? IdentityVersion(string assemblyPath) =>
        ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local("fixture identity"))
            .Identity.Version;

    static byte[] ReplaceAscii(
        byte[] source,
        string oldValue,
        string newValue)
    {
        Assert.Equal(oldValue.Length, newValue.Length);
        byte[] result = [.. source];
        ReadOnlySpan<byte> oldBytes = Encoding.UTF8.GetBytes(oldValue);
        ReadOnlySpan<byte> newBytes = Encoding.UTF8.GetBytes(newValue);
        int replacements = 0;
        for (int offset = 0;
            offset <= result.Length - oldBytes.Length;)
        {
            int relative = result.AsSpan(offset).IndexOf(oldBytes);
            if (relative < 0)
                break;

            offset += relative;
            newBytes.CopyTo(result.AsSpan(offset, newBytes.Length));
            replacements++;
            offset += newBytes.Length;
        }

        Assert.NotEqual(0, replacements);
        return result;
    }

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

    static void AssertTransferPolicy(
        RecordingTransferPolicy policy,
        string packageId,
        string version)
    {
        Assert.Equal(packageId, policy.Transfer?.Coordinate.PackageId);
        Assert.Equal(version, policy.Transfer?.Coordinate.Version);
        Assert.True(policy.Completed);
        Assert.True(policy.Disposed);
    }

    sealed class PayloadHandler(
        byte[] nupkg,
        string version,
        string packageId = PackageId)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(
                request.RequestUri!.ToString().Equals(
                    $"https://api.nuget.org/v3-flatcontainer/{packageId}/{version}/{packageId}.{version}.nupkg",
                    StringComparison.OrdinalIgnoreCase)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nupkg),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    sealed class RecordingTransferPolicy : IPackagePayloadTransferPolicy
    {
        public PackagePayloadTransfer? Transfer { get; private set; }
        public bool Completed { get; private set; }
        public bool Disposed { get; private set; }

        public IPackagePayloadReservation Reserve(
            PackagePayloadTransfer transfer)
        {
            Transfer = transfer;
            return new Reservation(this);
        }

        sealed class Reservation(RecordingTransferPolicy owner)
            : IPackagePayloadReservation
        {
            public void Complete() => owner.Completed = true;

            public void Dispose() => owner.Disposed = true;
        }
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

    sealed class PlatformListingHandler(params string[] versions)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            string versionArray = string.Join(
                ",",
                versions.Select(version => $"\"{version}\""));
            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{RuntimePackPackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                return Json($"{{\"versions\":[{versionArray}]}}");
            }

            if (url.Equals(
                $"https://api.nuget.org/v3/registration5-gz-semver2/{RuntimePackPackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                string entries = string.Join(
                    ",",
                    versions.Select(
                        version =>
                            $"{{\"catalogEntry\":{{\"version\":\"{version}\",\"listed\":true}}}}"));
                return Json($"{{\"items\":[{{\"items\":[{entries}]}}]}}");
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

    sealed class AlternatingPlatformListingHandler(params string[] versions)
        : HttpMessageHandler
    {
        int _listing = -1;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{RuntimePackPackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                int listing = Math.Min(
                    Interlocked.Increment(ref _listing),
                    versions.Length - 1);
                return Json(
                    $"{{\"versions\":[\"{versions[listing]}\"]}}");
            }

            if (url.Equals(
                $"https://api.nuget.org/v3/registration5-gz-semver2/{RuntimePackPackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                string version = versions[
                    Math.Clamp(
                        Volatile.Read(ref _listing),
                        0,
                        versions.Length - 1)];
                return Json(
                    "{\"items\":[{\"items\":[{\"catalogEntry\":"
                    + $"{{\"version\":\"{version}\",\"listed\":true}}"
                    + "}]}]}");
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
        readonly Dictionary<string, string[]> _listings =
            new(StringComparer.Ordinal);
        readonly HashSet<string> _failedListings =
            new(StringComparer.Ordinal);
        readonly HashSet<string> _failedServiceIndexes =
            new(StringComparer.Ordinal);
        readonly HashSet<string> _withoutFlatContainer =
            new(StringComparer.Ordinal);
        readonly HashSet<string> _malformedFlatContainerSiblings =
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

        internal void List(
            PackageSource feed,
            string packageId,
            params string[] versions) =>
            _listings[
                $"{FlatContainer(feed)}{packageId}/index.json"] =
                versions;

        internal void FailListing(
            PackageSource feed,
            string packageId) =>
            _failedListings.Add(
                $"{FlatContainer(feed)}{packageId}/index.json");

        internal void FailServiceIndex(PackageSource feed) =>
            _failedServiceIndexes.Add(feed.Url);

        /// <summary>
        /// Answers this feed's service index without a flat-container resource,
        /// so its package resources cannot be discovered at all.
        /// </summary>
        internal void WithoutFlatContainer(PackageSource feed) =>
            _withoutFlatContainer.Add(feed.Url);

        internal void AddMalformedFlatContainerSibling(
            PackageSource feed) =>
            _malformedFlatContainerSiblings.Add(feed.Url);

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
                    if (_failedServiceIndexes.Contains(feed.Url))
                    {
                        return Task.FromResult(
                            new HttpResponseMessage(
                                HttpStatusCode.ServiceUnavailable));
                    }

                    string resources = _withoutFlatContainer.Contains(feed.Url)
                        ? ""
                        : $$"""{"@id":"{{FlatContainer(feed)}}","@type":"PackageBaseAddress/3.0.0"}""";
                    if (_malformedFlatContainerSiblings.Contains(feed.Url))
                    {
                        resources +=
                            """,{"@id":"not a url","@type":"PackageBaseAddress/3.0.0"}""";
                    }
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
                _failedListings.Contains(url)
                    ? new HttpResponseMessage(
                        HttpStatusCode.InternalServerError)
                    : _listings.TryGetValue(url, out string[]? versions)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $$"""
                            {"versions":[{{string.Join(
                                ",",
                                versions.Select(version => $"\"{version}\""))}}]}
                            """),
                    }
                    : _payloads.TryGetValue(url, out byte[]? nupkg)
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
    /// Records how many times authorization was consulted, so a test can prove
    /// a front-door rejection happened before any host policy was asked.
    /// </summary>
    sealed class RecordingAuthorization : IPackageSourceAuthorization
    {
        int _requests;

        internal int Requests => Volatile.Read(ref _requests);

        public PackageSourceAuthorization AuthorizeSourcesFor(string packageId)
        {
            Interlocked.Increment(ref _requests);
            return PackageSourceAuthorization.Authorize([NuGetOrg]);
        }
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

    sealed class EntryCountingPackageStore(IPackageStore inner) : IPackageStore
    {
        int _entryOpens;

        internal int EntryOpens => Volatile.Read(ref _entryOpens);

        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            inner.TryGetCached(
                packageName,
                version,
                allowedSourceKeys,
                log) is { } content
                ? new EntryCountingPackageContent(
                    content,
                    () => Interlocked.Increment(ref _entryOpens))
                : null;

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default) =>
            inner.CommitAsync(
                packageName,
                version,
                sourceKey,
                nupkg,
                cancellationToken);
    }

    sealed class EntryCountingPackageContent(
        IPackageContent inner,
        Action onEntryOpen) : IPackageContent, IPackageContentEntryManifest
    {
        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public bool RequiresArchiveTreeMatch =>
            inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive(
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Stream? stream) =>
            inner.TryOpenArchive(out stream);

        public bool TryOpenEntry(
            string relativePath,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Stream? stream)
        {
            onEntryOpen();
            return inner.TryOpenEntry(relativePath, out stream);
        }

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Stream? stream)
        {
            onEntryOpen();
            return inner.TryOpenEntry(
                relativePath,
                maxExpandedBytes,
                out stream);
        }

        public IEnumerable<string> EnumerateEntries() =>
            inner.EnumerateEntries();

        public bool TryGetEntryLength(
            string relativePath,
            out long length) =>
            ((IPackageContentEntryManifest)inner)
                .TryGetEntryLength(relativePath, out length);

        public IReadOnlyList<PackageContentEntry>
            EnumerateEntriesWithLengths() =>
            ((IPackageContentEntryManifest)inner)
                .EnumerateEntriesWithLengths();
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
