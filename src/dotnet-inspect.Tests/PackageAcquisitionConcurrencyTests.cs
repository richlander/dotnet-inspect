using System.Diagnostics;
using System.IO.Compression;
using System.Net;

using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class PackageAcquisitionConcurrencyTests : IDisposable
{
    /// <summary>
    /// Identity of the source these fixtures speak for. Cached content is scoped
    /// to the source that committed it, so a test that seeds the cache by hand
    /// must use the same source the code under test resolves — otherwise the
    /// seeded entry is correctly invisible.
    /// </summary>
    private static readonly string TestSourceKey =
        NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json");

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
        Core.HttpClientFactory.Initialize(new Core.HttpClientFactoryOptions());
        Core.HttpClientFactory.ResetSharedForTesting();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            _cachePath,
            skipNuGetCache: true);
    }

    public void Dispose()
    {
        Core.HttpClientFactory.Initialize(new Core.HttpClientFactoryOptions());
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
                    NuGetCache.GetPackageCachePath(packageName, Version, TestSourceKey)));
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
            NuGetCache.GetPackageCachePath(packageName, Version, TestSourceKey),
            first.ExtractPath);
        Assert.True(File.Exists(first.NupkgPath));
        Assert.Equal(
            first.ExtractPath,
            NuGetCache.TryGetCachedPackage(packageName, Version, [TestSourceKey]));
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
            NuGetCache.GetPackageCachePath(PayloadPackage, Version, TestSourceKey),
            outcome.Result.ExtractPath);
        ToolWrapperPackage wrapper = Assert.Single(outcome.Result.ToolWrapperChain);
        Assert.Equal(WrapperPackage, wrapper.PackageName);
        Assert.Equal(Version, wrapper.Version);
        Assert.Equal(TestSourceKey, wrapper.ProducerKey);
        Assert.Equal(2, handler.RequestCount);
    }

    [Theory]
    [InlineData("Victim.Package@junk")]
    [InlineData("Bad Package")]
    [InlineData("Bad|Package")]
    public async Task ExtractPackageAsync_InvalidToolWrapperRedirectIsRejected(
        string redirectPackageName)
    {
        const string WrapperPackage = "Wrapper.Invalid";
        const string Version = "1.0.0";
        var handler = new QueuePackageHandler(
            CreateToolWrapperArchive(
                WrapperPackage,
                Version,
                redirectPackageName));
        using var client = new HttpClient(handler);

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                client,
                WrapperPackage,
                sourceOptions: s_nugetOrgSource,
                version: Version);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(
            $"Tool wrapper package '{WrapperPackage}' declares an invalid redirect package id.",
            outcome.ErrorMessage);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ExtractPackageAsync_RestoresActiveSourcesForPinnedRedirectTarget()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string wrapperPackage = $"wrapper.scope.{suffix}";
        string payloadPackage = $"payload.scope.{suffix}";
        const string Version = "1.0.0";
        const string FeedA = "https://feed-a.invalid/v3/index.json";
        const string FeedB = "https://feed-b.invalid/v3/index.json";
        var handler = new CoordinateScopedRedirectHandler(
            wrapperPackage,
            payloadPackage,
            CreateToolWrapperArchive(
                wrapperPackage,
                Version,
                payloadPackage),
            CreatePackageArchive(payloadPackage, Version));
        using var client = new HttpClient(handler);
        NuGetSourceOptions? sourceOptions =
            NuGetSourceResolver.RestrictToSources(
                new NuGetSourceOptions
                {
                    Sources = [FeedA, FeedB],
                },
                [FeedA]);

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                client,
                wrapperPackage,
                sourceOptions: sourceOptions,
                version: Version);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal(payloadPackage, outcome.Result!.PackageName);
        Assert.Equal(
            NuGetCache.GetSourceKey(FeedB),
            outcome.Result.ProducerKey);
        ToolWrapperPackage wrapper = Assert.Single(outcome.Result.ToolWrapperChain);
        Assert.Equal(wrapperPackage, wrapper.PackageName);
        Assert.Equal(
            NuGetCache.GetSourceKey(FeedA),
            wrapper.ProducerKey);
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

        Task<CommittedPackage> publishA = StartDedicatedWorker(
            ready,
            start,
            () => NuGetCache.CommitPackage(
                sourceA,
                nupkgPath: null,
                packageName,
                Version,
                TestSourceKey));
        Task<CommittedPackage> publishB = StartDedicatedWorker(
            ready,
            start,
            () => NuGetCache.CommitPackage(
                sourceB,
                nupkgPath: null,
                packageName,
                Version,
                TestSourceKey));

        (bool publishersReady, CommittedPackage[] committed) =
            await WaitForAndJoinWorkersAsync(
                ready,
                start,
                TestContext.Current.CancellationToken,
                publishA,
                publishB);
        Assert.True(publishersReady);

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
    public async Task WaitForAndJoinWorkersAsync_CancellationJoinsWorkersBeforeRethrowing()
    {
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim();
        using var workersWaiting = new CountdownEvent(2);
        using var releaseWorkers = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        int completedWorkers = 0;

        Task<int> StartWorker() =>
            StartDedicatedWorker(
                ready,
                start,
                () =>
                {
                    workersWaiting.Signal();
                    releaseWorkers.Wait();
                    return Interlocked.Increment(ref completedWorkers);
                });

        Task<int> first = StartWorker();
        Task<int> second = StartWorker();
        cancellation.Cancel();

        Task<(bool Ready, int[] Results)> completion =
            WaitForAndJoinWorkersAsync(
                ready,
                start,
                cancellation.Token,
                first,
                second);

        bool bothWorkersWaiting;
        bool completedBeforeRelease;
        try
        {
            bothWorkersWaiting =
                workersWaiting.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            completedBeforeRelease = completion.IsCompleted;
        }
        finally
        {
            releaseWorkers.Set();
        }

        OperationCanceledException exception =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => completion);

        Assert.True(bothWorkersWaiting);
        Assert.False(completedBeforeRelease);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.Equal(2, completedWorkers);
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
                Version,
                TestSourceKey));

        Assert.False(
            Directory.Exists(
                NuGetCache.GetPackageCachePath(packageName, Version, TestSourceKey)));
        AssertNoStagingDirectories(packageName);
    }

    [Fact]
    public void CommitPackage_PreservesExistingInvalidEntry()
    {
        string packageName = $"corrupt.test.{Guid.NewGuid():N}";
        const string Version = "3.1.0";
        string target = NuGetCache.GetPackageCachePath(packageName, Version, TestSourceKey);
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
                Version,
                TestSourceKey));

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
            Version,
            TestSourceKey);

        Assert.Equal(
            committed.ExtractPath,
            NuGetCache.TryGetCachedPackage(PackageName, Version, [TestSourceKey]));
    }

    /// <summary>
    /// Hostile/oversized commit markers must not be loaded wholesale when the
    /// app-cache tier is probed (same OOM class as unbounded .nupkg.metadata).
    /// </summary>
    [Fact]
    public void EnumerateCached_OversizedCommitMarker_IsAbsentWithoutHugeAlloc()
    {
        const string PackageName = "Marker.Bound.Test";
        const string Version = "1.0.0";
        string slot = NuGetCache.GetPackageCachePath(
            PackageName,
            Version,
            TestSourceKey);
        Directory.CreateDirectory(slot);
        File.WriteAllText(
            Path.Combine(slot, $"{PackageName.ToLowerInvariant()}.nuspec"),
            "<package />");
        // Just over the product cap — large enough to prove the gate, small
        // enough that a regression still fails the test without host OOM.
        int oversize = NuGetCache.MaxCommitMarkerBytes + 1;
        File.WriteAllBytes(
            Path.Combine(slot, NuGetCache.CommitMarkerFileName),
            new byte[oversize]);

        // Fail closed: oversized marker is treated as absent (Length gate
        // rejects before any read buffer is allocated for the file body).
        Assert.Null(
            NuGetCache.TryGetCachedPackage(PackageName, Version, [TestSourceKey]));
        Assert.Empty(
            NuGetCache.ListCachedPackageContent(
                PackageName,
                Version,
                [TestSourceKey]));
        // Sanity: file still present and still over the cap (we rejected on
        // size, not by deleting the hostile slot).
        Assert.True(
            new FileInfo(Path.Combine(slot, NuGetCache.CommitMarkerFileName)).Length
            > NuGetCache.MaxCommitMarkerBytes);
    }

    /// <summary>
    /// Marker-valid cache slots must still bound nuspec materialization
    /// (dependency-resolution hot path).
    /// </summary>
    [Fact]
    public async Task ProbeNuspecXmlAsync_OversizedCachedNuspec_IsIndeterminate()
    {
        const string PackageName = "Nuspec.Bound.Test";
        const string Version = "1.0.0";
        string sourceDir = Path.Combine(_testRoot, "nuspec-bound-src");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(
            Path.Combine(sourceDir, $"{PackageName}.nuspec"),
            """<?xml version="1.0"?><package><metadata><id>Nuspec.Bound.Test</id><version>1.0.0</version></metadata></package>""");
        Directory.CreateDirectory(Path.Combine(sourceDir, "lib", "net10.0"));
        File.WriteAllBytes(
            Path.Combine(sourceDir, "lib", "net10.0", "t.dll"),
            [1]);

        CommittedPackage committed = NuGetCache.CommitPackage(
            sourceDir,
            nupkgPath: null,
            PackageName,
            Version,
            TestSourceKey);

        string nuspecPath = Directory
            .GetFiles(committed.ExtractPath, "*.nuspec", SearchOption.TopDirectoryOnly)
            .Single();
        File.WriteAllBytes(
            nuspecPath,
            new byte[PackageExtractor.MaxNuspecBytes + 1]);

        // Cache path is present but unusable; network fallthrough must not throw.
        using var client = new HttpClient(new NotFoundHandler());
        NuspecProbeResult probe =
            await PackageExtractor.ProbeNuspecXmlAsync(
                client,
                PackageName,
                Version,
                sourceOptions: s_nugetOrgSource);
        Assert.Equal(NuspecProbeStatus.Indeterminate, probe.Status);
        Assert.Null(probe.Xml);
    }

    [Fact]
    public async Task ProbeNuspecXmlAsync_InvalidUtf8CachedNuspec_IsIndeterminate()
    {
        const string PackageName = "Nuspec.Invalid.Utf8";
        const string Version = "1.0.0";
        string sourceDir = Path.Combine(_testRoot, "nuspec-invalid-utf8");
        Directory.CreateDirectory(sourceDir);
        byte[] prefix = System.Text.Encoding.UTF8.GetBytes(
            $"<package><metadata><id>{PackageName}</id>"
            + $"<version>{Version}</version><!--");
        byte[] suffix = """--></metadata></package>"""u8.ToArray();
        File.WriteAllBytes(
            Path.Combine(sourceDir, $"{PackageName}.nuspec"),
            [.. prefix, 0xFF, .. suffix]);
        NuGetCache.CommitPackage(
            sourceDir,
            nupkgPath: null,
            PackageName,
            Version,
            TestSourceKey);

        using var client = new HttpClient(new NotFoundHandler());
        NuspecProbeResult probe =
            await PackageExtractor.ProbeNuspecXmlAsync(
                client,
                PackageName,
                Version,
                sourceOptions: s_nugetOrgSource);

        Assert.Equal(NuspecProbeStatus.Indeterminate, probe.Status);
        Assert.Null(probe.Xml);
        Assert.NotNull(
            await PackageExtractor.TryGetNuspecXmlAsync(
                client,
                PackageName,
                Version,
                sourceOptions: s_nugetOrgSource));
    }

    [Fact]
    public async Task ProbeNuspecXmlAsync_UnreadableCachedReplica_IsIndeterminate()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix permissions are required for this cache replica test.");
            return;
        }

        const string PackageName = "Nuspec.Unreadable.Cache";
        const string Version = "1.0.0";
        string sourceDir = Path.Combine(
            _testRoot,
            "nuspec-unreadable");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(
            Path.Combine(sourceDir, $"{PackageName}.nuspec"),
            $"<package><metadata><id>{PackageName}</id>"
            + $"<version>{Version}</version></metadata></package>");
        CommittedPackage committed = NuGetCache.CommitPackage(
            sourceDir,
            nupkgPath: null,
            PackageName,
            Version,
            TestSourceKey);

        File.SetUnixFileMode(
            committed.ExtractPath,
            UnixFileMode.UserExecute);
        try
        {
            using var client = new HttpClient(new NotFoundHandler());
            NuspecProbeResult probe =
                await PackageExtractor.ProbeNuspecXmlAsync(
                    client,
                    PackageName,
                    Version,
                    sourceOptions: s_nugetOrgSource);

            Assert.Equal(
                NuspecProbeStatus.Indeterminate,
                probe.Status);
            Assert.Null(probe.Xml);
        }
        finally
        {
            File.SetUnixFileMode(
                committed.ExtractPath,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task ProbeNuspecXmlAsync_UsesValidLowerPrecedenceCachedReplica()
    {
        string packageName = $"Nuspec.Replica.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        string sourceA = "https://a.example/v3/index.json";
        string sourceB = "https://b.example/v3/index.json";
        string sourceAKey = NuGetCache.GetSourceKey(sourceA);
        string sourceBKey = NuGetCache.GetSourceKey(sourceB);

        string invalidSource = Path.Combine(_testRoot, "replica-a");
        Directory.CreateDirectory(invalidSource);
        File.WriteAllText(
            Path.Combine(invalidSource, $"{packageName}.nuspec"),
            """<package><metadata><id>Other.Package</id><version>1.0.0</version></metadata></package>""");
        NuGetCache.CommitPackage(
            invalidSource,
            nupkgPath: null,
            packageName,
            version,
            sourceAKey);

        string validSource = Path.Combine(_testRoot, "replica-b");
        Directory.CreateDirectory(validSource);
        File.WriteAllText(
            Path.Combine(validSource, $"{packageName}.nuspec"),
            $"""
            <package>
              <metadata>
                <id>{packageName}</id>
                <version>{version}</version>
              </metadata>
            </package>
            """);
        NuGetCache.CommitPackage(
            validSource,
            nupkgPath: null,
            packageName,
            version,
            sourceBKey);

        using var client = new HttpClient(new FailingHandler());
        NuspecProbeResult probe =
            await PackageExtractor.ProbeNuspecXmlAsync(
                client,
                packageName,
                version,
                sourceOptions: new NuGetSourceOptions
                {
                    Sources = [sourceA, sourceB],
                });

        Assert.Equal(NuspecProbeStatus.Present, probe.Status);
        Assert.Contains($"<id>{packageName}</id>", probe.Xml);
    }

    /// <summary>
    /// Direct file helper rejects oversize without loading the body as text.
    /// </summary>
    [Fact]
    public async Task TryReadNuspecFileAsync_Oversized_ReturnsNull()
    {
        Directory.CreateDirectory(_testRoot);
        string path = Path.Combine(_testRoot, "huge.nuspec");
        File.WriteAllBytes(path, new byte[PackageExtractor.MaxNuspecBytes + 1]);
        Assert.Null(
            await PackageExtractor.TryReadNuspecFileAsync(
                path,
                TestContext.Current.CancellationToken));

        File.WriteAllText(path, """<?xml version="1.0"?><package />""");
        Assert.Equal(
            """<?xml version="1.0"?><package />""",
            await PackageExtractor.TryReadNuspecFileAsync(
                path,
                TestContext.Current.CancellationToken));
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
            CreateNupkgFromDirectory(source, "platform-pack"),
            PackageName,
            Version,
            TestSourceKey);
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
            NuGetCache.TryGetCachedPackage(PackageName, Version, [TestSourceKey]));
        AssertNoPackStagingDirectories(PackageName);
    }

    [Fact]
    public async Task EnsurePackAsync_PreservesExistingInvalidPackEntry()
    {
        const string PackageName = "Microsoft.WindowsDesktop.App.Ref";
        const string Version = "10.0.2";
        string source = Path.Combine(_testRoot, "windowsdesktop-pack");
        // Include a real ref file so the retained nupkg and extract agree;
        // empty directories alone are not archive entries and fail product-owned
        // admission after the commit marker is written.
        string refFile = Path.Combine(
            source,
            "ref",
            "net10.0",
            "PresentationCore.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(refFile)!);
        File.WriteAllText(
            Path.Combine(source, $"{PackageName}.nuspec"),
            "<package />");
        File.WriteAllText(refFile, "fixture");
        NuGetCache.CommitPackage(
            source,
            CreateNupkgFromDirectory(source, "windowsdesktop-pack"),
            PackageName,
            Version,
            TestSourceKey);
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
                NuGetCache.GetPackageCachePath(packageName, Version, TestSourceKey)));

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
    public async Task ExtractPackageAsync_ConcurrentFailedAttemptSettlesWaitersAndCanRetry()
    {
        string packageName = $"sharedretry.test.{Guid.NewGuid():N}";
        const string Version = "4.1.0";
        var handler = new GatedFirstPackageHandler(
            "not a zip"u8.ToArray(),
            CreatePackageArchive(packageName, Version));
        using var client = new HttpClient(handler);
        string tempPrefix = $"package-shared-retry-{Guid.NewGuid():N}-";

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
            await handler.FirstRequestStarted.WaitAsync(
                TestContext.Current.CancellationToken);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            handler.ReleaseFirst();
        }

        PackageExtractionOutcome[] failed = await Task.WhenAll(requests);

        Assert.All(failed, outcome => Assert.False(outcome.IsSuccess));
        Assert.All(
            failed,
            outcome => Assert.Equal(failed[0].ErrorMessage, outcome.ErrorMessage));
        Assert.Equal(1, handler.RequestCount);

        PackageExtractionOutcome retried =
            await PackageExtractor.ExtractPackageAsync(
                client,
                packageName,
                tempDirPrefix: tempPrefix,
                sourceOptions: s_nugetOrgSource,
                version: Version);

        Assert.True(retried.IsSuccess, retried.ErrorMessage);
        Assert.Equal(2, handler.RequestCount);
        AssertNoStagingDirectories(packageName);
        AssertNoTemporaryDirectories(tempPrefix);
    }

    [Fact]
    public void TryGetCachedPackage_PrefersTheHigherPrecedenceSourcesCopy()
    {
        // When two configured feeds both have the coordinate cached, the read
        // must answer from the one a cold run would have downloaded from —
        // the first in configured order. Consulting slots in an undefined
        // order (a set rather than a list) would let cache layout decide which
        // feed's bytes get inspected, which is the confusion this scoping
        // exists to prevent, just moved from across configs to within one.
        string packageName = $"precedence.test.{Guid.NewGuid():N}";
        const string Version = "1.0.0";
        string firstKey = NuGetCache.GetSourceKey("https://feed-first.invalid/v3/index.json");
        string secondKey = NuGetCache.GetSourceKey("https://feed-second.invalid/v3/index.json");

        NuGetCache.CommitPackage(
            CreateExtractedPackage(
                Path.Combine(_testRoot, "precedence-first"), packageName, "first", payloadCount: 1),
            nupkgPath: null, packageName, Version, firstKey);
        NuGetCache.CommitPackage(
            CreateExtractedPackage(
                Path.Combine(_testRoot, "precedence-second"), packageName, "second", payloadCount: 1),
            nupkgPath: null, packageName, Version, secondKey);

        // Whichever source is listed first answers, regardless of which was
        // cached first.
        Assert.Equal(
            firstKey,
            Path.GetFileName(NuGetCache.TryGetCachedPackage(packageName, Version, [firstKey, secondKey])));
        Assert.Equal(
            secondKey,
            Path.GetFileName(NuGetCache.TryGetCachedPackage(packageName, Version, [secondKey, firstKey])));
    }

    [Fact]
    public void SourceKeys_PreservesConfiguredOrderAndDeduplicates()
    {
        // The ordering guarantee above is only worth anything if the keys reach
        // the cache in configured order.
        var sources = new List<NuGetFetch.PackageSource>
        {
            new("first", "https://feed-first.invalid/v3/index.json"),
            new("second", "https://feed-second.invalid/v3/index.json"),
            new("first-again", "https://feed-first.invalid/v3/index.json"),
        };

        var keys = NuGetSourceResolver.SourceKeys(sources);

        Assert.Equal(
            [
                NuGetCache.GetSourceKey("https://feed-first.invalid/v3/index.json"),
                NuGetCache.GetSourceKey("https://feed-second.invalid/v3/index.json"),
            ],
            keys);
    }

    [Fact]
    public void TryGetCachedPackage_DoesNotServeContentCommittedByAnotherSource()
    {
        // A private feed's bytes must not answer a request made under a
        // configuration that does not list that feed. The read happens before
        // the tool knows which source would serve the package, so the cache is
        // asked "is the committing source still one this caller reads from".
        string packageName = $"crosssource.test.{Guid.NewGuid():N}";
        const string Version = "1.0.0";
        string privateFeedKey = NuGetCache.GetSourceKey("https://private.invalid/v3/index.json");
        string publicFeedKey = NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json");

        string staged = CreateExtractedPackage(
            Path.Combine(_testRoot, "cross-source-stage"),
            packageName,
            "private",
            payloadCount: 1);
        NuGetCache.CommitPackage(staged, nupkgPath: null, packageName, Version, privateFeedKey);

        Assert.Null(NuGetCache.TryGetCachedPackage(packageName, Version, [publicFeedKey]));
        Assert.Null(NuGetCache.TryGetCachedPackage(packageName, Version, []));
    }

    [Fact]
    public void TryGetCachedPackage_ServesContentWhenItsSourceIsStillConfigured()
    {
        // The converse of the cross-source miss. Without this, "scope the cache
        // to its source" could be satisfied by never returning a hit at all,
        // which would silently turn the cache off.
        string packageName = $"samesource.test.{Guid.NewGuid():N}";
        const string Version = "1.0.0";
        string privateFeedKey = NuGetCache.GetSourceKey("https://private.invalid/v3/index.json");
        string publicFeedKey = NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json");

        string staged = CreateExtractedPackage(
            Path.Combine(_testRoot, "same-source-stage"),
            packageName,
            "private",
            payloadCount: 1);
        CommittedPackage committed = NuGetCache.CommitPackage(
            staged, nupkgPath: null, packageName, Version, privateFeedKey);

        // Order matters: the committing source is not the first one offered.
        Assert.Equal(
            committed.ExtractPath,
            NuGetCache.TryGetCachedPackage(packageName, Version, [publicFeedKey, privateFeedKey]));
    }

    [Fact]
    public void GlobalPackageContent_RequiresMatchingRecordedSource()
    {
        string packageName = $"globalprovenance.test.{Guid.NewGuid():N}".ToLowerInvariant();
        const string Version = "1.0.0";
        const string RecordedSource = "https://private.invalid/v3/index.json";
        string packageDirectory = CreateExtractedPackage(
            Path.Combine(_testRoot, "global", packageName, Version),
            packageName,
            "private",
            payloadCount: 1);
        File.WriteAllText(
            Path.Combine(packageDirectory, ".nupkg.metadata"),
            $$"""{"version":2,"source":"{{RecordedSource}}"}""");

        string recordedKey = NuGetCache.GetSourceKey(RecordedSource);
        string otherKey = NuGetCache.GetSourceKey(
            "https://api.nuget.org/v3/index.json");

        Assert.Null(NuGetCache.TryGetGlobalPackageContent(
            Path.Combine(_testRoot, "global"),
            packageName,
            Version,
            [otherKey]));

        CachedPackage? matching = NuGetCache.TryGetGlobalPackageContent(
            Path.Combine(_testRoot, "global"),
            packageName,
            Version,
            [otherKey, recordedKey]);

        Assert.NotNull(matching);
        Assert.Equal(packageDirectory, matching.ExtractPath);
        Assert.Equal(recordedKey, matching.ProducerKey);
    }

    [Fact]
    public void GlobalPackageContent_FileUriProvenanceMatchesPathAuthorization()
    {
        string packageName =
            $"globallocalprovenance.test.{Guid.NewGuid():N}".ToLowerInvariant();
        const string Version = "1.0.0";
        string sourcePath = Path.Combine(_testRoot, "folder-feed");
        string packageDirectory = CreateExtractedPackage(
            Path.Combine(_testRoot, "global-local", packageName, Version),
            packageName,
            "local",
            payloadCount: 1);
        File.WriteAllText(
            Path.Combine(packageDirectory, ".nupkg.metadata"),
            $$"""{"version":2,"source":"{{new Uri(sourcePath).AbsoluteUri}}"}""");

        string authorizedKey = NuGetCache.GetSourceKey(sourcePath);
        CachedPackage? matching = NuGetCache.TryGetGlobalPackageContent(
            Path.Combine(_testRoot, "global-local"),
            packageName,
            Version,
            [authorizedKey]);

        Assert.NotNull(matching);
        Assert.Equal(authorizedKey, matching.ProducerKey);
    }

    [Fact]
    public void GlobalPackageContent_RejectsRelativeLocalProvenanceWithoutBase()
    {
        string packageName =
            $"globalrelativeprovenance.test.{Guid.NewGuid():N}".ToLowerInvariant();
        const string Version = "1.0.0";
        string relativeSource = Path.Combine(
            "relative-feed",
            Guid.NewGuid().ToString("N"));
        string packageDirectory = CreateExtractedPackage(
            Path.Combine(_testRoot, "global-relative", packageName, Version),
            packageName,
            "local",
            payloadCount: 1);
        File.WriteAllText(
            Path.Combine(packageDirectory, ".nupkg.metadata"),
            $$"""{"version":2,"source":"{{relativeSource}}"}""");

        string authorizedKey =
            NuGetCache.GetSourceKey(Path.GetFullPath(relativeSource));

        Assert.Null(NuGetCache.TryGetGlobalPackageContent(
            Path.Combine(_testRoot, "global-relative"),
            packageName,
            Version,
            [authorizedKey]));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"version":2,"source":null}""")]
    [InlineData("""{"version":2,"source":42}""")]
    [InlineData("""{"version":2,"source":""}""")]
    [InlineData("""{"version":2,"source":"https://a.invalid","source":"https://b.invalid"}""")]
    [InlineData("""{"version":2,"source":"file:///tmp/feed?tenant=a"}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void GlobalPackageContent_RejectsMissingAmbiguousOrMalformedProvenance(
        string metadata)
    {
        string packageName = $"globalmissing.test.{Guid.NewGuid():N}".ToLowerInvariant();
        const string Version = "1.0.0";
        string packageDirectory = CreateExtractedPackage(
            Path.Combine(_testRoot, "global", packageName, Version),
            packageName,
            "payload",
            payloadCount: 1);
        File.WriteAllText(
            Path.Combine(packageDirectory, ".nupkg.metadata"),
            metadata);

        Assert.Null(NuGetCache.TryGetGlobalPackageContent(
            Path.Combine(_testRoot, "global"),
            packageName,
            Version,
            [NuGetCache.GetSourceKey("https://a.invalid")]));
    }

    [Fact]
    public void GlobalPackageContent_RejectsAbsentMetadata()
    {
        string packageName = $"globalabsent.test.{Guid.NewGuid():N}".ToLowerInvariant();
        const string Version = "1.0.0";
        CreateExtractedPackage(
            Path.Combine(_testRoot, "global", packageName, Version),
            packageName,
            "payload",
            payloadCount: 1);

        Assert.Null(NuGetCache.TryGetGlobalPackageContent(
            Path.Combine(_testRoot, "global"),
            packageName,
            Version,
            [TestSourceKey]));
    }

    [Fact]
    public void PackageContentCaches_DoNotIntroduceVersionCandidates()
    {
        string packageName = $"latestscope.test.{Guid.NewGuid():N}";
        string privateFeedKey = NuGetCache.GetSourceKey("https://private.invalid/v3/index.json");
        string publicFeedKey = NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json");

        foreach (var (version, sourceKey) in
            new[] { ("1.0.0", publicFeedKey), ("2.0.0", privateFeedKey) })
        {
            string staged = CreateExtractedPackage(
                Path.Combine(_testRoot, $"latest-stage-{version}"),
                packageName,
                "payload",
                payloadCount: 1);
            NuGetCache.CommitPackage(staged, nupkgPath: null, packageName, version, sourceKey);
        }

        Assert.Null(PackageExtractor.TryGetLatestCachedCandidateVersion(
            packageName,
            [publicFeedKey, privateFeedKey]));
    }

    [Fact]
    public void GetCachedVersions_GlobalPackageRequiresMatchingRecordedSource()
    {
        string packageName = $"globalhint.test.{Guid.NewGuid():N}".ToLowerInvariant();
        const string Version = "1.0.0";
        const string PrivateFeed = "https://private.invalid/v3/index.json";
        string globalPackagesPath = Path.Combine(_testRoot, "global-hints");
        string packageDirectory = Path.Combine(globalPackagesPath, packageName, Version);
        Directory.CreateDirectory(packageDirectory);
        File.WriteAllText(
            Path.Combine(packageDirectory, $"{packageName}.nuspec"),
            "<package />");
        File.WriteAllText(
            Path.Combine(packageDirectory, ".nupkg.metadata"),
            $$"""{"source":"{{PrivateFeed}}"}""");

        string? previousPackagesPath =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        try
        {
            Environment.SetEnvironmentVariable(
                "NUGET_PACKAGES",
                globalPackagesPath);
            NuGetCache.Initialize(
                "dotnet-inspect-test",
                _cachePath,
                skipNuGetCache: false);

            Assert.Empty(NuGetCache.GetCachedVersions(
                packageName,
                [NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json")]));
            Assert.Equal(
                [Version],
                NuGetCache.GetCachedVersions(
                    packageName,
                    [NuGetCache.GetSourceKey(PrivateFeed)]));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "NUGET_PACKAGES",
                previousPackagesPath);
            NuGetCache.Initialize(
                "dotnet-inspect-test",
                _cachePath,
                skipNuGetCache: true);
        }
    }

    [Fact]
    public void GetCachedVersions_ReturnsNewestUsableVersionsForConfiguredSources()
    {
        string packageName = $"versionhints.test.{Guid.NewGuid():N}";
        string privateFeedKey = NuGetCache.GetSourceKey("https://private.invalid/v3/index.json");
        string publicFeedKey = NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json");

        foreach (var (version, sourceKey) in
            new[]
            {
                ("1.0.0", publicFeedKey),
                ("2.0.0-preview.1", publicFeedKey),
                ("3.0.0", privateFeedKey),
            })
        {
            string staged = CreateExtractedPackage(
                Path.Combine(_testRoot, $"hint-stage-{version}"),
                packageName,
                "payload",
                payloadCount: 1);
            NuGetCache.CommitPackage(
                staged,
                nupkgPath: null,
                packageName,
                version,
                sourceKey);
        }

        Assert.Equal(
            ["2.0.0-preview.1", "1.0.0"],
            NuGetCache.GetCachedVersions(packageName, [publicFeedKey]));
        Assert.Equal(
            ["3.0.0", "2.0.0-preview.1", "1.0.0"],
            NuGetCache.GetCachedVersions(packageName, [publicFeedKey, privateFeedKey]));
        Assert.Equal(
            ["1.0.0"],
            NuGetCache.GetCachedVersions(
                packageName,
                [publicFeedKey],
                includePrerelease: false));
    }

    [Fact]
    public void GetCachedVersions_InvalidPackageName_HasNoSuggestions()
    {
        Assert.Empty(NuGetCache.GetCachedVersions(
            "../outside",
            [TestSourceKey]));
    }

    [Fact]
    public async Task ExtractPackageAsync_LatestLookupTimesOutEarlyAndOffersCachedPins()
    {
        string packageName = $"versiontimeout.test.{Guid.NewGuid():N}";
        foreach (string version in new[] { "1.0.0", "2.0.0" })
        {
            string staged = CreateExtractedPackage(
                Path.Combine(_testRoot, $"timeout-stage-{version}"),
                packageName,
                "payload",
                payloadCount: 1);
            NuGetCache.CommitPackage(
                staged,
                nupkgPath: null,
                packageName,
                version,
                TestSourceKey);
        }

        var handler = new NeverAnsweringHandler();
        using var client = new HttpClient(handler);

        Assert.Equal(
            TimeSpan.FromSeconds(1),
            PackageExtractor.CachedVersionResolutionTimeout);

        Task<PackageExtractionOutcome> extraction = PackageExtractor.ExtractPackageAsync(
            client,
            packageName,
            sourceOptions: s_nugetOrgSource);

        PackageExtractionOutcome outcome = await extraction.WaitAsync(
            TimeSpan.FromSeconds(15),
            TestContext.Current.CancellationToken);

        Assert.False(outcome.IsSuccess);
        Assert.Contains("online lookup timed out", outcome.ErrorMessage);
        Assert.Contains("Locally cached versions: 2.0.0, 1.0.0", outcome.ErrorMessage);
        Assert.Contains(
            $"dotnet-inspect package {packageName}@2.0.0",
            outcome.ErrorMessage);
        Assert.True(handler.RequestStarted.IsCompletedSuccessfully);
        Assert.True(handler.CancellationObserved.IsCompletedSuccessfully);
        Assert.True(
            handler.CancellationDelay < TimeSpan.FromSeconds(5),
            handler.CancellationDelay.ToString());
    }

    [Theory]
    [InlineData("https://pkgs.invalid/v3/index.json", "https://pkgs.invalid/v3/index.json/")]
    [InlineData("https://pkgs.invalid/v3/index.json", "HTTPS://PKGS.INVALID/v3/index.json")]
    [InlineData("https://pkgs.invalid/v3/index.json", "  https://pkgs.invalid/v3/index.json  ")]
    [InlineData("https://pkgs.invalid/v3/index.json", "https://pkgs.invalid:443/v3/index.json")]
    [InlineData("https://pkgs.invalid/a%2Fb/index.json", "https://pkgs.invalid/a%2fb/index.json")]
    [InlineData("https://xn--bcher-kva.invalid/v3/index.json", "https://b\u00fccher.invalid/v3/index.json")]
    [InlineData("https://pkgs.invalid/v3/?feed=A", "https://pkgs.invalid/v3?feed=A")]
    public void GetSourceKey_TreatsSpellingsOfOneFeedAsOneSource(string left, string right)
    {
        // Scheme, host and a trailing slash are not distinctions any feed makes.
        // Two configs naming one feed differently must share cached bytes.
        // The default port, percent-escape hex casing and an IDN host written
        // in unicode rather than punycode are equivalences the URI grammar
        // itself defines, so they fold too.
        Assert.Equal(NuGetCache.GetSourceKey(left), NuGetCache.GetSourceKey(right));
    }

    [Fact]
    public void GetSourceKey_TreatsATrailingSlashInsideAQueryAsAValue()
    {
        // A trailing slash folds because it terminates a *path*. Inside a query
        // it is an ordinary character in a value, and two feeds whose tokens or
        // parameters differ only by that character are two feeds. Trimming the
        // recombined URL rather than its path alone folded these together.
        Assert.NotEqual(
            NuGetCache.GetSourceKey("https://pkgs.invalid/v3/index.json?feed=a/"),
            NuGetCache.GetSourceKey("https://pkgs.invalid/v3/index.json?feed=a"));
    }

    [Fact]
    public void GetSourceKey_TreatsAFragmentAsPartOfSourceIdentity()
    {
        Assert.NotEqual(
            NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json"),
            NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json#custom"));
    }

    [Fact]
    public void GetSourceKey_DistinguishesRepeatedTrailingSlashes()
    {
        Assert.NotEqual(
            NuGetCache.GetSourceKey("https://pkgs.invalid/v3/index.json"),
            NuGetCache.GetSourceKey("https://pkgs.invalid/v3/index.json//"));
    }

    [Fact]
    public void GetSourceKey_DerivesWebSourceIdentityFromTheSharedCanonicalizer()
    {
        // The cache identity and the credential-scope comparison must agree
        // about what "the same endpoint" means. A second canonicalizer would be
        // free to drift, and the two directions are not symmetric: folding a
        // distinction NuGetCredentialScope preserves lets one feed's slot
        // answer for another. This asserts they are the same function, so the
        // cases proven for IsSameEndpoint hold for cache slots too.
        string[] urls =
        [
            "https://pkgs.invalid/v3/index.json",
            "https://pkgs.invalid/v3/index.json/",
            "HTTPS://PKGS.INVALID:443/v3/index.json",
            "https://pkgs.invalid/FeedA/v3/index.json",
            "https://pkgs.invalid/v3/index.json?feed=a/",
            "https://pkgs.invalid/v3/index.json?feed=a",
            "https://pkgs.invalid/v3/index.json#custom",
        ];

        foreach (var left in urls)
        {
            foreach (var right in urls)
            {
                Assert.Equal(
                    NuGetCredentialScope.IsSameEndpoint(left, right),
                    NuGetCache.GetSourceKey(left) == NuGetCache.GetSourceKey(right));
            }
        }
    }

    [Fact]
    public void GetSourceKey_UsesHostCaseSemanticsForLocalFolders()
    {
        string upper =
            NuGetCache.GetSourceKey(
                Path.Combine(Path.GetTempPath(), "FeedA"));
        string lower =
            NuGetCache.GetSourceKey(
                Path.Combine(Path.GetTempPath(), "feeda"));

        Assert.Equal(OperatingSystem.IsWindows(), upper == lower);
    }

    [Fact]
    public void GetSourceKey_PathAndFileUriShareLocalFolderIdentity()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "feed with spaces");

        Assert.Equal(
            NuGetCache.GetSourceKey(path),
            NuGetCache.GetSourceKey(new Uri(path).AbsoluteUri));
    }

    [Fact]
    public void GetSourceKey_RejectsRelativeLocalFolderWithoutBase()
    {
        Assert.Throws<ArgumentException>(
            () => NuGetCache.GetSourceKey(
                Path.Combine("relative", "feed")));
    }

    [Theory]
    [InlineData("https://pkgs.invalid/FeedA/v3/index.json", "https://pkgs.invalid/feeda/v3/index.json")]
    [InlineData("https://pkgs.invalid/v3/index.json?feed=A", "https://pkgs.invalid/v3/index.json?feed=a")]
    public void GetSourceKey_KeepsFeedsThatDifferOnlyByPathCaseApart(string left, string right)
    {
        // Only scheme and host are case-insensitive. /FeedA and /feeda are
        // different feeds on a case-sensitive server, and aliasing them would
        // serve one feed's bytes for the other — the bug this scoping exists to
        // prevent. NuGetSourceResolver refuses whole-URL case-insensitive
        // matching for the same reason.
        Assert.NotEqual(NuGetCache.GetSourceKey(left), NuGetCache.GetSourceKey(right));
    }

    [Fact]
    public void GetSourceKey_DistinguishesDifferentFeeds()
    {
        Assert.NotEqual(
            NuGetCache.GetSourceKey("https://pkgs.invalid/one/v3/index.json"),
            NuGetCache.GetSourceKey("https://pkgs.invalid/two/v3/index.json"));
    }

    [Fact]
    public void GetSourceKey_IsAnOpaquePathSafeIdentifier()
    {
        // The key becomes a directory name, so it must be a safe path segment
        // for any URL and must not carry a URL's text into the cache layout.
        // This is opacity, not confidentiality: a feed URL is low entropy, and
        // a local reader who can see this can already see the cached packages.
        const string Secret = "s3cret-token";
        var key = NuGetCache.GetSourceKey($"https://user:{Secret}@pkgs.invalid/v3/index.json");

        Assert.DoesNotContain(Secret, key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pkgs.invalid", key, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[0-9a-f]{32}$", key);
        Assert.Equal(-1, key.IndexOfAny(Path.GetInvalidFileNameChars()));
    }

    [Fact]
    public async Task ExtractPackageAsync_DoesNotShareOneDownloadAcrossDifferentSourceSets()
    {
        // Single-flight coalescing must not join callers whose configurations
        // differ. If it did, a caller configured for one feed could be handed
        // bytes fetched from a feed it does not read from — the same confusion
        // the cache scoping prevents, arriving by a different route.
        string packageName = $"flightscope.test.{Guid.NewGuid():N}";
        const string Version = "1.0.0";
        var handler = new GatedPackageHandler(CreatePackageArchive(packageName, Version));
        using var client = new HttpClient(handler);
        string tempPrefix = $"package-flight-scope-{Guid.NewGuid():N}-";

        var feedB = new NuGetSourceOptions { Sources = ["https://feed-b.invalid/v3/index.json"] };

        Task<PackageExtractionOutcome> viaNuGetOrg = PackageExtractor.ExtractPackageAsync(
            client, packageName, tempDirPrefix: tempPrefix,
            sourceOptions: s_nugetOrgSource, version: Version);
        Task<PackageExtractionOutcome> viaFeedB = PackageExtractor.ExtractPackageAsync(
            client, packageName, tempDirPrefix: tempPrefix,
            sourceOptions: feedB, version: Version);

        bool bothInFlight;
        try
        {
            // The assertion: two callers with different source sets produce two
            // concurrent downloads. If they shared a flight the count stays at
            // 1 and this times out, which is the failure being guarded against.
            bothInFlight = await WaitForRequestCountAsync(handler, 2, TimeSpan.FromSeconds(20));
        }
        finally
        {
            handler.Release();
        }

        Assert.True(
            bothInFlight,
            "Callers with different source sets shared one download; "
                + $"observed {handler.RequestCount} request(s), expected 2.");

        PackageExtractionOutcome[] outcomes = await Task.WhenAll(viaNuGetOrg, viaFeedB);

        // Only the nuget.org flight is expected to complete: this fixture does
        // not serve a parseable service index for the second feed. That is
        // immaterial here — the point is that the flights were separate.
        Assert.True(outcomes[0].IsSuccess);
        AssertNoTemporaryDirectories(tempPrefix);
    }

    [Fact]
    public void PackageAcquisitionIdentity_IncludesAuthorizedProducerOrder()
    {
        string producerA = NuGetCache.GetSourceKey(
            "https://feed-a.invalid/v3/index.json");
        string producerB = NuGetCache.GetSourceKey(
            "https://feed-b.invalid/v3/index.json");

        var forward = PackageExtractor.CreatePackageAcquisitionRequest(
            "example",
            "1.0.0",
            [producerA, producerB],
            null,
            null);
        var reversed = PackageExtractor.CreatePackageAcquisitionRequest(
            "example",
            "1.0.0",
            [producerB, producerA],
            null,
            null);
        var reporterAOnly = PackageExtractor.CreatePackageAcquisitionRequest(
            "example",
            "1.0.0",
            [producerA],
            null,
            null);

        Assert.NotEqual(forward, reversed);
        Assert.NotEqual(forward, reporterAOnly);
    }

    [Fact]
    public void PackageAcquisitionIdentity_IncludesCredentialTransport()
    {
        const string SourceUrl = "https://feed.invalid/v3/index.json";
        string producer = NuGetCache.GetSourceKey(SourceUrl);
        using var client = new HttpClient();
        var stale = new NuGetFetch.PackageSource(
            "feed",
            SourceUrl,
            new NuGetFetch.PackageSourceCredential("user", "stale"));
        var valid = new NuGetFetch.PackageSource(
            "feed",
            SourceUrl,
            new NuGetFetch.PackageSourceCredential("user", "valid"));

        var staleRequest = PackageExtractor.CreatePackageAcquisitionRequest(
            "example",
            "1.0.0",
            [producer],
            [stale],
            client);
        var validRequest = PackageExtractor.CreatePackageAcquisitionRequest(
            "example",
            "1.0.0",
            [producer],
            [valid],
            client);

        Assert.NotEqual(staleRequest, validRequest);
        Assert.DoesNotContain("stale", staleRequest.ToString());
        Assert.DoesNotContain("valid", validRequest.ToString());
    }

    [Fact]
    public async Task ExtractPackageAsync_DoesNotShareFlightsAcrossHttpClients()
    {
        string packageName = $"clientflight.test.{Guid.NewGuid():N}";
        const string Version = "1.0.0";
        byte[] archive = CreatePackageArchive(packageName, Version);
        var firstHandler = new GatedPackageHandler(archive);
        var secondHandler = new GatedPackageHandler(archive);
        using var firstClient = new HttpClient(firstHandler);
        using var secondClient = new HttpClient(secondHandler);

        Task<PackageExtractionOutcome> first =
            PackageExtractor.ExtractPackageAsync(
                firstClient,
                packageName,
                sourceOptions: s_nugetOrgSource,
                version: Version);
        Task<PackageExtractionOutcome> second =
            PackageExtractor.ExtractPackageAsync(
                secondClient,
                packageName,
                sourceOptions: s_nugetOrgSource,
                version: Version);

        bool[] bothStarted;
        try
        {
            bothStarted = await Task.WhenAll(
                WaitForRequestCountAsync(
                    firstHandler,
                    1,
                    TimeSpan.FromSeconds(20)),
                WaitForRequestCountAsync(
                    secondHandler,
                    1,
                    TimeSpan.FromSeconds(20)));
        }
        finally
        {
            firstHandler.Release();
            secondHandler.Release();
        }

        Assert.All(bothStarted, Assert.True);
        PackageExtractionOutcome[] outcomes = await Task.WhenAll(first, second);
        Assert.All(outcomes, outcome => Assert.True(outcome.IsSuccess));
    }

    [Fact]
    public async Task DiscoveredPackage_DoesNotUsePayloadFromNonReportingActiveSource()
    {
        string packageName = $"reporterscope.test.{Guid.NewGuid():N}";
        const string SelectedVersion = "2.0.0";
        const string FeedA = "https://feed-a.invalid/v3/index.json";
        const string FeedB = "https://feed-b.invalid/v3/index.json";
        string producerA = NuGetCache.GetSourceKey(FeedA);
        string producerB = NuGetCache.GetSourceKey(FeedB);

        NuGetCache.CommitPackage(
            CreateExtractedPackage(
                Path.Combine(_testRoot, "reporter-a"),
                packageName,
                "A",
                payloadCount: 1),
            nupkgPath: null,
            packageName,
            SelectedVersion,
            producerA);

        byte[] selectedArchive = CreatePackageArchive(
            packageName,
            SelectedVersion);
        var handler = new ReporterScopedPackageHandler(
            packageName,
            selectedArchive);
        using var client = new HttpClient(handler);

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                client,
                packageName,
                sourceOptions: new NuGetSourceOptions
                {
                    Sources = [FeedA, FeedB],
                });

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal(SelectedVersion, outcome.Result!.Version);
        Assert.Equal(producerB, outcome.Result.ProducerKey);
        Assert.Equal(1, handler.PackageDownloadCount);
        Assert.Equal(
            NuGetCache.GetPackageCachePath(
                packageName,
                SelectedVersion,
                producerB),
            outcome.Result.ExtractPath);
    }

    [Fact]
    public async Task PinnedPackage_MayUseAnyActiveProducer()
    {
        string packageName = $"pinnedscope.test.{Guid.NewGuid():N}";
        const string Version = "2.0.0";
        const string FeedA = "https://feed-a.invalid/v3/index.json";
        const string FeedB = "https://feed-b.invalid/v3/index.json";
        string producerA = NuGetCache.GetSourceKey(FeedA);

        string extracted = CreateExtractedPackage(
            Path.Combine(_testRoot, "pinned-a"),
            packageName,
            "A",
            payloadCount: 1);
        CommittedPackage committed = NuGetCache.CommitPackage(
            extracted,
            CreateNupkgFromDirectory(extracted, "pinned-a"),
            packageName,
            Version,
            producerA);
        using var client = new HttpClient(new FailingHandler());

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                client,
                packageName,
                sourceOptions: new NuGetSourceOptions
                {
                    Sources = [FeedA, FeedB],
                },
                version: Version);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal(committed.ExtractPath, outcome.Result!.ExtractPath);
        Assert.Equal(producerA, outcome.Result.ProducerKey);
    }

    [Fact]
    public void CommitPackage_SecondSourceCommitsAlongsideTheFirst()
    {
        // The same coordinate served by two configured feeds. The second feed
        // correctly misses the first feed's slot, downloads, and must be able
        // to commit. Holding both in one slot cannot work: the second feed
        // would either overwrite content it is not entitled to serve, or fail
        // to cache a package it just downloaded successfully.
        string packageName = $"twofeeds.test.{Guid.NewGuid():N}";
        const string Version = "1.0.0";
        string keyA = NuGetCache.GetSourceKey("https://feed-a.invalid/v3/index.json");
        string keyB = NuGetCache.GetSourceKey("https://feed-b.invalid/v3/index.json");

        CommittedPackage a = NuGetCache.CommitPackage(
            CreateExtractedPackage(Path.Combine(_testRoot, "two-a"), packageName, "A", 1),
            nupkgPath: null, packageName, Version, keyA);

        Assert.Null(NuGetCache.TryGetCachedPackage(packageName, Version, [keyB]));

        CommittedPackage b = NuGetCache.CommitPackage(
            CreateExtractedPackage(Path.Combine(_testRoot, "two-b"), packageName, "B", 1),
            nupkgPath: null, packageName, Version, keyB);

        Assert.NotEqual(a.ExtractPath, b.ExtractPath);
        Assert.Equal(a.ExtractPath, NuGetCache.TryGetCachedPackage(packageName, Version, [keyA]));
        Assert.Equal(b.ExtractPath, NuGetCache.TryGetCachedPackage(packageName, Version, [keyB]));

        // Each slot keeps the bytes its own feed served.
        Assert.Equal("A", File.ReadAllText(Path.Combine(a.ExtractPath, "payload", "0000.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(b.ExtractPath, "payload", "0000.txt")));
        AssertNoStagingDirectories(packageName);
    }

    [Fact]
    public async Task CommitPackage_ConcurrentPublishersFromDifferentSourcesBothSucceed()
    {
        string packageName = $"twofeedsrace.test.{Guid.NewGuid():N}";
        const string Version = "1.0.0";
        string keyA = NuGetCache.GetSourceKey("https://feed-a.invalid/v3/index.json");
        string keyB = NuGetCache.GetSourceKey("https://feed-b.invalid/v3/index.json");
        string sourceA = CreateExtractedPackage(Path.Combine(_testRoot, "race-a"), packageName, "A", 64);
        string sourceB = CreateExtractedPackage(Path.Combine(_testRoot, "race-b"), packageName, "B", 64);

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim();

        Task<CommittedPackage> Publish(string dir, string key) =>
            StartDedicatedWorker(
                ready,
                start,
                () => NuGetCache.CommitPackage(
                    dir,
                    nupkgPath: null,
                    packageName,
                    Version,
                    key));

        Task<CommittedPackage> publishA = Publish(sourceA, keyA);
        Task<CommittedPackage> publishB = Publish(sourceB, keyB);

        (bool publishersReady, CommittedPackage[] committed) =
            await WaitForAndJoinWorkersAsync(
                ready,
                start,
                TestContext.Current.CancellationToken,
                publishA,
                publishB);
        Assert.True(publishersReady);

        Assert.NotEqual(committed[0].ExtractPath, committed[1].ExtractPath);
        AssertNoStagingDirectories(packageName);
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

        Assert.Null(NuGetCache.TryGetCachedPackage(packageName, Version, [TestSourceKey]));
    }

    /// <summary>
    /// Builds a retained nupkg whose entry set matches <paramref name="directory"/>
    /// exactly so product-owned admission (commit marker + archive match) accepts
    /// the committed slot.
    /// </summary>
    private string CreateNupkgFromDirectory(string directory, string name)
    {
        string path = Path.Combine(
            _testRoot,
            $"{name}-{Guid.NewGuid():N}.nupkg");
        using (FileStream file = File.Create(path))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            foreach (string filePath in Directory.EnumerateFiles(
                         directory,
                         "*",
                         SearchOption.AllDirectories))
            {
                string relative = Path
                    .GetRelativePath(directory, filePath)
                    .Replace('\\', '/');
                archive.CreateEntryFromFile(filePath, relative);
            }
        }

        return path;
    }

    private string CreateNupkg(
        string packageName,
        string version,
        string name)
    {
        string path = Path.Combine(
            _testRoot,
            $"{name}-{Guid.NewGuid():N}.nupkg");
        File.WriteAllBytes(
            path,
            CreatePackageArchive(packageName, version));
        return path;
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

    private static async Task<bool> WaitForRequestCountAsync(
        GatedPackageHandler handler,
        int expected,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (handler.RequestCount >= expected)
                return true;
            await Task.Delay(25);
        }

        return handler.RequestCount >= expected;
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

    private static Task<T> StartDedicatedWorker<T>(
        CountdownEvent ready,
        ManualResetEventSlim start,
        Func<T> action)
    {
        // These tests exercise filesystem concurrency, not ThreadPool scheduling.
        // Dedicated workers keep a loaded suite from consuming the readiness budget.
        return Task.Factory.StartNew(
            () =>
            {
                ready.Signal();
                start.Wait();
                return action();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static async Task<(bool Ready, T[] Results)> WaitForAndJoinWorkersAsync<T>(
        CountdownEvent ready,
        ManualResetEventSlim start,
        CancellationToken cancellationToken,
        params Task<T>[] workers)
    {
        bool workersReady = false;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? cancellation = null;
        try
        {
            workersReady = ready.Wait(
                TimeSpan.FromSeconds(5),
                cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            cancellation =
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            start.Set();
        }

        T[] results;
        try
        {
            results = await Task.WhenAll(workers);
        }
        finally
        {
            cancellation?.Throw();
        }

        return (workersReady, results);
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
        // Cache entries are {name}/{version}/{sourceKey}, so staging directories
        // sit one level deeper than the package directory.
        string packagePath = NuGetCache.GetPackageCachePath(
            packageName,
            "unused",
            TestSourceKey);
        string parent = Path.GetDirectoryName(Path.GetDirectoryName(packagePath)!)!;
        if (!Directory.Exists(parent))
            return;

        Assert.Empty(
            Directory.GetDirectories(parent, ".*.tmp-*", SearchOption.AllDirectories));
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

    [Fact]
    public async Task ExtractPackageAsync_PackageSourceMappingSelectsEligibleSource()
    {
        const string PackageName = "gamma.mapped";
        const string Version = "1.0.0";
        string configPath = Path.Combine(
            Path.GetTempPath(),
            $"acquisition-mapping-{Guid.NewGuid():N}.config");
        File.WriteAllText(configPath, """
            <configuration>
              <packageSources>
                <clear />
                <add key="refusing" value="https://refusing.example/v3/index.json" />
                <add key="serving" value="https://serving.example/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="serving">
                  <package pattern="gamma.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

        var handler = new RefusesOnePackageHandler(
            PackageName,
            CreatePackageArchive(PackageName, Version));
        using var client = new HttpClient(handler);

        try
        {
            PackageExtractionOutcome outcome = await PackageExtractor.ExtractPackageAsync(
                client,
                PackageName,
                sourceOptions: new NuGetSourceOptions { ConfigFile = configPath },
                version: Version);

            Assert.True(outcome.IsSuccess);
            Assert.DoesNotContain(
                handler.Requests,
                url => url.Contains("refusing.example", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task ExtractPackageAsync_OneSourceRefusesAndAnotherAnswers_SucceedsWithoutBlamingTheRefusal()
    {
        const string PackageName = "gamma.available";
        const string Version = "1.0.0";
        var sources = new NuGetSourceOptions
        {
            Sources =
            [
                "https://refusing.example/v3/index.json",
                "https://serving.example/v3/index.json",
            ],
        };

        // A recorded failure is advisory, not fatal. Configuring a private feed that 401s
        // alongside a public one that answers is the ordinary case, so a refusal from one
        // source must not fail, or annotate, a lookup another source satisfied.
        var handler = new RefusesOnePackageHandler(
            PackageName,
            CreatePackageArchive(PackageName, Version));
        using var client = new HttpClient(handler);

        PackageExtractionOutcome outcome = await PackageExtractor.ExtractPackageAsync(
            client,
            PackageName,
            sourceOptions: sources,
            version: Version);

        Assert.True(outcome.IsSuccess);
        Assert.Null(outcome.ErrorMessage);
        Assert.Equal(PackageName, outcome.Result!.PackageName);
    }

    [Fact]
    public async Task ExtractPackageAsync_RedirectTargetMissingDoesNotBlameTheWrappersRefusedSource()
    {
        const string WrapperPackage = "alpha.wrapper";
        const string TargetPackage = "beta.payload";
        const string Version = "1.0.0";
        var sources = new NuGetSourceOptions
        {
            Sources =
            [
                "https://refusing.example/v3/index.json",
                "https://serving.example/v3/index.json",
            ],
        };

        // refusing.example rejects the wrapper but answers a plain 404 for the redirect target,
        // so the only recorded refusal belongs to hop 1. If the collector spans the whole
        // redirect traversal, hop 2 inherits it and blames a source that never refused it.
        var handler = new RefusesOnePackageHandler(
            WrapperPackage,
            CreateToolWrapperArchive(
                WrapperPackage,
                Version,
                redirectPackageName: TargetPackage));
        using var client = new HttpClient(handler);

        PackageExtractionOutcome outcome = await PackageExtractor.ExtractPackageAsync(
            client,
            WrapperPackage,
            sourceOptions: sources,
            version: Version);

        Assert.False(outcome.IsSuccess);
        Assert.DoesNotContain("refusing.example", outcome.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain(WrapperPackage, outcome.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(TargetPackage, outcome.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RefusesOnePackageHandler(
        string refusedPackageId,
        byte[] refusedPackageArchive) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requests.Add(uri.AbsoluteUri);
            HttpResponseMessage response;

            if (uri.AbsolutePath.Equals("/v3/index.json", StringComparison.OrdinalIgnoreCase))
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""
                        {"version":"3.0.0","resources":[
                        {"@id":"https://{{uri.Host}}/v3/flat2/","@type":"PackageBaseAddress/3.0.0"}]}
                        """),
                };
            }
            else
            {
                bool isRefusedPackage = uri.AbsoluteUri.Contains(
                    refusedPackageId, StringComparison.OrdinalIgnoreCase);

                if (uri.Host.Equals("refusing.example", StringComparison.OrdinalIgnoreCase))
                {
                    response = new HttpResponseMessage(
                        isRefusedPackage ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound);
                }
                else if (isRefusedPackage)
                {
                    response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(refusedPackageArchive),
                    };
                }
                else
                {
                    response = new HttpResponseMessage(HttpStatusCode.NotFound);
                }
            }

            response.RequestMessage = request;
            response.Content ??= new StringContent(string.Empty);
            return Task.FromResult(response);
        }
    }

    private sealed class NeverAnsweringHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _requestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancellationObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _requestStartedTimestamp;
        private long _cancellationObservedTimestamp;

        public Task RequestStarted => _requestStarted.Task;

        public Task CancellationObserved => _cancellationObserved.Task;

        public TimeSpan CancellationDelay => Stopwatch.GetElapsedTime(
            Volatile.Read(ref _requestStartedTimestamp),
            Volatile.Read(ref _cancellationObservedTimestamp));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Volatile.Write(ref _requestStartedTimestamp, Stopwatch.GetTimestamp());
            _requestStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The request should have been canceled.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Volatile.Write(
                    ref _cancellationObservedTimestamp,
                    Stopwatch.GetTimestamp());
                _cancellationObserved.TrySetResult();
                throw;
            }
        }
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

    private sealed class GatedFirstPackageHandler(
        byte[] firstResponse,
        byte[] secondResponse)
        : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _firstRequestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public Task FirstRequestStarted => _firstRequestStarted.Task;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public void ReleaseFirst() => _releaseFirst.TrySetResult(true);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int requestNumber = Interlocked.Increment(ref _requestCount);
            byte[] response = requestNumber switch
            {
                1 => firstResponse,
                2 => secondResponse,
                _ => throw new InvalidOperationException(
                    $"Unexpected package request #{requestNumber}."),
            };

            if (requestNumber == 1)
            {
                _firstRequestStarted.TrySetResult(true);
                await _releaseFirst.Task.WaitAsync(cancellationToken);
            }

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

    private sealed class ReporterScopedPackageHandler(
        string packageName,
        byte[] packageArchive)
        : HttpMessageHandler
    {
        private readonly string _normalizedName =
            packageName.ToLowerInvariant();
        private int _packageDownloadCount;

        public int PackageDownloadCount =>
            Volatile.Read(ref _packageDownloadCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url == "https://feed-a.invalid/v3/index.json")
            {
                return Json(
                    """{"resources":[{"@id":"https://content-a.invalid/flat/","@type":"PackageBaseAddress/3.0.0"}]}""");
            }

            if (url == "https://feed-b.invalid/v3/index.json")
            {
                return Json(
                    """{"resources":[{"@id":"https://content-b.invalid/flat/","@type":"PackageBaseAddress/3.0.0"}]}""");
            }

            if (url == $"https://content-a.invalid/flat/{_normalizedName}/index.json")
                return Json("""{"versions":["1.0.0"]}""");
            if (url == $"https://content-b.invalid/flat/{_normalizedName}/index.json")
                return Json("""{"versions":["2.0.0"]}""");
            if (url == $"https://content-b.invalid/flat/{_normalizedName}/2.0.0/{_normalizedName}.2.0.0.nupkg")
            {
                Interlocked.Increment(ref _packageDownloadCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(packageArchive),
                });
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
    }

    private sealed class CoordinateScopedRedirectHandler(
        string wrapperPackage,
        string payloadPackage,
        byte[] wrapperArchive,
        byte[] payloadArchive)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url == "https://feed-a.invalid/v3/index.json")
            {
                return Json(
                    """{"resources":[{"@id":"https://content-a.invalid/flat/","@type":"PackageBaseAddress/3.0.0"}]}""");
            }

            if (url == "https://feed-b.invalid/v3/index.json")
            {
                return Json(
                    """{"resources":[{"@id":"https://content-b.invalid/flat/","@type":"PackageBaseAddress/3.0.0"}]}""");
            }

            if (url == NupkgUrl("content-a", wrapperPackage))
                return Bytes(wrapperArchive);
            if (url == NupkgUrl("content-b", payloadPackage))
                return Bytes(payloadArchive);

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string NupkgUrl(string host, string packageName)
            => $"https://{host}.invalid/flat/{packageName}/1.0.0/{packageName}.1.0.0.nupkg";

        private static Task<HttpResponseMessage> Json(string body)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });

        private static Task<HttpResponseMessage> Bytes(byte[] body)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            });
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request,
            });
    }
}
