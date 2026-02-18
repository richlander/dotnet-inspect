using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Tests for version resolution caching in PackageExtractor.
/// Validates cache-first behavior for nuget.org, multi-source support, TTL expiry, and skipCache.
/// </summary>
public class VersionCacheTests : IDisposable
{
    private const string VersionCacheCategory = "versions";

    /// <summary>
    /// An HttpClient that throws on any request — proves cache prevented network access.
    /// </summary>
    private static readonly HttpClient FailingClient = new(new FailingHandler());

    private static readonly NuGetSource NuGetOrgSource = NuGetSource.NuGetOrg;
    private static readonly NuGetSource CustomSource = new("custom", "https://custom.feed/v3/index.json");

    public VersionCacheTests()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        CoreCache.Clear(VersionCacheCategory);
    }

    public void Dispose()
    {
        CoreCache.Clear(VersionCacheCategory);
    }

    // --- GetLatestVersionAsync ---

    [Fact]
    public async Task GetLatestVersion_WithCachedVersion_ReturnsCachedValue()
    {
        CoreCache.Set(VersionCacheCategory, "testpackage", "1.2.3", extension: "txt");

        var result = await PackageExtractor.GetLatestVersionAsync(
            FailingClient, "TestPackage", [NuGetOrgSource], log: null);

        Assert.Equal("1.2.3", result);
    }

    [Fact]
    public async Task GetLatestVersion_WithCachedVersion_MultipleSourcesIncludingNuGetOrg_ReturnsCachedValue()
    {
        CoreCache.Set(VersionCacheCategory, "testpackage", "2.0.0", extension: "txt");

        var result = await PackageExtractor.GetLatestVersionAsync(
            FailingClient, "TestPackage", [NuGetOrgSource, CustomSource], log: null);

        Assert.Equal("2.0.0", result);
    }

    [Fact]
    public async Task GetLatestVersion_WithCustomSourceOnly_SkipsCache()
    {
        // Pre-seed cache — should be ignored when nuget.org is not in sources
        CoreCache.Set(VersionCacheCategory, "testpackage", "1.0.0", extension: "txt");

        // Custom source can't resolve (no real server), so result is null
        var result = await PackageExtractor.GetLatestVersionAsync(
            FailingClient, "TestPackage", [CustomSource], log: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestVersion_WithSkipCache_IgnoresCachedValue()
    {
        CoreCache.Set(VersionCacheCategory, "testpackage", "1.0.0", extension: "txt");

        // skipCache: true should bypass cache even with nuget.org source
        var result = await PackageExtractor.GetLatestVersionAsync(
            FailingClient, "TestPackage", [NuGetOrgSource], log: null, skipCache: true);

        // Network fails, so result is null — but the point is it didn't use cache
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestVersion_WithExpiredCache_DoesNotReturnStaleValue()
    {
        // Write to cache and backdate the file
        var key = "expiredpackage";
        CoreCache.Set(VersionCacheCategory, key, "0.9.0", extension: "txt");

        var cachePath = CoreCache.GetFilePath(VersionCacheCategory, key, extension: "txt");
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddHours(-2));

        var result = await PackageExtractor.GetLatestVersionAsync(
            FailingClient, "ExpiredPackage", [NuGetOrgSource], log: null);

        // Expired cache + failing client = null
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestVersion_CacheKeyIsCaseInsensitive()
    {
        CoreCache.Set(VersionCacheCategory, "system.text.json", "9.0.0", extension: "txt");

        var result = await PackageExtractor.GetLatestVersionAsync(
            FailingClient, "System.Text.Json", [NuGetOrgSource], log: null);

        Assert.Equal("9.0.0", result);
    }

    [Fact]
    public async Task GetLatestVersion_LogsOnCacheHit()
    {
        CoreCache.Set(VersionCacheCategory, "logpackage", "3.0.0", extension: "txt");
        var logs = new List<string>();

        await PackageExtractor.GetLatestVersionAsync(
            FailingClient, "LogPackage", [NuGetOrgSource], log: logs.Add);

        Assert.Single(logs);
        Assert.Contains("cached", logs[0], StringComparison.OrdinalIgnoreCase);
    }

    // --- GetVersionsAsync ---

    [Fact]
    public async Task GetVersions_WithCachedVersionList_ReturnsCachedValues()
    {
        var versions = "1.0.0\n1.1.0\n2.0.0";
        CoreCache.Set(VersionCacheCategory, "testpackage-all", versions, extension: "txt");

        var result = await PackageExtractor.GetVersionsAsync(
            FailingClient, "TestPackage", includePrerelease: false, limit: null, log: null);

        Assert.NotNull(result);
        Assert.Equal(["2.0.0", "1.1.0", "1.0.0"], result);
    }

    [Fact]
    public async Task GetVersions_WithCachedList_RespectsLimit()
    {
        var versions = "1.0.0\n1.1.0\n2.0.0\n3.0.0";
        CoreCache.Set(VersionCacheCategory, "limitpackage-all", versions, extension: "txt");

        var result = await PackageExtractor.GetVersionsAsync(
            FailingClient, "LimitPackage", includePrerelease: false, limit: 2, log: null);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal("3.0.0", result[0]);
        Assert.Equal("2.0.0", result[1]);
    }

    [Fact]
    public async Task GetVersions_WithCachedList_FiltersPrerelease()
    {
        var versions = "1.0.0\n2.0.0-beta.1\n2.0.0\n3.0.0-rc.1";
        CoreCache.Set(VersionCacheCategory, "prerelease-all", versions, extension: "txt");

        var result = await PackageExtractor.GetVersionsAsync(
            FailingClient, "Prerelease", includePrerelease: false, limit: null, log: null);

        Assert.NotNull(result);
        Assert.Equal(["2.0.0", "1.0.0"], result);
    }

    [Fact]
    public async Task GetVersions_WithCachedList_IncludesPrerelease()
    {
        var versions = "1.0.0\n2.0.0-beta.1\n2.0.0\n3.0.0-rc.1";
        CoreCache.Set(VersionCacheCategory, "prerelease2-all", versions, extension: "txt");

        var result = await PackageExtractor.GetVersionsAsync(
            FailingClient, "Prerelease2", includePrerelease: true, limit: null, log: null);

        Assert.NotNull(result);
        Assert.Equal(["3.0.0-rc.1", "2.0.0", "2.0.0-beta.1", "1.0.0"], result);
    }

    [Fact]
    public async Task GetVersions_WithExpiredCache_DoesNotReturnStaleList()
    {
        var key = "expired-all";
        CoreCache.Set(VersionCacheCategory, key, "1.0.0\n2.0.0", extension: "txt");

        var cachePath = CoreCache.GetFilePath(VersionCacheCategory, key, extension: "txt");
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddHours(-2));

        var result = await PackageExtractor.GetVersionsAsync(
            FailingClient, "Expired", includePrerelease: false, limit: null, log: null);

        // Expired + failing client = null
        Assert.Null(result);
    }

    [Fact]
    public async Task GetVersions_WithCustomSourceOnly_SkipsCache()
    {
        CoreCache.Set(VersionCacheCategory, "customonly-all", "1.0.0\n2.0.0", extension: "txt");

        // GetVersionsAsync resolves sources internally from NuGetSourceOptions.
        // With a source that isn't nuget.org, cache should be skipped.
        var sourceOptions = new NuGetSourceOptions { Sources = [CustomSource.Url] };

        var result = await PackageExtractor.GetVersionsAsync(
            FailingClient, "CustomOnly", includePrerelease: false, limit: null, log: null,
            sourceOptions: sourceOptions);

        // No nuget.org source → no cache → failing client → null
        Assert.Null(result);
    }

    /// <summary>
    /// HTTP handler that throws on any request — used to prove cache prevented network access.
    /// </summary>
    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException($"Network access not allowed in test: {request.RequestUri}");
        }
    }
}
