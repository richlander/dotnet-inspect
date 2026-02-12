using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

public class NuGetSearchServiceTests
{
    [Fact]
    public void ParseSearchResponse_ValidJson_ReturnsResults()
    {
        var json = """
        {
          "totalHits": 3,
          "data": [
            {
              "id": "Azure.AI.OpenAI",
              "version": "2.1.0",
              "description": "Azure OpenAI client library",
              "totalDownloads": 5000000,
              "verified": true
            },
            {
              "id": "Azure.AI.TextAnalytics",
              "version": "5.3.0",
              "description": "Azure Text Analytics client",
              "totalDownloads": 2000000,
              "verified": true
            },
            {
              "id": "Azure.AI.FormRecognizer",
              "version": "4.1.0",
              "description": "Azure Form Recognizer client",
              "totalDownloads": 1000000,
              "verified": false
            }
          ]
        }
        """;

        var results = NuGetSearchService.ParseSearchResponse(json);

        Assert.Equal(3, results.Count);

        Assert.Equal("Azure.AI.OpenAI", results[0].PackageId);
        Assert.Equal("2.1.0", results[0].Version);
        Assert.Equal("Azure OpenAI client library", results[0].Description);
        Assert.Equal(5_000_000, results[0].TotalDownloads);
        Assert.True(results[0].Verified);

        Assert.Equal("Azure.AI.TextAnalytics", results[1].PackageId);
        Assert.Equal("5.3.0", results[1].Version);

        Assert.False(results[2].Verified);
    }

    [Fact]
    public void ParseSearchResponse_EmptyData_ReturnsEmptyList()
    {
        var json = """{ "totalHits": 0, "data": [] }""";

        var results = NuGetSearchService.ParseSearchResponse(json);

        Assert.Empty(results);
    }

    [Fact]
    public void ParseSearchResponse_MissingOptionalFields_HandlesGracefully()
    {
        var json = """
        {
          "data": [
            {
              "id": "SomePackage",
              "version": "1.0.0"
            }
          ]
        }
        """;

        var results = NuGetSearchService.ParseSearchResponse(json);

        Assert.Single(results);
        Assert.Equal("SomePackage", results[0].PackageId);
        Assert.Equal("1.0.0", results[0].Version);
        Assert.Null(results[0].Description);
        Assert.Equal(0, results[0].TotalDownloads);
        Assert.False(results[0].Verified);
    }

    [Fact]
    public void ParseSearchResponse_MalformedJson_ReturnsEmptyList()
    {
        var results = NuGetSearchService.ParseSearchResponse("not json");

        Assert.Empty(results);
    }

    [Fact]
    public void ParseSearchResponse_NoDataProperty_ReturnsEmptyList()
    {
        var json = """{ "totalHits": 5 }""";

        var results = NuGetSearchService.ParseSearchResponse(json);

        Assert.Empty(results);
    }
}
