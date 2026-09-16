using DotnetInspector.Cache;

namespace DotnetInspector.Cache.Tests;

[Collection(CacheStateCollection.Name)]
public sealed class PersistentCacheTests : IDisposable
{
    private readonly string _cacheBasePath = Path.Combine(
        Path.GetTempPath(),
        $"dotnet-inspector-cache-tests-{Guid.NewGuid():N}");
    private readonly string _appName =
        $"dotnet-inspector-cache-tests-{Guid.NewGuid():N}";

    public PersistentCacheTests()
    {
        PersistentCache.Initialize(_appName, _cacheBasePath);
    }

    public void Dispose()
    {
        PersistentCache.CancelAndWaitForMaintenance(Timeout.InfiniteTimeSpan);
        if (Directory.Exists(_cacheBasePath))
            Directory.Delete(_cacheBasePath, recursive: true);
    }

    [Fact]
    public void GetFilePath_UsesStableSha256HashAndBucketsUnderCategory()
    {
        string path = PersistentCache.GetFilePath(
            "metadata",
            "cache-key",
            extension: "bin");

        Assert.Equal(
            Path.Combine(
                _cacheBasePath,
                "metadata",
                "89",
                "dd5bff4fb6fd83ee0679edfb07756d0d22222a414f0c3ac6fd92c120e4fe4f.bin"),
            path);
        Assert.True(PersistentCache.IsPathInCacheContext(path));
    }

    [Fact]
    public void CacheContext_AdmitsRootAndChildrenButRejectsSiblingPrefixes()
    {
        string child = Path.Combine(
            _cacheBasePath,
            "metadata",
            "..",
            "packages",
            "entry");
        string sibling = _cacheBasePath + "-outside";

        Assert.True(PersistentCache.IsPathInCacheContext(_cacheBasePath));
        Assert.True(PersistentCache.IsPathInCacheContext(child));
        Assert.False(PersistentCache.IsPathInCacheContext(sibling));
        Assert.False(PersistentCache.IsPathInCacheContext(" "));
        Assert.Throws<InvalidOperationException>(
            () => PersistentCache.EnsurePathInCacheContext(sibling));
    }

    [Theory]
    [InlineData("../../outside")]
    [InlineData("/absolute/path")]
    [InlineData("https://feed.example/package?key=secret")]
    [InlineData(@"..\..\outside")]
    public void GetFilePath_HashesUntrustedKeysWithoutChangingTheRoot(string key)
    {
        string path = PersistentCache.GetFilePath("entries", key);

        Assert.True(PersistentCache.IsPathInCacheContext(path));
        Assert.Equal(
            Path.Combine(_cacheBasePath, "entries"),
            Path.GetDirectoryName(Path.GetDirectoryName(path)));
        Assert.Equal(67, Path.GetFileName(path).Length);
    }

