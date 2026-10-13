using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserPackageVersionInventoryTests
{
    [Theory]
    [InlineData("2.0.0", "1.10.0", 0)]
    [InlineData("2.0.0-rc.2", "2.0.0-rc.1", 1)]
    [InlineData("1.0.0", null, 4)]
    [InlineData("1.11.0", "1.10.0", 2)]
    [InlineData("3.0.0", "2.0.0", 0)]
    [InlineData("0.5.0", null, 5)]
    public async Task PreviousVersionUsesNativeReleasePrecedence(
        string current,
        string? expected,
        int expectedInsertionIndex)
    {
        BrowserPackageVersionInventory result = await Inventory(
            ["1.9.0", "2.0.0-rc.1", "1.10.0", "2.0.0+build", "1.0.0"],
            current);

        Assert.Equal(expected, result.PreviousVersion);
        Assert.Equal(expectedInsertionIndex, result.CurrentVersionInsertionIndex);
        Assert.Null(result.PreviousVersionUnavailableReason);
        Assert.Equal(
            ["2.0.0", "2.0.0-rc.1", "1.10.0", "1.9.0", "1.0.0"],
            result.Versions);
    }

    [Fact]
    public void CurrentBuildMetadataDoesNotChangeReleasePrecedence()
    {
        var document = new PackageVersionListingDocument(
            new("Example.Package", IncludePrerelease: true, IncludeUnlisted: true),
            PackageVersionListingCompleteness.Authoritative,
            [
                new("1.10.0", Listed: true),
                new("2.0.0", Listed: true),
            ],
            []);

        BrowserPackageVersionInventory result =
            BrowserPackageVersionInventory.Create(
                document,
                "2.0.0+other");

        Assert.Equal("1.10.0", result.PreviousVersion);
        Assert.Equal(0, result.CurrentVersionInsertionIndex);
    }

    [Fact]
    public async Task UnlistedVersionsRemainExactChoicesButNotAutomaticDefaults()
    {
        BrowserPackageVersionInventory result =
            await Inventory(["1.0.0", "1.1.0", "2.0.0"], "2.0.0", unlisted: "1.1.0");

        Assert.Equal("1.0.0", result.PreviousVersion);
        Assert.Equal(0, result.CurrentVersionInsertionIndex);
        Assert.Contains("1.1.0", result.Versions);
        Assert.Equal(["1.1.0"], result.UnlistedVersions);
    }

    [Fact]
    public async Task WindowsPublishEarlierPointsNearestFirstUpToTheDeepestDepth()
    {
        BrowserPackageVersionInventory result = await Inventory(
            [
                "6.0.0", "7.0.0", "8.0.0", "8.0.1", "9.0.0", "9.0.1",
                "10.0.0", "10.0.1", "10.0.2", "10.0.3", "10.0.4", "10.0.5", "10.0.6",
                "11.0.0", "12.0.0", "12.0.1", "13.0.0", "13.0.1", "13.0.2",
            ],
            "13.0.2",
            unlisted: "13.0.1");

        Assert.Equal("13.0.0", result.PreviousVersion);
        Assert.Equal(
            ["13.0.0", "12.0.1", "12.0.0", "11.0.0", "10.0.6", "10.0.5"],
            result.SupplyChainVersions!);
        Assert.Equal(
            ["12.0.1", "11.0.0", "10.0.6", "9.0.1", "8.0.1", "7.0.0"],
            result.ApiEvolutionVersions!);
        Assert.Equal(result.PreviousVersion, result.SupplyChainVersions![0]);
    }

    [Fact]
    public async Task WindowsWithoutAnEarlierPointAreEmpty()
    {
        BrowserPackageVersionInventory result =
            await Inventory(["2.0.0", "2.1.0"], "2.1.0");

        Assert.Equal(["2.0.0"], result.SupplyChainVersions!);
        Assert.Empty(result.ApiEvolutionVersions!);
    }

    [Fact]
    public async Task UnknownListingStatePreservesExactChoicesAndWithholdsTheDefault()
    {
        BrowserPackageVersionInventory result =
            await Inventory(["1.0.0", "2.0.0"], "1.5.0", partial: true);

        Assert.Equal(["2.0.0", "1.0.0"], result.Versions);
        Assert.Equal(1, result.CurrentVersionInsertionIndex);
        Assert.Null(result.PreviousVersion);
        Assert.Contains("authoritative listing state", result.PreviousVersionUnavailableReason);
        Assert.Null(result.SupplyChainVersions);
        Assert.Null(result.ApiEvolutionVersions);
    }

    [Fact]
    public void EmptyInventoryIsAnHonestMissingPredecessor()
    {
        var document = new PackageVersionListingDocument(
            new("Example.Package", IncludePrerelease: true, IncludeUnlisted: true),
            PackageVersionListingCompleteness.Authoritative,
            [],
            []);
        BrowserPackageVersionInventory result =
            BrowserPackageVersionInventory.Create(document, "1.0.0");

        Assert.Empty(result.Versions);
        Assert.Equal(0, result.CurrentVersionInsertionIndex);
        Assert.Null(result.PreviousVersion);
        Assert.Null(result.PreviousVersionUnavailableReason);
    }

    [Fact]
    public async Task HouseNotFoundIsAVisibleFailure()
    {
        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Inventory([], "1.0.0"));

        Assert.Equal(
            "No configured authority reported the package.",
            failure.Message);
    }

    [Fact]
    public void BrowserWirePublishesOnlyAvailablePredecessorFacts()
    {
        Assert.Contains("\"unlistedVersions\":[\"1.0.0\"]",
            Serialize(new BrowserPackageVersions(["1.0.0"], 0, null, null, ["1.0.0"])));
        Assert.Equal(
            """{"versions":["2.0.0"],"currentVersionInsertionIndex":0,"previousVersion":"1.0.0"}""",
            Serialize(new BrowserPackageVersions(["2.0.0"], 0, "1.0.0", null)));
        Assert.Equal(
            """{"versions":["2.0.0"],"currentVersionInsertionIndex":0,"previousVersionUnavailableReason":"Listing authority unavailable."}""",
            Serialize(new BrowserPackageVersions(
                ["2.0.0"],
                0,
                null,
                "Listing authority unavailable.")));
        Assert.Equal(
            """{"versions":[],"currentVersionInsertionIndex":0}""",
            Serialize(new BrowserPackageVersions([], 0, null, null)));
        Assert.Equal(
            """{"versions":["2.0.0","1.1.0","1.0.0"],"currentVersionInsertionIndex":0,"previousVersion":"1.1.0","supplyChainVersions":["1.1.0","1.0.0"],"apiEvolutionVersions":["1.1.0"]}""",
            Serialize(new BrowserPackageVersions(
                ["2.0.0", "1.1.0", "1.0.0"],
                0,
                "1.1.0",
                null,
                SupplyChainVersions: ["1.1.0", "1.0.0"],
                ApiEvolutionVersions: ["1.1.0"])));
    }

    static string Serialize(BrowserPackageVersions value) =>
        JsonSerializer.Serialize(value, BrowserPackageJsonContext.Default.BrowserPackageVersions);

    static async Task<BrowserPackageVersionInventory> Inventory(
        string[] versions,
        string current,
        string? unlisted = null,
        bool partial = false)
    {
        var handler = new VersionHandler(versions, unlisted, partial);
        using IPackageSourceClient source =
            BrowserPackageWorkspace.CreateGallerySource(
                handler,
                new NuGetFetchOptions());
        BrowserPackageVersionInventory result =
            await BrowserPackageWorkspace.GetVersionInventoryAsync(
                "Example.Package",
                current,
                source,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Contains(handler.Requests, request =>
            request.Contains("/v3-flatcontainer/", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request =>
            request.Contains("/registration5-", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, request =>
            request.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        return result;
    }

    sealed class VersionHandler(
        string[] versions,
        string? unlisted,
        bool partial) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!.AbsolutePath);
            bool flat = request.RequestUri!.AbsolutePath.Contains("/v3-flatcontainer/", StringComparison.Ordinal);
            string body = flat
                ? JsonSerializer.Serialize(new { versions })
                : partial
                    ? "{"
                    : JsonSerializer.Serialize(new
                    {
                        items = new[]
                        {
                            new
                            {
                                items = versions.Select(version => new
                                {
                                    catalogEntry = new { version, listed = version != unlisted },
                                }),
                            },
                        },
                    });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }
}
