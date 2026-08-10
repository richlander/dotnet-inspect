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
            packageVersion: "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        var firstCount = handler.RequestCount;

        var second = await downloader.DownloadPdbAsync(
            guid, pdbAge: 1, pdbFileName: "Missing.pdb", isPortable: true,
            assemblyPath: "/tmp/Missing.dll",
            packageName: "Example.Package",
            packageVersion: "1.0.0",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(first.PdbFilePath);
        Assert.Null(second.PdbFilePath);
        Assert.True(firstCount > 0);
        Assert.Equal(firstCount, handler.RequestCount);
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
}
