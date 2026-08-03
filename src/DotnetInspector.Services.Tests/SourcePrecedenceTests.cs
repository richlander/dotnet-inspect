// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using NuGetSource = NuGetFetch.PackageSource;
using PackageSourceCredential = NuGetFetch.PackageSourceCredential;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Pins how multiple package sources combine.
/// </summary>
/// <remarks>
/// <para>
/// One rule governs every path: the answer is aggregated across all configured sources. Listing
/// returns the union, and resolving a single latest version returns the highest version any source
/// carries. Source order does not decide either answer.
/// </para>
/// <para>
/// Ordering was previously precedence for <c>--latest-version</c> alone, which meant a feed
/// appended after nuget.org could not raise the answer for a package nuget.org also carried. That
/// silently hid exactly what a private feed exists to publish, and it disagreed with both
/// <c>--versions</c> and wildcard resolution in this same file. NuGet has no such rule either:
/// source order is not precedence there, which is what package source mapping is for.
/// </para>
/// </remarks>
[Collection(CoreCacheCollection.Name)]
public class SourcePrecedenceTests : IDisposable
{
    private const string VersionCacheCategory = "versions-v2";

    private const string FeedAIndex = "feed-a.example.test/v3/index.json";
    private const string FeedBIndex = "feed-b.example.test/v3/index.json";

