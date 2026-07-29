// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using NuGetSource = NuGetFetch.PackageSource;

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
    private const string VersionCacheCategory = "versions";

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
    public async Task GetVersionsWithSource_EmitsOneRowPerFeedCarryingAVersion()
    {
        // 0.32.0 is on both feeds, so it appears twice; 0.32.99 is private to feed B.
        var handler = CreateHandler(feedAVersions: ["0.31.0", "0.32.0"], feedBVersions: ["0.32.0", "0.32.99"]);
        using var client = new HttpClient(handler);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [$"https://{FeedAIndex}", $"https://{FeedBIndex}"],
        };

        var rows = await PackageExtractor.GetVersionsWithSourceAsync(
            client, "Markout", includePrerelease: false, limit: null, log: null, sourceOptions: sourceOptions);

        Assert.NotNull(rows);
        Assert.Equal(
            [
                ("0.32.99", "feed-b.example.test"),
                ("0.32.0", "feed-a.example.test"),
                ("0.32.0", "feed-b.example.test"),
                ("0.31.0", "feed-a.example.test"),
            ],
            rows);
    }

    [Fact]
    public async Task GetVersionsWithSource_DistinguishesFeedsThatShareTheExplicitName()
    {
        // Sources named on the command line that match nothing in configuration are all called
        // "explicit". Labels must still tell them apart, or a version on two feeds collapses.
        var handler = CreateHandler(feedAVersions: ["1.0.0"], feedBVersions: ["1.0.0"]);
        using var client = new HttpClient(handler);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [$"https://{FeedAIndex}", $"https://{FeedBIndex}"],
        };

        var rows = await PackageExtractor.GetVersionsWithSourceAsync(
            client, "Markout", includePrerelease: false, limit: null, log: null, sourceOptions: sourceOptions);

        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.Feed).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task GetVersionsWithSource_LimitCountsVersionsNotRows()
    {
        // A limit of 1 keeps the newest version only, but still lists every feed carrying it.
        var handler = CreateHandler(feedAVersions: ["0.32.0"], feedBVersions: ["0.32.0"]);
        using var client = new HttpClient(handler);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [$"https://{FeedAIndex}", $"https://{FeedBIndex}"],
        };

        var rows = await PackageExtractor.GetVersionsWithSourceAsync(
            client, "Markout", includePrerelease: false, limit: 1, log: null, sourceOptions: sourceOptions);

        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("0.32.0", r.Version));
    }

    [Fact]
    public async Task GetVersionsWithSource_ReturnsNullWhenNoSourceHasPackage()
    {
        var handler = CreateHandler(feedAVersions: null, feedBVersions: null);
        using var client = new HttpClient(handler);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [$"https://{FeedAIndex}", $"https://{FeedBIndex}"],
        };

        var rows = await PackageExtractor.GetVersionsWithSourceAsync(
            client, "Markout", includePrerelease: false, limit: null, log: null, sourceOptions: sourceOptions);

        Assert.Null(rows);
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

        public void Add(string urlSubstring, string body) => _routes.Add((urlSubstring, body));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? "";
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
