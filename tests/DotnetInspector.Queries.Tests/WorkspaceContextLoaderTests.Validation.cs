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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();
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
            await using var workspace = new InspectionWorkspace();
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
        await using var workspace = new InspectionWorkspace();

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
        await using var workspace = new InspectionWorkspace();

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
}
