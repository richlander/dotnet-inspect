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
                Framework),
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
            Framework,
            "browser-wasm");
        var second = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            Framework,
            "browser-wasm");

        Assert.Equal(first, second);
        Assert.Throws<ArgumentException>(
            () => new RealizedMemberCoordinate.Package(
                PackageId,
                "1.0",
                Framework));
        Assert.Throws<ArgumentException>(
            () => new RealizedMemberCoordinate.Package(
                "Workspace.Sample",
                Version,
                Framework));
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
        IEmbeddedContentProvider? embeddedContent = null) =>
        new()
        {
            HttpClient = client,
            AuthorizedSources = [NuGetOrg],
            PackageStore = store,
            EmbeddedContent = embeddedContent,
        };

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

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }
}
