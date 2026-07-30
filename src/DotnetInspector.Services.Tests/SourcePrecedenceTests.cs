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
/// Two different rules are in play, and the difference is deliberate rather than accidental:
/// resolving a single latest version takes the first source that has the package, while listing
/// versions aggregates across every source. Source order is therefore the control a caller uses
/// to say which feed wins, which is what <c>--source</c> is for.
/// </para>
/// <para>
/// The practical consequence, pinned below, is that a source appended after nuget.org cannot
/// change the answer to <c>--latest-version</c> for a package that also exists on nuget.org.
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
    public async Task GetLatestVersion_TakesFirstSourceWithPackage_NotHighestVersion()
    {
        // Feed A carries an older version than feed B. First-with-package wins, so the
        // lower version is the correct answer when feed A is listed first.
        var handler = CreateHandler(feedAVersions: ["0.31.0", "0.32.0"], feedBVersions: ["0.32.0", "0.32.99"]);
        using var client = new HttpClient(handler);

        string? version = await PackageExtractor.GetLatestVersionAsync(
            client, "Markout", [FeedA(), FeedB()], log: null, skipCache: true);

        Assert.Equal("0.32.0", version);
    }

    [Fact]
    public async Task GetLatestVersion_SourceOrderDeterminesResult()
    {
        var handler = CreateHandler(feedAVersions: ["0.31.0", "0.32.0"], feedBVersions: ["0.32.0", "0.32.99"]);
        using var client = new HttpClient(handler);

        string? version = await PackageExtractor.GetLatestVersionAsync(
            client, "Markout", [FeedB(), FeedA()], log: null, skipCache: true);

        Assert.Equal("0.32.99", version);
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
