using DotnetInspector.Packages;
using NuGet.Versioning;

namespace DotnetInspector.Packages.Tests;

// Gates for version-resolution.md#version-window-projection.
public sealed class PackageVersionWindowTests
{
    static readonly string[] Releases =
    [
        "7.0.19", "7.0.20",
        "8.0.0", "8.0.17", "8.0.18",
        "9.0.0", "9.0.11", "9.0.12",
        "10.0.0", "10.0.5", "10.0.6", "10.0.7", "10.0.8", "10.0.9",
    ];

    [Theory]
    [InlineData(PackageVersionWindowPolicy.SupplyChain, 1, "10.0.8 10.0.9")]
    [InlineData(PackageVersionWindowPolicy.SupplyChain, 3, "10.0.6 10.0.7 10.0.8 10.0.9")]
    [InlineData(PackageVersionWindowPolicy.ApiEvolution, 1, "9.0.12 10.0.9")]
    [InlineData(PackageVersionWindowPolicy.ApiEvolution, 3, "7.0.20 8.0.18 9.0.12 10.0.9")]
    public void ServicingRelease(PackageVersionWindowPolicy policy, int depth, string expected)
    {
        PackageVersionWindow window = Project(policy, depth, "10.0.9", Releases);

        Assert.Equal(expected, Points(window));
        Assert.Equal(depth, window.ReachedDepth);
        Assert.Equal("10.0.9", window.Current.NormalizedVersion);
    }

    [Theory]
    [InlineData(PackageVersionWindowPolicy.SupplyChain, 1, "10.4.2 10.5.0")]
    [InlineData(PackageVersionWindowPolicy.SupplyChain, 3, "9.3.1 10.4.1 10.4.2 10.5.0")]
    [InlineData(PackageVersionWindowPolicy.ApiEvolution, 1, "9.3.1 10.5.0")]
    public void MinorRelease(PackageVersionWindowPolicy policy, int depth, string expected)
    {
        PackageVersionWindow window = Project(
            policy, depth, "10.5.0", ["9.2.0", "9.3.0", "9.3.1", "10.4.1", "10.4.2", "10.5.0"]);

        Assert.Equal(expected, Points(window));
    }

    [Theory]
    [InlineData(PackageVersionWindowPolicy.SupplyChain, 1, "9.0.12 10.0.0")]
    [InlineData(PackageVersionWindowPolicy.SupplyChain, 3, "9.0.0 9.0.11 9.0.12 10.0.0")]
    [InlineData(PackageVersionWindowPolicy.ApiEvolution, 1, "9.0.12 10.0.0")]
    [InlineData(PackageVersionWindowPolicy.ApiEvolution, 3, "7.0.20 8.0.18 9.0.12 10.0.0")]
    public void MajorReleaseCrossesIntoThePreviousMajor(
        PackageVersionWindowPolicy policy,
        int depth,
        string expected)
    {
        Assert.Equal(expected, Points(Project(policy, depth, "10.0.0", Releases)));
    }

    [Fact]
    public void MajorRepresentativeIsTheHighestMinor()
    {
        PackageVersionWindow window = Project(
            PackageVersionWindowPolicy.ApiEvolution,
            1,
            "11.0.0",
            ["10.5.3", "10.6.0", "10.6.1", "11.0.0"]);

        Assert.Equal("10.6.1 11.0.0", Points(window));
    }

    [Theory]
    [InlineData(PackageVersionWindowPolicy.SupplyChain, "8.0.0 8.0.1 9.0.0", 2)]
    [InlineData(PackageVersionWindowPolicy.ApiEvolution, "8.0.1 9.0.0", 1)]
    public void DepthBeyondTheListingReportsTheReachedDepth(
        PackageVersionWindowPolicy policy,
        string expected,
        int reached)
    {
        PackageVersionWindow window = Project(policy, 6, "9.0.0", ["8.0.0", "8.0.1", "9.0.0"]);

        Assert.Equal(expected, Points(window));
        Assert.Equal(6, window.RequestedDepth);
        Assert.Equal(reached, window.ReachedDepth);
    }

