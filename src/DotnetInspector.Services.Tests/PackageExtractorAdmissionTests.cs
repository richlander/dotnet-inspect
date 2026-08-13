using System.IO.Compression;
using System.Net;
using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

[Collection(CoreCacheCollection.Name)]
public sealed class PackageExtractorAdmissionTests
{
    const string PackageId = "sample.package";
    const string Version = "1.0.0";
    const string SourceA = "https://a.test/v3/index.json";
    const string SourceB = "https://b.test/v3/index.json";

    [Fact]
    public async Task InadmissibleLegacyCacheEntry_DoesNotMaskAnotherProducer()
    {
        string cacheRoot = TempDirectory();
        string stagingRoot = TempDirectory();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            cacheRoot,
            skipNuGetCache: true);

        try
        {
            Commit(
                stagingRoot,
                "a",
                TestPackageArchive.Create("../escape.txt"),
                SourceA);
            Commit(
                stagingRoot,
                "b",
                TestPackageArchive.Create("lib/net10.0/Sample.dll"),
                SourceB);
            using var client = new HttpClient(new FailingHandler());

            PackageExtractionOutcome outcome =
                await DotnetInspector.Packages.PackageExtractor
                    .ExtractPackageAsync(
                        client,
                        $"{PackageId}@{Version}",
                        sourceOptions: Sources(SourceA, SourceB));

            Assert.True(outcome.IsSuccess);
            Assert.Equal(
                NuGetCache.GetSourceKey(SourceB),
                outcome.Result!.ProducerKey);
        }
        finally
        {
            Delete(cacheRoot);
            Delete(stagingRoot);
        }
    }

    /// <summary>
    /// Product-owned app-cache commits retain an archive for tree match.
    /// Stripping that nupkg must not admit the extract offline (would skip
    /// CRC/path match and enable a delete-nupkg + mutate-tree downgrade).
    /// </summary>
    [Fact]
    public async Task ProductOwnedCacheWithoutRetainedNupkg_IsRejectedOffline()
    {
        string cacheRoot = TempDirectory();
        string stagingRoot = TempDirectory();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            cacheRoot,
            skipNuGetCache: true);

        try
        {
            Commit(
                stagingRoot,
                "only",
                TestPackageArchive.Create(
                    "lib/net10.0/Sample.dll",
                    $"{PackageId}.nuspec"),
                SourceA);
            string? nupkgPath = Directory
                .EnumerateFiles(
                    cacheRoot,
                    $"{PackageId}.{Version}.nupkg",
                    SearchOption.AllDirectories)
                .SingleOrDefault();
            Assert.NotNull(nupkgPath);
            File.Delete(nupkgPath);

            bool wasOffline = DotnetInspector.Core.HttpClientFactory.IsOffline;
            DotnetInspector.Core.HttpClientFactory.Initialize(
                new DotnetInspector.Core.HttpClientFactoryOptions
                {
                    Offline = true,
                });
            DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
            try
            {
                using var client = new HttpClient(new FailingHandler());
                PackageExtractionOutcome outcome =
                    await DotnetInspector.Packages.PackageExtractor
                        .ExtractPackageAsync(
                            client,
                            $"{PackageId}@{Version}",
                            sourceOptions: Sources(SourceA));

                Assert.False(outcome.IsSuccess);
                Assert.Contains(
                    "not available offline",
                    outcome.ErrorMessage,
                    StringComparison.OrdinalIgnoreCase);
                Assert.Contains(
                    "no retained archive",
                    outcome.ErrorMessage,
                    StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DotnetInspector.Core.HttpClientFactory.Initialize(
                    new DotnetInspector.Core.HttpClientFactoryOptions
                    {
                        Offline = wasOffline,
                    });
                DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
            }
        }
        finally
        {
            Delete(cacheRoot);
            Delete(stagingRoot);
        }
    }

    /// <summary>
    /// An archive-less slot whose extracted tree has no top-level nuspec must
    /// not be treated as a cache hit that answers offline as "not found".
    /// </summary>
    [Fact]
    public async Task DamagedExtractedCacheWithoutNuspec_ReportsUnusableOffline()
    {
        string cacheRoot = TempDirectory();
        string stagingRoot = TempDirectory();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            cacheRoot,
            skipNuGetCache: true);

        try
        {
            Commit(
                stagingRoot,
                "only",
                TestPackageArchive.Create("lib/net10.0/Sample.dll"),
                SourceA);
            string? nupkgPath = Directory
                .EnumerateFiles(
                    cacheRoot,
                    $"{PackageId}.{Version}.nupkg",
                    SearchOption.AllDirectories)
                .SingleOrDefault();
            Assert.NotNull(nupkgPath);
            File.Delete(nupkgPath);
            string extractRoot = Path.GetDirectoryName(nupkgPath)!;
            foreach (string nuspec in Directory.EnumerateFiles(
                         extractRoot,
                         "*.nuspec"))
            {
                File.Delete(nuspec);
            }

            bool wasOffline = DotnetInspector.Core.HttpClientFactory.IsOffline;
            DotnetInspector.Core.HttpClientFactory.Initialize(
                new DotnetInspector.Core.HttpClientFactoryOptions
                {
                    Offline = true,
                });
            DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
            try
            {
                using var client = new HttpClient(new FailingHandler());
                PackageExtractionOutcome outcome =
                    await DotnetInspector.Packages.PackageExtractor
                        .ExtractPackageAsync(
                            client,
                            $"{PackageId}@{Version}",
                            sourceOptions: Sources(SourceA));

                Assert.False(outcome.IsSuccess);
                Assert.Contains(
                    "no retained archive and no usable extracted tree",
                    outcome.ErrorMessage,
                    StringComparison.Ordinal);
            }
            finally
            {
                DotnetInspector.Core.HttpClientFactory.Initialize(
                    new DotnetInspector.Core.HttpClientFactoryOptions
                    {
                        Offline = wasOffline,
                    });
                DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
            }
        }
        finally
        {
            Delete(cacheRoot);
            Delete(stagingRoot);
        }
    }

    [Fact]
    public async Task InvalidLegacyDownload_LetsTheNextSourceServe()
    {
        string cacheRoot = TempDirectory();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            cacheRoot,
            skipNuGetCache: true);
        using var handler = new TwoSourcePackageHandler(
            TestPackageArchive.Create("../escape.txt"),
            TestPackageArchive.Create("lib/net10.0/Sample.dll"));
        using var client = new HttpClient(handler);

        try
        {
            PackageExtractionOutcome outcome =
                await DotnetInspector.Packages.PackageExtractor
                    .ExtractPackageAsync(
                        client,
                        $"{PackageId}@{Version}",
                        sourceOptions: Sources(SourceA, SourceB));

            Assert.True(outcome.IsSuccess);
            Assert.True(handler.RequestedSourceB);
            Assert.Equal(
                NuGetCache.GetSourceKey(SourceB),
                outcome.Result!.ProducerKey);
        }
        finally
        {
            Delete(cacheRoot);
        }
    }

    static NuGetSourceOptions Sources(params string[] sources) =>
        new() { Sources = sources };

    static void Commit(
        string stagingRoot,
        string name,
        byte[] archive,
        string sourceUrl)
    {
        // Prefer an extracted tree that matches the archive entry set so
        // retained-nupkg admission (path/size agreement) can accept it. Fall
        // back to a hand-built tree only when the archive is intentionally
        // invalid (e.g. zip-slip fixtures) — those fail archive validation
        // before the tree match runs.
        string extracted = Path.Combine(stagingRoot, name, "extracted");
        Directory.CreateDirectory(extracted);
        string nupkg = Path.Combine(stagingRoot, name, "package.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(nupkg)!);
        File.WriteAllBytes(nupkg, archive);
        if (!TryExtractMatchingTree(nupkg, extracted))
        {
            string assembly = Path.Combine(
                extracted,
                "lib",
                "net10.0",
                "Sample.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(assembly)!);
            File.WriteAllBytes(assembly, [1]);
            File.WriteAllText(
                Path.Combine(extracted, $"{PackageId}.nuspec"),
                """<?xml version="1.0"?><package />""");
        }

        NuGetCache.CommitPackage(
            extracted,
            nupkg,
            PackageId,
            Version,
            NuGetCache.GetSourceKey(sourceUrl));
    }

    static bool TryExtractMatchingTree(string nupkgPath, string destination)
    {
        try
        {
            using var archive = ZipFile.OpenRead(nupkgPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string relative = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrEmpty(relative) || relative.EndsWith('/'))
                    continue;
                if (relative.Contains("..", StringComparison.Ordinal)
                    || Path.IsPathRooted(relative))
                {
                    return false;
                }

                string target = Path.Combine(
                    destination,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return false;
        }
    }

    static string TempDirectory() =>
        Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-legacy-admission-{Guid.NewGuid():N}");

    static void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Admissible cached content performed network work: {request.RequestUri}");
    }

    sealed class TwoSourcePackageHandler(
        byte[] sourceAArchive,
        byte[] sourceBArchive)
        : HttpMessageHandler
    {
        internal bool RequestedSourceB { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            return Task.FromResult(
                url switch
                {
                    SourceA => Json(
                        """{"resources":[{"@id":"https://a.test/flat","@type":"PackageBaseAddress/3.0.0"}]}"""),
                    SourceB => Json(
                        """{"resources":[{"@id":"https://b.test/flat","@type":"PackageBaseAddress/3.0.0"}]}"""),
                    $"https://a.test/flat/{PackageId}/{Version}/{PackageId}.{Version}.nupkg" =>
                        Package(sourceAArchive),
                    $"https://b.test/flat/{PackageId}/{Version}/{PackageId}.{Version}.nupkg" =>
                        SourceBPackage(),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                });
        }

        HttpResponseMessage SourceBPackage()
        {
            RequestedSourceB = true;
            return Package(sourceBArchive);
        }

        static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            };

        static HttpResponseMessage Package(byte[] archive) =>
            new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive),
            };
    }
}
