using System.Net;

using DotnetInspector.Core;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Tests;

public class PackageVersionVectorTests
{
    static readonly string[] Versions =
    [
        "1.0.0",
        "1.1.0-alpha.1",
        "1.1.0",
        "1.2.0-preview.1",
        "1.2.0",
        "2.0.0",
    ];

    public static TheoryData<string> NonHttpSources() => new()
    {
        Path.Combine(Path.GetTempPath(), "packages"),
        new Uri(Path.Combine(Path.GetTempPath(), "packages")).AbsoluteUri,
    };

    [Fact]
    public void Create_ResolvesAnInclusiveVectorInCallerDirection()
    {
        Assert.True(PackageVersionRange.TryParse("Example@1.0.0..1.2.0", out var range, out var error));
        Assert.Null(error);

        var ascending = PackageVersionVector.Create(range!, Versions);
        Assert.Equal(["1.0.0", "1.1.0", "1.2.0"], ascending.Addresses.Select(address => address.Version.ToString()));

        Assert.True(PackageVersionRange.TryParse("Example@1.2.0..1.0.0", out range, out error));
        var descending = PackageVersionVector.Create(range!, Versions);
        Assert.Equal(["1.2.0", "1.1.0", "1.0.0"], descending.Addresses.Select(address => address.Version.ToString()));
    }

    [Fact]
    public void Create_IncludesPrereleasesOnlyWhenAnEndpointRequiresThem()
    {
        Assert.True(PackageVersionRange.TryParse("Example@1.1.0-alpha.1..1.2.0", out var range, out _));

        var vector = PackageVersionVector.Create(range!, Versions);

        Assert.Equal(
            ["1.1.0-alpha.1", "1.1.0", "1.2.0-preview.1", "1.2.0"],
            vector.Addresses.Select(address => address.Version.ToString()));
    }

    [Fact]
    public void Create_CanExplicitlyIncludePrereleasesBetweenStableEndpoints()
    {
        PackageVersionRange.TryParse("Example@1.0.0..1.2.0", out var range, out _);

        var vector = PackageVersionVector.Create(range!, Versions, includePrerelease: true);

        Assert.Equal(
            ["1.0.0", "1.1.0-alpha.1", "1.1.0", "1.2.0-preview.1", "1.2.0"],
            vector.Addresses.Select(address => address.Version.ToString()));
    }

    [Theory]
    [InlineData("#1", "1.0.0")]
    [InlineData("#3", "1.2.0")]
    [InlineData("first", "1.0.0")]
    [InlineData("last", "1.2.0")]
    [InlineData("1.1.0", "1.1.0")]
    public void TrySelect_ResolvesStableAddresses(string selector, string expectedVersion)
    {
        PackageVersionRange.TryParse("Example@1.0.0..1.2.0", out var range, out _);
        var vector = PackageVersionVector.Create(range!, Versions);

        Assert.True(vector.TrySelect(selector, out var address, out var error));
        Assert.Null(error);
        Assert.Equal(expectedVersion, address!.Version.ToString());
    }

    [Fact]
    public void TrySelect_RejectsAddressesOutsideTheVector()
    {
        PackageVersionRange.TryParse("Example@1.0.0..1.2.0", out var range, out _);
        var vector = PackageVersionVector.Create(range!, Versions);

        Assert.False(vector.TrySelect("#4", out _, out var indexError));
        Assert.Contains("#1..#3", indexError);
        Assert.False(vector.TrySelect("2.0.0", out _, out var versionError));
        Assert.Contains("not in range", versionError);
    }

    [Fact]
    public void Create_RequiresPublishedEndpoints()
    {
        PackageVersionRange.TryParse("Example@1.0.1..1.2.0", out var range, out _);

        var error = Assert.Throws<ArgumentException>(
            () => PackageVersionVector.Create(range!, Versions));

        Assert.Contains("does not contain range endpoint 1.0.1", error.Message);
    }

    static readonly PackageVersionInfo[] ListedVersions =
    [
        new("1.0.0", Listed: true),
        new("1.1.0", Listed: false), // unlisted, between listed endpoints
        new("1.2.0", Listed: true),
        new("2.0.0", Listed: true),
    ];

    [Fact]
    public void CreateListingAware_TagsListedStatusAcrossTheRange()
    {
        PackageVersionRange.TryParse("Example@1.0.0..1.2.0", out var range, out _);

        var rows = PackageVersionVector.CreateListingAware(range!, ListedVersions).ToArray();

        // The unlisted 1.1.0 is included (not dropped) and correctly tagged.
        Assert.Equal(
            [("1.0.0", true), ("1.1.0", false), ("1.2.0", true)],
            rows.Select(row => (row.Version, row.Listed)));
    }

    [Fact]
    public void CreateListingAware_ResolvesAnUnlistedEndpoint()
    {
        // A singleton range OF the unlisted version must resolve rather than reporting a missing
        // endpoint: the vector is built from the full listing set, unlisted versions included.
        PackageVersionRange.TryParse("Example@1.1.0..1.1.0", out var range, out _);

        var rows = PackageVersionVector.CreateListingAware(range!, ListedVersions).ToArray();

        Assert.Equal([("1.1.0", false)], rows.Select(row => (row.Version, row.Listed)));
    }

