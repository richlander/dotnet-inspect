using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Host-neutral payload acquisition: a store answers first, only the
/// coordinate's authorized sources are consulted, and the producer that served
/// the bytes stays with them.
/// </summary>
public sealed class PackagePayloadAcquisitionTests
{
    static readonly PackageSource NuGetOrg = PackageSource.NuGetOrg;
    static readonly PackageSource Primary =
        new("primary", "https://primary.test/v3/index.json");

    const string PackageId = "sample.package";
    const string Version = "1.2.3";
    const string NupkgUrl =
        $"https://api.nuget.org/v3-flatcontainer/{PackageId}/{Version}/{PackageId}.{Version}.nupkg";

    [Fact]
    public void PackageContentGenerationIdentity_ExternalBuffersCannotMutateGeneration()
    {
        byte[] supplied =
            TestPackageArchive.Create("lib/net10.0/Sample.dll");
        byte[] expected = supplied.ToArray();
        var content = new InMemoryPackageContent(
            supplied,
            fromCache: false,
            producerKey: "tests");
        PackageContentGenerationIdentity identity =
            content.GenerationIdentity;

        Array.Fill<byte>(supplied, 0);
        Assert.Equal(expected, ReadArchive(content));

        ReadOnlyMemory<byte> exported = content.NupkgBytes;
        Assert.True(
            MemoryMarshal.TryGetArray(
                exported,
                out ArraySegment<byte> segment));
        Array.Fill<byte>(segment.Array!, 0);

        Assert.Equal(expected, ReadArchive(content));
        Assert.Same(identity, content.GenerationIdentity);
    }

