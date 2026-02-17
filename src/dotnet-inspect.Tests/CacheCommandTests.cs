using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for cache management command.
/// </summary>
[Collection("Console")]
public class CacheCommandTests
{
    public CacheCommandTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }
    [Fact]
    public async Task ExecuteAsync_ShowsInfo_ReturnsZero()
    {
        var options = new CacheOptions(Clean: false, Verbose: false);

        var (result, output, _) = await ConsoleCapture.RunAsync(
            () => CacheCommand.ExecuteAsync(options));

        Assert.Equal(0, result);
        Assert.Contains("Cache location:", output);
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
