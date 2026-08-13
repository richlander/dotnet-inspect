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
    /// Global-packages entries often strip the retained <c>.nupkg</c>. Cache
    /// admission must still use the extracted tree when it is a valid NuGet
    /// layout within current expanded limits — including the offline path that
    /// previously reported "no cached package was found".
    /// </summary>
    [Fact]
    public async Task ExtractedCacheEntryWithoutRetainedNupkg_IsAdmitted()
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

            using var client = new HttpClient(new FailingHandler());
            PackageExtractionOutcome outcome =
                await DotnetInspector.Packages.PackageExtractor
                    .ExtractPackageAsync(
                        client,
                        $"{PackageId}@{Version}",
                        sourceOptions: Sources(SourceA));

            Assert.True(outcome.IsSuccess);
            Assert.Equal(
                NuGetCache.GetSourceKey(SourceA),
                outcome.Result!.ProducerKey);
            Assert.Null(outcome.Result.NupkgPath);
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
        string extracted = Path.Combine(stagingRoot, name, "extracted");
        string assembly = Path.Combine(
            extracted,
            "lib",
            "net10.0",
            "Sample.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(assembly)!);
        File.WriteAllBytes(assembly, [1]);
        string nupkg = Path.Combine(stagingRoot, name, "package.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(nupkg)!);
        File.WriteAllBytes(nupkg, archive);
        NuGetCache.CommitPackage(
            extracted,
            nupkg,
            PackageId,
            Version,
            NuGetCache.GetSourceKey(sourceUrl));
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
