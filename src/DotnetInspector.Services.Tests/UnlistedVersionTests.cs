using System.Net;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using NuGetSource = NuGetFetch.PackageSource;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Regression tests for issue #3388: NuGet unlisted versions must not surface during discovery
/// (enumeration and "latest" resolution). The flat-container index.json includes unlisted versions
/// with no listed flag; only the registration index exposes <c>catalogEntry.listed</c>, so version
/// resolution must consult it and filter unlisted versions in one shared place.
/// </summary>
[Collection(CoreCacheCollection.Name)]
public class UnlistedVersionTests : IDisposable
{
    private const string VersionCacheCategory = "versions";
    private static readonly NuGetSource NuGetOrgSource = NuGetSource.NuGetOrg;

    public UnlistedVersionTests()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        CoreCache.Clear(VersionCacheCategory);
    }

    public void Dispose() => CoreCache.Clear(VersionCacheCategory);

    // Version history shared by the enumeration/resolution tests: two unlisted versions
    // (a stable 2.0.0 and a prerelease 3.0.0-beta.1) interleaved with listed versions.
    private static readonly (string Version, bool Listed)[] Registry =
    [
        ("1.0.0", true),
        ("1.5.0", true),
        ("2.0.0", false),          // unlisted stable — must never appear or become "latest"
        ("2.0.0-beta.1", true),
        ("3.0.0-beta.1", false),   // unlisted prerelease — must never become prerelease "latest"
    ];

    [Fact]
    public async Task GetVersions_Stable_ExcludesUnlisted()
    {
        using var client = new HttpClient(new NuGetOrgHandler("unlistedpkg", Registry));

        var result = await PackageExtractor.GetVersionsAsync(
            client, "UnlistedPkg", includePrerelease: false, limit: null, log: null);

        // 2.0.0 is unlisted and must be excluded; newest-first ordering.
        Assert.Equal(["1.5.0", "1.0.0"], result);
    }

    [Fact]
    public async Task GetVersions_Prerelease_ExcludesUnlisted()
    {
        using var client = new HttpClient(new NuGetOrgHandler("unlistedpkg", Registry));

        var result = await PackageExtractor.GetVersionsAsync(
            client, "UnlistedPkg", includePrerelease: true, limit: null, log: null);

        // Both unlisted versions (2.0.0 and 3.0.0-beta.1) excluded; newest-first.
        Assert.Equal(["2.0.0-beta.1", "1.5.0", "1.0.0"], result);
    }

    [Fact]
    public async Task GetLatestVersion_Prerelease_SkipsUnlistedHead()
    {
        using var client = new HttpClient(new NuGetOrgHandler("unlistedpkg", Registry));

        var result = await PackageExtractor.GetLatestVersionAsync(
            client, "UnlistedPkg", [NuGetOrgSource], log: null, includePrerelease: true);

        // The newest version overall is the unlisted 3.0.0-beta.1; latest must be the newest
        // listed version instead. This is the core prerelease-path defect from #3388.
        Assert.Equal("2.0.0-beta.1", result);
    }

    [Fact]
    public async Task ResolveVersionPattern_SkipsUnlistedMatch()
    {
        using var client = new HttpClient(new NuGetOrgHandler("unlistedpkg", Registry));

        var result = await PackageExtractor.ResolveVersionPatternAsync(
            client, "UnlistedPkg", "2.0.*", [NuGetOrgSource], log: null);

        // 2.0.0 (unlisted) is excluded, so the only 2.0.* match is the listed prerelease.
        Assert.Equal("2.0.0-beta.1", result);
    }

    [Fact]
    public async Task GetVersions_FailsOpen_WhenRegistrationUnavailable()
    {
        // Registration index 404s → we cannot determine listed status → do not drop versions.
        using var client = new HttpClient(new NuGetOrgHandler("unlistedpkg", Registry, serveRegistration: false));

        var result = await PackageExtractor.GetVersionsAsync(
            client, "UnlistedPkg", includePrerelease: false, limit: null, log: null);

        // Unfiltered: the unlisted 2.0.0 is retained rather than silently disappearing.
        Assert.Equal(["2.0.0", "1.5.0", "1.0.0"], result);
    }

    [Fact]
    public async Task GetVersions_FailsOpen_WhenRegistrationMalformed()
    {
        // Registration returns valid JSON whose shape defies the accessors (`items` is a number,
        // not an array). Parsing must fail open (no crash) rather than throw InvalidOperationException.
        using var client = new HttpClient(new NuGetOrgHandler(
            "unlistedpkg", Registry, registrationBodyOverride: "{\"items\":42}"));

        var result = await PackageExtractor.GetVersionsAsync(
            client, "UnlistedPkg", includePrerelease: false, limit: null, log: null);

        // Could not determine listing → unfiltered (unlisted 2.0.0 retained), no exception thrown.
        Assert.Equal(["2.0.0", "1.5.0", "1.0.0"], result);
    }

    [Fact]
    public async Task GetVersions_FailOpenResult_IsNotCached()
    {
        // When registration is unavailable the returned list is an unfiltered fail-open snapshot;
        // it must NOT be persisted, or a transient registration outage would re-surface unlisted
        // versions for the whole cache TTL.
        using var client = new HttpClient(new NuGetOrgHandler("unlistedpkg", Registry, serveRegistration: false));

        var result = await PackageExtractor.GetVersionsAsync(
            client, "UnlistedPkg", includePrerelease: false, limit: null, log: null);

        Assert.Equal(["2.0.0", "1.5.0", "1.0.0"], result);   // fail-open, unfiltered
        Assert.Null(CoreCache.TryGet(VersionCacheCategory, "unlistedpkg-all", TimeSpan.FromHours(1), extension: "txt"));
    }

    [Fact]
    public async Task GetLatestVersion_Prerelease_DoesNotFallThroughToUnfiltered_OnTransientFlatContainerFailure()
    {
        // The registration path is authoritative for nuget.org. If the flat-container read fails
        // (so no listed list can be produced), latest resolution must return null rather than
        // falling through and re-reading the flat-container UNFILTERED — which on a transient
        // recovery would surface the unlisted 3.0.0-beta.1 as prerelease "latest".
        using var client = new HttpClient(new NuGetOrgHandler("unlistedpkg", Registry, failFlatContainerFirstCall: true));

        var result = await PackageExtractor.GetLatestVersionAsync(
            client, "UnlistedPkg", [NuGetOrgSource], log: null, includePrerelease: true);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestVersion_Prerelease_ReturnsNull_WhenRegistrationUnavailable()
    {
        // Flat-container is healthy but the registration index (the authoritative source of listed
        // status) is unavailable, so the listed list is a fail-open UNFILTERED snapshot flagged
        // non-authoritative. Latest resolution must NOT pick from it — otherwise the unlisted head
        // 3.0.0-beta.1 would surface as prerelease "latest" during a transient registration outage.
        using var client = new HttpClient(new NuGetOrgHandler("unlistedpkg", Registry, serveRegistration: false));

        var result = await PackageExtractor.GetLatestVersionAsync(
            client, "UnlistedPkg", [NuGetOrgSource], log: null, includePrerelease: true);

        Assert.Null(result);
    }

    /// <summary>
    /// Serves the three nuget.org endpoints version resolution touches: the flat-container version
    /// list (no listed flag), the registration index (single inline page carrying listed flags),
    /// and the search API (listing-aware stable latest). Any other URL returns 404.
    /// </summary>
    private sealed class NuGetOrgHandler(
        string packageId,
        (string Version, bool Listed)[] registry,
        bool serveRegistration = true,
        string? registrationBodyOverride = null,
        bool failFlatContainerFirstCall = false)
        : HttpMessageHandler
    {
        private int _flatContainerCalls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            string? body = null;

            if (string.Equals(url, $"https://api.nuget.org/v3-flatcontainer/{packageId}/index.json", StringComparison.OrdinalIgnoreCase))
            {
                // Simulate a transient (non-retryable 404) failure on the first read so a caller
                // that wrongly falls through would see the endpoint recover on a later read.
                if (failFlatContainerFirstCall && ++_flatContainerCalls == 1)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

                string versions = string.Join(",", registry.Select(r => "\"" + r.Version + "\""));
                body = "{\"versions\":[" + versions + "]}";
            }
            else if (serveRegistration &&
                string.Equals(url, $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId}/index.json", StringComparison.OrdinalIgnoreCase))
            {
                if (registrationBodyOverride != null)
                {
                    body = registrationBodyOverride;
                }
                else
                {
                    string items = string.Join(",", registry.Select(r =>
                        "{\"catalogEntry\":{\"version\":\"" + r.Version + "\",\"listed\":"
                            + (r.Listed ? "true" : "false") + "}}"));
                    body = "{\"items\":[{\"items\":[" + items + "]}]}";
                }
            }
            else if (url.StartsWith("https://azuresearch-usnc.nuget.org/query", StringComparison.OrdinalIgnoreCase))
            {
                // Search API is listing-aware: report the newest listed stable version.
                string latestStable = registry.Where(r => r.Listed && !r.Version.Contains('-'))
                    .Select(r => r.Version).Last();
                body = "{\"data\":[{\"version\":\"" + latestStable + "\"}]}";
            }

            var response = body != null
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }
}
