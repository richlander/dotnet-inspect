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

        // Capture console output
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var result = await CacheCommand.ExecuteAsync(options);
            var output = sw.ToString();

            Assert.Equal(0, result);
            Assert.Contains("Cache location:", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithClean_OnEmptyCache_ReturnsZero()
    {
        // This test assumes the cache might be empty or non-existent
        // It should still return 0 (success)
        var options = new CacheOptions(Clean: true, Verbose: false);

        // Capture console output
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var result = await CacheCommand.ExecuteAsync(options);

            // Should succeed even if cache is empty
            Assert.Equal(0, result);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
