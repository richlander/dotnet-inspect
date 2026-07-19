using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Views;
using Markout;
using System.Text.Json;

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
    public void CacheInfoView_DefaultFormat_RendersMarkdownTable()
    {
        var view = new CacheInfoView
        {
            Location = "/tmp/cache",
            Total = "1.0 MB",
            Categories = [new CacheCategoryRow("Packages", "1.0 MB", "3 packages")]
        };

        var output = MarkoutSerializer.Serialize(view, CacheInfoContext.Default);

        // Finding 9: cache should render markdown tables, not dashed plaintext.
        Assert.Contains("| Field | Value |", output);
        Assert.Contains("| Location |", output);
        Assert.Contains("## Categories", output);
        Assert.Contains("| Packages | 1.0 MB | 3 packages |", output);
        Assert.DoesNotContain("----------", output);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCache_JsonFormat_EmitsValidJson()
    {
        // Finding 9 follow-up: machine formats must emit structured output even for
        // an empty cache, not the "Cache is empty." plaintext fallback.
        var options = new CacheOptions(Clean: false, Verbose: false, Format: OutputFormat.Json);

        var (result, output, _) = await ConsoleCapture.RunAsync(
            () => CacheCommand.ExecuteAsync(options));

        Assert.Equal(0, result);
        Assert.DoesNotContain("Cache is empty", output);
        using var doc = JsonDocument.Parse(output.Trim());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("categories").ValueKind);
        Assert.Empty(doc.RootElement.GetProperty("categories").EnumerateArray());
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCache_JsonlFormat_EmitsSingleValidLine()
    {
        // JSONL must be a single valid JSON record per line, with no blank separator.
        var options = new CacheOptions(Clean: false, Verbose: false, Format: OutputFormat.Jsonl);

        var (result, output, _) = await ConsoleCapture.RunAsync(
            () => CacheCommand.ExecuteAsync(options));

        Assert.Equal(0, result);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Single(lines);
        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
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
    public async Task CacheMiss_CleansObsoleteVersionedCategories()
    {
        var oldDir = Path.Combine(_cacheBasePath, "pkg-index-v9");
        var currentDir = Path.Combine(_cacheBasePath, PackageIndexCache.Category);
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(currentDir);
        File.WriteAllText(Path.Combine(oldDir, "old.txt"), new string('x', 4096));
        File.WriteAllText(Path.Combine(currentDir, "current.txt"), "keep");

        CoreCache.RegisterVersionedCategory("pkg-index-v", PackageIndexCache.Category);

        Assert.Null(CoreCache.TryGet("versions", $"missing-{Guid.NewGuid():N}", extension: "txt"));
        var cleanup = CoreCache.RequestVersionedCategoryCleanupAsync();
        Assert.Same(cleanup, CoreCache.RequestVersionedCategoryCleanupAsync());
        var result = await cleanup;

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
