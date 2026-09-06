using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using NuGetFetch;

namespace InspectWeb.Engine.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserPackageVersionInventoryTests
{
    [Theory]
    [InlineData("2.0.0", "1.10.0", 0)]
    [InlineData("2.0.0-rc.2", "2.0.0-rc.1", 1)]
    [InlineData("1.0.0", null, 4)]
    [InlineData("1.11.0", "1.10.0", 2)]
    [InlineData("2.0.0+other", "1.10.0", 0)]
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
    public async Task EmptyInventoryIsAnHonestMissingPredecessor()
    {
        BrowserPackageVersionInventory result = await Inventory([], "1.0.0");

        Assert.Empty(result.Versions);
        Assert.Equal(0, result.CurrentVersionInsertionIndex);
        Assert.Null(result.PreviousVersion);
        Assert.Null(result.PreviousVersionUnavailableReason);
    }

    static async Task<BrowserPackageVersionInventory> Inventory(
        string[] versions,
        string current,
        string? unlisted = null,
        bool partial = false)
    {
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(),
            new VersionHandler(versions, unlisted, partial),
            new NuGetFetchOptions());
        PackageSourceOperationResult<PackageVersionResult> result =
            await source.GetVersionsAsync("Example.Package", TestContext.Current.CancellationToken);
        Assert.Null(result.Failure);
        return BrowserPackageVersionInventory.Create(Assert.IsType<PackageVersionResult>(result.Value), current);
    }

    sealed class VersionHandler(
        string[] versions,
        string? unlisted,
        bool partial) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
