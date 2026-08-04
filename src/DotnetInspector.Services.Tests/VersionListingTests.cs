using System.Net;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using NuGetSource = NuGetFetch.PackageSource;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Tests for the listing-aware version model (issue #3388 follow-up): GetVersionListingsAsync
/// exposes the per-version <see cref="PackageVersionInfo.Listed"/> bit so surfaces can mark unlisted
/// versions rather than silently hiding them, while still hiding them by default.
/// </summary>
[Collection(CoreCacheCollection.Name)]
public class VersionListingTests : IDisposable
{
    private const string VersionCacheCategory = "versions-v4";
    private static readonly NuGetSource NuGetOrgSource = NuGetSource.NuGetOrg;
    private static readonly HttpClient FailingClient = new(new FailingHandler());

    public VersionListingTests()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        CoreCache.Clear(VersionCacheCategory);
    }

    public void Dispose() => CoreCache.Clear(VersionCacheCategory);

    private static readonly (string Version, bool Listed)[] Registry =
    [
        ("1.0.0", true),
        ("1.5.0", true),
        ("2.0.0", false),          // unlisted stable
        ("2.0.0-beta.1", true),
        ("3.0.0-beta.1", false),   // unlisted prerelease
    ];

    [Fact]
    public async Task Listings_WithoutIncludeUnlisted_MatchesListedOnly()
    {
        using var client = new HttpClient(new NuGetOrgHandler("pkg", Registry));

        var result = await PackageExtractor.GetVersionListingsAsync(
            client, "Pkg", includePrerelease: false, includeUnlisted: false, limit: null, log: null);

        Assert.NotNull(result);
        Assert.Equal(["1.5.0", "1.0.0"], result!.Select(v => v.Version));
        Assert.All(result, v => Assert.True(v.Listed));
    }

    [Fact]
    public async Task Listings_WithIncludeUnlisted_MarksUnlisted()
    {
        using var client = new HttpClient(new NuGetOrgHandler("pkg", Registry));

        var result = await PackageExtractor.GetVersionListingsAsync(
            client, "Pkg", includePrerelease: true, includeUnlisted: true, limit: null, log: null);

        Assert.NotNull(result);
        // Newest-first, all versions present.
        Assert.Equal(["3.0.0-beta.1", "2.0.0", "2.0.0-beta.1", "1.5.0", "1.0.0"],
            result!.Select(v => v.Version));
        // Only the two unlisted versions carry Listed == false.
        Assert.False(result.Single(v => v.Version == "2.0.0").Listed);
        Assert.False(result.Single(v => v.Version == "3.0.0-beta.1").Listed);
        Assert.True(result.Single(v => v.Version == "2.0.0-beta.1").Listed);
    }

    [Fact]
    public async Task Listings_IncludeUnlisted_RespectsPrereleaseFilter()
    {
        using var client = new HttpClient(new NuGetOrgHandler("pkg", Registry));

        var result = await PackageExtractor.GetVersionListingsAsync(
            client, "Pkg", includePrerelease: false, includeUnlisted: true, limit: null, log: null);

        // Stable-only, but the unlisted stable 2.0.0 is now visible (marked).
        Assert.Equal(["2.0.0", "1.5.0", "1.0.0"], result!.Select(v => v.Version));
        Assert.False(result!.Single(v => v.Version == "2.0.0").Listed);
    }

    [Fact]
    public async Task Listings_FailsOpen_WhenRegistrationUnavailable()
    {
        // Registration 404s → listing status unknown → everything reported as listed (fail open),
        // and no version is dropped.
        using var client = new HttpClient(new NuGetOrgHandler("pkg", Registry, serveRegistration: false));

        var result = await PackageExtractor.GetVersionListingsAsync(
            client, "Pkg", includePrerelease: true, includeUnlisted: true, limit: null, log: null);

        Assert.NotNull(result);
        Assert.Equal(["3.0.0-beta.1", "2.0.0", "2.0.0-beta.1", "1.5.0", "1.0.0"],
            result!.Select(v => v.Version));
        Assert.All(result, v => Assert.True(v.Listed));
    }

    [Fact]
    public async Task Listings_FailOpenResult_IsNotCached()
    {
        // A fail-open snapshot (registration unavailable) marks every version listed; it must not
        // be cached, or a transient outage would hide real unlisted versions for the whole TTL.
        using var client = new HttpClient(new NuGetOrgHandler("pkg", Registry, serveRegistration: false));

        _ = await PackageExtractor.GetVersionListingsAsync(
            client, "Pkg", includePrerelease: true, includeUnlisted: true, limit: null, log: null);

        Assert.Null(CoreCache.TryGet(
            VersionCacheCategory,
            PackageExtractor.GetListingsVersionCacheKey(
                "Pkg",
                NuGetOrgSource),
            TimeSpan.FromHours(1),
            extension: "txt"));
    }

    [Fact]
    public async Task Listings_AreCached_WithListingBits()
    {
        using (var client = new HttpClient(new NuGetOrgHandler("pkg", Registry)))
        {
            _ = await PackageExtractor.GetVersionListingsAsync(
                client, "Pkg", includePrerelease: true, includeUnlisted: true, limit: null, log: null);
        }

        // A failing client proves the second call is served from cache; the Listed bits must
        // survive the cache round-trip (SerializeListings/DeserializeListings).
        var cached = await PackageExtractor.GetVersionListingsAsync(
            FailingClient, "Pkg", includePrerelease: true, includeUnlisted: true, limit: null, log: null);

        Assert.NotNull(cached);
        Assert.False(cached!.Single(v => v.Version == "2.0.0").Listed);
        Assert.True(cached.Single(v => v.Version == "1.5.0").Listed);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException($"Network access not allowed in test: {request.RequestUri}");
    }

    private sealed class NuGetOrgHandler(
        string packageId,
        (string Version, bool Listed)[] registry,
        bool serveRegistration = true)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            string? body = null;

            if (string.Equals(url, $"https://api.nuget.org/v3-flatcontainer/{packageId}/index.json", StringComparison.OrdinalIgnoreCase))
            {
                string versions = string.Join(",", registry.Select(r => "\"" + r.Version + "\""));
                body = "{\"versions\":[" + versions + "]}";
            }
            else if (serveRegistration &&
                string.Equals(url, $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId}/index.json", StringComparison.OrdinalIgnoreCase))
            {
                string items = string.Join(",", registry.Select(r =>
                    "{\"catalogEntry\":{\"version\":\"" + r.Version + "\",\"listed\":"
                        + (r.Listed ? "true" : "false") + "}}"));
                body = "{\"items\":[{\"items\":[" + items + "]}]}";
            }

            var response = body != null
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }
}
