using System.Net;
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
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DownloadPdbAsync_DefinitiveMiss_IsCached(HttpStatusCode statusCode)
    {
        var handler = new CountingHandler(_ => new HttpResponseMessage(statusCode));
        using var client = new HttpClient(handler);
        var downloader = new SymbolPackageDownloader(client);
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        var first = await downloader.DownloadPdbAsync(
            guid, pdbAge: 1, pdbFileName: "Missing.pdb", isPortable: true,
            assemblyPath: "/tmp/Missing.dll",
            packageName: "Example.Package",
            packageVersion: "1.0.0");
        var firstCount = handler.RequestCount;

        var second = await downloader.DownloadPdbAsync(
            guid, pdbAge: 1, pdbFileName: "Missing.pdb", isPortable: true,
            assemblyPath: "/tmp/Missing.dll",
            packageName: "Example.Package",
            packageVersion: "1.0.0");

        Assert.Null(first.PdbFilePath);
        Assert.Null(second.PdbFilePath);
        Assert.True(firstCount > 0);
        Assert.Equal(firstCount, handler.RequestCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responder(request));
        }
    }
}