    [Fact]
    public void Clear_RejectsTraversalAndAbsolutePathsOutsideActiveAndLegacyRoots()
    {
        string outside = _cacheBasePath + "-outside";
        Directory.CreateDirectory(outside);
        string file = Path.Combine(outside, "keep.txt");
        File.WriteAllText(file, "keep");
        try
        {
            Assert.Throws<InvalidOperationException>(() => PersistentCache.Clear(".."));
            Assert.Throws<InvalidOperationException>(() => PersistentCache.Clear(outside));
            Assert.Equal("keep", File.ReadAllText(file));

            string? legacy = PersistentCache.GetLegacyBasePath();
            if (legacy is not null)
            {
                Assert.True(PersistentCache.IsPathInCacheContext(legacy));
                Assert.True(PersistentCache.IsPathInCacheContext(Path.Combine(legacy, "entry")));
                Assert.False(PersistentCache.IsPathInCacheContext(legacy + "-outside"));
            }
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetAndTryGet_RoundTripAndReportStoreThenHit(bool bytes)
    {
        string category = $"roundtrip-{Guid.NewGuid():N}";
        string key = "entry";
        var observer = new RecordingObserver(category);
        using var subscription = CacheTelemetry.Subscribe(observer);

        if (bytes)
        {
            byte[] content = [0, 1, 2, 255];
            PersistentCache.SetBytes(category, key, content, extension: "bin");
            Assert.Equal(
                content,
                PersistentCache.TryGetBytes(category, key, extension: "bin"));
        }
        else
        {
            const string content = "cached text \u2603";
            PersistentCache.Set(category, key, content, extension: "txt");
            Assert.Equal(
                content,
                PersistentCache.TryGet(category, key, extension: "txt"));
        }

        Assert.Collection(
            observer.Observations,
            observation => Assert.Equal(CacheAccessResult.Store, observation.Result),
            observation => Assert.Equal(CacheAccessResult.Hit, observation.Result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryGet_ExpiredEntryReturnsMiss(bool bytes)
    {
        string category = $"expiration-{Guid.NewGuid():N}";
        string key = "entry";
        string extension = bytes ? "bin" : "txt";
        if (bytes)
            PersistentCache.SetBytes(category, key, [1, 2, 3], extension);
        else
            PersistentCache.Set(category, key, "expired", extension);

        string path = PersistentCache.GetFilePath(category, key, extension);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromHours(1));

        var observer = new RecordingObserver(category);
        using var subscription = CacheTelemetry.Subscribe(observer);

        if (bytes)
        {
            Assert.Null(PersistentCache.TryGetBytes(
                category,
                key,
                TimeSpan.FromMinutes(1),
                extension));
        }
        else
        {
            Assert.Null(PersistentCache.TryGet(
                category,
                key,
                TimeSpan.FromMinutes(1),
                extension));
        }

        Assert.Equal(
            CacheAccessResult.Miss,
            Assert.Single(observer.Observations).Result);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TryGet_ReadFailureIsBestEffortAndNeverReportsHit(
        bool bytes,
        bool timeToLive)
    {
        string category = $"read-failure-{Guid.NewGuid():N}";
        string key = "entry";
        string extension = bytes ? "bin" : "txt";
        string path = PersistentCache.GetFilePath(category, key, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);

        using IDisposable readBlock = BlockReads(path);
        var observer = new RecordingObserver(category);
        using var subscription = CacheTelemetry.Subscribe(observer);

        if (bytes)
        {
            byte[]? result = timeToLive
                ? PersistentCache.TryGetBytes(
                    category,
                    key,
                    TimeSpan.FromMinutes(1),
                    extension)
                : PersistentCache.TryGetBytes(category, key, extension);
            Assert.Null(result);
        }
        else
        {
            string? result = timeToLive
                ? PersistentCache.TryGet(
                    category,
                    key,
                    TimeSpan.FromMinutes(1),
                    extension)
                : PersistentCache.TryGet(category, key, extension);
            Assert.Null(result);
        }

        Assert.DoesNotContain(
            observer.Observations,
            observation => observation.Result == CacheAccessResult.Hit);
        if (timeToLive)
        {
            Assert.Equal(
                CacheAccessResult.Miss,
                Assert.Single(observer.Observations).Result);
        }
        else
        {
            Assert.Empty(observer.Observations);
        }
    }

    [Fact]
    public async Task CategoryClear_DoesNotConsumeMaintenanceAccounting()
    {
        string prefix = $"category-clear-accounting-{Guid.NewGuid():N}-v";
        string current = prefix + "2";
        string oldDir = Path.Combine(_cacheBasePath, prefix + "1");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "old.txt"), new string('x', 4096));

        PersistentCache.RegisterVersionedCategory(prefix, current);
        await PersistentCache.RequestVersionedCategoryCleanupAsync();

        Assert.Equal(0, PersistentCache.Clear("metadata"));
        Assert.Equal(4096, PersistentCache.Clear());
        Assert.Equal(0, PersistentCache.Clear());
    }

    [Fact]
    public async Task RegisteringVersionedCategory_CleansOnlyOlderContracts()
    {
        string prefix = $"registration-clean-{Guid.NewGuid():N}-v";
        string current = prefix + "3";
        string oldDir = Path.Combine(_cacheBasePath, prefix + "2");
        string currentDir = Path.Combine(_cacheBasePath, current);
        string futureDir = Path.Combine(_cacheBasePath, prefix + "4");
        string malformedDir = Path.Combine(_cacheBasePath, prefix + "preview");
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(currentDir);
        Directory.CreateDirectory(futureDir);
        Directory.CreateDirectory(malformedDir);
        File.WriteAllText(Path.Combine(oldDir, "old.txt"), new string('x', 4096));
        File.WriteAllText(Path.Combine(currentDir, "current.txt"), "keep");

        PersistentCache.RegisterVersionedCategory(prefix, current);
        await WaitForDeletionAsync(oldDir);

        Task<CacheMaintenanceResult> cleanup =
            PersistentCache.RequestVersionedCategoryCleanupAsync();
        Assert.Same(
            cleanup,
            PersistentCache.RequestVersionedCategoryCleanupAsync());
        CacheMaintenanceResult result = await cleanup;

        Assert.False(Directory.Exists(oldDir));
        Assert.True(Directory.Exists(currentDir));
        Assert.True(Directory.Exists(futureDir));
        Assert.True(Directory.Exists(malformedDir));
        Assert.True(result.BytesFreed >= 4096);
        Assert.True(result.DirectoriesDeleted >= 1);
    }

    [Fact]
    public async Task Initialize_RechecksContractsRecreatedByAnOlderTool()
    {
        string prefix = $"recreated-contract-{Guid.NewGuid():N}-v";
        string current = prefix + "2";
        string oldDir = Path.Combine(_cacheBasePath, prefix + "1");
        Directory.CreateDirectory(oldDir);

        PersistentCache.RegisterVersionedCategory(prefix, current);
        await PersistentCache.RequestVersionedCategoryCleanupAsync();
        Assert.False(Directory.Exists(oldDir));

        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "recreated.txt"), "stale");

        PersistentCache.Initialize(_appName, _cacheBasePath);
        await WaitForDeletionAsync(oldDir);
        await PersistentCache.RequestVersionedCategoryCleanupAsync();

        Assert.False(Directory.Exists(oldDir));
    }

    [Fact]
    public async Task InitializeSameRoot_PreservesMaintenanceAccounting()
    {
        string prefix = $"same-root-accounting-{Guid.NewGuid():N}-v";
        string current = prefix + "2";
        string oldDir = Path.Combine(_cacheBasePath, prefix + "1");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "old.txt"), new string('x', 4096));

