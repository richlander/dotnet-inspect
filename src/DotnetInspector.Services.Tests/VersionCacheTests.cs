using DotnetInspector.Core;
using DotnetInspector.Packages;
using NuGetFetch;
using NuGetSource = NuGetFetch.PackageSource;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Tests for version resolution caching in PackageExtractor.
/// Validates source-scoped candidate caching, multi-source support, TTL expiry, and skipCache.
/// </summary>
[Collection(CoreCacheCollection.Name)]
public class VersionCacheTests : IDisposable
{
    private const string VersionCacheCategory = "versions-v5";

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
        SetLatest("TestPackage", NuGetOrgSource, "1.2.3");

        var result = await PackageExtractor.GetLatestVersionAsync(
            FailingClient, "TestPackage", [NuGetOrgSource], log: null);

        Assert.Equal("1.2.3", result);
    }

    [Fact]
    public async Task GetLatestVersion_WithCachedVersion_MultipleSourcesIncludingNuGetOrg_ReturnsCachedValue()
    {
        SetLatest("TestPackage", NuGetOrgSource, "2.0.0");

        var result = await PackageExtractor.GetLatestVersionAsync(
            FailingClient, "TestPackage", [NuGetOrgSource, CustomSource], log: null);

        Assert.Equal("2.0.0", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData(" 1.2.3 ")]
    public async Task GetLatestVersion_WithMalformedLatestEntry_FallsBackToListings(
        string malformed)
    {
        SetLatest("MalformedLatest", CustomSource, malformed);
        SetListings("MalformedLatest", CustomSource, "1.2.3");

        string? result = await PackageExtractor.GetLatestVersionAsync(
            FailingClient,
            "MalformedLatest",
            [CustomSource],
            log: null);

        Assert.Equal("1.2.3", result);
    }

    [Fact]
    public async Task GetLatestVersion_NormalizesCachedCandidate()
    {
        SetLatest("ShorthandCached", CustomSource, "1.2");

        string? result = await PackageExtractor.GetLatestVersionAsync(
            FailingClient,
            "ShorthandCached",
            [CustomSource],
            log: null);

        Assert.Equal("1.2.0", result);
    }

    [Fact]
    public void PrereleaseInclusiveSelectionDoesNotUseStableLatestEntry()
    {
        SetLatest("StableOnly", CustomSource, "1.2.3");

        string? result =
            PackageExtractor.TryGetLatestCachedCandidateVersion(
                "StableOnly",
                [NuGetCache.GetSourceKey(CustomSource.Url)],
                includePrerelease: true);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateExistenceUsesEitherLatestFlavor(
        bool includePrerelease)
    {
        SetLatest(
            "EitherFlavor",
            CustomSource,
            includePrerelease ? "2.0.0-preview.1" : "1.2.3",
            includePrerelease);

        bool result = PackageExtractor.HasCachedCandidateVersion(
            "EitherFlavor",
            [NuGetCache.GetSourceKey(CustomSource.Url)]);

        Assert.True(result);
    }

    [Theory]
    [InlineData("2.0.0", "3.0.0-preview.1", "3.0.0-preview.1")]
    [InlineData("4.0.0", "3.0.0-preview.1", "4.0.0")]
    public void PrereleaseInclusiveCandidateLookupChoosesNewestCachedFlavor(
        string stable,
        string prereleaseInclusive,
        string expected)
    {
        SetLatest("BothFlavors", CustomSource, stable);
        SetLatest(
            "BothFlavors",
            CustomSource,
            prereleaseInclusive,
            includePrerelease: true);

        string? result =
            PackageExtractor.TryGetLatestCachedCandidateVersion(
                "BothFlavors",
                [NuGetCache.GetSourceKey(CustomSource.Url)],
                includePrerelease: true);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetLatestVersion_PrereleaseDoesNotUseStableOnlyCache()
    {
        SetLatest("PreviewAfterStable", CustomSource, "1.2.3");
        using var client = new HttpClient(new CustomFeedHandler(
            serviceIndexUrl: CustomSource.Url,
            flatContainerBase: "https://custom.feed/flat/",
            packageId: "previewafterstable",
            versions: ["1.2.3", "2.0.0-preview.1"]));

        string? result = await PackageExtractor.GetLatestVersionAsync(
            client,
            "PreviewAfterStable",
            [CustomSource],
            log: null,
            includePrerelease: true);

        Assert.Equal("2.0.0-preview.1", result);
    }

    [Fact]
    public async Task GetLatestVersion_CustomSourceDoesNotUseAnotherSourcesCache()
    {
        // Pre-seed cache — should be ignored when nuget.org is not in sources
        SetLatest("TestPackage", NuGetOrgSource, "1.0.0");

        // Custom source can't resolve (no real server), so result is null
        var result = await PackageExtractor.GetLatestVersionAsync(
            FailingClient, "TestPackage", [CustomSource], log: null);

        Assert.Null(result);
    }

    [Fact]
    public void CandidateCacheKeys_DoNotAliasPackageIdsAndCacheKinds()
    {
        Assert.NotEqual(
            PackageExtractor.GetLatestVersionCacheKey(
                "foo-listings",
                CustomSource),
            PackageExtractor.GetListingsVersionCacheKey(
                "foo",
                CustomSource));
        Assert.NotEqual(
            PackageExtractor.GetLatestVersionCacheKey(
                "foo",
                CustomSource,
                includePrerelease: true),
            PackageExtractor.GetLatestVersionCacheKey(
                "foo-prerelease",
                CustomSource));
    }

    [Fact]
    public async Task GetLatestVersion_CustomSourceCandidateCacheAvoidsSecondRequest()
    {
        using var client = new HttpClient(new CustomFeedHandler(
            serviceIndexUrl: CustomSource.Url,
            flatContainerBase: "https://custom.feed/flat/",
            packageId: "customcached",
            versions: ["4.5.6"]));

        Assert.Equal(
            "4.5.6",
            await PackageExtractor.GetLatestVersionAsync(
                client,
                "CustomCached",
                [CustomSource],
                log: null));

        Assert.Equal(
            "4.5.6",
            await PackageExtractor.GetLatestVersionAsync(
                FailingClient,
                "CustomCached",
                [CustomSource],
                log: null));
    }

    [Fact]
    public async Task GetLatestVersion_CustomSourceNormalizesFetchedCandidate()
    {
        using var client = new HttpClient(new CustomFeedHandler(
            serviceIndexUrl: CustomSource.Url,
            flatContainerBase: "https://custom.feed/flat/",
            packageId: "customshorthand",
            versions: ["1.2"]));

        string? result = await PackageExtractor.GetLatestVersionAsync(
            client,
            "CustomShorthand",
            [CustomSource],
            log: null);

        Assert.Equal("1.2.0", result);
    }

    [Fact]
    public async Task GetLatestVersion_NoncanonicalNuGetOrgPathUsesServiceIndex()
    {
        var source = new NuGetSource(
            "custom",
            "https://api.nuget.org/private/v3/index.json");
        using var client = new HttpClient(new CustomFeedHandler(
            serviceIndexUrl: source.Url,
            flatContainerBase: "https://private.invalid/flat/",
            packageId: "custompath",
            versions: ["7.8.9"]));

        string? result = await PackageExtractor.GetLatestVersionAsync(
            client,
            "CustomPath",
            [source],
            log: null);

        Assert.Equal("7.8.9", result);
    }

    [Fact]
    public async Task GetLatestVersion_QueryBearingServiceIndexIsNotCorrupted()
    {
        var source = new NuGetSource(
            "custom",
            "https://api.nuget.org/private/v3/index.json?source=custom");
        using var client = new HttpClient(new CustomFeedHandler(
            serviceIndexUrl: source.Url,
            flatContainerBase: "https://private.invalid/query-flat/",
            packageId: "querypath",
            versions: ["8.9.0"]));

        string? result = await PackageExtractor.GetLatestVersionAsync(
            client,
            "QueryPath",
            [source],
            log: null);

        Assert.Equal("8.9.0", result);
    }

    [Fact]
    public async Task GetLatestVersion_NonStringVersionIndexEntryIsAMiss()
    {
        using var client = new HttpClient(new RawCustomFeedHandler(
            CustomSource,
            "nonstrlatest",
            """{"versions":["1.2.3",4]}"""));

        string? result = await PackageExtractor.GetLatestVersionAsync(
            client,
            "NonstrLatest",
            [CustomSource],
            log: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestVersion_WithSkipCache_IgnoresCachedValue()
    {
        SetLatest("TestPackage", NuGetOrgSource, "1.0.0");

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
        var key = PackageExtractor.GetLatestVersionCacheKey(
            "ExpiredPackage",
            NuGetOrgSource);
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
        SetLatest("System.Text.Json", NuGetOrgSource, "9.0.0");

        var result = await PackageExtractor.GetLatestVersionAsync(
            FailingClient, "System.Text.Json", [NuGetOrgSource], log: null);

        Assert.Equal("9.0.0", result);
    }

    [Fact]
    public async Task GetLatestVersion_LogsOnCacheHit()
    {
        SetLatest("LogPackage", NuGetOrgSource, "3.0.0");
        var logs = new List<string>();

        await PackageExtractor.GetLatestVersionAsync(
            FailingClient, "LogPackage", [NuGetOrgSource], log: logs.Add);

        Assert.Single(logs);
        Assert.Contains("cached", logs[0], StringComparison.OrdinalIgnoreCase);
    }

    // --- GetVersionsAsync ---

    [Fact]
    public async Task ResolveVersionPattern_WithCachedVersionList_ReturnsCachedMatch()
    {
        var versions = "1.0.0\n2.0.0-beta.1\n2.0.0\n2.1.0";
        SetListings("TestPackage", NuGetOrgSource, versions);

        var result = await PackageExtractor.ResolveVersionPatternAsync(
            FailingClient, "TestPackage", "2.0.*", [NuGetOrgSource], log: null);

        Assert.Equal("2.0.0", result);
    }

    [Fact]
    public async Task GetVersions_WithCachedVersionList_ReturnsCachedValues()
    {
        var versions = "1.0.0\n1.1.0\n2.0.0";
        SetListings("TestPackage", NuGetOrgSource, versions);

        var result = await PackageExtractor.GetVersionsAsync(
            FailingClient, "TestPackage", includePrerelease: false, limit: null, log: null);

        Assert.NotNull(result);
        Assert.Equal(["2.0.0", "1.1.0", "1.0.0"], result);
    }

    [Fact]
    public async Task GetVersions_WithIncompleteCachedSnapshot_RefetchesSource()
    {
        string packageName = $"partial-{Guid.NewGuid():N}";
        CoreCache.Set(
            VersionCacheCategory,
            PackageExtractor.GetListingsVersionCacheKey(
                packageName,
                CustomSource),
            "9.9.9",
            extension: "txt");
        using var client = new HttpClient(new CustomFeedHandler(
            serviceIndexUrl: CustomSource.Url,
            flatContainerBase: "https://custom.feed/flat/",
            packageId: packageName,
            versions: ["1.2.3"]));

        List<string>? result = await PackageExtractor.GetVersionsAsync(
            client,
            packageName,
            includePrerelease: false,
            limit: null,
            log: null,
            sourceOptions: new NuGetSourceOptions
            {
                Sources = [CustomSource.Url],
            });

        Assert.Equal(["1.2.3"], result);
    }

    [Fact]
    public async Task GetVersions_WithCachedList_RespectsLimit()
    {
        var versions = "1.0.0\n1.1.0\n2.0.0\n3.0.0";
        SetListings("LimitPackage", NuGetOrgSource, versions);

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
        SetListings("Prerelease", NuGetOrgSource, versions);

        var result = await PackageExtractor.GetVersionsAsync(
            FailingClient, "Prerelease", includePrerelease: false, limit: null, log: null);

        Assert.NotNull(result);
        Assert.Equal(["2.0.0", "1.0.0"], result);
    }

    [Fact]
    public async Task GetVersions_WithCachedList_IncludesPrerelease()
    {
        var versions = "1.0.0\n2.0.0-beta.1\n2.0.0\n3.0.0-rc.1";
        SetListings("Prerelease2", NuGetOrgSource, versions);

        var result = await PackageExtractor.GetVersionsAsync(
            FailingClient, "Prerelease2", includePrerelease: true, limit: null, log: null);

        Assert.NotNull(result);
        Assert.Equal(["3.0.0-rc.1", "2.0.0", "2.0.0-beta.1", "1.0.0"], result);
    }

    [Fact]
    public async Task GetVersions_WithExpiredCache_DoesNotReturnStaleList()
    {
        var key = PackageExtractor.GetListingsVersionCacheKey(
            "Expired",
            NuGetOrgSource);
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
        SetListings(
            "CustomOnly",
            NuGetOrgSource,
            "1.0.0\n2.0.0");

        // GetVersionsAsync resolves sources internally from NuGetSourceOptions.
        // With a source that isn't nuget.org, cache should be skipped.
        var sourceOptions = new NuGetSourceOptions { Sources = [CustomSource.Url] };

        var result = await PackageExtractor.GetVersionsAsync(
            FailingClient, "CustomOnly", includePrerelease: false, limit: null, log: null,
            sourceOptions: sourceOptions);

        // No nuget.org source → no cache → failing client → null
        Assert.Null(result);
    }

    [Fact]
    public async Task GetVersions_NormalizesFetchedCandidates()
    {
        using var client = new HttpClient(new CustomFeedHandler(
            serviceIndexUrl: CustomSource.Url,
            flatContainerBase: "https://custom.feed/flat/",
            packageId: "listshorthand",
            versions: ["1.2", "2.0.0"]));

        List<string>? result = await PackageExtractor.GetVersionsAsync(
            client,
            "ListShorthand",
            includePrerelease: false,
            limit: null,
            log: null,
            sourceOptions: new NuGetSourceOptions
            {
                Sources = [CustomSource.Url],
            });

        Assert.Equal(["2.0.0", "1.2.0"], result);
    }

    [Fact]
    public async Task GetVersions_NonStringVersionIndexEntryIsAMiss()
    {
        using var client = new HttpClient(new RawCustomFeedHandler(
            CustomSource,
            "nonstrlist",
            """{"versions":["1.2.3",4]}"""));

        List<string>? result = await PackageExtractor.GetVersionsAsync(
            client,
            "NonstrList",
            includePrerelease: false,
            limit: null,
            log: null,
            sourceOptions: new NuGetSourceOptions
            {
                Sources = [CustomSource.Url],
            });

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveVersionPattern_WithNuGetOrgCached_AlsoQueriesCustomSource()
    {
        // nuget.org's list is cached and contains no match for the pattern; the only matching
        // version lives on a secondary (custom) feed. Cross-source merging must still find it.
        // Regression guard: a first-source-wins shortcut would never query the custom feed.
        SetListings(
            "MergePackage",
            NuGetOrgSource,
            "1.0.0\n2.0.0");

        using var client = new HttpClient(new CustomFeedHandler(
            serviceIndexUrl: "https://custom.feed/v3/index.json",
            flatContainerBase: "https://custom.feed/flat/",
            packageId: "mergepackage",
            versions: ["9.9.9-custom.1"]));

        var result = await PackageExtractor.ResolveVersionPatternAsync(
            client, "MergePackage", "9.9.*", [NuGetOrgSource, CustomSource], log: null);

        Assert.Equal("9.9.9-custom.1", result);
    }

    private static void SetLatest(
        string packageName,
        NuGetSource source,
        string version,
        bool includePrerelease = false)
        => CoreCache.Set(
            VersionCacheCategory,
            PackageExtractor.GetLatestVersionCacheKey(
                packageName,
                source,
                includePrerelease),
            version,
            extension: "txt");

    private static void SetListings(
        string packageName,
        NuGetSource source,
        string versions)
        => CoreCache.Set(
            VersionCacheCategory,
            PackageExtractor.GetListingsVersionCacheKey(packageName, source),
            string.Join(
                '\n',
                versions.Split('\n').Select(version => $"{version}\tL")),
            extension: "txt");

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

    /// <summary>
    /// Minimal V3 feed handler: serves a service index pointing at a flat-container base, and a
    /// flat-container version list for one package. Any other URL returns 404.
    /// </summary>
    private sealed class CustomFeedHandler(
        string serviceIndexUrl, string flatContainerBase, string packageId, string[] versions)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            string? body = null;

            if (string.Equals(url, serviceIndexUrl, StringComparison.OrdinalIgnoreCase))
            {
                body = $$"""
                {"resources":[{"@id":"{{flatContainerBase}}","@type":"PackageBaseAddress/3.0.0"}]}
                """;
            }
            else if (string.Equals(url, $"{flatContainerBase}{packageId}/index.json", StringComparison.OrdinalIgnoreCase))
            {
                body = $$"""
                {"versions":[{{string.Join(",", versions.Select(v => $"\"{v}\""))}}]}
                """;
            }

            var response = body != null
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }

    private sealed class RawCustomFeedHandler(
        NuGetSource source,
        string packageId,
        string versionIndex)
        : HttpMessageHandler
    {
        private const string FlatContainerBase =
            "https://custom.feed/raw-flat/";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            string? body = url switch
            {
                _ when url.Equals(
                    source.Url,
                    StringComparison.OrdinalIgnoreCase) => $$"""
                    {"resources":[{"@id":"{{FlatContainerBase}}","@type":"PackageBaseAddress/3.0.0"}]}
                    """,
                _ when url.Equals(
                    $"{FlatContainerBase}{packageId}/index.json",
                    StringComparison.OrdinalIgnoreCase) => versionIndex,
                _ => null,
            };

            return Task.FromResult(new HttpResponseMessage(
                body is null
                    ? System.Net.HttpStatusCode.NotFound
                    : System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? ""),
            });
        }
    }
}
