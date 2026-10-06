using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace MsdlProxy.Tests;

public sealed class PackageChangeProxyRequestValidatorTests
{
    [Theory]
    [InlineData(
        "/v3/index.json",
        "https://api.nuget.org/v3/index.json")]
    [InlineData(
        "/v3/catalog0/index.json",
        "https://api.nuget.org/v3/catalog0/index.json")]
    [InlineData(
        "/v3/catalog0/page20764.json",
        "https://api.nuget.org/v3/catalog0/page20764.json")]
    [InlineData(
        "/v3/catalog0/data/2026.09.15.03.00.00/"
        + "microsoft.extensions.ai.10.10.0.json",
        "https://api.nuget.org/v3/catalog0/data/2026.09.15.03.00.00/"
        + "microsoft.extensions.ai.10.10.0.json")]
    [InlineData(
        "/v3/catalog0/data/2026.09.15.03.00.00/"
        + "example.1.0.0+build.1.json",
        "https://api.nuget.org/v3/catalog0/data/2026.09.15.03.00.00/"
        + "example.1.0.0+build.1.json")]
    public void NuGetRequest_AdmitsOnlyRequiredResourceShapes(
        string path,
        string expected)
    {
        bool accepted =
            PackageChangeProxyRequestValidator.TryCreateNuGetRequest(
                Query(("path", path)),
                out Uri? upstream);

        Assert.True(accepted);
        Assert.Equal(expected, upstream?.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com/v3/index.json")]
    [InlineData("/v3-flatcontainer/example/index.json")]
    [InlineData("/v3/catalog0/../index.json")]
    [InlineData("/v3/catalog0/page.json")]
    [InlineData("/v3/catalog0/page1.xml")]
    [InlineData("/v3/catalog0/data/not-a-time/example.1.0.0.json")]
    [InlineData("/v3/catalog0/data/2026.09.15.03.00.00/example%2f1.0.0.json")]
    public void NuGetRequest_RejectsOtherPaths(string path)
    {
        Assert.False(
            PackageChangeProxyRequestValidator.TryCreateNuGetRequest(
                Query(("path", path)),
                out _));
    }

    [Fact]
    public void NuGetRequest_RejectsAdditionalQueryInput()
    {
        Assert.False(
            PackageChangeProxyRequestValidator.TryCreateNuGetRequest(
                Query(
                    ("path", "/v3/index.json"),
                    ("url", "https://example.com")),
                out _));
    }

    [Fact]
    public void AdvisoryRequest_ConstructsFixedReviewedNuGetQuery()
    {
        bool accepted =
            PackageChangeProxyRequestValidator.TryCreateAdvisoryRequest(
                AdvisoryQuery(
                    "Microsoft.Extensions.AI,System.Text.Json",
                    after: "Y3Vyc29yOnYyOpHOAQ=="),
                out Uri? upstream);

        Assert.True(accepted);
        Assert.Equal(
            "https://api.github.com/advisories"
            + "?ecosystem=nuget&type=reviewed&is_withdrawn=false"
            + "&per_page=100"
            + "&affects=Microsoft.Extensions.AI%2CSystem.Text.Json"
            + "&after=Y3Vyc29yOnYyOpHOAQ%3D%3D",
            upstream?.AbsoluteUri);
    }

    [Theory]
    [InlineData("ecosystem", "npm")]
    [InlineData("type", "unreviewed")]
    [InlineData("is_withdrawn", "true")]
    [InlineData("per_page", "99")]
    [InlineData("affects", "Example/Package")]
    [InlineData("after", "cursor&next")]
    public void AdvisoryRequest_RejectsChangedFixedOrBoundedValues(
        string key,
        string value)
    {
        var query = new Dictionary<string, StringValues>(
            AdvisoryQuery("Microsoft.Extensions.AI", "cursor"),
            StringComparer.Ordinal)
        {
            [key] = value,
        };

        Assert.False(
            PackageChangeProxyRequestValidator.TryCreateAdvisoryRequest(
                new QueryCollection(query),
                out _));
    }

    [Fact]
    public void AdvisoryRequest_RejectsAdditionalOrRepeatedInput()
    {
        var additional = new Dictionary<string, StringValues>(
            AdvisoryQuery("Microsoft.Extensions.AI"),
            StringComparer.Ordinal)
        {
            ["url"] = "https://example.com",
        };
        Assert.False(
            PackageChangeProxyRequestValidator.TryCreateAdvisoryRequest(
                new QueryCollection(additional),
                out _));

        var repeated = new Dictionary<string, StringValues>(
            AdvisoryQuery("Microsoft.Extensions.AI"),
            StringComparer.Ordinal)
        {
            ["affects"] = new StringValues(
                ["Microsoft.Extensions.AI", "System.Text.Json"]),
        };
        Assert.False(
            PackageChangeProxyRequestValidator.TryCreateAdvisoryRequest(
                new QueryCollection(repeated),
                out _));
    }

    private static QueryCollection Query(
        params (string Key, string Value)[] values) =>
        new(values.ToDictionary(
            static item => item.Key,
            static item => new StringValues(item.Value),
            StringComparer.Ordinal));

    private static QueryCollection AdvisoryQuery(
        string affects,
        string? after = null)
    {
        var values = new Dictionary<string, StringValues>(
            StringComparer.Ordinal)
        {
            ["ecosystem"] = "nuget",
            ["type"] = "reviewed",
            ["is_withdrawn"] = "false",
            ["per_page"] = "100",
            ["affects"] = affects,
        };
        if (after is not null)
            values.Add("after", after);
        return new QueryCollection(values);
    }
}