    static readonly PackageVersionInfo[] PrereleaseListedVersions =
    [
        new("2.0.0-beta2.21617.1", Listed: true),
        new("2.0.0-beta3.22101.1", Listed: false), // unlisted prerelease, between listed endpoints
        new("2.0.0-beta3.22103.3", Listed: true),
    ];

    [Fact]
    public void CreateListingAware_ResolvesAPrereleaseEndpointRangeWithoutExplicitPrerelease()
    {
        // A range whose endpoints are prerelease must resolve and include the in-range prereleases
        // even when includePrerelease is not explicitly requested — the prerelease endpoints
        // themselves require it. The CLI mirrors this by fetching prereleases whenever
        // range.IncludesPrerelease is true; here Create re-derives the same effective flag.
        PackageVersionRange.TryParse(
            "Example@2.0.0-beta2.21617.1..2.0.0-beta3.22103.3", out var range, out _);

        var rows = PackageVersionVector.CreateListingAware(
            range!, PrereleaseListedVersions, includePrerelease: false).ToArray();

        Assert.Equal(
            [("2.0.0-beta2.21617.1", true), ("2.0.0-beta3.22101.1", false), ("2.0.0-beta3.22103.3", true)],
            rows.Select(row => (row.Version, row.Listed)));
    }

    [Theory]
    [InlineData("Example@1.0.0", false, null)]
    [InlineData("Example@1.0.0..", false, "Expected Package@A..B")]
    [InlineData("Example@bad..2.0.0", false, "Invalid package version 'bad'")]
    public void TryParse_DistinguishesNonRangesFromInvalidRanges(
        string value,
        bool expectedResult,
        string? expectedError)
    {
        bool result = PackageVersionRange.TryParse(value, out var range, out var error);

        Assert.Equal(expectedResult, result);
        Assert.Null(range);
        if (expectedError is null)
            Assert.Null(error);
        else
            Assert.Contains(expectedError, error);
    }

    [Theory]
    [MemberData(nameof(NonHttpSources))]
    public async Task ResolveAsync_SkipsNonHttpSource(
        string localSource)
    {
        CoreCache.Initialize("dotnet-inspect-test");
        Assert.True(
            PackageVersionRange.TryParse(
                "Example@1.0.0..2.0.0",
                out PackageVersionRange? range,
                out _));
        using var client = new HttpClient(
            new VersionListingHandler());

        PackageVersionVector vector =
            await PackageVersionVector.ResolveAsync(
                client,
                range!,
                new NuGetSourceOptions
                {
                    Sources =
                    [
                        localSource,
                        VersionListingHandler.IndexUrl,
                    ],
                });

        Assert.Equal(
            ["1.0.0", "2.0.0"],
            vector.Addresses.Select(
                address => address.Version.ToNormalizedString()));
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ResolveAsync_FallsThroughFailedHttpSource(
        HttpStatusCode statusCode)
    {
        CoreCache.Initialize("dotnet-inspect-test");
        Assert.True(
            PackageVersionRange.TryParse(
                "Example@1.0.0..2.0.0",
                out PackageVersionRange? range,
                out _));
        using var client = new HttpClient(
            new VersionListingHandler(statusCode));

        PackageVersionVector vector =
            await PackageVersionVector.ResolveAsync(
                client,
                range!,
                new NuGetSourceOptions
                {
                    Sources =
                    [
                        VersionListingHandler.FailingIndexUrl,
                        VersionListingHandler.IndexUrl,
                    ],
                });

        Assert.Equal(
            ["1.0.0", "2.0.0"],
            vector.Addresses.Select(
                address => address.Version.ToNormalizedString()));
    }

    sealed class VersionListingHandler(
        HttpStatusCode? failingStatus = null) : HttpMessageHandler
    {
        internal const string IndexUrl =
            "https://healthy.test/v3/index.json";
        internal const string FailingIndexUrl =
            "https://failing.test/v3/index.json";
        const string FlatContainer =
            "https://healthy.test/flat/";
        const string FailingFlatContainer =
            "https://failing.test/flat/";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url == $"{FailingFlatContainer}example/index.json")
            {
                return Task.FromResult(
                    new HttpResponseMessage(
                        failingStatus ?? HttpStatusCode.NotFound));
            }

            string? body = url switch
            {
                IndexUrl =>
                    $$"""
                    {"resources":[{"@id":"{{FlatContainer}}","@type":"PackageBaseAddress/3.0.0"}]}
                    """,
                FailingIndexUrl =>
                    $$"""
                    {"resources":[{"@id":"{{FailingFlatContainer}}","@type":"PackageBaseAddress/3.0.0"}]}
                    """,
                $"{FlatContainer}example/index.json" =>
                    """{"versions":["1.0.0","2.0.0"]}""",
                _ => null,
            };
            return Task.FromResult(
                new HttpResponseMessage(
                    body is null
                        ? HttpStatusCode.NotFound
                        : HttpStatusCode.OK)
                {
                    Content = new StringContent(body ?? ""),
                });
        }
    }
}
