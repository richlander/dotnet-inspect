using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Options;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for cache management command.
/// </summary>
[Collection("Console")]
public class CacheCommandTests : IDisposable
{
    private readonly string _cacheBasePath = Path.Combine(
        Path.GetTempPath(),
        "dotnet-inspect-cache-command-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _appName = "dotnet-inspect-cache-command-tests-" + Guid.NewGuid().ToString("N");

    public CacheCommandTests()
    {
        NuGetCache.Initialize(_appName, basePath: _cacheBasePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheBasePath))
            Directory.Delete(_cacheBasePath, recursive: true);
    }

    [Fact]
    public async Task ExecuteAsync_ShowsInfo_ReturnsZero()
    {
        var options = new CacheOptions(Clean: false, Verbose: false);

        var (result, output, _) = await ConsoleCapture.RunAsync(
            () => CacheCommand.ExecuteAsync(options));

        Assert.Equal(0, result);
        Assert.True(
            output.Contains("Location") || output.Contains("Cache is empty"),
            "Expected cache info or empty cache message");
    }

    [Fact]
    public async Task ExecuteAsync_WithClean_OnEmptyCache_ReturnsZero()
    {
        // This test assumes the cache might be empty or non-existent
        // It should still return 0 (success)
        var options = new CacheOptions(Clean: true, Verbose: false);

        var (result, _, _) = await ConsoleCapture.RunAsync(
            () => CacheCommand.ExecuteAsync(options));

        // Should succeed even if cache is empty
        Assert.Equal(0, result);
    }

    [Fact]
    public void CacheMiss_CleansObsoleteVersionedCategories()
    {
        var oldDir = Path.Combine(_cacheBasePath, "pkg-index-v8");
        var currentDir = Path.Combine(_cacheBasePath, "pkg-index-v9");
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(currentDir);
        File.WriteAllText(Path.Combine(oldDir, "old.txt"), new string('x', 4096));
        File.WriteAllText(Path.Combine(currentDir, "current.txt"), "keep");

        CoreCache.RegisterVersionedCategory("pkg-index-v", "pkg-index-v9");

        Assert.Null(CoreCache.TryGet("versions", $"missing-{Guid.NewGuid():N}", extension: "txt"));
        var result = CoreCache.CancelAndWaitForMaintenance(TimeSpan.FromSeconds(5));

        Assert.False(Directory.Exists(oldDir));
        Assert.True(Directory.Exists(currentDir));
        Assert.True(result.BytesFreed > 0);
        Assert.True(result.DirectoriesDeleted >= 1);
    }

    [Fact]
    public async Task CoreCache_Clear_RejectsTraversalOutsideCacheRoot()
    {
        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            Assert.Throws<InvalidOperationException>(() => CoreCache.Clear(".."));
            return Task.FromResult(0);
        });

        Assert.Contains("Warning: refusing to delete path outside dotnet-inspect cache", error);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidSessionName_ReturnsError()
    {
        var options = new CacheOptions(Clean: true, Verbose: false, Session: "../user-data");

        var (result, _, error) = await ConsoleCapture.RunAsync(
            () => CacheCommand.ExecuteAsync(options));

        Assert.Equal(1, result);
        Assert.Contains("Session name must not contain path separators", error);
    }
}
