using System.Text;
using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

/// <summary>
/// Tests for NuGetApi stream-based JSON deserialization.
/// Includes resilience tests ported from dotnet-inspect.
/// </summary>
public class NuGetApiTests
{
    [Fact]
    public async Task GetVersionIndexAsync_ValidJson()
    {
        string json = """{"versions":["1.0.0","2.0.0","3.0.0-preview.1"]}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await NuGetApi.GetVersionIndexAsync(stream, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(3, result.Versions.Count);
        Assert.Equal("1.0.0", result.Versions[0]);
        Assert.Equal("3.0.0-preview.1", result.Versions[2]);
    }

    [Fact]
    public async Task GetVersionIndexAsync_EmptyVersions()
    {
        string json = """{"versions":[]}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await NuGetApi.GetVersionIndexAsync(stream, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Empty(result.Versions);
    }

    [Fact]
    public async Task GetServiceIndexAsync_ValidJson()
    {
        string json = """
        {
            "version": "3.0.0",
            "resources": [
                {"@id": "https://api.nuget.org/v3-flatcontainer/", "@type": "PackageBaseAddress/3.0.0"}
            ]
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await NuGetApi.GetServiceIndexAsync(stream, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Resources);
        Assert.Equal("PackageBaseAddress/3.0.0", result.Resources[0].Type);
    }

    [Fact]
    public async Task GetServiceIndexAsync_FindsPackageBaseAddress()
    {
        string json = """
        {
            "version": "3.0.0",
            "resources": [
                {"@id": "https://example.com/search", "@type": "SearchQueryService"},
                {"@id": "https://example.com/flatcontainer/", "@type": "PackageBaseAddress/3.0.0"},
                {"@id": "https://example.com/registration/", "@type": "RegistrationsBaseUrl"}
            ]
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await NuGetApi.GetServiceIndexAsync(stream, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        var packageBase = result.Resources.FirstOrDefault(r => r.Type.StartsWith("PackageBaseAddress"));
        Assert.NotNull(packageBase);
        Assert.Equal("https://example.com/flatcontainer/", packageBase.Id);
    }

    // --- Search response deserialization (ported from dotnet-inspect NuGetSearchServiceTests) ---

    [Fact]
    public async Task GetSearchResponseAsync_FullPayload()
    {
        string json = """
        {
            "totalHits": 3,
            "data": [
                {
                    "id": "Azure.AI.OpenAI",
                    "version": "2.1.0",
                    "description": "Azure OpenAI client library",
                    "totalDownloads": 5000000,
                    "verified": true,
                    "versions": [
                        {"version": "2.0.0", "@id": ""},
                        {"version": "2.1.0", "@id": ""}
                    ]
                },
                {
                    "id": "Azure.AI.TextAnalytics",
                    "version": "5.3.0",
                    "description": "Azure Text Analytics client",
                    "totalDownloads": 2000000,
                    "verified": true,
                    "versions": []
                },
                {
                    "id": "Azure.AI.FormRecognizer",
                    "version": "4.1.0",
                    "description": "Azure Form Recognizer client",
                    "totalDownloads": 1000000,
                    "verified": false,
                    "versions": []
                }
            ]
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await NuGetApi.GetSearchResponseAsync(stream, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(3, result.Data.Count);

        Assert.Equal("Azure.AI.OpenAI", result.Data[0].Id);
        Assert.Equal("2.1.0", result.Data[0].Version);
        Assert.Equal("Azure OpenAI client library", result.Data[0].Description);
        Assert.Equal(5_000_000, result.Data[0].TotalDownloads);
        Assert.True(result.Data[0].Verified);
        Assert.Equal(2, result.Data[0].Versions!.Count);

        Assert.Equal("Azure.AI.TextAnalytics", result.Data[1].Id);
        Assert.False(result.Data[2].Verified);
    }

    [Fact]
    public async Task GetSearchResponseAsync_EmptyData()
    {
        string json = """{"totalHits":0,"data":[]}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await NuGetApi.GetSearchResponseAsync(stream, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task GetSearchResponseAsync_MissingOptionalFields()
    {
        string json = """
        {
            "data": [
                {
                    "id": "SomePackage",
                    "version": "1.0.0"
                }
            ]
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await NuGetApi.GetSearchResponseAsync(stream, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Data);
        Assert.Equal("SomePackage", result.Data[0].Id);
        Assert.Equal("1.0.0", result.Data[0].Version);
        Assert.Null(result.Data[0].Description);
        Assert.Equal(0, result.Data[0].TotalDownloads);
        Assert.False(result.Data[0].Verified);
    }

    [Fact]
    public async Task GetSearchResponseAsync_MalformedJson_ReturnsNull()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not json"));
        var result = await NuGetApi.GetSearchResponseAsync(stream, TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetVersionIndexAsync_MalformedJson_ReturnsNull()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{broken"));
        var result = await NuGetApi.GetVersionIndexAsync(stream, TestContext.Current.CancellationToken);
        Assert.Null(result);
    }
}
