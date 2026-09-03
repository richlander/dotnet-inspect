using System.Net;
using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class SymbolPackageDownloaderTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-symbol-tests-{Guid.NewGuid():N}");

    public SymbolPackageDownloaderTests()
    {
        NuGetCache.Initialize("dotnet-inspect", _cacheDir, skipNuGetCache: true);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true, false)]
    [InlineData(HttpStatusCode.Forbidden, true, true)]
    [InlineData(HttpStatusCode.Unauthorized, false, true)]
    [InlineData(HttpStatusCode.TooManyRequests, false, true)]
    [InlineData(HttpStatusCode.Gone, false, true)]
    public async Task DownloadPdbAsync_CachePreservesAbsenceAndFailure(
        HttpStatusCode statusCode,
        bool expectedCached,
        bool expectedFailure)
    {
        var handler = new CountingHandler(_ => new HttpResponseMessage(statusCode));
        using var client = new HttpClient(handler);
        var downloader = new SymbolPackageDownloader(client);
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        var first = await downloader.DownloadPdbAsync(
            guid, pdbAge: 1, pdbFileName: "Missing.pdb", isPortable: true,
            assemblyPath: "/tmp/Missing.dll",
            packageName: "Example.Package",
            packageVersion: "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        var firstCount = handler.RequestCount;

        PdbDownloadResult second;
        bool cachedFailure;
        using (FeedFailureTelemetry.Scope(mergeIntoParent: false))
        {
            second = await downloader.DownloadPdbAsync(
                guid, pdbAge: 1, pdbFileName: "Missing.pdb", isPortable: true,
                assemblyPath: "/tmp/Missing.dll",
                packageName: "Example.Package",
                packageVersion: "1.0.0",
                cancellationToken: TestContext.Current.CancellationToken);
            cachedFailure =
                FeedFailureTelemetry.Current is { HasFailures: true };
        }

        Assert.Null(first.PdbFilePath);
        Assert.Null(second.PdbFilePath);
        Assert.True(firstCount > 0);
        Assert.Equal(
            expectedCached,
            handler.RequestCount == firstCount);
        Assert.Equal(expectedFailure, cachedFailure);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task DownloadPdbAsync_LegacyOperationalMissIsRetried(
        HttpStatusCode statusCode)
    {
        var handler = new CountingHandler(
            _ => new HttpResponseMessage(statusCode));
        using var client = new HttpClient(handler);
        var downloader = new SymbolPackageDownloader(client);
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        await downloader.DownloadPdbAsync(
            guid,
            pdbAge: 1,
            pdbFileName: "Legacy.pdb",
            isPortable: true,
            assemblyPath: "/tmp/Legacy.dll",
            cancellationToken: TestContext.Current.CancellationToken);
        int firstCount = handler.RequestCount;
        foreach (string key in handler.RequestUris)
        {
            CoreCache.Set(
                "symbol-misses",
                key,
                ((int)statusCode).ToString(),
                extension: "miss");
        }

        bool failure;
        using (FeedFailureTelemetry.Scope(mergeIntoParent: false))
        {
            await downloader.DownloadPdbAsync(
                guid,
                pdbAge: 1,
                pdbFileName: "Legacy.pdb",
                isPortable: true,
                assemblyPath: "/tmp/Legacy.dll",
                cancellationToken: TestContext.Current.CancellationToken);
            failure = FeedFailureTelemetry.Current is { HasFailures: true };
        }

        Assert.True(firstCount > 0);
        Assert.True(handler.RequestCount > firstCount);
        Assert.True(failure);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("some/dir/")]
    public async Task DownloadPdbAsync_UnusablePdbFileName_SkipsSymbolServersButStillAttemptsSnupkg(string pdbFileName)
    {
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var downloader = new SymbolPackageDownloader(client);
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        var result = await downloader.DownloadPdbAsync(
            guid, pdbAge: 1, pdbFileName: pdbFileName, isPortable: true,
            assemblyPath: "/tmp/Missing.dll",
            packageName: "Example.Package",
            packageVersion: "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.PdbFilePath);
        // snupkg acquisition is keyed off assembly name + GUID, so an unusable
        // PDB file name must not suppress it.
        Assert.Contains(handler.RequestUris, u => u.Contains(".snupkg", StringComparison.Ordinal));
        // But no symbol-server request is ever built from the unusable name.
        Assert.DoesNotContain(handler.RequestUris, u => u.Contains("msdl.microsoft.com", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.RequestUris, u => u.Contains("symbols.nuget.org", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadPdbAsync_NormalizesCodeViewPathForSymbolServerUrl()
    {
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var downloader = new SymbolPackageDownloader(client);
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        await downloader.DownloadPdbAsync(
            guid, pdbAge: 1,
            pdbFileName: @"D:\a\_work\1\s\artifacts\obj\System.Text.Json.pdb",
            isPortable: true,
            assemblyPath: "/tmp/System.Text.Json.dll",
            packageName: "System.Text.Json",
            packageVersion: "8.0.0",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(handler.RequestUris, uri =>
        {
            Assert.DoesNotContain("D:", uri);
            Assert.DoesNotContain("_work", uri);
        });
        Assert.Contains(handler.RequestUris, uri =>
            uri.Contains("/System.Text.Json.pdb/00112233445566778899AABBCCDDEEFFFFFFFFFF/System.Text.Json.pdb", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AcquirePdbAsync_InMemoryStore_ReturnsRepeatableDottedNameContent()
    {
        var guid = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var (pdbBytes, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(guid);
        byte[] snupkg =
            SnupkgPdbReaderTests.MakeSnupkg(
                ("lib/net10.0/System.Text.Json.pdb", pdbBytes));
        var handler = new CountingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith(
                    ".snupkg",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(snupkg),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler);
        var store = new InMemoryPdbStore();
        var downloader = new SymbolPackageDownloader(client, store);

        PortablePdbAcquisitionResult first =
            await downloader.AcquirePdbAsync(
                guid,
                pdbAge: 1,
                pdbFileName: "System.Text.Json.pdb",
                isPortable: true,
                assemblyName: "Wrong.Fallback.Name",
                packageName: "Example.Package",
                packageVersion: "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var acquired =
            Assert.IsType<PortablePdbAcquisitionResult.Acquired>(
                first);
        Assert.Null(acquired.Pdb.LocalPath);
        Assert.Equal("nuget.org", acquired.Pdb.SymbolServer);
        Assert.False(acquired.Pdb.FromCache);
        await using (Stream content =
                     await acquired.Pdb.OpenReadAsync(
                         TestContext.Current.CancellationToken))
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(
                buffer,
                TestContext.Current.CancellationToken);
            Assert.Equal(pdbBytes, buffer.ToArray());
        }

        using var offlineClient =
            new HttpClient(new ThrowingHandler());
        var cachedDownloader =
            new SymbolPackageDownloader(offlineClient, store);
        PortablePdbAcquisitionResult second =
            await cachedDownloader.AcquirePdbAsync(
                guid,
                pdbAge: 1,
                pdbFileName: "System.Text.Json.pdb",
                isPortable: true,
                assemblyName: "Wrong.Fallback.Name",
                packageName: "Example.Package",
                packageVersion: "1.0.0",
                cacheOnly: true,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var cached =
            Assert.IsType<PortablePdbAcquisitionResult.Acquired>(
                second);
        Assert.True(cached.Pdb.FromCache);
        Assert.Equal("nuget.org", cached.Pdb.SymbolServer);
    }

    [Fact]
    public async Task AcquirePdbAsync_LimitedHostRejectsOversizedSymbolPackage()
    {
        var guid = Guid.NewGuid();
        var handler = new CountingHandler(request =>
            request.RequestUri?.AbsolutePath.EndsWith(
                ".snupkg",
                StringComparison.OrdinalIgnoreCase) == true
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[65]),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var downloader = new SymbolPackageDownloader(
            client,
            new InMemoryPdbStore(),
            new UniformPackageSourceAuthorization(
                [NuGetFetch.PackageSource.NuGetOrg]),
            new SymbolAcquisitionLimits(
                maxSymbolPackageBytes: 64,
                maxPortablePdbBytes: 32,
                maxSymbolPackageEntries: 8));
        using var failureScope =
            FeedFailureTelemetry.Scope(mergeIntoParent: false);
        FeedFailureCollector failures = FeedFailureTelemetry.Current!;

        PortablePdbAcquisitionResult result =
            await downloader.AcquirePdbAsync(
                guid,
                pdbAge: 1,
                pdbFileName: "Example.pdb",
                isPortable: true,
                assemblyName: "Example",
                packageName: "Example.Package",
                packageVersion: "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PortablePdbAcquisitionResult.Unavailable>(result);
        Assert.Contains(
            failures.Failures,
            failure => failure.Status == HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcquirePdbAsync_LimitedHostRejectsOversizedMsdlBeforeStore(
        bool declaresLength)
    {
        var handler = new CountingHandler(request =>
        {
            if (request.RequestUri?.Host == "msdl.microsoft.com")
            {
                HttpContent content = declaresLength
                    ? new ByteArrayContent(new byte[65])
                    : new UnknownLengthContent(new byte[65]);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler);
        var downloader = new SymbolPackageDownloader(
            client,
            new ThrowingPutPdbStore(),
            new UniformPackageSourceAuthorization(
                [NuGetFetch.PackageSource.NuGetOrg]),
            new SymbolAcquisitionLimits(
                maxSymbolPackageBytes: 64,
                maxPortablePdbBytes: 64,
                maxSymbolPackageEntries: 8));
        using var failureScope =
            FeedFailureTelemetry.Scope(mergeIntoParent: false);
        FeedFailureCollector failures = FeedFailureTelemetry.Current!;

        PortablePdbAcquisitionResult result =
            await downloader.AcquirePdbAsync(
                Guid.NewGuid(),
                pdbAge: 1,
                pdbFileName: "System.Example.pdb",
                isPortable: true,
                assemblyName: "System.Example",
                packageName: "System.Example",
                packageVersion: "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PortablePdbAcquisitionResult.Unavailable>(result);
        Assert.Contains(
            failures.Failures,
            failure => failure.Status == HttpStatusCode.OK);
    }

    [Fact]
    public async Task AcquirePdbAsync_SymbolPackageWithSiblingIdentitiesRemainsAbsence()
    {
        var expectedGuid = Guid.NewGuid();
        var (pdbBytes, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(Guid.NewGuid());
        byte[] snupkg =
            SnupkgPdbReaderTests.MakeSnupkg(
                ("lib/net10.0/Example.pdb", pdbBytes));
        var handler = new CountingHandler(request =>
            request.RequestUri?.AbsolutePath.EndsWith(
                ".snupkg",
                StringComparison.OrdinalIgnoreCase) == true
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(snupkg),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var downloader =
            new SymbolPackageDownloader(
                client,
                new InMemoryPdbStore());
        using var failureScope =
            FeedFailureTelemetry.Scope(mergeIntoParent: false);
        FeedFailureCollector failures = FeedFailureTelemetry.Current!;

        PortablePdbAcquisitionResult result =
            await downloader.AcquirePdbAsync(
                expectedGuid,
                pdbAge: 1,
                pdbFileName: "Example.pdb",
                isPortable: true,
                assemblyName: "Example",
                packageName: "Example.Package",
                packageVersion: "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PortablePdbAcquisitionResult.Unavailable>(result);
        Assert.Empty(failures.Failures);
    }

    [Fact]
    public async Task AcquirePdbAsync_InvalidSymbolPackageCandidateRecordsFailure()
    {
        var expectedGuid = Guid.NewGuid();
        byte[] snupkg =
            SnupkgPdbReaderTests.MakeSnupkg(
                ("lib/net10.0/Example.pdb",
                    [(byte)'B', (byte)'S', (byte)'J', (byte)'B', 0]));
        var handler = new CountingHandler(request =>
            request.RequestUri?.AbsolutePath.EndsWith(
                ".snupkg",
                StringComparison.OrdinalIgnoreCase) == true
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(snupkg),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var downloader =
            new SymbolPackageDownloader(
                client,
                new InMemoryPdbStore());
        using var failureScope =
            FeedFailureTelemetry.Scope(mergeIntoParent: false);
        FeedFailureCollector failures = FeedFailureTelemetry.Current!;

        PortablePdbAcquisitionResult result =
            await downloader.AcquirePdbAsync(
                expectedGuid,
                pdbAge: 1,
                pdbFileName: "Example.pdb",
                isPortable: true,
                assemblyName: "Example",
                packageName: "Example.Package",
                packageVersion: "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PortablePdbAcquisitionResult.Unavailable>(result);
        Assert.Contains(
            failures.Failures,
            failure => failure.Status == HttpStatusCode.OK);
    }

    [Fact]
    public async Task AcquirePdbAsync_SymbolPackageWithoutCandidateRemainsAbsence()
    {
        var expectedGuid = Guid.NewGuid();
        var (pdbBytes, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(expectedGuid);
        byte[] snupkg =
            SnupkgPdbReaderTests.MakeSnupkg(
                ("lib/net10.0/Other.pdb", pdbBytes));
        var handler = new CountingHandler(request =>
            request.RequestUri?.AbsolutePath.EndsWith(
                ".snupkg",
                StringComparison.OrdinalIgnoreCase) == true
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(snupkg),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var downloader =
            new SymbolPackageDownloader(
                client,
                new InMemoryPdbStore());
        using var failureScope =
            FeedFailureTelemetry.Scope(mergeIntoParent: false);
        FeedFailureCollector failures = FeedFailureTelemetry.Current!;

        PortablePdbAcquisitionResult result =
            await downloader.AcquirePdbAsync(
                expectedGuid,
                pdbAge: 1,
                pdbFileName: "Example.pdb",
                isPortable: true,
                assemblyName: "Example",
                packageName: "Example.Package",
                packageVersion: "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PortablePdbAcquisitionResult.Unavailable>(result);
        Assert.Empty(failures.Failures);
    }

    [Fact]
    public async Task InMemoryPdbStore_RetainedByteLimitRejectsAdditionalContent()
    {
        var store = new InMemoryPdbStore(maxRetainedBytes: 4);
        await store.PutAsync(
            "first",
            new MemoryStream([1, 2, 3]),
            TestContext.Current.CancellationToken);

        InvalidDataException error =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => store.PutAsync(
                    "second",
                    new MemoryStream([4, 5]),
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("retained-byte limit", error.Message);
        Assert.Equal(3, store.RetainedBytes);
        Assert.Null(await store.TryOpenAsync(
            "second",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadPdbAsync_FileSystemStore_PreservesPathContract()
    {
        var guid = Guid.Parse("21112222-3333-4444-5555-666677778888");
        var (pdbBytes, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(guid);
        byte[] snupkg =
            SnupkgPdbReaderTests.MakeSnupkg(
                ("lib/net10.0/System.Text.Json.pdb", pdbBytes));
        var handler = new CountingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith(
                    ".snupkg",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(snupkg),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler);
        string root =
            Path.Combine(
                Path.GetTempPath(),
                $"pdb-download-{Guid.NewGuid():N}");
        try
        {
            var downloader =
                new SymbolPackageDownloader(
                    client,
                    new FileSystemPdbStore(root));

            PdbDownloadResult result =
                await downloader.DownloadPdbAsync(
                    guid,
                    pdbAge: 1,
                    pdbFileName: "System.Text.Json.pdb",
                    isPortable: true,
                    assemblyPath: "/tmp/System.Text.Json.dll",
                    packageName: "Example.Package",
                    packageVersion: "1.0.0",
                    cancellationToken:
                        TestContext.Current.CancellationToken);

            Assert.NotNull(result.PdbFilePath);
            Assert.Equal(
                pdbBytes,
                File.ReadAllBytes(result.PdbFilePath!));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AcquirePdbAsync_MsdlCachePreservesProvider()
    {
        var guid = Guid.Parse("31112222-3333-4444-5555-666677778888");
        var (pdbBytes, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(guid);
        var handler = new CountingHandler(request =>
        {
            if (request.RequestUri?.Host == "msdl.microsoft.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(pdbBytes),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler);
        var store = new InMemoryPdbStore();
        var downloader = new SymbolPackageDownloader(client, store);

        PortablePdbAcquisitionResult first =
            await downloader.AcquirePdbAsync(
                guid,
                pdbAge: 1,
                pdbFileName: "Provider.pdb",
                isPortable: true,
                packageName: "Example.Package",
                packageVersion: "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var acquired =
            Assert.IsType<PortablePdbAcquisitionResult.Acquired>(
                first);
        Assert.Equal(
            "msdl.microsoft.com",
            acquired.Pdb.SymbolServer);
        Assert.False(acquired.Pdb.FromCache);

        using var offlineClient =
            new HttpClient(new ThrowingHandler());
        var cachedDownloader =
            new SymbolPackageDownloader(offlineClient, store);
        PortablePdbAcquisitionResult second =
            await cachedDownloader.AcquirePdbAsync(
                guid,
                pdbAge: 1,
                pdbFileName: "Provider.pdb",
                isPortable: true,
                packageName: "Example.Package",
                packageVersion: "1.0.0",
                cacheOnly: true,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var cached =
            Assert.IsType<PortablePdbAcquisitionResult.Acquired>(
                second);
        Assert.Equal(
            "msdl.microsoft.com",
            cached.Pdb.SymbolServer);
        Assert.True(cached.Pdb.FromCache);
    }

    [Fact]
    public async Task AcquiredPortablePdb_DifferentStampsRemainRepeatable()
    {
        var guid =
            Guid.Parse(
                "32112222-3333-4444-5555-666677778888");
        const uint FirstStamp = 0x01020304;
        const uint SecondStamp = 0x05060708;
        var (firstBytes, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(
                guid,
                FirstStamp);
        var (secondBytes, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(
                guid,
                SecondStamp);
        int requestCount = 0;
        var handler = new CountingHandler(request =>
        {
            if (request.RequestUri?.Host == "msdl.microsoft.com")
            {
                byte[] content =
                    Interlocked.Increment(ref requestCount) == 1
                        ? firstBytes
                        : secondBytes;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler);
        var downloader =
            new SymbolPackageDownloader(
                client,
                new InMemoryPdbStore());

        var first =
            Assert.IsType<PortablePdbAcquisitionResult.Acquired>(
                await downloader.AcquirePdbAsync(
                    guid,
                    pdbAge: 1,
                    pdbFileName: "Repeatable.pdb",
                    isPortable: true,
                    isPlatformAssembly: true,
                    cancellationToken:
                        TestContext.Current.CancellationToken,
                    portablePdbStamp: FirstStamp));
        var second =
            Assert.IsType<PortablePdbAcquisitionResult.Acquired>(
                await downloader.AcquirePdbAsync(
                    guid,
                    pdbAge: 1,
                    pdbFileName: "Repeatable.pdb",
                    isPortable: true,
                    isPlatformAssembly: true,
                    cancellationToken:
                        TestContext.Current.CancellationToken,
                    portablePdbStamp: SecondStamp));

        await using Stream firstContent =
            await first.Pdb.OpenReadAsync(
                TestContext.Current.CancellationToken);
        await using Stream secondContent =
            await second.Pdb.OpenReadAsync(
                TestContext.Current.CancellationToken);
        using var firstBuffer = new MemoryStream();
        using var secondBuffer = new MemoryStream();
        await firstContent.CopyToAsync(
            firstBuffer,
            TestContext.Current.CancellationToken);
        await secondContent.CopyToAsync(
            secondBuffer,
            TestContext.Current.CancellationToken);

        Assert.Equal(firstBytes, firstBuffer.ToArray());
        Assert.Equal(secondBytes, secondBuffer.ToArray());
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task AcquirePdbAsync_InvalidCachedPdbContinuesToNextProvider()
    {
        var guid =
            Guid.Parse(
                "41112222-3333-4444-5555-666677778888");
        const uint Stamp = 0x04030201;
        var (pdbBytes, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(
                guid,
                Stamp);
        var store = new InMemoryPdbStore();
        string symbolKey =
            guid.ToString("N").ToUpperInvariant()
            + Stamp.ToString("X8");
        string poisonedKey =
            "servers/symbols.nuget.org/Provider.pdb/"
            + $"{symbolKey}/Provider.pdb";
        using (var poisoned =
               new MemoryStream(
                   [(byte)'B', (byte)'S', (byte)'J', (byte)'B'],
                   writable: false))
        {
            await store.PutAsync(
                poisonedKey,
                poisoned,
                TestContext.Current.CancellationToken);
        }

        var handler = new CountingHandler(request =>
        {
            if (request.RequestUri?.Host == "msdl.microsoft.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(pdbBytes),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler);
        var downloader =
            new SymbolPackageDownloader(
                client,
                store);
        List<string> log = [];
        using var failureScope =
            FeedFailureTelemetry.Scope(mergeIntoParent: false);
        FeedFailureCollector failures = FeedFailureTelemetry.Current!;

        PortablePdbAcquisitionResult result =
            await downloader.AcquirePdbAsync(
                guid,
                pdbAge: 1,
                pdbFileName: "Provider.pdb",
                isPortable: true,
                log: log.Add,
                cancellationToken:
                    TestContext.Current.CancellationToken,
                portablePdbStamp: Stamp);

        Assert.True(
            result is PortablePdbAcquisitionResult.Acquired,
            string.Join(Environment.NewLine, log));
        var acquired =
            (PortablePdbAcquisitionResult.Acquired)result;
        Assert.Equal(
            "msdl.microsoft.com",
            acquired.Pdb.SymbolServer);
        Assert.Contains(
            handler.RequestUris,
            uri => uri.Contains(
                "symbols.nuget.org",
                StringComparison.Ordinal));
        Assert.Contains(
            handler.RequestUris,
            uri => uri.Contains(
                "msdl.microsoft.com",
                StringComparison.Ordinal));
        Assert.Null(result.StoreFailure);
        Assert.Empty(failures.Failures);
    }

    [Fact]
    public async Task AcquirePdbAsync_InvalidCachedPdbRecordsFailure()
    {
        var guid = Guid.NewGuid();
        const uint Stamp = 0x04030201;
        var store = new InMemoryPdbStore();
        string symbolKey =
            guid.ToString("N").ToUpperInvariant()
            + Stamp.ToString("X8");
        string poisonedKey =
            "servers/symbols.nuget.org/Provider.pdb/"
            + $"{symbolKey}/Provider.pdb";
        using (var poisoned =
               new MemoryStream(
                   [(byte)'B', (byte)'S', (byte)'J', (byte)'B'],
                   writable: false))
        {
            await store.PutAsync(
                poisonedKey,
                poisoned,
                TestContext.Current.CancellationToken);
        }

        using var client =
            new HttpClient(
                new CountingHandler(
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var downloader = new SymbolPackageDownloader(client, store);
        using var failureScope =
            FeedFailureTelemetry.Scope(mergeIntoParent: false);
        FeedFailureCollector failures = FeedFailureTelemetry.Current!;

        PortablePdbAcquisitionResult result =
            await downloader.AcquirePdbAsync(
                guid,
                pdbAge: 1,
                pdbFileName: "Provider.pdb",
                isPortable: true,
                cancellationToken:
                    TestContext.Current.CancellationToken,
                portablePdbStamp: Stamp);

        var unavailable =
            Assert.IsType<PortablePdbAcquisitionResult.Unavailable>(result);
        Assert.Equal(
            PortablePdbStoreFailureKind.InvalidCachedContent,
            unavailable.StoreFailure);
        Assert.Empty(failures.Failures);
    }

    [Fact]
    public async Task AcquirePdbAsync_RejectedDownloadIsNotPublished()
    {
        var expectedGuid = Guid.NewGuid();
        const uint Stamp = 0x04030201;
        var (mismatchedPdb, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(Guid.NewGuid(), Stamp);
        int response = 0;
        var handler = new CountingHandler(_ =>
            Interlocked.Increment(ref response) == 1
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(mismatchedPdb),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var store = new InMemoryPdbStore();
        var downloader = new SymbolPackageDownloader(client, store);
        using var failureScope =
            FeedFailureTelemetry.Scope(mergeIntoParent: false);
        FeedFailureCollector failures = FeedFailureTelemetry.Current!;

        await downloader.AcquirePdbAsync(
            expectedGuid,
            pdbAge: 1,
            pdbFileName: "Provider.pdb",
            isPortable: true,
            cancellationToken:
                TestContext.Current.CancellationToken,
            portablePdbStamp: Stamp);

        string symbolKey =
            expectedGuid.ToString("N").ToUpperInvariant()
            + Stamp.ToString("X8");
        string cacheKey =
            "servers/symbols.nuget.org/Provider.pdb/"
            + $"{symbolKey}/Provider.pdb";
        Assert.Null(
            await store.TryOpenAsync(
                cacheKey,
                TestContext.Current.CancellationToken));
        Assert.Contains(
            failures.Failures,
            failure => failure.Status == HttpStatusCode.OK);
    }

    [Fact]
    public async Task AcquirePdbAsync_ExplicitStore_DoesNotUseAmbientCaches()
    {
        string configPath =
            Path.Combine(
                Path.GetTempPath(),
                $"symbol-browser-{Guid.NewGuid():N}.config");
        File.WriteAllText(
            configPath,
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="private" value="https://private.example/v3/index.json" />
              </packageSources>
            </configuration>
            """);
        var handler =
            new CountingHandler(
                _ => new HttpResponseMessage(
                    HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var downloader =
            new SymbolPackageDownloader(
                client,
                new InMemoryPdbStore(),
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]));

        try
        {
            await downloader.AcquirePdbAsync(
                Guid.NewGuid(),
                pdbAge: 1,
                pdbFileName: "Browser.pdb",
                isPortable: true,
                packageName: "Example.Package",
                packageVersion: "1.0.0",
                sourceOptions:
                    new NuGetSourceOptions
                    {
                        ConfigFile = configPath,
                    },
                cancellationToken:
                    TestContext.Current.CancellationToken);

            Assert.Contains(
                handler.RequestUris,
                uri => uri.Contains(
                    ".snupkg",
                    StringComparison.Ordinal));
            Assert.False(
                Directory.Exists(_cacheDir),
                "The explicit-store path must not initialize the filesystem cache.");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task AcquirePdbAsync_StoreFailureIsVisible()
    {
        var guid =
            Guid.Parse(
                "51112222-3333-4444-5555-666677778888");
        var (pdbBytes, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(guid);
        var handler = new CountingHandler(request =>
        {
            if (request.RequestUri?.Host == "msdl.microsoft.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(pdbBytes),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler);
        var downloader =
            new SymbolPackageDownloader(
                client,
                new ThrowingPutPdbStore());

        await Assert.ThrowsAsync<IOException>(
            () => downloader.AcquirePdbAsync(
                guid,
                pdbAge: 1,
                pdbFileName: "Failure.pdb",
                isPortable: true,
                isPlatformAssembly: true,
                cancellationToken:
                    TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AcquirePdbAsync_UnretainedDownloadRecordsFailure()
    {
        var guid =
            Guid.Parse(
                "61112222-3333-4444-5555-666677778888");
        var (pdbBytes, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(guid);
        var handler = new CountingHandler(request =>
            request.RequestUri?.Host == "msdl.microsoft.com"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(pdbBytes),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var downloader =
            new SymbolPackageDownloader(
                client,
                new DroppingPutPdbStore());
        using var failureScope =
            FeedFailureTelemetry.Scope(mergeIntoParent: false);
        FeedFailureCollector failures = FeedFailureTelemetry.Current!;

        PortablePdbAcquisitionResult result =
            await downloader.AcquirePdbAsync(
                guid,
                pdbAge: 1,
                pdbFileName: "Failure.pdb",
                isPortable: true,
                isPlatformAssembly: true,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<PortablePdbAcquisitionResult.Unavailable>(result);
        Assert.Equal(
            PortablePdbStoreFailureKind.PublicationNotRetained,
            unavailable.StoreFailure);
        Assert.Empty(failures.Failures);
    }

    [Fact]
    public async Task AcquirePdbAsync_ReadbackStoreFailureIsVisible()
    {
        var guid =
            Guid.Parse(
                "71112222-3333-4444-5555-666677778888");
        var (pdbBytes, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(guid);
        var handler = new CountingHandler(request =>
            request.RequestUri?.Host == "msdl.microsoft.com"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(pdbBytes),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var downloader =
            new SymbolPackageDownloader(
                client,
                new ThrowingReadbackPdbStore());

        IOException exception =
            await Assert.ThrowsAsync<IOException>(
                () => downloader.AcquirePdbAsync(
                    guid,
                    pdbAge: 1,
                    pdbFileName: "Failure.pdb",
                    isPortable: true,
                    isPlatformAssembly: true,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Contains(
            "Injected read-back failure",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcquirePdbAsync_UnretainedDownloadContinuesToNextProvider()
    {
        var guid =
            Guid.Parse(
                "81112222-3333-4444-5555-666677778888");
        var (pdbBytes, _) =
            SnupkgPdbReaderTests.BuildPortablePdb(guid);
        var handler = new CountingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(pdbBytes),
            });
        using var client = new HttpClient(handler);
        var downloader =
            new SymbolPackageDownloader(
                client,
                new DropFirstPutPdbStore());
        using var failureScope =
            FeedFailureTelemetry.Scope(mergeIntoParent: false);
        FeedFailureCollector failures = FeedFailureTelemetry.Current!;

        PortablePdbAcquisitionResult result =
            await downloader.AcquirePdbAsync(
                guid,
                pdbAge: 1,
                pdbFileName: "Provider.pdb",
                isPortable: true,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var acquired =
            Assert.IsType<PortablePdbAcquisitionResult.Acquired>(result);
        Assert.Equal(
            "msdl.microsoft.com",
            acquired.Pdb.SymbolServer);
        Assert.Null(acquired.StoreFailure);
        Assert.Empty(failures.Failures);
    }

    [Fact]
    public async Task AcquirePdbAsync_UnusableNames_SkipEveryStoreKeyPath()
    {
        var handler =
            new CountingHandler(
                _ => new HttpResponseMessage(
                    HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var downloader =
            new SymbolPackageDownloader(
                client,
                new InMemoryPdbStore());

        PortablePdbAcquisitionResult result =
            await downloader.AcquirePdbAsync(
                Guid.NewGuid(),
                pdbAge: 1,
                pdbFileName: "/",
                isPortable: true,
                assemblyName: "../escape",
                packageName: "Example.Package",
                packageVersion: "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PortablePdbAcquisitionResult.Unavailable>(
            result);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task AcquiredPortablePdb_MissingStoreEntryFailsVisibly()
    {
        var acquired =
            new AcquiredPortablePdb(
                new InMemoryPdbStore(),
                "missing.pdb",
                "test",
                fromCache: true);

        IOException exception =
            await Assert.ThrowsAsync<IOException>(
                () => acquired.OpenReadAsync(
                        TestContext.Current.CancellationToken)
                    .AsTask());

        Assert.Contains(
            "no longer available",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadPdbAsync_CancellationStopsSymbolRequest()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler);
        var downloader = new SymbolPackageDownloader(client);
        using var cancellation = new CancellationTokenSource();
        var guid = Guid.NewGuid();

        Task<PdbDownloadResult> download = downloader.DownloadPdbAsync(
            guid,
            pdbAge: 1,
            pdbFileName: $"Cancel-{guid:N}.pdb",
            isPortable: true,
            assemblyPath: "/tmp/Cancel.dll",
            isPlatformAssembly: true,
            cancellationToken: cancellation.Token);

        await handler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
    }

    [Fact]
    public async Task DownloadPdbAsync_PrivateMappingDoesNotProbeNuGetOrgSnupkg()
    {
        string configPath = Path.Combine(
            Path.GetTempPath(),
            $"symbol-mapping-{Guid.NewGuid():N}.config");
        File.WriteAllText(configPath, """
            <configuration>
              <packageSources>
                <clear />
                <add key="private" value="https://private.example/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="private">
                  <package pattern="Private.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var downloader = new SymbolPackageDownloader(client);

        try
        {
            await downloader.DownloadPdbAsync(
                Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
                pdbAge: 1,
                pdbFileName: "Symbols.pdb",
                isPortable: true,
                assemblyPath: "/tmp/Private.Package.dll",
                packageName: "Private.Package",
                packageVersion: "1.0.0",
                sourceOptions: new NuGetSourceOptions { ConfigFile = configPath },
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.DoesNotContain(
                handler.RequestUris,
                uri => uri.Contains(".snupkg", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                handler.RequestUris,
                uri => uri.Contains("private.package", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUris.Add(request.RequestUri?.ToString() ?? "");
            return Task.FromResult(responder(request));
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource<bool> RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "No network request was expected.");
    }

    private sealed class ThrowingPutPdbStore : IPdbStore
    {
        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream?>(null);

        public ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException(
                new IOException("Injected store failure."));

        public string? TryGetLocalPath(string key)
            => null;
    }

    private sealed class DroppingPutPdbStore : IPdbStore
    {
        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream?>(null);

        public ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public string? TryGetLocalPath(string key) => null;
    }

    private sealed class ThrowingReadbackPdbStore : IPdbStore
    {
        private bool _published;

        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
            => _published
                ? ValueTask.FromException<Stream?>(
                    new IOException("Injected read-back failure."))
                : ValueTask.FromResult<Stream?>(null);

        public ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            _published = true;
            return ValueTask.CompletedTask;
        }

        public string? TryGetLocalPath(string key) => null;
    }

    private sealed class DropFirstPutPdbStore : IPdbStore
    {
        private readonly InMemoryPdbStore _inner = new();
        private int _putCount;

        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
            => _inner.TryOpenAsync(key, cancellationToken);

        public ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref _putCount) == 1
                ? ValueTask.CompletedTask
                : _inner.PutAsync(key, content, cancellationToken);

        public string? TryGetLocalPath(string key)
            => _inner.TryGetLocalPath(key);
    }

    private sealed class UnknownLengthContent(byte[] content) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            stream.WriteAsync(content).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
