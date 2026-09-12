using DotnetInspector.Cache;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using Markout;
using System.Text.Json;

namespace DotnetInspect.Cli.Tests;

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

    [Theory]
    [InlineData("--head", "| Field | Value |")]
    [InlineData("--tail", "Run 'dotnet-inspect cache clear' to clear the cache.")]
    public async Task Cli_LineLimitSelectsRenderedCacheLines(
        string direction,
        string expectedLine)
    {
        var categoryPath = Path.Combine(_cacheBasePath, "versions");
        Directory.CreateDirectory(categoryPath);
        File.WriteAllText(Path.Combine(categoryPath, "versions.json"), "{}");

        string[] args = ["cache", "-n", "1", direction];
        var parseResult = CommandLineBuilder.CreateRootCommand().Parse(args);
        var (result, output, error) = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeWithLineWindowAsync(parseResult, args));

        Assert.Equal(0, result);
        Assert.Empty(error);
        Assert.Equal(
            [expectedLine],
            output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
    }

    [Theory]
    [InlineData("--head")]
    [InlineData("--tail")]
    public async Task Cli_ClearRejectsAncestorDirectionWithoutLineLimitBeforeAction(
        string direction)
    {
        string[] args =
        [
            "cache",
            direction,
            "clear",
            "--session",
            "cache-command-missing-probe"
        ];
        var parseResult = CommandLineBuilder.CreateRootCommand().Parse(args);
        var (result, output, error) = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeWithLineWindowAsync(parseResult, args));

        Assert.Equal(1, result);
        Assert.Empty(output);
        Assert.Contains($"{direction} requires -n.", error);
    }

    [Theory]
    [InlineData("--head=false", false)]
    [InlineData("--tail=false", false)]
    [InlineData("--head=false", true)]
    [InlineData("--tail=false", true)]
    public async Task Cli_RejectsAttachedDirectionValueBeforeAction(
        string direction,
        bool clear)
    {
        string[] args = clear
            ?
            [
                "cache",
                direction,
                "clear",
                "--session",
                "cache-command-missing-probe"
            ]
            : ["cache", direction];
        var parseResult = CommandLineBuilder.CreateRootCommand().Parse(args);
        var (result, output, error) = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeWithLineWindowAsync(parseResult, args));

        string option = direction[..direction.IndexOf('=')];
        Assert.Equal(1, result);
        Assert.Empty(output);
        Assert.Equal(
            $"Error: {option} does not accept a value.{Environment.NewLine}",
            error);
    }

    [Theory]
    [InlineData("--head", false)]
    [InlineData("--tail", false)]
    [InlineData("--head", true)]
    [InlineData("--tail", true)]
    public async Task Cli_ParsesSeparateDirectionValueIndependently(
        string direction,
        bool clear)
    {
        string[] args = clear
            ?
            [
                "cache",
                direction,
                "false",
                "clear",
                "--session",
                "cache-command-missing-probe"
            ]
            : ["cache", direction, "false"];
        var parseResult = CommandLineBuilder.CreateRootCommand().Parse(args);
        var (result, output, error) = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeWithLineWindowAsync(parseResult, args));

        Assert.Equal(1, result);
        Assert.Empty(output);
        Assert.Equal(
            $"Error: {direction} does not accept a value.{Environment.NewLine}",
            error);
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
    public void ClearCache_CoordinatesWithVersionedMaintenance()
    {
        string prefix = $"clear-race-{Guid.NewGuid():N}-v";
        string current = prefix + "2";
        var oldDir = Path.Combine(_cacheBasePath, prefix + "1");
        var currentDir = Path.Combine(_cacheBasePath, current);
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(currentDir);
        for (int i = 0; i < 256; i++)
        {
            File.WriteAllText(
                Path.Combine(oldDir, $"{i}.txt"),
                new string('x', 1024));
        }
        File.WriteAllText(Path.Combine(currentDir, "current.txt"), "keep");

        PersistentCache.RegisterVersionedCategory(prefix, current);
        long freed = PackageCacheService.ClearCache();

        Assert.False(Directory.Exists(_cacheBasePath));
        Assert.True(freed >= 256 * 1024);
        Assert.Equal(0, PackageCacheService.ClearCache());
    }

    [Fact]
    public async Task Initialize_CleansPriorPackageContentContract()
    {
        var oldDir = Path.Combine(_cacheBasePath, "package-content-v4");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "stale.txt"), "stale");

        NuGetCache.Initialize(_appName, basePath: _cacheBasePath);
        await WaitForDeletionAsync(oldDir);
        await PersistentCache.RequestVersionedCategoryCleanupAsync();

        Assert.False(Directory.Exists(oldDir));
    }

    [Fact]
    public async Task PersistentCache_Clear_RejectsTraversalOutsideCacheRoot()
    {
        // The guard throws and does not also write. It used to do both, and
        // the write was the uncontained copy: it interpolated the rejected path
        // straight to stderr, while the throw is rendered by the one writer
        // that contains it.
        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            var thrown = Assert.Throws<InvalidOperationException>(() => PersistentCache.Clear(".."));
            Assert.Contains("Refusing to delete path outside dotnet-inspect cache", thrown.Message);
            return Task.FromResult(0);
        });

        Assert.Empty(error);
    }

    private static async Task WaitForDeletionAsync(string path)
    {
        for (int attempt = 0; attempt < 100 && Directory.Exists(path); attempt++)
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(10),
                TestContext.Current.CancellationToken);
        }

        Assert.False(Directory.Exists(path), $"Expected cache cleanup to delete '{path}'.");
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
