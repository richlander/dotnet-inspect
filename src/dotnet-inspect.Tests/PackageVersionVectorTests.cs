using DotnetInspector.Packages;

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
}
