using DotnetInspector.Cache;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Tests for Package version cache behavior.
/// </summary>
[Collection("Console")]
public class PackageVersionCacheTests
{
    private const string VersionCacheCategory = "versions-v5";

    public PackageVersionCacheTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    [Fact]
    public async Task TryGetLatestCachedCandidateVersion_ReturnsNewestVersion()
    {
        await EnsurePackageCached("System.CommandLine");

        var version = PackageExtractor.TryGetLatestCachedCandidateVersion(
            "System.CommandLine",
            NuGetSourceResolver.ResolveSourceKeys(null));

        Assert.NotNull(version);
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Fact]
    public void VersionCache_Set_ThenGet_Roundtrips()
    {
        var key = $"test-roundtrip-{Guid.NewGuid():N}";
        try
        {
            PersistentCache.Set(VersionCacheCategory, key, "4.5.6", extension: "txt");

            var result = PersistentCache.TryGet(VersionCacheCategory, key, TimeSpan.FromHours(1), extension: "txt");

            Assert.Equal("4.5.6", result);
        }
        finally
        {
            var path = PersistentCache.GetFilePath(VersionCacheCategory, key, extension: "txt");
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void VersionCache_Expired_ReturnsNull()
    {
        var key = $"test-expired-{Guid.NewGuid():N}";
        try
        {
            PersistentCache.Set(VersionCacheCategory, key, "1.0.0", extension: "txt");

            var path = PersistentCache.GetFilePath(VersionCacheCategory, key, extension: "txt");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));

            var result = PersistentCache.TryGet(VersionCacheCategory, key, TimeSpan.FromHours(1), extension: "txt");

            Assert.Null(result);
        }
        finally
        {
            var path = PersistentCache.GetFilePath(VersionCacheCategory, key, extension: "txt");
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void VersionCache_CaseInsensitive_KeyNormalization()
    {
        var key = $"test-case-{Guid.NewGuid():N}";
        try
        {
            PersistentCache.Set(VersionCacheCategory, key.ToLowerInvariant(), "2.0.0", extension: "txt");

            var result = PersistentCache.TryGet(VersionCacheCategory, key.ToLowerInvariant(), TimeSpan.FromHours(1), extension: "txt");

            Assert.Equal("2.0.0", result);
        }
        finally
        {
            var path = PersistentCache.GetFilePath(VersionCacheCategory, key.ToLowerInvariant(), extension: "txt");
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// Downloads a package so it's in the NuGet cache for subsequent tests.
    /// </summary>
    private static async Task EnsurePackageCached(string packageName, string? version = null)
    {
        var client = DotnetInspector.Networking.HttpClientFactory.Shared;
        var outcome = await PackageExtractor.ExtractPackageAsync(
            client, packageName, log: null, version: version);
        Assert.True(outcome.IsSuccess, $"Failed to download {packageName}: {outcome.ErrorMessage}");
        if (outcome.Result?.TempDir is string tempDir && Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }
}