        PersistentCache.RegisterVersionedCategory(prefix, current);
        await PersistentCache.RequestVersionedCategoryCleanupAsync();

        PersistentCache.Initialize(_appName, _cacheBasePath);

        Assert.Equal(4096, PersistentCache.Clear());
        Assert.Equal(0, PersistentCache.Clear());
    }

    [Fact]
    public void RegisterVersionedCategory_RequiresNumericContract()
    {
        string prefix = $"invalid-contract-{Guid.NewGuid():N}-v";

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => PersistentCache.RegisterVersionedCategory(
                prefix,
                prefix + "preview"));

        Assert.Contains("non-negative integer contract version", error.Message);
    }

    [Fact]
    public void RegisterVersionedCategory_RejectsAlternateCurrentSpelling()
    {
        string prefix = $"spelling-contract-{Guid.NewGuid():N}-v";
        PersistentCache.RegisterVersionedCategory(prefix, prefix + "1");

        Assert.Throws<InvalidOperationException>(
            () => PersistentCache.RegisterVersionedCategory(
                prefix,
                prefix + "01"));
    }

    private static IDisposable BlockReads(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            if (Record.Exception(() => File.ReadAllBytes(path)) is not null)
                return stream;

            stream.Dispose();
            Assert.Skip("This Windows filesystem did not enforce FileShare.None.");
            return NoopDisposable.Instance;
        }

        UnixFileMode originalMode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, UnixFileMode.None);
        if (Record.Exception(() => File.ReadAllBytes(path)) is not null)
        {
            return new CallbackDisposable(
                () =>
                {
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(path, originalMode);
                });
        }

        File.SetUnixFileMode(path, originalMode);
        Assert.Skip("This process can read a file with Unix mode 000.");
        return NoopDisposable.Instance;
    }

    private static async Task WaitForDeletionAsync(string path)
    {
        for (int attempt = 0; attempt < 100 && Directory.Exists(path); attempt++)
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(10),
                TestContext.Current.CancellationToken);
        }

        Assert.False(
            Directory.Exists(path),
            $"Expected cache cleanup to delete '{path}'.");
    }

    private sealed class RecordingObserver(string category)
        : IObserver<CacheObservation>
    {
        public List<CacheObservation> Observations { get; } = [];

        public void OnNext(CacheObservation value)
        {
            if (value.Category == category)
                Observations.Add(value);
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
