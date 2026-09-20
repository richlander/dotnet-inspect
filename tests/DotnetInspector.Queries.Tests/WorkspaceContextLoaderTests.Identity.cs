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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();

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

        var portablePackage = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            PackageProducerIdentity.NuGetOrg.PortableKey,
            Framework,
            runtimeIdentifier: null);
        Assert.Equal(
            PackageProducerIdentity.NuGetOrg.PortableKey,
            portablePackage.Producer);

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
        Assert.Throws<ArgumentException>(
            () => new RealizedMemberCoordinate.Platform(
                "runtime",
                RuntimePackVersion,
                PackageProducerIdentity.NuGetOrg.PortableKey,
                Framework,
                assembly: null));
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
        await using var workspace = new InspectionWorkspace();
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
}
