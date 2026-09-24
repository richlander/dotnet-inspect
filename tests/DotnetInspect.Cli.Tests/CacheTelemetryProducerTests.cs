using DotnetInspector.Cache;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// The cache producers report each lookup as exactly one hit or miss and a
/// store as neither, and name symbol-miss categories with their extension.
/// Cache statistics and the Debug network traffic log consume these
/// observations.
/// </summary>
[Collection("Console")]
public sealed class CacheTelemetryProducerTests
{
    [Fact]
    public async Task PersistentAndPackageProducersRecordEachLookupOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), $"cache-counts-{Guid.NewGuid():N}");
        await ConsoleCapture.RunAsync(() =>
        {
            // Other suites use the process-wide cache concurrently, so only
            // this test's own category and package are counted.
            string category = $"producer-{Guid.NewGuid():N}";
            string packageId = $"Example{Guid.NewGuid():N}";
            var observer = new CacheObservationRecorder(observation =>
                observation.Category == category
                || observation.Key.ToString().Contains(
                    packageId,
                    StringComparison.OrdinalIgnoreCase));
            try
            {
                NuGetCache.Initialize("dotnet-inspect-test", root, skipNuGetCache: true);
                using IDisposable subscription = CacheTelemetry.Subscribe(observer);

                PersistentCache.Set(category, "key", "content");
                Assert.Equal((0, 0), observer.Counts);
                Assert.Equal("content", PersistentCache.TryGet(category, "key"));
                Assert.Null(PersistentCache.TryGet(category, "absent"));
                Assert.Equal((1, 1), observer.Counts);

                string sourceKey = NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json");
                Assert.Null(NuGetCache.TryGetCachedPackage(packageId, "1.0.0", [sourceKey]));
                Assert.Equal((1, 2), observer.Counts);

                string extracted = Path.Combine(root, "extracted");
                Directory.CreateDirectory(extracted);
                File.WriteAllText(Path.Combine(extracted, $"{packageId}.nuspec"), "<package />");
                NuGetCache.CommitPackage(extracted, null, packageId, "1.0.0", sourceKey);
                Assert.Equal((1, 2), observer.Counts);

                Assert.NotNull(NuGetCache.TryGetCachedPackage(packageId, "1.0.0", [sourceKey]));
                Assert.Equal((2, 2), observer.Counts);
            }
            finally
            {
                PersistentCache.CancelAndWaitForMaintenance(Timeout.InfiniteTimeSpan);
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        });
    }

    [Fact]
    public void SymbolMissesIncludeExtensionInCategory()
    {
        var observer = new CacheObservationRecorder(observation =>
            observation.Category.StartsWith("symbol-misses/", StringComparison.Ordinal));
        using IDisposable subscription = CacheTelemetry.Subscribe(observer);
        string key = $"https://example.test/symbols/{Guid.NewGuid():N}.pdb";

        PersistentCache.Set("symbol-misses", key, "403", extension: "forbidden");
        _ = PersistentCache.TryGet("symbol-misses", key, extension: "forbidden");
        _ = PersistentCache.TryGet("symbol-misses", key, extension: "miss");

        Assert.Contains(
            observer.Snapshot(),
            observation => observation is
            {
                Category: "symbol-misses/forbidden",
                Result: CacheAccessResult.Store,
            });
        Assert.Contains(
            observer.Snapshot(),
            observation => observation is
            {
                Category: "symbol-misses/miss",
                Result: CacheAccessResult.Miss,
            });
    }
}

/// <summary>Records the cache observations a test selects, and counts hits and misses.</summary>
internal sealed class CacheObservationRecorder(Func<CacheObservation, bool> include)
    : IObserver<CacheObservation>
{
    private readonly List<CacheObservation> _observations = [];

    public (int Hits, int Misses) Counts
    {
        get
        {
            lock (_observations)
            {
                return (
                    _observations.Count(o => o.Result == CacheAccessResult.Hit),
                    _observations.Count(o => o.Result == CacheAccessResult.Miss));
            }
        }
    }

    public CacheObservation[] Snapshot()
    {
        lock (_observations)
            return [.. _observations];
    }

    public void OnNext(CacheObservation value)
    {
        if (!include(value))
            return;
        lock (_observations)
            _observations.Add(value);
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }
}
