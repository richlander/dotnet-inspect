using DotnetInspector.Cache;
using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class InfoTrackerCacheTests
{
    [Theory]
    [InlineData(NetworkTrafficKind.PackageDownload)]
    [InlineData(NetworkTrafficKind.VulnerabilityData)]
    public async Task RepeatedStartCountsObservationsOnceAndDoesNotCountStores(
        NetworkTrafficKind trafficKind)
    {
        await ConsoleCapture.RunAsync(() =>
        {
            try
            {
                InfoTracker.ResetForTests();
                InfoTracker.Start();
                InfoTracker.Start();
                using var scope = NetworkTelemetry.Scope(trafficKind);

                CacheTelemetry.Record("test", "key", CacheAccessResult.Store);
                Assert.Equal(0, InfoTracker.CacheHits);
                Assert.Equal(0, InfoTracker.CacheMisses);

                CacheTelemetry.Record("test", "key", CacheAccessResult.Hit);
                CacheTelemetry.Record("test", "absent", CacheAccessResult.Miss);
                Assert.Equal(1, InfoTracker.CacheHits);
                Assert.Equal(1, InfoTracker.CacheMisses);
            }
            finally
            {
                InfoTracker.ResetForTests();
            }
        });
    }

    [Fact]
    public async Task ResetDisposesSubscriptionAndRestartDoesNotRetainObserver()
    {
        await ConsoleCapture.RunAsync(() =>
        {
            try
            {
                InfoTracker.ResetForTests();
                InfoTracker.Start();
                InfoTracker.ResetForTests();
                CacheTelemetry.Record("test", "key", CacheAccessResult.Hit);
                CacheTelemetry.Record("test", "absent", CacheAccessResult.Miss);
                Assert.Equal(0, InfoTracker.CacheHits);
                Assert.Equal(0, InfoTracker.CacheMisses);

                InfoTracker.Start();
                CacheTelemetry.Record("test", "key", CacheAccessResult.Hit);
                CacheTelemetry.Record("test", "absent", CacheAccessResult.Miss);
                Assert.Equal(1, InfoTracker.CacheHits);
                Assert.Equal(1, InfoTracker.CacheMisses);
            }
            finally
            {
                InfoTracker.ResetForTests();
            }
        });
    }

    [Fact]
    public async Task PersistentAndPackageProducersCountExactlyOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), $"cache-counts-{Guid.NewGuid():N}");
        await ConsoleCapture.RunAsync(() =>
        {
            try
            {
                NuGetCache.Initialize("dotnet-inspect-test", root, skipNuGetCache: true);
                InfoTracker.ResetForTests();
                InfoTracker.Start();

                PersistentCache.Set("test", "key", "content");
                Assert.Equal(0, InfoTracker.CacheHits);
                Assert.Equal(0, InfoTracker.CacheMisses);
                Assert.Equal("content", PersistentCache.TryGet("test", "key"));
                Assert.Null(PersistentCache.TryGet("test", "absent"));
                Assert.Equal(1, InfoTracker.CacheHits);
                Assert.Equal(1, InfoTracker.CacheMisses);

                string sourceKey = NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json");
                Assert.Null(NuGetCache.TryGetCachedPackage("Example", "1.0.0", [sourceKey]));
                Assert.Equal(2, InfoTracker.CacheMisses);

                string extracted = Path.Combine(root, "extracted");
                Directory.CreateDirectory(extracted);
                File.WriteAllText(Path.Combine(extracted, "Example.nuspec"), "<package />");
                NuGetCache.CommitPackage(extracted, null, "Example", "1.0.0", sourceKey);
                Assert.Equal(1, InfoTracker.CacheHits);
                Assert.Equal(2, InfoTracker.CacheMisses);

                Assert.NotNull(NuGetCache.TryGetCachedPackage("Example", "1.0.0", [sourceKey]));
                Assert.Equal(2, InfoTracker.CacheHits);
                Assert.Equal(2, InfoTracker.CacheMisses);
            }
            finally
            {
                InfoTracker.ResetForTests();
                PersistentCache.CancelAndWaitForMaintenance(Timeout.InfiniteTimeSpan);
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        });
    }
}
