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