    public SourcePrecedenceTests()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        CoreCache.Clear(VersionCacheCategory);
    }

    public void Dispose() => CoreCache.Clear(VersionCacheCategory);

    [Fact]
    public async Task GetLatestVersion_TakesHighestVersionAcrossSources_NotFirstSource()
    {
        // Feed A carries an older version than feed B. Listing feed A first must not cap the
        // answer at what feed A knows; the higher version on feed B is still the latest.
        var handler = CreateHandler(feedAVersions: ["0.31.0", "0.32.0"], feedBVersions: ["0.32.0", "0.32.99"]);
        using var client = new HttpClient(handler);

        string? version = await PackageExtractor.GetLatestVersionAsync(
            client, "Markout", [FeedA(), FeedB()], log: null, skipCache: true);

        Assert.Equal("0.32.99", version);
    }

    [Fact]
    public async Task GetLatestVersion_IsOrderIndependent()
    {
        var handler = CreateHandler(feedAVersions: ["0.31.0", "0.32.0"], feedBVersions: ["0.32.0", "0.32.99"]);
        using var client = new HttpClient(handler);

        string? forward = await PackageExtractor.GetLatestVersionAsync(
            client, "Markout", [FeedA(), FeedB()], log: null, skipCache: true);
        string? reversed = await PackageExtractor.GetLatestVersionAsync(
            client, "Markout", [FeedB(), FeedA()], log: null, skipCache: true);

        Assert.Equal("0.32.99", forward);
        Assert.Equal(forward, reversed);
    }

    [Fact]
    public async Task GetLatestVersion_LowerVersionOnLaterSourceDoesNotWin()
    {
        // The reverse of the ordering case: the highest version is on the FIRST feed, so a later
        // feed carrying only older versions must not pull the answer back down.
        var handler = CreateHandler(feedAVersions: ["0.32.99"], feedBVersions: ["0.31.0", "0.32.0"]);
        using var client = new HttpClient(handler);

        string? version = await PackageExtractor.GetLatestVersionAsync(
            client, "Markout", [FeedA(), FeedB()], log: null, skipCache: true);

        Assert.Equal("0.32.99", version);
    }

    [Fact]
    public async Task GetLatestVersion_ComparesBySemanticOrderNotStringOrder()
    {
        // "0.9.0" sorts after "0.10.0" as text but before it as a version. Pinned so the
        // comparison cannot regress to an ordinal string compare.
        var handler = CreateHandler(feedAVersions: ["0.9.0"], feedBVersions: ["0.10.0"]);
        using var client = new HttpClient(handler);

        string? version = await PackageExtractor.GetLatestVersionAsync(
            client, "Markout", [FeedA(), FeedB()], log: null, skipCache: true);

        Assert.Equal("0.10.0", version);
    }

    [Fact]
    public async Task GetLatestVersion_CachedNuGetOrgAnswerDoesNotSuppressHigherFeedVersion()
    {
        // The version cache only ever holds nuget.org's own latest. Serving that hit as the final
        // answer would let a cached public version outrank a higher one on a private feed, so the
        // hit must stand in for nuget.org alone while the remaining sources are still consulted.
        using var client = new HttpClient(new NuGetOrgPlusPrivateHandler(
            packageId: "cachedpkg",
            nugetOrgRegistry: [("1.0.0", true)],
            privateVersions: ["2.0.0"]));

        var sources = new List<NuGetSource>
        {
            new("nuget.org", "https://api.nuget.org/v3/index.json"),
            FeedB(),
        };

        // Populate the cache the way a previous invocation would have.
        string? first = await PackageExtractor.GetLatestVersionAsync(
            client, "CachedPkg", sources, log: null, skipCache: false);
        Assert.Equal("2.0.0", first);
        Assert.Equal("1.0.0", CoreCache.TryGet(VersionCacheCategory, "cachedpkg", TimeSpan.FromHours(1), extension: "txt"));

        string? second = await PackageExtractor.GetLatestVersionAsync(
            client, "CachedPkg", sources, log: null, skipCache: false);

        Assert.Equal("2.0.0", second);
    }

    [Fact]
    public async Task GetLatestVersion_SkipsSourceThatDoesNotHavePackage()
    {
        // Feed A does not carry the package at all, so resolution falls through to feed B
        // rather than reporting the package as missing.
        var handler = CreateHandler(feedAVersions: null, feedBVersions: ["0.32.99"]);
        using var client = new HttpClient(handler);

        string? version = await PackageExtractor.GetLatestVersionAsync(
            client, "Markout", [FeedA(), FeedB()], log: null, skipCache: true);

        Assert.Equal("0.32.99", version);
    }

    [Fact]
    public async Task GetLatestVersion_ReturnsNullWhenNoSourceHasPackage()
    {
        var handler = CreateHandler(feedAVersions: null, feedBVersions: null);
        using var client = new HttpClient(handler);

        string? version = await PackageExtractor.GetLatestVersionAsync(
            client, "Markout", [FeedA(), FeedB()], log: null, skipCache: true);

        Assert.Null(version);
    }

    [Fact]
    public async Task GetVersions_AggregatesAcrossSources()
    {
        // Listing does not stop at the first source: the result is the union, newest first,
        // so a version present only on the second feed still appears.
        var handler = CreateHandler(feedAVersions: ["0.31.0", "0.32.0"], feedBVersions: ["0.32.0", "0.32.99"]);
        using var client = new HttpClient(handler);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [$"https://{FeedAIndex}", $"https://{FeedBIndex}"],
        };

        List<string>? versions = await PackageExtractor.GetVersionsAsync(
            client, "Markout", includePrerelease: false, limit: null, log: null, sourceOptions: sourceOptions);

        Assert.NotNull(versions);
        Assert.Equal(["0.32.99", "0.32.0", "0.31.0"], versions);
    }

    [Fact]
    public async Task GetVersions_IsOrderIndependent()
    {
        var handler = CreateHandler(feedAVersions: ["0.31.0", "0.32.0"], feedBVersions: ["0.32.0", "0.32.99"]);
        using var client = new HttpClient(handler);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [$"https://{FeedBIndex}", $"https://{FeedAIndex}"],
        };

        List<string>? versions = await PackageExtractor.GetVersionsAsync(
            client, "Markout", includePrerelease: false, limit: null, log: null, sourceOptions: sourceOptions);

        Assert.Equal(["0.32.99", "0.32.0", "0.31.0"], versions);
    }

    [Fact]
    public async Task GetVersionListingsWithSource_EmitsOneRowPerFeedCarryingAVersion()
    {
        // 0.32.0 is on both feeds, so it appears twice; 0.32.99 is private to feed B.
        var handler = CreateHandler(feedAVersions: ["0.31.0", "0.32.0"], feedBVersions: ["0.32.0", "0.32.99"]);
        using var client = new HttpClient(handler);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [$"https://{FeedAIndex}", $"https://{FeedBIndex}"],
        };

        var rows = await PackageExtractor.GetVersionListingsWithSourceAsync(
            client, "Markout", includePrerelease: false, includeUnlisted: false,
            limit: null, log: null, sourceOptions: sourceOptions);

        Assert.NotNull(rows);
        Assert.Equal(
            [
                ("0.32.99", "feed-b.example.test"),
                ("0.32.0", "feed-a.example.test"),
                ("0.32.0", "feed-b.example.test"),
                ("0.31.0", "feed-a.example.test"),
            ],
            rows.Select(r => (r.Version, r.Feed)));
    }

    [Fact]
    public async Task GetVersionListingsWithSource_DistinguishesFeedsThatShareTheExplicitName()
    {
        // Sources named on the command line that match nothing in configuration are all called
        // "explicit". Labels must still tell them apart, or a version on two feeds collapses.
        var handler = CreateHandler(feedAVersions: ["1.0.0"], feedBVersions: ["1.0.0"]);
        using var client = new HttpClient(handler);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [$"https://{FeedAIndex}", $"https://{FeedBIndex}"],
        };

        var rows = await PackageExtractor.GetVersionListingsWithSourceAsync(
            client, "Markout", includePrerelease: false, includeUnlisted: false,
            limit: null, log: null, sourceOptions: sourceOptions);

        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.Feed).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task GetVersionListingsWithSource_LimitCountsVersionsNotRows()
    {
        // A limit of 1 keeps the newest version only, but still lists every feed carrying it.
        var handler = CreateHandler(feedAVersions: ["0.32.0"], feedBVersions: ["0.32.0"]);
        using var client = new HttpClient(handler);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [$"https://{FeedAIndex}", $"https://{FeedBIndex}"],
        };

        var rows = await PackageExtractor.GetVersionListingsWithSourceAsync(
            client, "Markout", includePrerelease: false, includeUnlisted: false,
            limit: 1, log: null, sourceOptions: sourceOptions);

        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("0.32.0", r.Version));
    }

    [Fact]
    public async Task GetVersionListingsWithSource_ReturnsNullWhenNoSourceHasPackage()
    {
        var handler = CreateHandler(feedAVersions: null, feedBVersions: null);
        using var client = new HttpClient(handler);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [$"https://{FeedAIndex}", $"https://{FeedBIndex}"],
        };

        var rows = await PackageExtractor.GetVersionListingsWithSourceAsync(
            client, "Markout", includePrerelease: false, includeUnlisted: false,
            limit: null, log: null, sourceOptions: sourceOptions);

        Assert.Null(rows);
    }

    [Fact]
    public async Task GetVersionListingsWithSource_AppliesListingPerFeed_NotMerged()
    {
        // 2.0.0 is unlisted on nuget.org but is also published to a private feed. The merged
        // views cannot express that split: a version listed on any source counts as listed, and
        // private feeds have no listed concept, so they always report listed. Provenance can
        // express it, so the nuget.org row is hidden while the private-feed row survives.
        using var client = new HttpClient(new NuGetOrgPlusPrivateHandler(
            packageId: "splitpkg",
            nugetOrgRegistry: [("1.0.0", true), ("2.0.0", false)],
            privateVersions: ["2.0.0"]));

        var sourceOptions = new NuGetSourceOptions
        {
            Sources = ["https://api.nuget.org/v3/index.json", $"https://{FeedBIndex}"],
        };

        var rows = await PackageExtractor.GetVersionListingsWithSourceAsync(
            client, "SplitPkg", includePrerelease: false, includeUnlisted: false,
            limit: null, log: null, sourceOptions: sourceOptions);

        Assert.NotNull(rows);
        Assert.Equal(
            [
                ("2.0.0", "feed-b.example.test"),
                ("1.0.0", "nuget.org"),
            ],
            rows.Select(r => (r.Version, r.Feed)));
    }

    [Fact]
    public async Task GetVersionListingsWithSource_IncludeUnlisted_ShowsBothRowsWithStatus()
    {
        // With --include-unlisted the hidden nuget.org row reappears, marked unlisted, next to
        // the private-feed row for the same version. That contrast is the point of the view.
        using var client = new HttpClient(new NuGetOrgPlusPrivateHandler(
            packageId: "splitpkg",
            nugetOrgRegistry: [("1.0.0", true), ("2.0.0", false)],
            privateVersions: ["2.0.0"]));

        var sourceOptions = new NuGetSourceOptions
        {
            Sources = ["https://api.nuget.org/v3/index.json", $"https://{FeedBIndex}"],
        };

        var rows = await PackageExtractor.GetVersionListingsWithSourceAsync(
            client, "SplitPkg", includePrerelease: false, includeUnlisted: true,
            limit: null, log: null, sourceOptions: sourceOptions);

        Assert.NotNull(rows);
        Assert.Equal(
            [
                ("2.0.0", "nuget.org", false),
                ("2.0.0", "feed-b.example.test", true),
                ("1.0.0", "nuget.org", true),
            ],
            rows.Select(r => (r.Version, r.Feed, r.Listed)));
    }

    /// <summary>
    /// Serves nuget.org (flat container, registration index, search) alongside one private V3
    /// feed, so a single package version can be unlisted on nuget.org and present on the feed.
    /// </summary>
    private sealed class NuGetOrgPlusPrivateHandler(
        string packageId,
        (string Version, bool Listed)[] nugetOrgRegistry,
        string[] privateVersions)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            string? body = null;

            if (string.Equals(url, $"https://api.nuget.org/v3-flatcontainer/{packageId}/index.json", StringComparison.OrdinalIgnoreCase))
            {
                body = "{\"versions\":["
                    + string.Join(",", nugetOrgRegistry.Select(r => "\"" + r.Version + "\""))
                    + "]}";
            }
            else if (string.Equals(url, $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId}/index.json", StringComparison.OrdinalIgnoreCase))
            {
                string items = string.Join(",", nugetOrgRegistry.Select(r =>
                    "{\"catalogEntry\":{\"version\":\"" + r.Version + "\",\"listed\":"
                        + (r.Listed ? "true" : "false") + "}}"));
                body = "{\"items\":[{\"items\":[" + items + "]}]}";
            }
            else if (string.Equals(url, $"https://{FeedBIndex}", StringComparison.OrdinalIgnoreCase))
            {
                body = "{\"resources\":[{\"@type\":\"PackageBaseAddress/3.0.0\","
                    + "\"@id\":\"https://feed-b.example.test/v3/flat2/\"}]}";
            }
            else if (string.Equals(url, $"https://feed-b.example.test/v3/flat2/{packageId}/index.json", StringComparison.OrdinalIgnoreCase))
            {
                body = "{\"versions\":["
                    + string.Join(",", privateVersions.Select(v => "\"" + v + "\""))
                    + "]}";
            }

            var response = body != null
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task VersionListing_WithholdsCredentialsFromForeignContentHost()
    {
        // A service index names its own content endpoint, so whoever controls the feed controls
        // that URL. The credential the user configured for the feed must not follow the redirect
        // onto another origin, or a compromised or misconfigured feed collects it.
        var handler = new StubHandler();
        AddFeed(handler, FeedAIndex, "a-content.example.test", ["0.32.0"]);
        using var client = new HttpClient(handler);

        var credentialed = new NuGetSource("feed-a", $"https://{FeedAIndex}", new PackageSourceCredential("pat", "s3cret"));

        await PackageExtractor.GetLatestVersionAsync(
            client, "Markout", [credentialed], log: null, skipCache: true);

        Assert.NotNull(handler.AuthForUrlContaining(FeedAIndex));
        Assert.Null(handler.AuthForUrlContaining("a-content.example.test"));
    }

    [Fact]
    public async Task VersionListing_SendsCredentialsToSameOriginContentHost()
    {
        // The gate narrows to foreign origins only. A feed whose content lives on its own origin
        // — the ordinary Azure DevOps shape — must still be authenticated, or the withholding
        // rule would break every private feed it is meant to protect.
        var handler = new StubHandler();
        AddFeed(handler, FeedAIndex, "feed-a.example.test", ["0.32.0"]);
        using var client = new HttpClient(handler);

        var credentialed = new NuGetSource("feed-a", $"https://{FeedAIndex}", new PackageSourceCredential("pat", "s3cret"));

        string? version = await PackageExtractor.GetLatestVersionAsync(
            client, "Markout", [credentialed], log: null, skipCache: true);

        Assert.Equal("0.32.0", version);
        Assert.NotNull(handler.AuthForUrlContaining("feed-a.example.test/flat/"));
    }

    private static NuGetSource FeedA() => new("feed-a", $"https://{FeedAIndex}");

    private static NuGetSource FeedB() => new("feed-b", $"https://{FeedBIndex}");

    private static StubHandler CreateHandler(string[]? feedAVersions, string[]? feedBVersions)
    {
        var handler = new StubHandler();
        AddFeed(handler, FeedAIndex, "a-content.example.test", feedAVersions);
        AddFeed(handler, FeedBIndex, "b-content.example.test", feedBVersions);
        return handler;
    }

    private static void AddFeed(StubHandler handler, string indexUrl, string contentHost, string[]? versions)
    {
        handler.Add(
            indexUrl,
            $$"""{"resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://{{contentHost}}/flat/"}]}""");

        if (versions is not null)
        {
            string list = string.Join(",", versions.Select(v => $"\"{v}\""));
            handler.Add($"{contentHost}/flat/markout/index.json", $$"""{"versions":[{{list}}]}""");
        }
    }

    /// <summary>
    /// Returns canned JSON when the request URL contains a registered substring, 404s otherwise.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<(string Match, string Body)> _routes = [];

        public List<(string Url, string? Auth)> Requests { get; } = [];

        public void Add(string urlSubstring, string body) => _routes.Add((urlSubstring, body));

        public string? AuthForUrlContaining(string substring) =>
            Requests.First(r => r.Url.Contains(substring, StringComparison.Ordinal)).Auth;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? "";

            lock (Requests)
            {
                Requests.Add((url, request.Headers.Authorization?.Parameter));
            }

            foreach ((string match, string body) in _routes)
            {
                if (url.Contains(match, StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
