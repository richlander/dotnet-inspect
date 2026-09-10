using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed class PackageContentDigestTests
{
    const string PackageId = "digest.sample";
    const string Version = "1.0.0";
    const string Producer = "digest-tests";

    [Fact]
    public void PackageContentDigest_ChargesColdPassAndReusesGenerationValue()
    {
        byte[] archive = Archive([1, 2, 3]);
        var content = new InMemoryPackageContent(
            archive,
            fromCache: false,
            Producer);
        var charges = new List<long>();

        PackageContentDigest first = Digest(Payload(content), charges.Add);
        InMemoryPackageContent cacheHit = content.AsCacheHit();
        PackageContentDigest second = Digest(
            Payload(cacheHit),
            _ => Assert.Fail("Warm digest charged again."));
        var resolvedPayload = new AcquiredPackagePayload(
            new ResolvedPackageCoordinate(
                PackageId,
                Version,
                framework: null,
                runtimeIdentifier: null,
                [PackageSource.NuGetOrg],
                wasFloating: false),
            cacheHit,
            Producer,
            PackagePayloadOrigin.Cache);
        PackageContentDigest third = Assert.IsType<PackageContentDigest>(
            resolvedPayload.GetContentDigest(
                _ => Assert.Fail("Resolved-payload warm digest charged again."),
                TestContext.Current.CancellationToken));

        Assert.NotSame(content, cacheHit);
        Assert.Same(content.GenerationIdentity, cacheHit.GenerationIdentity);
        Assert.Same(first, second);
        Assert.Same(first, third);
        Assert.Same(content.GenerationIdentity, first.Generation);
        Assert.Equal("SHA-256", first.Algorithm);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(archive)),
            first.HexValue);
        Assert.Equal([archive.LongLength], charges);
    }

    [Fact]
    public async Task PackageContentDigest_ChargeFailureDoesNotPublish()
    {
        byte[] archive = Archive([4, 5, 6]);
        AcquiredPackageSourcePayload payload = Payload(
            new InMemoryPackageContent(archive, fromCache: false, Producer));
        var failure = new InvalidOperationException("Operation budget refused.");

        Assert.Same(
            failure,
            Assert.Throws<InvalidOperationException>(
                () => payload.GetContentDigest(
                    _ => throw failure,
                    TestContext.Current.CancellationToken)));
        var charges = new List<long>();

        PackageContentDigest digest = Digest(payload, charges.Add);
        Assert.Same(
            digest,
            Digest(payload, _ => Assert.Fail("Failed charge published a value.")));
        Assert.Equal([archive.LongLength], charges);

        string root = Directory.CreateTempSubdirectory(
            "package-content-digest-charge-").FullName;
        try
        {
            IPackageContent fileSystem =
                await CommitFileSystemAsync(archive, root);
            AcquiredPackageSourcePayload acquired =
                Assert.IsType<AcquiredPackageSourcePayload>(
                    await AcquireCachedAsync(fileSystem));
            var ioFailure = new IOException("Charging failed.");
            var accessFailure = new UnauthorizedAccessException(
                "Charging failed.");

            Assert.Same(
                ioFailure,
                Assert.Throws<IOException>(
                    () => acquired.GetContentDigest(
                        _ => throw ioFailure,
                        TestContext.Current.CancellationToken)));
            Assert.Same(
                accessFailure,
                Assert.Throws<UnauthorizedAccessException>(
                    () => acquired.GetContentDigest(
                        _ => throw accessFailure,
                        TestContext.Current.CancellationToken)));

            Assert.NotNull(acquired.GetContentDigest(
                _ => { },
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageContentDigest_ReentrantChargeFailsVisibly()
    {
        AcquiredPackageSourcePayload payload = Payload(
            new InMemoryPackageContent(
                Archive([4, 5, 6]),
                fromCache: false,
                Producer));

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(
                () => payload.GetContentDigest(
                    _ => payload.GetContentDigest(
                        _ => Assert.Fail("Nested charge should not run."),
                        TestContext.Current.CancellationToken),
                    TestContext.Current.CancellationToken));

        Assert.Contains("cannot re-enter", failure.Message);
        Assert.NotNull(payload.GetContentDigest(
            _ => { },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PackageContentDigest_CancellationAfterChargeMemoizesButCancelsCaller()
    {
        byte[] archive = Archive([7, 8, 9]);
        AcquiredPackageSourcePayload payload = Payload(
            new InMemoryPackageContent(archive, fromCache: false, Producer));
        using var cancellation = new CancellationTokenSource();
        int charges = 0;

        Assert.Throws<OperationCanceledException>(
            () => payload.GetContentDigest(
                _ =>
                {
                    charges++;
                    cancellation.Cancel();
                },
                cancellation.Token));

        PackageContentDigest digest = Digest(
            payload,
            _ => Assert.Fail("Completed cancelled work charged again."));
        Assert.Equal(1, charges);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(archive)),
            digest.HexValue);
    }

    [Fact]
    public async Task PackageContentDigest_ConcurrentRequestsShareColdPass()
    {
        AcquiredPackageSourcePayload payload = Payload(
            new InMemoryPackageContent(
                Archive([10, 11, 12]),
                fromCache: false,
                Producer));
        int charges = 0;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;

        Task<PackageContentDigest?>[] requests = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(
                () => payload.GetContentDigest(
                    _ => Interlocked.Increment(ref charges),
                    cancellationToken),
                cancellationToken))
            .ToArray();

        PackageContentDigest?[] digests = await Task.WhenAll(requests);
        PackageContentDigest first = Assert.IsType<PackageContentDigest>(
            digests[0]);
        Assert.All(
            digests,
            digest => Assert.Same(
                first,
                Assert.IsType<PackageContentDigest>(digest)));
        Assert.Equal(1, charges);
    }

    [Fact]
    public void PackageContentDigest_ReplacementChangesGenerationAndDigestSubject()
    {
        byte[] w = Archive([1, 1, 1]);
        byte[] s = Archive([2, 2, 2]);
        var firstW = new InMemoryPackageContent(w, fromCache: false, Producer);
        var replacementS = new InMemoryPackageContent(s, fromCache: false, Producer);
        var secondW = new InMemoryPackageContent(w, fromCache: false, Producer);

        PackageContentDigest first = Digest(Payload(firstW));
        PackageContentDigest middle = Digest(Payload(replacementS));
        PackageContentDigest last = Digest(Payload(secondW));

        Assert.NotSame(first.Generation, middle.Generation);
        Assert.NotSame(middle.Generation, last.Generation);
        Assert.NotSame(first.Generation, last.Generation);
        Assert.Same(firstW.GenerationIdentity, first.Generation);
        Assert.Same(replacementS.GenerationIdentity, middle.Generation);
        Assert.Same(secondW.GenerationIdentity, last.Generation);
        Assert.NotEqual(first.HexValue, middle.HexValue);
        Assert.Equal(first.HexValue, last.HexValue);
        Assert.NotSame(first, last);
    }

    [Fact]
    public async Task PackageContentDigest_ProductOwnedArchiveBindsAdmittedTreeAcrossHosts()
    {
        byte[] archive = TestPackageArchive.CreateWithContent(
            ("lib/net11.0/Sample.dll", new byte[] { 1, 2, 3, 4 }),
            ("Digest.Sample.nuspec", """<package />"""u8.ToArray()));
        string root = Directory.CreateTempSubdirectory(
            "package-content-digest-").FullName;
        try
        {
            IPackageContent fileSystem =
                await CommitFileSystemAsync(archive, root);
            var inMemory = new InMemoryPackageContent(
                archive,
                fromCache: true,
                Producer);

            AcquiredPackageSourcePayload admittedFileSystem =
                Assert.IsType<AcquiredPackageSourcePayload>(
                    await AcquireCachedAsync(fileSystem));
            AcquiredPackageSourcePayload admittedInMemory =
                Assert.IsType<AcquiredPackageSourcePayload>(
                    await AcquireCachedAsync(inMemory));
            PackageContentDigest desktop = Digest(admittedFileSystem);
            PackageContentDigest browser = Digest(admittedInMemory);

            Assert.Same(fileSystem.GenerationIdentity, desktop.Generation);
            Assert.Same(inMemory.GenerationIdentity, browser.Generation);
            Assert.Equal(
                Convert.ToHexStringLower(SHA256.HashData(archive)),
                desktop.HexValue);
            Assert.Equal(desktop.HexValue, browser.HexValue);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PackageContentDigest_MissingRetainedArchiveDoesNotPublish()
    {
        byte[] archive = Archive([5, 5, 5]);
        string root = Directory.CreateTempSubdirectory(
            "package-content-digest-missing-").FullName;
        try
        {
            IPackageContent content =
                await CommitFileSystemAsync(archive, root);
            AcquiredPackageSourcePayload acquired =
                Assert.IsType<AcquiredPackageSourcePayload>(
                    await AcquireCachedAsync(content));
            string nupkg = Assert.IsType<string>(content.NupkgPath);
            File.Delete(nupkg);
            int charges = 0;

            Assert.Null(acquired.GetContentDigest(
                _ => charges++,
                TestContext.Current.CancellationToken));
            Assert.Equal(0, charges);

            File.WriteAllBytes(nupkg, archive);
            PackageContentDigest digest = Digest(acquired, _ => charges++);
            Assert.Equal(1, charges);
            Assert.Equal(
                Convert.ToHexStringLower(SHA256.HashData(archive)),
                digest.HexValue);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PackageContentDigest_ForeignGlobalPackagesTreesAreIneligible()
    {
        byte[] archive = TestPackageArchive.CreateWithContent(
            ("lib/net11.0/Sample.dll", new byte[] { 1, 2, 3 }),
            ("Digest.Sample.nuspec", """<package />"""u8.ToArray()));
        string root = Directory.CreateTempSubdirectory(
            "package-content-digest-foreign-").FullName;
        string withArchive = Path.Combine(root, "with-archive");
        string withoutArchive = Path.Combine(root, "without-archive");
        string nupkg = Path.Combine(root, $"{PackageId}.{Version}.nupkg");
        try
        {
            File.WriteAllBytes(nupkg, archive);
            ZipFile.ExtractToDirectory(nupkg, withArchive);
            Directory.CreateDirectory(withoutArchive);
            File.WriteAllText(
                Path.Combine(withoutArchive, "Digest.Sample.nuspec"),
                "<package />");

            AcquiredPackageSourcePayload admittedWithArchive =
                Assert.IsType<AcquiredPackageSourcePayload>(
                    await AcquireCachedAsync(new FileSystemPackageContent(
                        withArchive,
                        nupkg,
                        fromCache: true,
                        Producer)));
            AcquiredPackageSourcePayload admittedWithoutArchive =
                Assert.IsType<AcquiredPackageSourcePayload>(
                    await AcquireCachedAsync(new FileSystemPackageContent(
                        withoutArchive,
                        nupkgPath: null,
                        fromCache: true,
                        Producer)));
            int charges = 0;

            Assert.Null(admittedWithArchive.GetContentDigest(
                _ => charges++,
                TestContext.Current.CancellationToken));
            Assert.Null(admittedWithoutArchive.GetContentDigest(
                _ => charges++,
                TestContext.Current.CancellationToken));
            Assert.Equal(0, charges);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PackageContentDigest_OrdinaryAcquisitionDoesNotHash()
    {
        var content = new TrackingDigestContent(
            new InMemoryPackageContent(
                Archive([13, 14, 15]),
                fromCache: true,
                Producer));

        AcquiredPackageSourcePayload acquired =
            Assert.IsType<AcquiredPackageSourcePayload>(
                await AcquireCachedAsync(content));

        Assert.Equal(0, content.DigestRequests);
        Assert.NotNull(acquired.GetContentDigest(
            _ => { },
            TestContext.Current.CancellationToken));
        Assert.Equal(1, content.DigestRequests);
    }

    static byte[] Archive(byte[] content) =>
        TestPackageArchive.CreateWithContent(
            ("lib/net11.0/Sample.dll", content),
            ("Digest.Sample.nuspec", """<package />"""u8.ToArray()));

    static AcquiredPackageSourcePayload Payload(IPackageContent content) =>
        new(
            PackageSourceCoordinate.Create(PackageId, Version),
            content,
            Producer,
            PackagePayloadOrigin.Download);

    static PackageContentDigest Digest(
        AcquiredPackageSourcePayload payload,
        Action<long>? chargeWork = null) =>
        Assert.IsType<PackageContentDigest>(
            payload.GetContentDigest(
                chargeWork ?? (_ => { }),
                TestContext.Current.CancellationToken));

    static ValueTask<AcquiredPackageSourcePayload?> AcquireCachedAsync(
        IPackageContent content) =>
        PackagePayloadAcquisition.TryGetCachedAsync(
            PackageSourceCoordinate.Create(PackageId, Version),
            Producer,
            new SingleContentStore(content),
            limits: null,
            log: null,
            TestContext.Current.CancellationToken);

    static async ValueTask<IPackageContent> CommitFileSystemAsync(
        byte[] archive,
        string root)
    {
        using var archiveStream = new MemoryStream(archive, writable: false);
        return await FileSystemPackageStore.CommitAsync(
            PackageId,
            Version,
            archiveStream,
            () => Directory.CreateDirectory(
                Path.Combine(root, "commit-staging")).FullName,
            (stagedExtract, stagedNupkg) =>
                NuGetCache.CommitPackageToSlot(
                    stagedExtract,
                    stagedNupkg,
                    PackageId,
                    Version,
                    Producer,
                    Path.Combine(root, "committed"),
                    "digest-test-commit",
                    useAppCache: false),
            TestContext.Current.CancellationToken);
    }

    sealed class SingleContentStore(IPackageContent content) : IPackageStore
    {
        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            allowedSourceKeys?.Contains(content.ProducerKey, StringComparer.Ordinal)
                == true
                ? content
                : null;

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    sealed class TrackingDigestContent(InMemoryPackageContent inner) :
        IPackageContent,
        IPackageContentDigestSource
    {
        int _digestRequests;

        internal int DigestRequests => Volatile.Read(ref _digestRequests);
        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public PackageContentGenerationIdentity GenerationIdentity =>
            inner.GenerationIdentity;
        public bool RequiresArchiveTreeMatch => inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenArchive(out stream);

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenEntry(relativePath, out stream);

        public IEnumerable<string> EnumerateEntries() =>
            inner.EnumerateEntries();

        PackageContentDigest? IPackageContentDigestSource.GetContentDigest(
            Action<long> chargeWork,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _digestRequests);
            return ((IPackageContentDigestSource)inner)
                .GetContentDigest(chargeWork, cancellationToken);
        }
    }
}
