using DotnetInspector.Commands;
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
}
