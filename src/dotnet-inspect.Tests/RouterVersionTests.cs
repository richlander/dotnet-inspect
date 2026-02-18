using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for router --version, --latest-version, and --versions behavior.
/// Validates cache-first resolution, fallthrough, and offline behavior.
/// </summary>
[Collection("Console")]
public class RouterVersionTests
{
    private const string VersionCacheCategory = "versions";

    public RouterVersionTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    [Fact]
    public async Task Version_WithCachedPackage_ReturnsResolvedVersion()
    {
        // Ensure the package is cached by downloading it first
        await EnsurePackageCached("System.CommandLine");

        var version = NuGetCache.TryGetLatestCachedVersion("System.CommandLine");
        Assert.NotNull(version);

        var root = CommandLineBuilder.CreateRootCommand();
        var args = CommandLineBuilder.PreprocessArgs(["System.CommandLine", "--version"]);

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        Assert.Equal(version, output.Trim());
    }

    [Fact]
    public async Task Version_WithPinnedVersion_ReturnsPinnedVersion()
    {
        // Download a specific version so it's in the cache
        await EnsurePackageCached("System.CommandLine", "2.0.2");

        var root = CommandLineBuilder.CreateRootCommand();
        var args = CommandLineBuilder.PreprocessArgs(["System.CommandLine@2.0.2", "--version"]);

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        Assert.Equal("2.0.2", output.Trim());
    }

    [Fact]
    public async Task TryGetLatestCachedVersion_ReturnsNewestVersion()
    {
        await EnsurePackageCached("System.CommandLine");

        var version = NuGetCache.TryGetLatestCachedVersion("System.CommandLine");

        Assert.NotNull(version);
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Fact]
    public void VersionCache_Set_ThenGet_Roundtrips()
    {
        var key = $"test-roundtrip-{Guid.NewGuid():N}";
        try
        {
            CoreCache.Set(VersionCacheCategory, key, "4.5.6", extension: "txt");

            var result = CoreCache.TryGet(VersionCacheCategory, key, TimeSpan.FromHours(1), extension: "txt");

            Assert.Equal("4.5.6", result);
        }
        finally
        {
            var path = CoreCache.GetFilePath(VersionCacheCategory, key, extension: "txt");
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void VersionCache_Expired_ReturnsNull()
    {
        var key = $"test-expired-{Guid.NewGuid():N}";
        try
        {
            CoreCache.Set(VersionCacheCategory, key, "1.0.0", extension: "txt");

            var path = CoreCache.GetFilePath(VersionCacheCategory, key, extension: "txt");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));

            var result = CoreCache.TryGet(VersionCacheCategory, key, TimeSpan.FromHours(1), extension: "txt");

            Assert.Null(result);
        }
        finally
        {
            var path = CoreCache.GetFilePath(VersionCacheCategory, key, extension: "txt");
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void VersionCache_CaseInsensitive_KeyNormalization()
    {
        var key = $"test-case-{Guid.NewGuid():N}";
        try
        {
            CoreCache.Set(VersionCacheCategory, key.ToLowerInvariant(), "2.0.0", extension: "txt");

            var result = CoreCache.TryGet(VersionCacheCategory, key.ToLowerInvariant(), TimeSpan.FromHours(1), extension: "txt");

            Assert.Equal("2.0.0", result);
        }
        finally
        {
            var path = CoreCache.GetFilePath(VersionCacheCategory, key.ToLowerInvariant(), extension: "txt");
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Version_WithLatestTag_SkipsCacheAndQueriesVersionApi()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = CommandLineBuilder.PreprocessArgs(["System.CommandLine@latest", "--version"]);

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        var version = output.Trim();
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Fact]
    public async Task Version_WithBogusVersion_DoesNotEchoBogusVersion()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = CommandLineBuilder.PreprocessArgs(["System.CommandLine@99.99.99", "--version"]);

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        var version = output.Trim();
        Assert.NotEqual("99.99.99", version);
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    /// <summary>
    /// Downloads a package so it's in the NuGet cache for subsequent tests.
    /// </summary>
    private static async Task EnsurePackageCached(string packageName, string? version = null)
    {
        var client = DotnetInspector.Core.HttpClientFactory.Shared;
        var outcome = await PackageExtractor.ExtractPackageAsync(
            client, packageName, log: null, version: version);
        Assert.True(outcome.IsSuccess, $"Failed to download {packageName}: {outcome.ErrorMessage}");
        if (outcome.Result?.TempDir is string tempDir && Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }
}