    [Theory]
    [InlineData(PackageVersionWindowPolicy.SupplyChain)]
    [InlineData(PackageVersionWindowPolicy.ApiEvolution)]
    public void FirstReleaseHasNoEarlierVersion(PackageVersionWindowPolicy policy)
    {
        Assert.Equal(
            PackageVersionWindowFailure.NoEarlierVersion,
            Fail(policy, "1.0.0", ["1.0.0", "1.1.0"]));
    }

    [Fact]
    public void NoLowerMajorHasNoApiEvolutionWindow()
    {
        string[] listing = ["2.0.0", "2.1.0", "2.2.0"];

        Assert.Equal(
            PackageVersionWindowFailure.NoEarlierVersion,
            Fail(PackageVersionWindowPolicy.ApiEvolution, "2.2.0", listing));
        Assert.Equal(
            "2.1.0 2.2.0",
            Points(Project(PackageVersionWindowPolicy.SupplyChain, 1, "2.2.0", listing)));
    }

    [Fact]
    public void PrereleaseCurrentAdmitsPrereleaseCandidates()
    {
        string[] listing = ["9.0.12", "10.0.0-preview.1", "10.0.0-preview.2", "10.0.0-preview.3"];

        Assert.Equal(
            "10.0.0-preview.1 10.0.0-preview.2 10.0.0-preview.3",
            Points(Project(PackageVersionWindowPolicy.SupplyChain, 2, "10.0.0-preview.3", listing)));
    }

    [Fact]
    public void StableCurrentExcludesPrereleaseCandidates()
    {
        string[] listing = ["9.0.12", "10.0.0-rc.1", "10.0.0-rc.2", "10.0.0"];

        Assert.Equal(
            "9.0.12 10.0.0",
            Points(Project(PackageVersionWindowPolicy.SupplyChain, 1, "10.0.0", listing)));
    }

    [Fact]
    public void PrereleaseCurrentKeepsStableMajorRepresentatives()
    {
        string[] listing =
        [
            "9.0.12", "9.1.0-preview.1",
            "10.0.0-preview.1",
            "11.0.0-preview.1",
        ];

        PackageVersionWindow window = Project(
            PackageVersionWindowPolicy.ApiEvolution, 2, "11.0.0-preview.2", listing);

        Assert.Equal("9.0.12 10.0.0-preview.1 11.0.0-preview.2", Points(window));
    }

