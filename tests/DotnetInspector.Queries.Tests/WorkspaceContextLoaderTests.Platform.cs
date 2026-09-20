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
    public async Task PlatformMember_ResolvesFrameworkMatchedVersionAndRealizesContentParticipants()
    {
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var firstWorkspace = new InspectionWorkspace();
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

        await using var secondWorkspace = new InspectionWorkspace();
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
    public async Task RealizedPlatformCoordinates_OpenAndSealEachSelectedImage()
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
        await using var workspace = new InspectionWorkspace();

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
        // Each selected entry is opened once to establish its descriptor and
        // once to seal the immutable image published with the group.
        Assert.Equal(4, store.EntryOpens);
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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
}
