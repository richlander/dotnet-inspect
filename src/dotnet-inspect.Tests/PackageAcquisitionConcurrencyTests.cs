using System.IO.Compression;
using System.Net;

using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class PackageAcquisitionConcurrencyTests : IDisposable
{
    private static readonly NuGetSourceOptions s_nugetOrgSource = new()
    {
        Sources = ["https://api.nuget.org/v3/index.json"],
    };

    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        $"dotnet-inspect-package-transaction-{Guid.NewGuid():N}");
    private readonly string _cachePath;

    public PackageAcquisitionConcurrencyTests()
    {
        _cachePath = Path.Combine(_testRoot, "cache");
        Core.HttpClientFactory.Initialize(offline: false);
        Core.HttpClientFactory.ResetSharedForTesting();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            _cachePath,
            skipNuGetCache: true);
    }

    public void Dispose()
    {
        Core.HttpClientFactory.Initialize(offline: false);
        Core.HttpClientFactory.ResetSharedForTesting();
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [Fact]
    public async Task ExtractPackageAsync_ConcurrentRequestsShareOneDownload()
    {
        string packageName = $"singleflight.test.{Guid.NewGuid():N}";
        const string Version = "1.0.0";
        byte[] archive = CreatePackageArchive(packageName, Version);
        var handler = new GatedPackageHandler(archive);
        using var client = new HttpClient(handler);
        string tempPrefix = $"package-flight-{Guid.NewGuid():N}-";

        Task<PackageExtractionOutcome>[] requests = Enumerable.Range(0, 32)
            .Select(_ => PackageExtractor.ExtractPackageAsync(
                client,
                packageName,
                tempDirPrefix: tempPrefix,
                sourceOptions: s_nugetOrgSource,
                version: Version))
            .ToArray();

        try
        {
            await handler.RequestStarted.WaitAsync(
                TestContext.Current.CancellationToken);
            Assert.Equal(1, handler.RequestCount);
            Assert.False(
                Directory.Exists(
                    NuGetCache.GetPackageCachePath(packageName, Version)));
        }
        finally
        {
            handler.Release();
        }

        PackageExtractionOutcome[] outcomes = await Task.WhenAll(requests);

        Assert.All(outcomes, outcome => Assert.True(outcome.IsSuccess));
        PackageExtractionResult first = outcomes[0].Result!;
        Assert.All(
            outcomes,
            outcome =>
            {
                Assert.Equal(first.ExtractPath, outcome.Result!.ExtractPath);
                Assert.Null(outcome.Result.TempDir);
                Assert.True(outcome.Result.FromCache);
            });
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(
            NuGetCache.GetPackageCachePath(packageName, Version),
            first.ExtractPath);
        Assert.True(File.Exists(first.NupkgPath));
        Assert.Equal(
            first.ExtractPath,
            NuGetCache.TryGetCachedPackage(packageName, Version));
        AssertNoStagingDirectories(packageName);
        AssertNoTemporaryDirectories(tempPrefix);

        var cached = await PackageExtractor.ExtractPackageAsync(
            client,
            packageName,
            tempDirPrefix: tempPrefix,
            sourceOptions: s_nugetOrgSource,
            version: Version);

        Assert.True(cached.IsSuccess);
        Assert.Equal(first.ExtractPath, cached.Result!.ExtractPath);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ExtractPackageAsync_DirectToolWrapperCycleReturnsError()
    {
        const string PackageName = "Wrapper.Direct";
        const string RedirectPackageName = "wrapper.direct";
        const string Version = "1.0.0";
        var handler = new QueuePackageHandler(
            CreateToolWrapperArchive(
                PackageName,
                Version,
                redirectPackageName: RedirectPackageName));
        using var client = new HttpClient(handler);

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                client,
                PackageName,
                sourceOptions: s_nugetOrgSource,
                version: Version);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(
            $"Tool wrapper redirect cycle detected: {PackageName} -> {RedirectPackageName}.",
            outcome.ErrorMessage);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ExtractPackageAsync_MultiPackageToolWrapperCycleReturnsError()
    {
        const string FirstPackage = "Wrapper.First";
        const string SecondPackage = "Wrapper.Second";
        const string Version = "1.0.0";
        var handler = new QueuePackageHandler(
            CreateToolWrapperArchive(
                FirstPackage,
                Version,
                redirectPackageName: SecondPackage),
            CreateToolWrapperArchive(
                SecondPackage,
                Version,
                redirectPackageName: FirstPackage));
        using var client = new HttpClient(handler);

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                client,
                FirstPackage,
                sourceOptions: s_nugetOrgSource,
                version: Version);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(
            $"Tool wrapper redirect cycle detected: {FirstPackage} -> {SecondPackage} -> {FirstPackage}.",
            outcome.ErrorMessage);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ExtractPackageAsync_ToolWrapperRedirectReturnsPayload()
    {
        const string WrapperPackage = "Wrapper.Valid";
        const string PayloadPackage = "Wrapper.Valid.Any";
        const string Version = "1.0.0";
        var handler = new QueuePackageHandler(
            CreateToolWrapperArchive(
                WrapperPackage,
                Version,
                redirectPackageName: PayloadPackage),
            CreatePackageArchive(PayloadPackage, Version));
        using var client = new HttpClient(handler);

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                client,
                WrapperPackage,
                sourceOptions: s_nugetOrgSource,
                version: Version);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(PayloadPackage, outcome.Result!.PackageName);
        Assert.Equal(
            NuGetCache.GetPackageCachePath(PayloadPackage, Version),
            outcome.Result.ExtractPath);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ExtractPackageAsync_ToolWrapperRedirectAtHopLimitReturnsPayload()
    {
        const int MaxRedirectHops = 8;
        const string PackagePrefix = "Wrapper.Bounded";
        const string Version = "1.0.0";
        byte[][] responses = Enumerable.Range(0, MaxRedirectHops)
            .Select(index => CreateToolWrapperArchive(
                $"{PackagePrefix}.{index}",
                Version,
                redirectPackageName: $"{PackagePrefix}.{index + 1}"))
            .Append(CreatePackageArchive($"{PackagePrefix}.{MaxRedirectHops}", Version))
            .ToArray();
        var handler = new QueuePackageHandler(responses);
        using var client = new HttpClient(handler);

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                client,
                $"{PackagePrefix}.0",
                sourceOptions: s_nugetOrgSource,
                version: Version);

        Assert.True(outcome.IsSuccess);
        Assert.Equal($"{PackagePrefix}.{MaxRedirectHops}", outcome.Result!.PackageName);
        Assert.Equal(MaxRedirectHops + 1, handler.RequestCount);
    }

    [Fact]
    public async Task ExtractPackageAsync_ToolWrapperRedirectBeyondHopLimitReturnsErrorBeforeNextDownload()
    {
        const int MaxRedirectHops = 8;
        const string PackagePrefix = "Wrapper.Unbounded";
        const string Version = "1.0.0";
        byte[][] responses = Enumerable.Range(0, MaxRedirectHops + 1)
            .Select(index => CreateToolWrapperArchive(
                $"{PackagePrefix}.{index}",
                Version,
                redirectPackageName: $"{PackagePrefix}.{index + 1}"))
            .ToArray();
        var handler = new QueuePackageHandler(responses);
        using var client = new HttpClient(handler);

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                client,
                $"{PackagePrefix}.0",
                sourceOptions: s_nugetOrgSource,
                version: Version);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(
            $"Tool wrapper redirect limit of {MaxRedirectHops} exceeded: " +
            $"{string.Join(" -> ", Enumerable.Range(0, MaxRedirectHops + 1).Select(index => $"{PackagePrefix}.{index}"))}" +
            $" -> {PackagePrefix}.{MaxRedirectHops + 1}.",
            outcome.ErrorMessage);
        Assert.Equal(MaxRedirectHops + 1, handler.RequestCount);
    }

    [Fact]
    public async Task ExtractPackageAsync_CycleAtHopLimitReturnsSpecificCycleError()
    {
        const int MaxRedirectHops = 8;
        const string PackagePrefix = "Wrapper.CycleAtLimit";
        const string Version = "1.0.0";
        byte[][] responses = Enumerable.Range(0, MaxRedirectHops + 1)
            .Select(index => CreateToolWrapperArchive(
                $"{PackagePrefix}.{index}",
                Version,
                redirectPackageName: index == MaxRedirectHops
                    ? $"{PackagePrefix}.0"
                    : $"{PackagePrefix}.{index + 1}"))
            .ToArray();
        var handler = new QueuePackageHandler(responses);
        using var client = new HttpClient(handler);

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                client,
                $"{PackagePrefix}.0",
                sourceOptions: s_nugetOrgSource,
                version: Version);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(
            $"Tool wrapper redirect cycle detected: " +
            $"{string.Join(" -> ", Enumerable.Range(0, MaxRedirectHops + 1).Select(index => $"{PackagePrefix}.{index}"))}" +
            $" -> {PackagePrefix}.0.",
            outcome.ErrorMessage);
        Assert.Equal(MaxRedirectHops + 1, handler.RequestCount);
    }

    [Fact]
    public async Task CommitPackage_ConcurrentPublishersConvergeOnOneCompleteTree()
    {
        string packageName = $"publisher.test.{Guid.NewGuid():N}";
        const string Version = "2.0.0";
        string sourceA = CreateExtractedPackage(
            Path.Combine(_testRoot, "source-a"),
            packageName,
            "A",
            payloadCount: 128);
        string sourceB = CreateExtractedPackage(
            Path.Combine(_testRoot, "source-b"),
            packageName,
            "B",
            payloadCount: 128);
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim();

        Task<CommittedPackage> publishA = Task.Run(() =>
        {
            ready.Signal();
            start.Wait();
            return NuGetCache.CommitPackage(
                sourceA,
                nupkgPath: null,
                packageName,
                Version);
        });
        Task<CommittedPackage> publishB = Task.Run(() =>
        {
            ready.Signal();
            start.Wait();
            return NuGetCache.CommitPackage(
                sourceB,
                nupkgPath: null,
                packageName,
                Version);
        });

        Assert.True(
            ready.Wait(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        start.Set();
        CommittedPackage[] committed = await Task.WhenAll(publishA, publishB);

        Assert.Equal(committed[0].ExtractPath, committed[1].ExtractPath);
        string[] payloads = Directory.GetFiles(
            Path.Combine(committed[0].ExtractPath, "payload"),
            "*.txt");
        Assert.Equal(128, payloads.Length);
        string winner = File.ReadAllText(payloads[0]);
        Assert.True(winner is "A" or "B");
        Assert.All(
            payloads,
            payload => Assert.Equal(winner, File.ReadAllText(payload)));
        AssertNoStagingDirectories(packageName);
    }

    [Fact]
    public void CommitPackage_InvalidPackageLeavesNoVisibleEntry()
    {
        string packageName = $"invalid.test.{Guid.NewGuid():N}";
        const string Version = "3.0.0";
        string source = Path.Combine(_testRoot, "invalid-source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "readme.txt"), "not a package");

        Assert.Throws<InvalidDataException>(
            () => NuGetCache.CommitPackage(
                source,
                nupkgPath: null,
                packageName,
                Version));

        Assert.False(
            Directory.Exists(
                NuGetCache.GetPackageCachePath(packageName, Version)));
        AssertNoStagingDirectories(packageName);
    }

    [Fact]
    public void CommitPackage_PreservesExistingInvalidEntry()
    {
        string packageName = $"corrupt.test.{Guid.NewGuid():N}";
        const string Version = "3.1.0";
        string target = NuGetCache.GetPackageCachePath(packageName, Version);
        Directory.CreateDirectory(target);
        string existingFile = Path.Combine(target, "existing.txt");
        File.WriteAllText(existingFile, "preserve");
        string source = CreateExtractedPackage(
            Path.Combine(_testRoot, "valid-source"),
            packageName,
            "valid",
            payloadCount: 1);

        Assert.Throws<InvalidDataException>(
            () => NuGetCache.CommitPackage(
                source,
                nupkgPath: null,
                packageName,
                Version));

        Assert.Equal("preserve", File.ReadAllText(existingFile));
        AssertNoStagingDirectories(packageName);
    }

    [Fact]
    public void CommitPackage_AcceptsAuthoredCaseRefOnlyPackage()
    {
        const string PackageName = "Microsoft.AspNetCore.App.Ref";
        const string Version = "10.0.0";
        string source = Path.Combine(_testRoot, "authored-case-ref-pack");
        Directory.CreateDirectory(
            Path.Combine(source, "ref", "net10.0"));
        File.WriteAllText(
            Path.Combine(source, $"{PackageName}.nuspec"),
            "<package />");
        File.WriteAllText(
            Path.Combine(source, "ref", "net10.0", "Test.dll"),
            "fixture");

        CommittedPackage committed = NuGetCache.CommitPackage(
            source,
            nupkgPath: null,
            PackageName,
            Version);

        Assert.Equal(
            committed.ExtractPath,
            NuGetCache.TryGetCachedPackage(PackageName, Version));
    }

    [Fact]
    public async Task EnsurePackAsync_ConcurrentRequestsPublishOneImmutablePack()
    {
        const string PackageName = "Microsoft.NETCore.App.Ref";
        const string Version = "10.0.1";
        string source = Path.Combine(_testRoot, "platform-pack");
        string refFile = Path.Combine(
            source,
            "ref",
            "net10.0",
            "System.Runtime.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(refFile)!);
        File.WriteAllText(
            Path.Combine(source, $"{PackageName}.nuspec"),
            "<package />");
        File.WriteAllText(refFile, "fixture");
        CommittedPackage committed = NuGetCache.CommitPackage(
            source,
            nupkgPath: null,
            PackageName,
            Version);
        using var client = new HttpClient(new FailingHandler());

        Task<string?>[] requests = Enumerable.Range(0, 16)
            .Select(_ => PlatformPackService.EnsurePackAsync(
                PackageName,
                Version,
                client))
            .ToArray();
        string?[] packPaths = await Task.WhenAll(requests);
        string? packPath = packPaths[0];

        Assert.NotNull(packPath);
        Assert.All(packPaths, path => Assert.Equal(packPath, path));
        Assert.True(
            File.Exists(
                Path.Combine(
                    packPath,
                    "ref",
                    "net10.0",
                    "System.Runtime.dll")));
        Assert.True(
            File.Exists(
                Path.Combine(
                    committed.ExtractPath,
                    "ref",
                    "net10.0",
                    "System.Runtime.dll")));
        Assert.Equal(
            committed.ExtractPath,
            NuGetCache.TryGetCachedPackage(PackageName, Version));
        AssertNoPackStagingDirectories(PackageName);
    }

    [Fact]
    public async Task EnsurePackAsync_PreservesExistingInvalidPackEntry()
    {
        const string PackageName = "Microsoft.WindowsDesktop.App.Ref";
        const string Version = "10.0.2";
        string source = Path.Combine(_testRoot, "windowsdesktop-pack");
        Directory.CreateDirectory(Path.Combine(source, "ref", "net10.0"));
        File.WriteAllText(
            Path.Combine(source, $"{PackageName}.nuspec"),
            "<package />");
        NuGetCache.CommitPackage(
            source,
            nupkgPath: null,
            PackageName,
            Version);
        string packPath = Path.Combine(
            PlatformPackService.GetPacksCachePath()!,
            PackageName,
            Version);
        Directory.CreateDirectory(packPath);
        string existingFile = Path.Combine(packPath, "existing.txt");
        File.WriteAllText(existingFile, "preserve");
        using var client = new HttpClient(new FailingHandler());

        string? result = await PlatformPackService.EnsurePackAsync(
            PackageName,
            Version,
            client);

        Assert.Null(result);
        Assert.Equal("preserve", File.ReadAllText(existingFile));
        AssertNoPackStagingDirectories(PackageName);
    }

    [Fact]
    public async Task ExtractPackageAsync_FailedArchiveCanRetry()
    {
        string packageName = $"retry.test.{Guid.NewGuid():N}";
        const string Version = "4.0.0";
        var handler = new QueuePackageHandler(
            "not a zip"u8.ToArray(),
            CreatePackageArchive(packageName, Version));
        using var client = new HttpClient(handler);
        string tempPrefix = $"package-retry-{Guid.NewGuid():N}-";

        var failed = await PackageExtractor.ExtractPackageAsync(
            client,
            packageName,
            tempDirPrefix: tempPrefix,
            sourceOptions: s_nugetOrgSource,
            version: Version);

        Assert.False(failed.IsSuccess);
        Assert.False(
            Directory.Exists(
                NuGetCache.GetPackageCachePath(packageName, Version)));

        var retried = await PackageExtractor.ExtractPackageAsync(
            client,
            packageName,
            tempDirPrefix: tempPrefix,
            sourceOptions: s_nugetOrgSource,
            version: Version);

        Assert.True(retried.IsSuccess);
        Assert.Equal(2, handler.RequestCount);
        AssertNoTemporaryDirectories(tempPrefix);
    }

    [Fact]
    public void TryGetCachedPackage_DoesNotUseLegacyDirectCopyNamespace()
    {
        string packageName = $"legacy.test.{Guid.NewGuid():N}";
        const string Version = "5.0.0";
        CreateExtractedPackage(
            Path.Combine(
                NuGetCache.GetAppCachePath(),
                packageName,
                Version),
            packageName,
            "legacy",
            payloadCount: 1);

        Assert.Null(NuGetCache.TryGetCachedPackage(packageName, Version));
    }

    private static byte[] CreatePackageArchive(
        string packageName,
        string version)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            WriteEntry(
                archive,
                $"{packageName}.nuspec",
                $"""
                <?xml version="1.0"?>
                <package>
                  <metadata>
                    <id>{packageName}</id>
                    <version>{version}</version>
                  </metadata>
                </package>
                """);
            WriteEntry(archive, "lib/net10.0/Test.dll", "fixture");
        }

        return buffer.ToArray();
    }

    private static byte[] CreateToolWrapperArchive(
        string packageName,
        string version,
        string redirectPackageName)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            WriteEntry(
                archive,
                $"{packageName}.nuspec",
                $"""
                <?xml version="1.0"?>
                <package>
                  <metadata>
                    <id>{packageName}</id>
                    <version>{version}</version>
                  </metadata>
                </package>
                """);
            WriteEntry(
                archive,
                "tools/net10.0/any/DotnetToolSettings.xml",
                $"""
                <DotNetCliTool Version="2">
                  <Commands>
                    <Command Name="{packageName}" />
                  </Commands>
                  <RuntimeIdentifierPackages>
                    <RuntimeIdentifierPackage RuntimeIdentifier="any" Id="{redirectPackageName}" />
                  </RuntimeIdentifierPackages>
                </DotNetCliTool>
                """);
        }

        return buffer.ToArray();
    }

    private static void WriteEntry(
        ZipArchive archive,
        string path,
        string content)
    {
        using Stream stream = archive.CreateEntry(path).Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static string CreateExtractedPackage(
        string path,
        string packageName,
        string content,
        int payloadCount)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(
            Path.Combine(path, $"{packageName.ToLowerInvariant()}.nuspec"),
            "<package />");
        string payloadPath = Path.Combine(path, "payload");
        Directory.CreateDirectory(payloadPath);
        for (int i = 0; i < payloadCount; i++)
        {
            File.WriteAllText(
                Path.Combine(payloadPath, $"{i:D4}.txt"),
                content);
        }

        return path;
    }

    private static void AssertNoTemporaryDirectories(string prefix)
    {
        Assert.Empty(
            Directory.GetDirectories(
                Path.GetTempPath(),
                $"{prefix}*"));
    }

    private static void AssertNoStagingDirectories(string packageName)
    {
        string packagePath = NuGetCache.GetPackageCachePath(
            packageName,
            "unused");
        string parent = Path.GetDirectoryName(packagePath)!;
        if (!Directory.Exists(parent))
            return;

        Assert.Empty(Directory.GetDirectories(parent, ".*.tmp-*"));
    }

    private static void AssertNoPackStagingDirectories(string packageName)
    {
        string parent = Path.Combine(
            PlatformPackService.GetPacksCachePath()!,
            packageName);
        if (!Directory.Exists(parent))
            return;

        Assert.Empty(Directory.GetDirectories(parent, ".*.tmp-*"));
    }

    private sealed class GatedPackageHandler(byte[] response)
        : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _requestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public Task RequestStarted => _requestStarted.Task;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public void Release() => _release.TrySetResult(true);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            _requestStarted.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(response),
            };
        }
    }

    private sealed class QueuePackageHandler(params byte[][] responses)
        : HttpMessageHandler
    {
        private readonly Queue<byte[]> _responses = new(responses);
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_responses.Dequeue()),
                });
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }
}