    [Fact]
    public void DepthBelowOneIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PackageVersionWindow.TryProject(
            "Example.Package",
            PackageVersionWindowPolicy.SupplyChain,
            0,
            NuGetVersion.Parse("2.0.0"),
            Listing(["1.0.0"]),
            listingIsAuthoritative: true,
            out _,
            out _));
    }

    [Fact]
    public void UnlistedCandidatesAreSkipped()
    {
        PackageVersionInfo[] listing =
        [
            new("1.0.0", Listed: true),
            new("1.1.0", Listed: true),
            new("1.2.0", Listed: false),
            new("1.3.0", Listed: true),
        ];

        PackageVersionWindow window = Project(
            PackageVersionWindowPolicy.SupplyChain, 2, "1.3.0", listing);

        Assert.Equal("1.0.0 1.1.0 1.3.0", Points(window));
        Assert.Equal(
            ["1.0.0", "1.1.0", "1.3.0"],
            window.Population.Addresses.Select(address => address.NormalizedVersion));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PinnedCurrentEndsThePopulationWhetherUnlistedOrAbsent(bool listedAsUnlisted)
    {
        PackageVersionInfo[] listing =
        [
            new("1.0.0", Listed: true),
            new("1.1.0", Listed: false),
            new("1.2.0", Listed: true),
            .. listedAsUnlisted ? [new PackageVersionInfo("2.0.0", Listed: false)] : Array.Empty<PackageVersionInfo>(),
        ];

        PackageVersionWindow window = Project(
            PackageVersionWindowPolicy.SupplyChain, 6, "2.0.0", listing);

        Assert.Equal("1.0.0 1.2.0 2.0.0", Points(window));
        Assert.Equal(window.Population.Addresses[^1], window.Current);
        Assert.Equal(
            ["1.0.0", "1.2.0", "2.0.0"],
            window.Population.Addresses.Select(address => address.NormalizedVersion));
    }

    [Fact]
    public void ApiEvolutionPopulationKeepsListedInteriorVersions()
    {
        PackageVersionWindow window = Project(
            PackageVersionWindowPolicy.ApiEvolution, 1, "10.0.2", Releases.Append("10.0.2"));

        Assert.Equal("9.0.12 10.0.2", Points(window));
        Assert.Equal(
            ["9.0.12", "10.0.0", "10.0.2"],
            window.Population.Addresses.Select(address => address.NormalizedVersion));
    }

    [Fact]
    public void CurrentBuildMetadataDoesNotCreateAnEarlierRelease()
    {
        PackageVersionWindow window = Project(
            PackageVersionWindowPolicy.SupplyChain, 1, "2.0.0+other", ["1.10.0", "2.0.0", "2.0.0+build"]);

        Assert.Equal("1.10.0 2.0.0", Points(window));
    }

    [Theory]
    [InlineData("10.0.9", 3)]
    [InlineData("10.0.0", 6)]
    [InlineData("8.0.18", 1)]
    public void ApiEvolutionPointsAreThePopulationsLatestStableRepresentatives(string current, int depth)
    {
        string[] listing = [.. Releases, "9.1.0-preview.1", "8.1.0-rc.1"];
        PackageVersionWindow window = Project(PackageVersionWindowPolicy.ApiEvolution, depth, current, listing);

        Assert.Equal(
            window.Points[..^1],
            window.Population
                .ProjectMajorRepresentatives(PackageVersionMajorRepresentativePolicy.LatestStable)
                .Addresses
                .Where(address => address.Version.Major < window.Current.Version.Major));
    }

    [Theory]
    [InlineData(PackageVersionWindowPolicy.SupplyChain)]
    [InlineData(PackageVersionWindowPolicy.ApiEvolution)]
    public void NonAuthoritativeListingHasNoWindow(PackageVersionWindowPolicy policy)
    {
        bool projected = PackageVersionWindow.TryProject(
            "Example.Package",
            policy,
            1,
            NuGetVersion.Parse("10.0.9"),
            Listing(Releases),
            listingIsAuthoritative: false,
            out PackageVersionWindow? window,
            out PackageVersionWindowFailure failure);

        Assert.False(projected);
        Assert.Null(window);
        Assert.Equal(PackageVersionWindowFailure.ListingNotAuthoritative, failure);
    }

    static PackageVersionWindow Project(
        PackageVersionWindowPolicy policy,
        int depth,
        string current,
        IEnumerable<string> versions)
        => Project(policy, depth, current, Listing(versions));

    static PackageVersionWindow Project(
        PackageVersionWindowPolicy policy,
        int depth,
        string current,
        IEnumerable<PackageVersionInfo> listing)
    {
        Assert.True(PackageVersionWindow.TryProject(
            "Example.Package",
            policy,
            depth,
            NuGetVersion.Parse(current),
            listing,
            listingIsAuthoritative: true,
            out PackageVersionWindow? window,
            out _));
        return window!;
    }

    static PackageVersionWindowFailure Fail(
        PackageVersionWindowPolicy policy,
        string current,
        IEnumerable<string> versions)
    {
        Assert.False(PackageVersionWindow.TryProject(
            "Example.Package",
            policy,
            1,
            NuGetVersion.Parse(current),
            Listing(versions),
            listingIsAuthoritative: true,
            out PackageVersionWindow? window,
            out PackageVersionWindowFailure failure));
        Assert.Null(window);
        return failure;
    }

    static PackageVersionInfo[] Listing(IEnumerable<string> versions)
        => [.. versions.Select(version => new PackageVersionInfo(version, Listed: true))];

    static string Points(PackageVersionWindow window)
        => string.Join(' ', window.Points.Select(point => point.Version.ToNormalizedString()));
}