    [Fact]
    public async Task CacheMiss_DownloadsAndCommitsWithProducerIdentity()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new NuGetOrgPayloadHandler(nupkg));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        AcquiredPackagePayload payload = Acquired(result);
        Assert.Equal(PackagePayloadOrigin.Download, payload.Origin);
        Assert.Equal(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            payload.ProducerKey);
        Assert.Equal(
            "lib/net10.0/Sample.dll",
            Assert.Single(payload.Content.EnumerateEntries()));
    }

    static byte[] ReadArchive(IPackageContent content)
    {
        Assert.True(content.TryOpenArchive(out Stream? archive));
        using (archive)
        using (var buffer = new MemoryStream())
        {
            archive.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    [Fact]
    public async Task CacheHit_AnswersWithoutNetworkWork()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            new MemoryStream(nupkg),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        AcquiredPackagePayload payload = Acquired(result);
        Assert.Equal(PackagePayloadOrigin.Cache, payload.Origin);
        Assert.Equal(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            payload.ProducerKey);
    }

    [Fact]
    public async Task TypedCacheHit_DoesNotEscapeExpiredOperationContext()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            new MemoryStream(nupkg),
            TestContext.Current.CancellationToken);
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(new FailingHandler());
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            OperationTimeout = TimeSpan.FromMilliseconds(20),
        };
        using var operation = new NuGetOperationContext(
            options.RequestTimeout,
            options.OperationTimeout,
            TestContext.Current.CancellationToken);
        await Task.Delay(
            TimeSpan.FromMilliseconds(40),
            TestContext.Current.CancellationToken);

        NuGetOperationTimeoutException error =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                () => PackagePayloadAcquisition.AcquireAsync(
                    source,
                    PackageSourceCoordinate.Create(PackageId, Version),
                    store,
                    cancellationToken:
                        TestContext.Current.CancellationToken,
                    operationContext: operation));

        Assert.Equal(options.OperationTimeout, error.Timeout);
    }

    [Fact]
    public async Task CacheHit_IsRevalidatedAgainstCurrentPayloadLimits()
    {
        byte[] nupkg = TestPackageArchive.Create(
            "lib/net10.0/One.dll",
            "lib/net10.0/Two.dll",
            "lib/net10.0/Three.dll");
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            new MemoryStream(nupkg),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new NotFoundHandler());

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                limits: new PackagePayloadLimits { MaxEntryCount = 2 },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
    }

    /// <summary>
    /// Product-owned app-cache slots require the retained archive for tree
    /// match. Stripping the nupkg must not fall through to walk-only admission.
    /// </summary>
    [Fact]
    public async Task CacheHitWithoutRetainedNupkg_IsNotServedAsProductOwned()
    {
        string cacheRoot = TempDirectory();
        string stagingRoot = TempDirectory();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            cacheRoot,
            skipNuGetCache: true);
        try
        {
            Directory.CreateDirectory(stagingRoot);
            string nupkg = Path.Combine(stagingRoot, "package.nupkg");
            File.WriteAllBytes(
                nupkg,
                TestPackageArchive.Create(
                    "lib/net10.0/Sample.dll",
                    $"{PackageId}.nuspec"));
            string extracted = Path.Combine(stagingRoot, "from-nupkg");
            ZipFile.ExtractToDirectory(nupkg, extracted);
            NuGetCache.CommitPackage(
                extracted,
                nupkg,
                PackageId,
                Version,
                NuGetCache.GetSourceKey(NuGetOrg.Url));
            string retained = Directory
                .EnumerateFiles(
                    cacheRoot,
                    $"{PackageId}.{Version}.nupkg",
                    SearchOption.AllDirectories)
                .Single();
            File.Delete(retained);

            var store = new FileSystemPackageStore();
            using var client = new HttpClient(new NotFoundHandler());

            PackagePayloadResult result =
                await PackagePayloadAcquisition.AcquireAsync(
                    client,
                    Coordinate(NuGetOrg),
                    store,
                    cancellationToken:
                        TestContext.Current.CancellationToken);

            Assert.IsType<PackagePayloadResult.Unavailable>(result);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    [Fact]
    public void ExtractedTree_RejectsSymlinkEscape()
    {
        string root = TempDirectory();
        string outside = TempDirectory();
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(
                Path.Combine(root, $"{PackageId}.nuspec"),
                """<?xml version="1.0"?><package />""");
            File.WriteAllBytes(Path.Combine(outside, "Outside.dll"), [1]);
            File.CreateSymbolicLink(Path.Combine(root, "lib"), outside);

            Assert.False(
                PackageContentAdmission.IsExtractedTreeWithinLimits(
                    root,
                    retainedNupkgPath: null,
                    PackagePayloadLimits.Default));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task CacheHitWithArchive_RejectsMutatedExtractedDll()
    {
        string cacheRoot = TempDirectory();
        string stagingRoot = TempDirectory();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            cacheRoot,
            skipNuGetCache: true);
        try
        {
            byte[] nupkgBytes = TestPackageArchive.Create(
                "lib/net10.0/Sample.dll");
            Directory.CreateDirectory(stagingRoot);
            string nupkg = Path.Combine(stagingRoot, "package.nupkg");
            File.WriteAllBytes(nupkg, nupkgBytes);
            string realExtract = Path.Combine(stagingRoot, "from-nupkg");
            ZipFile.ExtractToDirectory(nupkg, realExtract);
            NuGetCache.CommitPackage(
                realExtract,
                nupkg,
                PackageId,
                Version,
                NuGetCache.GetSourceKey(NuGetOrg.Url));

            string committedRoot = Directory
                .EnumerateFiles(
                    cacheRoot,
                    $"{PackageId}.{Version}.nupkg",
                    SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Single()!;
            string dll = Directory
                .EnumerateFiles(
                    committedRoot,
                    "Sample.dll",
                    SearchOption.AllDirectories)
                .Single();
            // Different length — size check alone would catch this.
            File.WriteAllBytes(dll, [9, 9, 9, 9, 9]);

            var store = new FileSystemPackageStore();
            using var client = new HttpClient(new NotFoundHandler());

            PackagePayloadResult result =
                await PackagePayloadAcquisition.AcquireAsync(
                    client,
                    Coordinate(NuGetOrg),
                    store,
                    cancellationToken:
                        TestContext.Current.CancellationToken);

            Assert.IsType<PackagePayloadResult.Unavailable>(result);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    /// <summary>
    /// Same-length content swaps must fail CRC agreement, not only size.
    /// </summary>
    [Fact]
    public async Task CacheHitWithArchive_RejectsSameLengthMutatedDll()
    {
        string cacheRoot = TempDirectory();
        string stagingRoot = TempDirectory();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            cacheRoot,
            skipNuGetCache: true);
        try
        {
            // TestPackageArchive default content is {1,2,3}.
            byte[] nupkgBytes = TestPackageArchive.Create(
                "lib/net10.0/Sample.dll");
            Directory.CreateDirectory(stagingRoot);
            string nupkg = Path.Combine(stagingRoot, "package.nupkg");
            File.WriteAllBytes(nupkg, nupkgBytes);
            string realExtract = Path.Combine(stagingRoot, "from-nupkg");
            ZipFile.ExtractToDirectory(nupkg, realExtract);
            NuGetCache.CommitPackage(
                realExtract,
                nupkg,
                PackageId,
                Version,
                NuGetCache.GetSourceKey(NuGetOrg.Url));

            string committedRoot = Directory
                .EnumerateFiles(
                    cacheRoot,
                    $"{PackageId}.{Version}.nupkg",
                    SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Single()!;
            string dll = Directory
                .EnumerateFiles(
                    committedRoot,
                    "Sample.dll",
                    SearchOption.AllDirectories)
                .Single();
            Assert.Equal(3, new FileInfo(dll).Length);
            File.WriteAllBytes(dll, [9, 9, 9]);

            var store = new FileSystemPackageStore();
            using var client = new HttpClient(new NotFoundHandler());

            PackagePayloadResult result =
                await PackagePayloadAcquisition.AcquireAsync(
                    client,
                    Coordinate(NuGetOrg),
                    store,
                    cancellationToken:
                        TestContext.Current.CancellationToken);

            Assert.IsType<PackagePayloadResult.Unavailable>(result);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    [Fact]
    public void ArchiveBackedTree_RejectsExtraEmptyDirectories()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            byte[] archive = TestPackageArchive.Create(
                "lib/net10.0/Sample.dll");
            Directory.CreateDirectory(Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                Path.Combine(root, "lib", "net10.0", "Sample.dll"),
                [1, 2, 3]);
            Directory.CreateDirectory(Path.Combine(root, "extra-empty"));
            Directory.CreateDirectory(
                Path.Combine(root, "extra-empty", "nested"));

            Assert.False(
                PackageContentAdmission.ExtractedTreeMatchesArchive(
                    root,
                    archive,
                    retainedNupkgPath: null,
                    PackagePayloadLimits.Default,
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Product-owned provenance requires archive tree match even when the
    /// commit marker file is missing (concurrent delete race).
    /// </summary>
    [Fact]
    public async Task ProductOwned_WithoutMarker_StillRequiresArchiveTreeMatch()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            byte[] original = [1, 2, 3, 4];
            byte[] mutated = [9, 9, 9, 9]; // same length, different CRC
            byte[] nuspec = """<?xml version="1.0"?><package />"""u8.ToArray();
            byte[] archive = TestPackageArchive.CreateWithContent(
                ("lib/net10.0/Sample.dll", original),
                ("Sample.Package.nuspec", nuspec));
            string nupkg = Path.Combine(root, $"{PackageId}.{Version}.nupkg");
            File.WriteAllBytes(nupkg, archive);

            Directory.CreateDirectory(Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                Path.Combine(root, "lib", "net10.0", "Sample.dll"),
                mutated);
            File.WriteAllBytes(
                Path.Combine(root, "Sample.Package.nuspec"),
                nuspec);
            // No commit marker on disk.

            var content = new FileSystemPackageContent(
                root,
                nupkg,
                fromCache: true,
                producerKey: "app-cache",
                requiresArchiveTreeMatch: true);
            Assert.False(
                await PackageContentAdmission.IsAdmissibleAsync(
                    content,
                    PackagePayloadLimits.Default,
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Product-owned archive open I/O failures classify as MissingArchive, not
    /// LimitsExceeded (offline diagnostics stay accurate).
    /// </summary>
    [Fact]
    public async Task ProductOwned_UnreadableArchivePath_IsMissingArchive()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            // Directory at the nupkg path: OpenRead throws IOException.
            string nupkgDir = Path.Combine(root, $"{PackageId}.{Version}.nupkg");
            Directory.CreateDirectory(nupkgDir);
            File.WriteAllText(
                Path.Combine(root, $"{PackageId}.nuspec"),
                """<?xml version="1.0"?><package />""");

            var content = new FileSystemPackageContent(
                root,
                nupkgDir,
                fromCache: true,
                producerKey: "app-cache",
                requiresArchiveTreeMatch: true);
            Assert.Equal(
                PackageContentAdmission.Outcome.MissingArchive,
                await PackageContentAdmission.EvaluateAsync(
                    content,
                    PackagePayloadLimits.Default,
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Product-owned node budget saturates instead of throwing on huge limits.
    /// </summary>
    [Fact]
    public void ProductOwned_NodeBudget_SaturatesWithoutOverflow()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            byte[] archive = TestPackageArchive.Create(
                "lib/net10.0/Sample.dll",
                "Sample.Package.nuspec");
            string nupkg = Path.Combine(root, $"{PackageId}.{Version}.nupkg");
            File.WriteAllBytes(nupkg, archive);
            Directory.CreateDirectory(Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                Path.Combine(root, "lib", "net10.0", "Sample.dll"),
                [1]);
            File.WriteAllText(
                Path.Combine(root, "Sample.Package.nuspec"),
                """<?xml version="1.0"?><package />""");
            File.WriteAllText(
                Path.Combine(root, NuGetCache.CommitMarkerFileName),
                "complete");

            // Exact match will fail (archive bytes vs written stub) but must not
            // throw OverflowException while computing the walk budget.
            Assert.False(
                PackageContentAdmission.ExtractedTreeMatchesArchive(
                    root,
                    archive,
                    nupkg,
                    new PackagePayloadLimits
                    {
                        MaxEntryCount = int.MaxValue,
                        MaxUniqueDirectories = int.MaxValue,
                    },
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Deleting the retained nupkg must not downgrade product-owned content to
    /// foreign walk-only gates (mutated extract would otherwise admit).
    /// </summary>
    [Fact]
    public async Task ProductOwned_DeletedNupkg_DoesNotAdmitMutatedTree()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            byte[] original = [1, 2, 3, 4];
            byte[] mutated = [9, 9, 9, 9];
            byte[] nuspec = """<?xml version="1.0"?><package />"""u8.ToArray();
            byte[] archive = TestPackageArchive.CreateWithContent(
                ("lib/net10.0/Sample.dll", original),
                ("Sample.Package.nuspec", nuspec));
            string nupkg = Path.Combine(root, $"{PackageId}.{Version}.nupkg");
            File.WriteAllBytes(nupkg, archive);

            Directory.CreateDirectory(Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                Path.Combine(root, "lib", "net10.0", "Sample.dll"),
                mutated);
            File.WriteAllBytes(
                Path.Combine(root, "Sample.Package.nuspec"),
                nuspec);
            File.WriteAllText(
                Path.Combine(root, NuGetCache.CommitMarkerFileName),
                "complete");

            // Control: retained archive still rejects the mutation.
            var withArchive = new FileSystemPackageContent(
                root,
                nupkg,
                fromCache: true,
                producerKey: "app-cache",
                requiresArchiveTreeMatch: true);
            Assert.False(
                await PackageContentAdmission.IsAdmissibleAsync(
                    withArchive,
                    PackagePayloadLimits.Default,
                    CancellationToken.None));

            File.Delete(nupkg);
            var withoutArchive = new FileSystemPackageContent(
                root,
                nupkgPath: null,
                fromCache: true,
                producerKey: "app-cache",
                requiresArchiveTreeMatch: true);
            Assert.Equal(
                PackageContentAdmission.Outcome.MissingArchive,
                await PackageContentAdmission.EvaluateAsync(
                    withoutArchive,
                    PackagePayloadLimits.Default,
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// App-cache tiers are listed before global-packages so admission can fall
    /// through a damaged foreign tree to product-owned content.
    /// </summary>
    [Fact]
    public void ListCachedPackageContent_PrefersAppCacheOverGlobalPackages()
    {
        string cacheRoot = TempDirectory();
        string globalRoot = TempDirectory();
        string stagingRoot = TempDirectory();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            cacheRoot,
            skipNuGetCache: false);
        try
        {
            string sourceKey = NuGetCache.GetSourceKey(NuGetOrg.Url);
            Directory.CreateDirectory(stagingRoot);
            string nupkg = Path.Combine(stagingRoot, "package.nupkg");
            File.WriteAllBytes(
                nupkg,
                TestPackageArchive.Create(
                    "lib/net10.0/Sample.dll",
                    $"{PackageId}.nuspec"));
            string extracted = Path.Combine(stagingRoot, "from-nupkg");
            ZipFile.ExtractToDirectory(nupkg, extracted);
            NuGetCache.CommitPackage(
                extracted,
                nupkg,
                PackageId,
                Version,
                sourceKey);

            string globalDir = Path.Combine(
                globalRoot,
                PackageId.ToLowerInvariant(),
                Version.ToLowerInvariant());
            Directory.CreateDirectory(globalDir);
            // .nupkg.metadata stores the feed URL; GetSourceKey is applied on read.
            File.WriteAllText(
                Path.Combine(globalDir, ".nupkg.metadata"),
                $$"""{"source":"{{NuGetOrg.Url}}"}""");
            File.WriteAllBytes(
                Path.Combine(globalDir, $"{PackageId.ToLowerInvariant()}.{Version.ToLowerInvariant()}.nupkg"),
                [1]);

            IReadOnlyList<CachedPackage> listed = NuGetCache.ListCachedPackageContent(
                PackageId,
                Version,
                [sourceKey],
                globalPackagesPath: globalRoot);

            Assert.Equal(2, listed.Count);
            Assert.True(listed[0].RequiresArchiveTreeMatch);
            Assert.False(listed[1].RequiresArchiveTreeMatch);
            Assert.Contains(
                Path.DirectorySeparatorChar + sourceKey,
                listed[0].ExtractPath,
                StringComparison.Ordinal);

            // Lazy path: taking only the first candidate must not depend on
            // global-packages metadata being readable.
            File.WriteAllBytes(
                Path.Combine(globalDir, ".nupkg.metadata"),
                new byte[NuGetCache.MaxGlobalPackageMetadataBytes + 1]);
            CachedPackage? firstOnly = NuGetCache.TryGetCachedPackageContent(
                PackageId,
                Version,
                [sourceKey],
                globalPackagesPath: globalRoot);
            Assert.NotNull(firstOnly);
            Assert.True(firstOnly!.RequiresArchiveTreeMatch);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(globalRoot))
                Directory.Delete(globalRoot, recursive: true);
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    [Fact]
    public void GlobalPackageMetadata_OverMaxBytes_IsIgnored()
    {
        string cacheRoot = TempDirectory();
        string globalRoot = TempDirectory();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            cacheRoot,
            skipNuGetCache: false);
        try
        {
            string sourceKey = NuGetCache.GetSourceKey(NuGetOrg.Url);
            string globalDir = Path.Combine(
                globalRoot,
                PackageId.ToLowerInvariant(),
                Version.ToLowerInvariant());
            Directory.CreateDirectory(globalDir);
            File.WriteAllBytes(
                Path.Combine(globalDir, ".nupkg.metadata"),
                new byte[NuGetCache.MaxGlobalPackageMetadataBytes + 8]);

            Assert.Null(
                NuGetCache.TryGetGlobalPackageContent(
                    globalRoot,
                    PackageId.ToLowerInvariant(),
                    Version.ToLowerInvariant(),
                    [sourceKey]));
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(globalRoot))
                Directory.Delete(globalRoot, recursive: true);
        }
    }

    /// <summary>
    /// A retained nupkg with no extracted package layout must not admit as
    /// foreign/global-packages content — filesystem consumers never fall back
    /// to reading assets from the archive.
    /// </summary>
    [Fact]
    public async Task GlobalPackages_ArchiveOnlyWithoutNuspec_IsRejected()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            byte[] archive = TestPackageArchive.Create(
                "lib/net10.0/Sample.dll",
                "Sample.Package.nuspec");
            string nupkg = Path.Combine(root, $"{PackageId}.{Version}.nupkg");
            File.WriteAllBytes(nupkg, archive);
            // No extracted .nuspec or lib/ — only the retained archive.

            var content = new FileSystemPackageContent(
                root,
                nupkg,
                fromCache: true,
                producerKey: "global");
            Assert.False(
                await PackageContentAdmission.IsAdmissibleAsync(
                    content,
                    PackagePayloadLimits.Default,
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// NuGet global-packages trees are not 1:1 archive extracts (OPC omitted,
    /// sidecar metadata, nuspec casing). Without a product commit marker they
    /// must still admit under walk-only gates.
    /// </summary>
    [Fact]
    public async Task GlobalPackagesShapedTree_IsAdmittedWithoutExactArchiveMatch()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            byte[] archive = TestPackageArchive.Create(
                "lib/net10.0/Sample.dll",
                "Sample.Package.nuspec",
                "[Content_Types].xml",
                "_rels/.rels",
                "package/services/metadata/core-properties/x.psmdcp");
            string nupkg = Path.Combine(root, $"{PackageId}.{Version}.nupkg");
            File.WriteAllBytes(nupkg, archive);

            // NuGet-shaped extract: no OPC, lowercase nuspec, sidecar files.
            Directory.CreateDirectory(Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                Path.Combine(root, "lib", "net10.0", "Sample.dll"),
                [1, 2, 3]);
            File.WriteAllText(
                Path.Combine(root, $"{PackageId}.nuspec"),
                """<?xml version="1.0"?><package />""");
            File.WriteAllText(Path.Combine(root, ".nupkg.metadata"), "{}");
            File.WriteAllText(
                Path.Combine(root, $"{PackageId}.{Version}.nupkg.sha512"),
                "abc");

            var content = new FileSystemPackageContent(
                root,
                nupkg,
                fromCache: true,
                producerKey: "global");
            Assert.True(
                await PackageContentAdmission.IsAdmissibleAsync(
                    content,
                    PackagePayloadLimits.Default,
                    CancellationToken.None));

            // Same tree with a product commit marker must require exact match
            // and therefore reject the NuGet-shaped divergence.
            File.WriteAllText(
                Path.Combine(root, NuGetCache.CommitMarkerFileName),
                "complete");
            Assert.False(
                await PackageContentAdmission.IsAdmissibleAsync(
                    content,
                    PackagePayloadLimits.Default,
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Foreign/global-packages trees count every filesystem node toward
    /// <see cref="PackagePayloadLimits.MaxEntryCount"/> so a zero-byte fan-out
    /// cannot unbounded-walk past the archive's own entry budget.
    /// </summary>
    [Fact]
    public async Task ForeignTree_ExcessNodes_IsRejected()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            byte[] archive = TestPackageArchive.Create(
                "lib/net10.0/Sample.dll",
                "Sample.Package.nuspec");
            string nupkg = Path.Combine(root, $"{PackageId}.{Version}.nupkg");
            File.WriteAllBytes(nupkg, archive);
            File.WriteAllText(
                Path.Combine(root, $"{PackageId}.nuspec"),
                """<?xml version="1.0"?><package />""");
            Directory.CreateDirectory(Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                Path.Combine(root, "lib", "net10.0", "Sample.dll"),
                [1]);
            // Zero-byte fan-out under an otherwise small tree.
            for (int i = 0; i < 20; i++)
                File.WriteAllBytes(Path.Combine(root, $"pad-{i}"), []);

            var content = new FileSystemPackageContent(
                root,
                nupkg,
                fromCache: true,
                producerKey: "global");
            Assert.False(
                await PackageContentAdmission.IsAdmissibleAsync(
                    content,
                    new PackagePayloadLimits { MaxEntryCount = 8 },
                    CancellationToken.None));
            Assert.True(
                await PackageContentAdmission.IsAdmissibleAsync(
                    content,
                    new PackagePayloadLimits { MaxEntryCount = 50 },
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Product-owned trees still bound filesystem nodes so a mutated cache
    /// fan-out cannot force an unbounded walk before archive matching rejects.
    /// </summary>
    [Fact]
    public async Task ProductOwnedTree_ExcessNodes_IsRejected()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            byte[] archive = TestPackageArchive.Create(
                "lib/net10.0/Sample.dll",
                "Sample.Package.nuspec");
            string nupkg = Path.Combine(root, $"{PackageId}.{Version}.nupkg");
            File.WriteAllBytes(nupkg, archive);
            Directory.CreateDirectory(Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                Path.Combine(root, "lib", "net10.0", "Sample.dll"),
                [1]);
            File.WriteAllText(
                Path.Combine(root, "Sample.Package.nuspec"),
                """<?xml version="1.0"?><package />""");
            File.WriteAllText(
                Path.Combine(root, NuGetCache.CommitMarkerFileName),
                "complete");
            for (int i = 0; i < 40; i++)
                Directory.CreateDirectory(Path.Combine(root, $"pad-{i}"));

            var content = new FileSystemPackageContent(
                root,
                nupkg,
                fromCache: true,
                producerKey: "app",
                requiresArchiveTreeMatch: true);
            // Node budget = MaxEntryCount + MaxUniqueDirectories + 8.
            Assert.False(
                await PackageContentAdmission.IsAdmissibleAsync(
                    content,
                    new PackagePayloadLimits
                    {
                        MaxEntryCount = 4,
                        MaxUniqueDirectories = 4,
                    },
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Directory enumeration failures during the walk must fail closed as a
    /// rejection, not escape as an unhandled IO fault (archive-less has no
    /// outer catch around the walk).
    /// </summary>
    [Fact]
    public void ExtractedTreeWalk_UnreadableDirectory_FailsClosed()
    {
        if (OperatingSystem.IsWindows())
            return; // chmod mode bits are the portable probe

        string root = TempDirectory();
        Directory.CreateDirectory(root);
        string locked = Path.Combine(root, "locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(
            Path.Combine(root, $"{PackageId}.nuspec"),
            """<?xml version="1.0"?><package />""");
        try
        {
            // Owner-only nothing: EnumerateFileSystemEntries throws.
            File.SetUnixFileMode(locked, UnixFileMode.None);
            Assert.False(
                PackageContentAdmission.IsExtractedTreeWithinLimits(
                    root,
                    retainedNupkgPath: null,
                    PackagePayloadLimits.Default));
        }
        finally
        {
            try
            {
                File.SetUnixFileMode(
                    locked,
                    UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute);
            }
            catch (IOException)
            {
                // Best-effort restore so recursive delete can clean up.
            }
            catch (UnauthorizedAccessException)
            {
            }

            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// App-cache slots for every authorized producer precede the single
    /// global-packages tier — not app+global per producer.
    /// </summary>
    [Fact]
    public void ListCachedPackageContent_AllAppSlotsBeforeGlobal()
    {
        string cacheRoot = TempDirectory();
        string globalRoot = TempDirectory();
        string stagingRoot = TempDirectory();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            cacheRoot,
            skipNuGetCache: false);
        try
        {
            string primaryKey = NuGetCache.GetSourceKey(Primary.Url);
            string nugetKey = NuGetCache.GetSourceKey(NuGetOrg.Url);
            byte[] nupkgBytes = TestPackageArchive.Create(
                "lib/net10.0/Sample.dll",
                $"{PackageId}.nuspec");

            foreach (string sourceKey in new[] { primaryKey, nugetKey })
            {
                string staging = Path.Combine(stagingRoot, sourceKey);
                Directory.CreateDirectory(staging);
                string nupkg = Path.Combine(staging, "package.nupkg");
                File.WriteAllBytes(nupkg, nupkgBytes);
                string extracted = Path.Combine(staging, "from-nupkg");
                ZipFile.ExtractToDirectory(nupkg, extracted);
                NuGetCache.CommitPackage(
                    extracted,
                    nupkg,
                    PackageId,
                    Version,
                    sourceKey);
            }

            string globalDir = Path.Combine(
                globalRoot,
                PackageId.ToLowerInvariant(),
                Version.ToLowerInvariant());
            Directory.CreateDirectory(globalDir);
            File.WriteAllText(
                Path.Combine(globalDir, ".nupkg.metadata"),
                $$"""{"source":"{{Primary.Url}}"}""");
            File.WriteAllBytes(
                Path.Combine(
                    globalDir,
                    $"{PackageId.ToLowerInvariant()}.{Version.ToLowerInvariant()}.nupkg"),
                [1]);

            IReadOnlyList<CachedPackage> listed =
                NuGetCache.ListCachedPackageContent(
                    PackageId,
                    Version,
                    [primaryKey, nugetKey],
                    globalPackagesPath: globalRoot);

            Assert.Equal(3, listed.Count);
            Assert.True(listed[0].RequiresArchiveTreeMatch);
            Assert.Equal(primaryKey, listed[0].ProducerKey);
            Assert.True(listed[1].RequiresArchiveTreeMatch);
            Assert.Equal(nugetKey, listed[1].ProducerKey);
            Assert.False(listed[2].RequiresArchiveTreeMatch);
            Assert.Equal(primaryKey, listed[2].ProducerKey);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(globalRoot))
                Directory.Delete(globalRoot, recursive: true);
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    /// <summary>
    /// Acquisition must hand the store the full authorized producer list so
    /// app-before-global ordering is not defeated by a per-producer loop.
    /// </summary>
    [Fact]
    public async Task Acquire_EnumeratesCachedOnceWithAllAuthorizedProducers()
    {
        var store = new RecordingEnumerateStore();
        using var client = new HttpClient(new NotFoundHandler());

        _ = await PackagePayloadAcquisition.AcquireAsync(
            client,
            Coordinate(Primary, NuGetOrg),
            store,
            cancellationToken: TestContext.Current.CancellationToken);

        IReadOnlyList<string> expected =
        [
            NuGetCache.GetSourceKey(Primary.Url),
            NuGetCache.GetSourceKey(NuGetOrg.Url),
        ];
        Assert.Equal(
            [expected],
            store.EnumerateAllowedSourceKeys.Select(keys => keys!.ToArray()));
    }

    [Fact]
    public void ArchiveBackedTree_CommitMarkerDoesNotConsumeExpandedBudget()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            byte[] payload = new byte[100];
            Array.Fill(payload, (byte)7);
            byte[] archive = TestPackageArchive.CreateWithContent(
                ("lib/net10.0/Sample.dll", payload));
            Directory.CreateDirectory(Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                Path.Combine(root, "lib", "net10.0", "Sample.dll"),
                payload);
            File.WriteAllText(
                Path.Combine(root, NuGetCache.CommitMarkerFileName),
                "complete");

            var limits = new PackagePayloadLimits { MaxExpandedBytes = 100 };
            Assert.True(
                PackageContentAdmission.ExtractedTreeMatchesArchive(
                    root,
                    archive,
                    retainedNupkgPath: null,
                    limits,
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArchiveBackedTree_AllowsInteriorDoubleDotFileName()
    {
        // Segment safety (StorePath.IsSafeSegment) permits Foo..dll; a substring
        // ".." check would false-reject a legal package entry.
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            byte[] payload = [1, 2, 3];
            byte[] archive = TestPackageArchive.CreateWithContent(
                ("lib/net10.0/Foo..dll", payload));
            Directory.CreateDirectory(Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                Path.Combine(root, "lib", "net10.0", "Foo..dll"),
                payload);
            File.WriteAllText(
                Path.Combine(root, NuGetCache.CommitMarkerFileName),
                "complete");

            Assert.True(
                PackageContentAdmission.ExtractedTreeMatchesArchive(
                    root,
                    archive,
                    retainedNupkgPath: null,
                    PackagePayloadLimits.Default,
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArchiveBackedTree_RejectsExactDotDotSegment()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            // Craft a central-directory path the tree matcher must still refuse
            // even if ZipArchive surfaces it.
            byte[] archive = TestPackageArchive.CreateWithContent(
                ("lib/net10.0/../Escape.dll", [1, 2, 3]));
            Directory.CreateDirectory(Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                Path.Combine(root, "lib", "Escape.dll"),
                [1, 2, 3]);
            File.WriteAllText(
                Path.Combine(root, NuGetCache.CommitMarkerFileName),
                "complete");

            Assert.False(
                PackageContentAdmission.ExtractedTreeMatchesArchive(
                    root,
                    archive,
                    retainedNupkgPath: null,
                    PackagePayloadLimits.Default,
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArchiveLessTree_CountsDecoyNupkgTowardExpandedBytes()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, $"{PackageId}.nuspec"),
                """<?xml version="1.0"?><package />""");
            File.WriteAllBytes(
                Path.Combine(root, "payload.nupkg"),
                new byte[4096]);

            Assert.False(
                PackageContentAdmission.IsExtractedTreeWithinLimits(
                    root,
                    retainedNupkgPath: null,
                    new PackagePayloadLimits { MaxExpandedBytes = 1024 }));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Only the package-root commit marker is internal metadata. A nested file
    /// reusing the marker name must still consume MaxExpandedBytes.
    /// </summary>
    [Fact]
    public async Task ReadBoundedAsync_ReturnsExactLengthWithoutOverread()
    {
        byte[] payload = new byte[100_000];
        Array.Fill(payload, (byte)7);
        await using var stream = new MemoryStream(payload, writable: false);

        byte[]? read = await PackageContentAdmission.ReadBoundedAsync(
            stream,
            maxBytes: 100_000,
            CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(payload.Length, read!.Length);
        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task ReadExactAsync_FillsExactlyTheDeclaredLength()
    {
        byte[] payload = new byte[100_000];
        for (int index = 0; index < payload.Length; index++)
            payload[index] = (byte)index;
        await using var stream = new MemoryStream(payload, writable: false);

        byte[]? read = await PackageContentAdmission.ReadExactAsync(
            stream,
            payload.Length,
            CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(payload.Length, read!.Length);
        Assert.Equal(payload, read);
    }

    // A body that arrives in fragments is normal for a network stream; only a body that never
    // reaches the declared length is a truncated transfer.
    [Fact]
    public async Task ReadExactAsync_AssemblesAFragmentedBody()
    {
        byte[] payload = [.. Enumerable.Range(0, 5000).Select(value => (byte)value)];
        await using var stream = new FragmentedStream(payload, fragment: 7);

        byte[]? read = await PackageContentAdmission.ReadExactAsync(
            stream,
            payload.Length,
            CancellationToken.None);

        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task ReadExactAsync_RejectsABodyThatEndsBeforeTheDeclaredLength()
    {
        byte[] payload = new byte[64];
        await using var stream = new MemoryStream(payload, writable: false);

        byte[]? read = await PackageContentAdmission.ReadExactAsync(
            stream,
            payload.Length + 1,
            CancellationToken.None);

        Assert.Null(read);
    }

    [Fact]
    public async Task ReadExactAsync_RejectsABodyCarryingMoreThanTheDeclaredLength()
    {
        byte[] payload = new byte[64];
        await using var stream = new MemoryStream(payload, writable: false);

        byte[]? read = await PackageContentAdmission.ReadExactAsync(
            stream,
            payload.Length - 1,
            CancellationToken.None);

        Assert.Null(read);
    }

    [Fact]
    public async Task ReadExactAsync_AcceptsAnEmptyDeclaredLength()
    {
        await using var empty = new MemoryStream([], writable: false);
        Assert.Empty(
            (await PackageContentAdmission.ReadExactAsync(
                empty,
                length: 0,
                CancellationToken.None))!);

        await using var nonEmpty = new MemoryStream(new byte[1], writable: false);
        Assert.Null(
            await PackageContentAdmission.ReadExactAsync(
                nonEmpty,
                length: 0,
                CancellationToken.None));
    }

    [Fact]
    public async Task ReadExactAsync_ObservesCancellation()
    {
        await using var stream = new FragmentedStream(new byte[4096], fragment: 16);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await PackageContentAdmission.ReadExactAsync(
                stream,
                4096,
                cancellation.Token));
    }

    [Fact]
    public async Task ReadBoundedAsync_RejectsStreamOverMaxBytes()
    {
        byte[] payload = new byte[1024];
        await using var stream = new MemoryStream(payload, writable: false);

        byte[]? read = await PackageContentAdmission.ReadBoundedAsync(
            stream,
            maxBytes: 512,
            CancellationToken.None);

        Assert.Null(read);
    }

    [Fact]
    public async Task ReadBoundedAsync_SeekableUnderreportedLength_StillRejectsOverMax()
    {
        // Length claims 4 bytes; actual body is 16. Stop condition must be EOF
        // under the bound, not the Length snapshot.
        byte[] actual = new byte[16];
        Array.Fill(actual, (byte)9);
        await using var stream = new UnderreportingLengthStream(actual, reportedLength: 4);

        byte[]? read = await PackageContentAdmission.ReadBoundedAsync(
            stream,
            maxBytes: 8,
            CancellationToken.None);

        Assert.Null(read);
    }

    [Fact]
    public async Task ReadBoundedAsync_SeekableUnderreportedLength_DrainsFullBodyUnderMax()
    {
        byte[] actual = new byte[12];
        for (int i = 0; i < actual.Length; i++)
            actual[i] = (byte)i;
        await using var stream = new UnderreportingLengthStream(actual, reportedLength: 3);

        byte[]? read = await PackageContentAdmission.ReadBoundedAsync(
            stream,
            maxBytes: 32,
            CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(actual, read);
    }

    [Fact]
    public async Task ReadBoundedAsync_ExactSeekableLength_AtCapacityHint_ReturnsPayload()
    {
        // Length-sized fill (initialCapacity == payload) must succeed without
        // needing a grow cycle after the buffer is full — probe-before-grow
        // observes EOF and returns the filled buffer as-is.
        byte[] payload = new byte[200_000];
        Array.Fill(payload, (byte)3);
        await using var stream = new MemoryStream(payload, writable: false);

        byte[]? read = await PackageContentAdmission.ReadBoundedAsync(
            stream,
            maxBytes: 500_000,
            CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(payload, read);
    }

    [Fact]
    public void NestedCommitMarkerNamedFile_CountsTowardExpandedBytes()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, $"{PackageId}.nuspec"),
                """<?xml version="1.0"?><package />""");
            string nested = Path.Combine(root, "sub");
            Directory.CreateDirectory(nested);
            File.WriteAllBytes(
                Path.Combine(nested, NuGetCache.CommitMarkerFileName),
                new byte[4096]);

            Assert.False(
                PackageContentAdmission.IsExtractedTreeWithinLimits(
                    root,
                    retainedNupkgPath: null,
                    new PackagePayloadLimits { MaxExpandedBytes = 1024 }));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// On case-sensitive volumes, a case-only sibling of an archive entry is a
    /// distinct extra file and must fail product-owned archive matching.
    /// </summary>
    [Fact]
    public void ArchiveBackedTree_RejectsCaseOnlyExtraFileOnCaseSensitiveFs()
    {
        if (OperatingSystem.IsWindows())
            return;

        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            byte[] archive = TestPackageArchive.Create(
                "lib/net10.0/Sample.dll");
            Directory.CreateDirectory(Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                Path.Combine(root, "lib", "net10.0", "Sample.dll"),
                [1, 2, 3]);
            File.WriteAllBytes(
                Path.Combine(root, "lib", "net10.0", "sample.dll"),
                [9, 9, 9]);
            File.WriteAllText(
                Path.Combine(root, NuGetCache.CommitMarkerFileName),
                "complete");

            Assert.False(
                PackageContentAdmission.ExtractedTreeMatchesArchive(
                    root,
                    archive,
                    retainedNupkgPath: null,
                    PackagePayloadLimits.Default,
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// On case-sensitive volumes, a case-only sibling of the retained nupkg is
    /// a distinct file and must count toward MaxExpandedBytes.
    /// </summary>
    [Fact]
    public void CaseOnlyNupkgSibling_CountsTowardExpandedBytesOnCaseSensitiveFs()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, $"{PackageId}.nuspec"),
                """<?xml version="1.0"?><package />""");
            string retained = Path.Combine(root, $"{PackageId}.{Version}.nupkg");
            File.WriteAllBytes(retained, new byte[16]);
            string sibling = Path.Combine(
                root,
                $"{PackageId}.{Version}.NUPKG");
            if (File.Exists(sibling))
            {
                Assert.Skip(
                    "The filesystem does not support case-distinct sibling files.");
                return;
            }

            File.WriteAllBytes(sibling, new byte[4096]);

            Assert.False(
                PackageContentAdmission.IsExtractedTreeWithinLimits(
                    root,
                    retainedNupkgPath: retained,
                    new PackagePayloadLimits { MaxExpandedBytes = 1024 }));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CacheHitWithArchive_StillRejectsSymlinkDamagedExtractedTree()
    {
        string cacheRoot = TempDirectory();
        string stagingRoot = TempDirectory();
        string outside = TempDirectory();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            cacheRoot,
            skipNuGetCache: true);
        try
        {
            Directory.CreateDirectory(outside);
            File.WriteAllBytes(Path.Combine(outside, "Outside.dll"), [9]);

            Directory.CreateDirectory(stagingRoot);
            string nupkg = Path.Combine(stagingRoot, "package.nupkg");
            File.WriteAllBytes(
                nupkg,
                TestPackageArchive.Create("lib/net10.0/Sample.dll"));
            string extracted = Path.Combine(stagingRoot, "from-nupkg");
            ZipFile.ExtractToDirectory(nupkg, extracted);
            NuGetCache.CommitPackage(
                extracted,
                nupkg,
                PackageId,
                Version,
                NuGetCache.GetSourceKey(NuGetOrg.Url));

            // Leave the retained nupkg intact but plant a symlink escape in the
            // extracted tree consumers would open.
            string committedRoot = Directory
                .EnumerateFiles(
                    cacheRoot,
                    $"{PackageId}.{Version}.nupkg",
                    SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Single()!;
            string libDir = Path.Combine(committedRoot, "lib");
            if (Directory.Exists(libDir))
                Directory.Delete(libDir, recursive: true);
            File.CreateSymbolicLink(libDir, outside);

            var store = new FileSystemPackageStore();
            using var client = new HttpClient(new NotFoundHandler());

            PackagePayloadResult result =
                await PackagePayloadAcquisition.AcquireAsync(
                    client,
                    Coordinate(NuGetOrg),
                    store,
                    cancellationToken:
                        TestContext.Current.CancellationToken);

            Assert.IsType<PackagePayloadResult.Unavailable>(result);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractedTree_WithBackslashFileName_DoesNotThrow()
    {
        // Windows path APIs cannot create a single filename containing '\' —
        // Path.Combine treats it as a separator. The product still must not
        // throw when such a name appears after Linux extract; that probe is
        // Unix-only.
        if (OperatingSystem.IsWindows())
            return;

        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, $"{PackageId}.nuspec"),
                """<?xml version="1.0"?><package />""");
            // Linux can host a literal backslash in a filename after ZipFile.ExtractToDirectory.
            File.WriteAllBytes(Path.Combine(root, @"lib\net45\Foo.dll"), [1]);

            PackageContentAdmission.Outcome outcome =
                await PackageContentAdmission.EvaluateAsync(
                    new FileSystemPackageContent(
                        root,
                        nupkgPath: null,
                        fromCache: true,
                        producerKey: "test"),
                    PackagePayloadLimits.Default,
                    TestContext.Current.CancellationToken);

            Assert.Equal(PackageContentAdmission.Outcome.Admissible, outcome);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExtractedTree_CountsDirectoriesTowardEntryLimit()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, $"{PackageId}.nuspec"),
                """<?xml version="1.0"?><package />""");
            for (int i = 0; i < 5; i++)
                Directory.CreateDirectory(Path.Combine(root, $"d{i}"));

            Assert.False(
                PackageContentAdmission.IsExtractedTreeWithinLimits(
                    root,
                    retainedNupkgPath: null,
                    new PackagePayloadLimits { MaxEntryCount = 3 }));
            Assert.True(
                PackageContentAdmission.IsExtractedTreeWithinLimits(
                    root,
                    retainedNupkgPath: null,
                    new PackagePayloadLimits { MaxEntryCount = 20 }));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractedTree_WithoutTopLevelNuspec_IsMissingArchive()
    {
        string root = TempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "lib"));
            var content = new FileSystemPackageContent(
                root,
                nupkgPath: null,
                fromCache: true,
                producerKey: "test");

            Assert.Equal(
                PackageContentAdmission.Outcome.MissingArchive,
                await PackageContentAdmission.EvaluateAsync(
                    content,
                    PackagePayloadLimits.Default,
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    static string TempDirectory() =>
        Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-payload-admission-{Guid.NewGuid():N}");

    [Fact]
    public async Task InadmissibleCacheEntry_DoesNotMaskAnotherProducer()
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(Primary.Url),
            new MemoryStream(
                TestPackageArchive.Create(
                    "lib/net10.0/One.dll",
                    "lib/net10.0/Two.dll")),
            TestContext.Current.CancellationToken);
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            new MemoryStream(
                TestPackageArchive.Create("lib/net10.0/Only.dll")),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(Primary, NuGetOrg),
                store,
                limits: new PackagePayloadLimits { MaxEntryCount = 1 },
                cancellationToken: TestContext.Current.CancellationToken);

        AcquiredPackagePayload payload = Acquired(result);
        Assert.Equal(PackagePayloadOrigin.Cache, payload.Origin);
        Assert.Equal(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            payload.ProducerKey);
        Assert.Equal(
            "lib/net10.0/Only.dll",
            Assert.Single(payload.Content.EnumerateEntries()));
    }

    [Fact]
    public async Task CachedContentOfAnUnauthorizedProducer_IsNotServed()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(Primary.Url),
            new MemoryStream(nupkg),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new NotFoundHandler());

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
    }

    [Fact]
    public async Task CommitThatLosesToInadmissibleCachedContent_IsNotServed()
    {
        byte[] valid = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        byte[] inadmissible = TestPackageArchive.Create(
            "lib/net10.0/One.dll",
            "lib/net10.0/Two.dll",
            "lib/net10.0/Three.dll");
        string producerKey = NuGetCache.GetSourceKey(NuGetOrg.Url);
        var store = new CommitWinnerStore(
            new InMemoryPackageContent(
                inadmissible,
                fromCache: true,
                producerKey));
        using var client = new HttpClient(new NuGetOrgPayloadHandler(valid));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                limits: new PackagePayloadLimits { MaxEntryCount = 2 },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
    }

    [Fact]
    public async Task SourcesAreTriedInOrderUntilOneServesThePayload()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        var handler = new NuGetOrgPayloadHandler(nupkg);
        using var client = new HttpClient(handler);

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(Primary, NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        AcquiredPackagePayload payload = Acquired(result);
        Assert.Equal(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            payload.ProducerKey);
        Assert.Contains(
            handler.Requests,
            url => url.StartsWith("https://primary.test/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoSourceServingTheCoordinate_IsUnavailable()
    {
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new NotFoundHandler());

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Contains(PackageId, unavailable.Message, StringComparison.Ordinal);
        Assert.Contains("nuget.org", unavailable.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoAuthorizedSource_IsUnavailable()
    {
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new FailingHandler());

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
    }

    [Fact]
    public async Task Acquisition_ObservesCancellationBeforeStoreOrSourceWork()
    {
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new FailingHandler());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task UnboundedChunkedPayload_IsRejectedWithoutContentLength()
    {
        // The response never advertises a length, so only counting the bytes
        // that actually arrive can bound it.
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(
            new NuGetOrgHandler(() => new StreamContent(new EndlessStream())));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                limits: new PackagePayloadLimits { MaxArchiveBytes = 4096 },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
    }

    [Fact]
    public async Task AdvertisedOversizePayload_IsATypedSourceFailure()
    {
        var store = new InMemoryPackageStore();
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        using var client = new HttpClient(
            new NuGetOrgHandler(() => new ByteArrayContent(nupkg)));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                limits: new PackagePayloadLimits { MaxArchiveBytes = 16 },
                cancellationToken: TestContext.Current.CancellationToken);

        // An oversized payload stays an outcome rather than an exception, so
        // the remaining authorized sources are still tried.
        Assert.IsType<PackagePayloadResult.Unavailable>(result);
    }

    /// <summary>
    /// The finding, end to end: an archive whose entry escapes its root is
    /// refused by the in-memory store's path just as the filesystem store would
    /// refuse it at extraction, nothing is cached under the source that served
    /// it, and the next authorized source still serves the coordinate.
    /// </summary>
    [Fact]
    public async Task TraversingArchiveFromOneSource_IsRejectedAndNotCached()
    {
        byte[] hostile = ArchiveWithNames(
            ("../ignored.txt", "escaped"u8.ToArray()),
            ("lib/net10.0/Sample.dll", [1, 2, 3]));
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(
            new TwoSourceHandler(
                primaryContent: hostile,
                nuGetOrgContent: nupkg));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(Primary, NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        AcquiredPackagePayload payload = Acquired(result);
        Assert.Equal(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            payload.ProducerKey);
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(Primary.Url)]));
    }

    /// <summary>
    /// An entry compressed with a method this runtime cannot decode declares an
    /// ordinary length, so only opening it finds the problem. Publishing first
    /// would credit the source, poison the cache, and move the failure to a
    /// consumer that can no longer try another source.
    /// </summary>
    [Fact]
    public async Task ArchiveWithUnsupportedCompression_IsRejectedBeforePublication()
    {
        byte[] undecodable = WithCompressionMethod(
            TestPackageArchive.Create("lib/net10.0/Sample.dll"),
            method: 99);
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(
            new NuGetOrgHandler(() => new ByteArrayContent(undecodable)));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
    }

    /// <summary>
    /// A pre-signed flat-container base keeps its query, and the package path
    /// is appended to the path rather than to the query value.
    /// </summary>
    [Fact]
    public async Task SignedFlatContainerBase_ComposesThePackagePath()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var handler = new ServiceIndexHandler(
            baseAddress: "https://primary.test/flat?sig=abc",
            nupkgUrl:
                $"https://primary.test/flat/{PackageId}/{Version}/{PackageId}.{Version}.nupkg?sig=abc",
            nupkg);
        using var client = new HttpClient(handler);

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(Primary),
                new InMemoryPackageStore(),
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Acquired>(result);
        Assert.Contains(
            handler.Requests,
            url => url.EndsWith(
                $"{PackageId}.{Version}.nupkg?sig=abc",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/relative/flat")]
    [InlineData("ftp://primary.test/flat")]
    [InlineData("not a url at all")]
    public async Task MalformedFlatContainerBase_IsATypedSourceFailure(
        string baseAddress)
    {
        var handler = new ServiceIndexHandler(
            baseAddress,
            nupkgUrl: "https://primary.test/never",
            nupkg: []);
        using var client = new HttpClient(handler);

        // The malformed resource metadata ends this source rather than
        // throwing out of acquisition, so the coordinate's remaining sources
        // are still consulted.
        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(Primary),
                new InMemoryPackageStore(),
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
    }

    [Fact]
    public async Task InvalidArchiveFromOneSource_LetsTheNextSourceServe()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(
            new TwoSourceHandler(
                primaryContent: "this is not a zip archive"u8.ToArray(),
                nuGetOrgContent: nupkg));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(Primary, NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        AcquiredPackagePayload payload = Acquired(result);
        Assert.Equal(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            payload.ProducerKey);

        // The unusable payload never entered the cache under the source that
        // served it, so a later run is not answered from poisoned bytes.
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(Primary.Url)]));
    }

    [Fact]
    public async Task ArchiveDeclaringTooManyEntries_IsRejected()
    {
        byte[] nupkg = TestPackageArchive.Create(
            "lib/net10.0/One.dll",
            "lib/net10.0/Two.dll",
            "lib/net10.0/Three.dll");
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new NuGetOrgPayloadHandler(nupkg));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                limits: new PackagePayloadLimits { MaxEntryCount = 2 },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
    }

    [Fact]
    public async Task ArchiveDeclaringTooMuchExpandedContent_IsRejected()
    {
        // A small archive whose entries expand far beyond it: the bound is on
        // what the archive declares it will become, not on what it weighs.
        byte[] nupkg = TestPackageArchive.CreateWithContent(
            ("lib/net10.0/Bomb.dll", new byte[512 * 1024]));
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new NuGetOrgPayloadHandler(nupkg));

        Assert.True(nupkg.Length < 64 * 1024);

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                limits: new PackagePayloadLimits { MaxExpandedBytes = 4096 },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
    }

    [Fact]
    public async Task Acquisition_ObservesCancellationDuringDownload()
    {
        var store = new InMemoryPackageStore();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(
            new NuGetOrgHandler(
                () => new StreamContent(
                    new EndlessStream(onRead: cancellation.Cancel))));

        // Cancellation requested while the body is being copied is a
        // cancellation, not a source failure that would try the next feed.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: cancellation.Token));
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
    }

    [Fact]
    public async Task TypedAcquisition_PreservesPayloadStreamTimeout()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(40),
            // If both bounds elapse, Operation correctly wins.
            OperationTimeout = TimeSpan.FromSeconds(30),
        };
        var store = new InMemoryPackageStore();
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                new GalleryPayloadHandler(
                    () => new StreamContent(
                        new StallingStream())),
                options);
        using var operation = new NuGetOperationContext(
                options.RequestTimeout,
                options.OperationTimeout,
                TestContext.Current.CancellationToken);

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => PackagePayloadAcquisition.AcquireAsync(
                    source,
                    PackageSourceCoordinate.Create(PackageId, Version),
                    store,
                    cancellationToken:
                        TestContext.Current.CancellationToken,
                    operationContext: operation));

        Assert.Equal(source.Identity, error.Producer);
        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Request,
                options.RequestTimeout),
            error.Timeout);
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
    }

    /// <summary>
    /// Content disguised as a directory entry is refused before publication
    /// too, and the next authorized source still serves the coordinate.
    /// </summary>
    [Fact]
    public async Task ArchiveHidingContentInADirectoryEntry_IsRejectedAndNotCached()
    {
        byte[] hostile = ArchiveWithNames(("lib/", new byte[8192]));
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(
            new TwoSourceHandler(
                primaryContent: hostile,
                nuGetOrgContent: nupkg));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(Primary, NuGetOrg),
                store,
                limits: new PackagePayloadLimits { MaxExpandedBytes = 16 },
                cancellationToken: TestContext.Current.CancellationToken);

        AcquiredPackagePayload payload = Acquired(result);
        Assert.Equal(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            payload.ProducerKey);
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(Primary.Url)]));
    }

    /// <summary>
    /// Rejection happens before publication, so no store of any kind is asked
    /// to commit the payload. That is what keeps the in-memory and filesystem
    /// stores from disagreeing about what a package is: neither is consulted.
    /// </summary>
    [Fact]
    public async Task RejectedPayload_ReachesNoStoreCommit()
    {
        byte[] hostile = ArchiveWithNames(
            ("../ignored.txt", "escaped"u8.ToArray()),
            ("lib/net10.0/Sample.dll", [1, 2, 3]));
        var store = new CountingPackageStore(new InMemoryPackageStore());
        using var client = new HttpClient(
            new NuGetOrgHandler(() => new ByteArrayContent(hostile)));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Equal(0, store.Commits);
    }

    /// <summary>
    /// A feed-declared package URL can carry the credential in its query, under
    /// a parameter name the feed also chooses. The request must use it exactly;
    /// nothing that prints may — and the unfamiliar name is the point, because
    /// recognizing familiar ones is what a redaction can do and still leak.
    /// </summary>
    [Fact]
    public async Task SignedPackageUrl_NeverReachesALogLine()
    {
        const string secret = "s3cr3t-signature-value";
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var handler = new ServiceIndexHandler(
            baseAddress: $"https://primary.test/flat?x={secret}",
            nupkgUrl:
                $"https://primary.test/flat/{PackageId}/{Version}/{PackageId}.{Version}.nupkg?x={secret}",
            nupkg);
        using var client = new HttpClient(handler);
        List<string> logs = [];

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(Primary),
                new InMemoryPackageStore(),
                log: line =>
                {
                    lock (logs)
                        logs.Add(line);
                },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Acquired>(result);

        // The signature travelled on the wire, exactly as the feed declared it.
        Assert.Contains(
            handler.Requests,
            url => url.Contains(secret, StringComparison.Ordinal));

        // And nowhere else.
        Assert.NotEmpty(logs);
        Assert.All(
            logs,
            line => Assert.DoesNotContain(
                secret,
                line,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// The retry path prints the URL it is retrying and, on a transport
    /// failure, the exception whose own message embeds the request URI. Both
    /// are diagnostics, and both carried the signature.
    /// </summary>
    [Fact]
    public async Task SignedPackageUrl_NeverReachesARetryFailureLogLine()
    {
        const string secret = "s3cr3t-signature-value";
        var handler = new ServiceIndexHandler(
            baseAddress: $"https://primary.test/flat?x={secret}",
            nupkgUrl:
                $"https://primary.test/flat/{PackageId}/{Version}/{PackageId}.{Version}.nupkg?x={secret}",
            nupkg: [],
            payloadStatus: HttpStatusCode.InternalServerError);
        using var client = new HttpClient(handler);
        List<string> logs = [];

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(Primary),
                new InMemoryPackageStore(),
                log: line =>
                {
                    lock (logs)
                        logs.Add(line);
                },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Contains(
            logs,
            line => line.Contains("retryable", StringComparison.Ordinal)
                || line.Contains("Max retries", StringComparison.Ordinal));
        Assert.All(
            logs,
            line => Assert.DoesNotContain(
                secret,
                line,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// A cross-origin signed URL also reaches the credential scope, which
    /// explains why it withheld the source's credentials by naming the
    /// endpoint.
    /// </summary>
    [Fact]
    public async Task CrossOriginSignedUrl_IsNotNamedInTheCredentialScopeLog()
    {
        const string secret = "s3cr3t-signature-value";
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var credentialed = new PackageSource(
            "primary",
            Primary.Url,
            new PackageSourceCredential("user", "pass"));
        var handler = new ServiceIndexHandler(
            baseAddress: $"https://elsewhere.test/flat?x={secret}",
            nupkgUrl:
                $"https://elsewhere.test/flat/{PackageId}/{Version}/{PackageId}.{Version}.nupkg?x={secret}",
            nupkg);
        using var client = new HttpClient(handler);
        List<string> logs = [];

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(credentialed),
                new InMemoryPackageStore(),
                log: line =>
                {
                    lock (logs)
                        logs.Add(line);
                },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Acquired>(result);
        Assert.Contains(
            logs,
            line => line.Contains("Withholding credentials", StringComparison.Ordinal));
        Assert.All(
            logs,
            line => Assert.DoesNotContain(
                secret,
                line,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// An explicit <c>--source</c> that matches no configured entry is built
    /// with its URL as its name, so every <c>{source.Name}</c> in a log line,
    /// a failure list, or the unavailable message re-emitted the signature the
    /// URL redaction was added to keep out of those sinks.
    /// </summary>
    [Fact]
    public async Task ExplicitUrlNamedSignedSource_NeverReachesADiagnostic()
    {
        const string secret = "s3cr3t-signature-value";
        string sourceUrl = $"https://primary.test/v3/index.json?x={secret}";
        var urlNamed = new PackageSource(sourceUrl, sourceUrl);
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var handler = new ServiceIndexHandler(
            baseAddress: $"https://primary.test/flat?x={secret}",
            nupkgUrl:
                $"https://primary.test/flat/{PackageId}/{Version}/{PackageId}.{Version}.nupkg?x={secret}",
            nupkg,
            serviceIndexUrl: sourceUrl);
        using var client = new HttpClient(handler);
        List<string> logs = [];

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(urlNamed),
                new InMemoryPackageStore(),
                log: line =>
                {
                    lock (logs)
                        logs.Add(line);
                },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Acquired>(result);
        Assert.Contains(
            handler.Requests,
            url => url.Contains(secret, StringComparison.Ordinal));
        Assert.NotEmpty(logs);
        Assert.All(
            logs,
            line => Assert.DoesNotContain(secret, line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExplicitUrlNamedSignedSource_NeverReachesAFailureOrUnavailableMessage()
    {
        const string secret = "s3cr3t-signature-value";
        string sourceUrl = $"https://primary.test/v3/index.json?x={secret}";
        var urlNamed = new PackageSource(sourceUrl, sourceUrl);
        var handler = new ServiceIndexHandler(
            baseAddress: $"https://primary.test/flat?x={secret}",
            nupkgUrl:
                $"https://primary.test/flat/{PackageId}/{Version}/{PackageId}.{Version}.nupkg?x={secret}",
            nupkg: [],
            payloadStatus: HttpStatusCode.InternalServerError,
            serviceIndexUrl: sourceUrl);
        using var client = new HttpClient(handler);
        List<string> logs = [];

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(urlNamed),
                new InMemoryPackageStore(),
                log: line =>
                {
                    lock (logs)
                        logs.Add(line);
                },
                cancellationToken: TestContext.Current.CancellationToken);

        // The unavailable message lists every source that failed, by name.
        var unavailable =
            Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.DoesNotContain(
            secret,
            unavailable.Message,
            StringComparison.Ordinal);
        Assert.All(
            logs,
            line => Assert.DoesNotContain(secret, line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TransferPolicy_ReservesBeforeBodyReadAndCompletesAfterCommit()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        bool bodyRead = false;
        var store = new CountingPackageStore(new InMemoryPackageStore());
        var policy = new RecordingTransferPolicy(
            onReserve: transfer =>
            {
                Assert.False(bodyRead);
                Assert.Equal(nupkg.LongLength, transfer.AdvertisedLength);
            },
            onComplete: () => Assert.Equal(1, store.Commits));
        using var client = new HttpClient(
            new NuGetOrgHandler(() =>
            {
                var content = new StreamContent(
                    new ReadTrackingStream(
                        nupkg,
                        () => bodyRead = true));
                content.Headers.ContentLength = nupkg.LongLength;
                return content;
            }));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken,
                transferPolicy: policy);

        Assert.IsType<PackagePayloadResult.Acquired>(result);
        Assert.True(bodyRead);
        Assert.True(policy.Reservation.Completed);
        Assert.True(policy.Reservation.Disposed);
    }

    // The reservation the host makes from the advertised length is the allocation the body read
    // performs, so a host accounting for its own memory is not told one number and handed another.
    [Fact]
    public async Task AdvertisedLengthPayload_IsReservedAndReadAtThatExactLength()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        long? reserved = null;
        var policy = new RecordingTransferPolicy(
            onReserve: transfer => reserved = transfer.AdvertisedLength);
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(
            new NuGetOrgHandler(() =>
            {
                // Fragmented, so a reader that trusted one ReadAsync call would come up short.
                var content = new StreamContent(new FragmentedStream(nupkg, fragment: 13));
                content.Headers.ContentLength = nupkg.LongLength;
                return content;
            }));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken,
                transferPolicy: policy);

        var acquired = Assert.IsType<PackagePayloadResult.Acquired>(result);
        Assert.Equal(nupkg.LongLength, reserved);
        Assert.True(policy.Reservation.Completed);
        Assert.True(acquired.Payload.Content.TryOpenArchive(out Stream? committed));
        using (committed)
        {
            using var buffer = new MemoryStream();
            await committed!.CopyToAsync(buffer, TestContext.Current.CancellationToken);
            Assert.Equal(nupkg, buffer.ToArray());
        }
    }

    [Fact]
    public async Task InMemoryAdmission_DoesNotDuplicateTheRetainedArchive()
    {
        byte[] content = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(content);
        byte[] nupkg = TestPackageArchive.CreateWithContent(
            ("lib/net10.0/Sample.dll", content));
        var package = new InMemoryPackageContent(
            nupkg,
            fromCache: true,
            "test");

        long before = GC.GetAllocatedBytesForCurrentThread();
        PackageContentAdmission.Outcome outcome =
            await PackageContentAdmission.EvaluateAsync(
                package,
                PackagePayloadLimits.Default,
                TestContext.Current.CancellationToken);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(PackageContentAdmission.Outcome.Admissible, outcome);
        Assert.True(
            allocated < nupkg.LongLength,
            $"admission allocated {allocated} bytes for a {nupkg.LongLength}-byte retained archive");
    }

    // A body that stops short of its own declaration is a truncated transfer, not a shorter
    // package: the source fails, nothing is committed, and the reservation is abandoned.
    [Fact]
    public async Task PayloadShorterThanItsAdvertisedLength_IsATypedSourceFailure()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        List<string> log = [];
        var policy = new RecordingTransferPolicy();
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(
            new NuGetOrgHandler(() =>
            {
                var content = new StreamContent(
                    new MemoryStream(nupkg, writable: false));
                content.Headers.ContentLength = nupkg.LongLength + 16;
                return content;
            }));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                log: log.Add,
                cancellationToken: TestContext.Current.CancellationToken,
                transferPolicy: policy);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Contains(
            log,
            line => line.Contains(
                "did not send the package payload length it advertised",
                StringComparison.Ordinal));
        Assert.False(policy.Reservation.Completed);
        Assert.True(policy.Reservation.Disposed);
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
    }

    // The overrun case is the one an "accept the declared prefix" reader would silently admit: the
    // bytes up to the declaration are a valid archive, and the extra bytes would disappear.
    [Fact]
    public async Task PayloadLongerThanItsAdvertisedLength_IsATypedSourceFailure()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        byte[] overrun = [.. nupkg, 0x00, 0x01, 0x02, 0x03];
        List<string> log = [];
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(
            new NuGetOrgHandler(() =>
            {
                var content = new StreamContent(
                    new MemoryStream(overrun, writable: false));
                content.Headers.ContentLength = nupkg.LongLength;
                return content;
            }));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                log: log.Add,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Contains(
            log,
            line => line.Contains(
                "did not send the package payload length it advertised",
                StringComparison.Ordinal));
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
    }

    [Fact]
    public async Task TransferPolicy_RejectedPayloadDisposesWithoutCompleting()
    {
        byte[] malformed = [0x01, 0x02, 0x03];
        var policy = new RecordingTransferPolicy();
        using var client = new HttpClient(
            new NuGetOrgHandler(() => new ByteArrayContent(malformed)));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                new InMemoryPackageStore(),
                cancellationToken: TestContext.Current.CancellationToken,
                transferPolicy: policy);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.False(policy.Reservation.Completed);
        Assert.True(policy.Reservation.Disposed);
    }

    [Fact]
    public async Task TransferPolicy_CanRequireContentLengthBeforeBodyRead()
    {
        bool bodyRead = false;
        var policy = new RecordingTransferPolicy(
            onReserve: transfer =>
            {
                Assert.Null(transfer.AdvertisedLength);
                throw new InvalidOperationException("A length is required.");
            });
        using var client = new HttpClient(
            new NuGetOrgHandler(
                () => new StreamContent(
                    new EndlessStream(() => bodyRead = true))));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PackagePayloadAcquisition.AcquireAsync(
                    client,
                    Coordinate(NuGetOrg),
                    new InMemoryPackageStore(),
                    cancellationToken:
                        TestContext.Current.CancellationToken,
                    transferPolicy: policy));

        Assert.Equal("A length is required.", failure.Message);
        Assert.False(bodyRead);
    }

    [Fact]
    public async Task BodyTransferDeadline_DoesNotBoundCacheCommit()
    {
        byte[] nupkg = TestPackageArchive.Create(
            "lib/net10.0/Sample.dll");
        var store = new DelayedCommitStore(
            TimeSpan.FromMilliseconds(150));
        using var client = new HttpClient(
            new NuGetOrgPayloadHandler(nupkg))
        {
            Timeout = TimeSpan.FromMilliseconds(50),
        };

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            PackagePayloadOrigin.Download,
            Acquired(result).Origin);
    }

    static AcquiredPackagePayload Acquired(PackagePayloadResult result)
        => Assert.IsType<PackagePayloadResult.Acquired>(result).Payload;

    static ResolvedPackageCoordinate Coordinate(
        params PackageSource[] sources)
        => new(
            PackageId,
            Version,
            "net10.0",
            runtimeIdentifier: null,
            sources,
            wasFloating: false);

    sealed class NuGetOrgPayloadHandler(byte[] nupkg) : HttpMessageHandler
    {
        readonly List<string> _requests = [];

        internal IReadOnlyList<string> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            lock (_requests)
                _requests.Add(url);

            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{PackageId}/{Version}/{PackageId}.{Version}.nupkg",
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
    /// A private feed whose service index declares an arbitrary
    /// <c>PackageBaseAddress</c>, so a test can shape the resource metadata the
    /// URL is composed from.
    /// </summary>
    sealed class ServiceIndexHandler(
        string baseAddress,
        string nupkgUrl,
        byte[] nupkg,
        HttpStatusCode payloadStatus = HttpStatusCode.OK,
        string? serviceIndexUrl = null) : HttpMessageHandler
    {
        readonly List<string> _requests = [];

        internal IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                    return [.. _requests];
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            lock (_requests)
                _requests.Add(url);

            if (url.Equals(
                serviceIndexUrl ?? Primary.Url,
                StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $$"""
                            {"resources":[{"@id":"{{baseAddress}}","@type":"PackageBaseAddress/3.0.0"}]}
                            """),
                    });
            }

            if (!url.Equals(nupkgUrl, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(
                payloadStatus == HttpStatusCode.OK
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nupkg),
                    }
                    : new HttpResponseMessage(payloadStatus));
        }
    }

    /// <summary>
    /// Builds an archive with entry names a compliant writer would refuse to
    /// produce, which is what an adversarial feed can serve.
    /// </summary>
    static byte[] ArchiveWithNames(
        params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(
            buffer,
            System.IO.Compression.ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(name).Open();
                stream.Write(content, 0, content.Length);
            }
        }

        return buffer.ToArray();
    }

    static byte[] WithCompressionMethod(byte[] archive, ushort method)
    {
        byte[] rewritten = (byte[])archive.Clone();
        for (int offset = 0; offset + 4 <= rewritten.Length; offset++)
        {
            uint signature =
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                    rewritten.AsSpan(offset));
            if (signature == 0x04034B50)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                    rewritten.AsSpan(offset + 8),
                    method);
            }
            else if (signature == 0x02014B50)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                    rewritten.AsSpan(offset + 10),
                    method);
            }
        }

        return rewritten;
    }

    /// <summary>
    /// Counts commits so a test can prove a rejected payload was never offered
    /// to a store, whichever store the host supplies.
    /// </summary>
    sealed class CountingPackageStore(IPackageStore inner) : IPackageStore
    {
        int _commits;

        internal int Commits => Volatile.Read(ref _commits);

        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null)
            => inner.TryGetCached(
                packageName,
                version,
                allowedSourceKeys,
                log);

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commits);
            return inner.CommitAsync(
                packageName,
                version,
                sourceKey,
                nupkg,
                cancellationToken);
        }
    }

    sealed class CommitWinnerStore(IPackageContent winner) : IPackageStore
    {
        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null)
            => null;

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(winner);
    }

    /// <summary>
    /// Records each <see cref="IPackageStore.EnumerateCached"/> allowed-source
    /// argument so callers can prove they pass the full producer list once.
    /// </summary>
    sealed class RecordingEnumerateStore : IPackageStore
    {
        internal List<IReadOnlyList<string>?> EnumerateAllowedSourceKeys { get; } = [];

        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null)
            => null;

        public IEnumerable<IPackageContent> EnumerateCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null)
        {
            EnumerateAllowedSourceKeys.Add(allowedSourceKeys);
            yield break;
        }

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "RecordingEnumerateStore does not commit.");
    }

    /// <summary>
    /// Answers the nuget.org flat-container URL for this coordinate with
    /// caller-supplied content, so a test can shape the response body
    /// (advertised length, endlessness, cancellation) rather than only its
    /// bytes.
    /// </summary>
    sealed class NuGetOrgHandler(Func<HttpContent> content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            return Task.FromResult(
                url.Equals(NupkgUrl, StringComparison.OrdinalIgnoreCase)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = content(),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    sealed class GalleryPayloadHandler(Func<HttpContent> content)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string expected =
                $"https://globalcdn.nuget.org/packages/{PackageId}.{Version}.nupkg";
            return Task.FromResult(
                request.RequestUri!.ToString().Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = content(),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    sealed class RecordingTransferPolicy(
        Action<PackagePayloadTransfer>? onReserve = null,
        Action? onComplete = null)
        : IPackagePayloadTransferPolicy
    {
        internal RecordingReservation Reservation { get; } =
            new(onComplete);

        public IPackagePayloadReservation Reserve(
            PackagePayloadTransfer transfer)
        {
            onReserve?.Invoke(transfer);
            return Reservation;
        }
    }

    sealed class RecordingReservation(Action? onComplete)
        : IPackagePayloadReservation
    {
        internal bool Completed { get; private set; }
        internal bool Disposed { get; private set; }

        public void Complete()
        {
            onComplete?.Invoke();
            Completed = true;
        }

        public void Dispose() => Disposed = true;
    }

    sealed class ReadTrackingStream(byte[] bytes, Action onRead)
        : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;

        public override int Read(byte[] buffer, int offset, int count)
        {
            onRead();
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            onRead();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    sealed class DelayedCommitStore(TimeSpan delay) : IPackageStore
    {
        readonly InMemoryPackageStore _inner = new();

        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            _inner.TryGetCached(
                packageName,
                version,
                allowedSourceKeys,
                log);

        public IEnumerable<IPackageContent> EnumerateCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            _inner.EnumerateCached(
                packageName,
                version,
                allowedSourceKeys,
                log);

        public async ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return await _inner.CommitAsync(
                packageName,
                version,
                sourceKey,
                nupkg,
                cancellationToken);
        }
    }

    /// <summary>
    /// A non-seekable stream that hands out at most <c>fragment</c> bytes per read, the way a
    /// network body arrives.
    /// </summary>
    sealed class FragmentedStream(byte[] body, int fragment) : Stream
    {
        int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int available = Math.Min(Math.Min(fragment, buffer.Length), body.Length - _position);
            if (available <= 0)
                return 0;

            body.AsSpan(_position, available).CopyTo(buffer);
            _position += available;
            return available;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// A non-seekable stream that never ends, so the response advertises no
    /// length and only a bound on bytes actually read can stop it.
    /// </summary>
    sealed class EndlessStream(Action? onRead = null) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            onRead?.Invoke();
            buffer.AsSpan(offset, count).Clear();
            return count;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            onRead?.Invoke();
            buffer.Span.Clear();
            return ValueTask.FromResult(buffer.Length);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Seekable stream whose <see cref="Length"/> under-reports the readable body.
    /// </summary>
    sealed class UnderreportingLengthStream(byte[] body, long reportedLength) : Stream
    {
        int _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => reportedLength;

        public override long Position
        {
            get => _position;
            set => _position = (int)value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= body.Length)
                return 0;
            int n = Math.Min(count, body.Length - _position);
            body.AsSpan(_position, n).CopyTo(buffer.AsSpan(offset, n));
            _position += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position >= body.Length)
                return ValueTask.FromResult(0);
            int n = Math.Min(buffer.Length, body.Length - _position);
            body.AsSpan(_position, n).CopyTo(buffer.Span);
            _position += n;
            return ValueTask.FromResult(n);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Two feeds with their own flat containers, so a payload one source
    /// cannot serve usably can be answered by the next authorized source.
    /// </summary>
    sealed class TwoSourceHandler(
        byte[] primaryContent,
        byte[] nuGetOrgContent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url.Equals(Primary.Url, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """
                            {"resources":[{"@id":"https://primary.test/flat/","@type":"PackageBaseAddress/3.0.0"}]}
                            """),
                    });
            }

            if (url.Equals(
                $"https://primary.test/flat/{PackageId}/{Version}/{PackageId}.{Version}.nupkg",
                StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(primaryContent),
                    });
            }

            return Task.FromResult(
                url.Equals(NupkgUrl, StringComparison.OrdinalIgnoreCase)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nuGetOrgContent),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
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
