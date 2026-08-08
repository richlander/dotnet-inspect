using System.Text;
using System.Text.Json;
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
    public async Task GetSearchResponseAsync_MalformedJson_Throws()
    {
        // A swallowed parse failure is indistinguishable from a zero-result search,
        // so the failure propagates instead of being reported as an absent response.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not json"));
        await Assert.ThrowsAsync<JsonException>(async () =>
            await NuGetApi.GetSearchResponseAsync(stream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSearchResponseAsync_AzureDevOpsStringTotalHits_ParsesData()
    {
        // Azure DevOps serialises totalHits as a string and reports "0" alongside a
        // populated data array. Modelling the field at all rejected the whole document.
        // Issue #3417.
        //
        // The payload is a real Azure DevOps Artifacts response captured off the wire,
        // reduced only by sanitising the package identifiers. Keeping the members this
        // host actually sends is the point: nuget.org sends none of "@context",
        // "lastReopen" or "index", nor the empty "@id"/"registration"/"iconUrl" and the
        // null "projectUrl"/"summary"/"title" on each hit. A fixture trimmed to the
        // members the model happens to bind today would still pass if an unbound member
        // later became bound to a non-nullable type, which is exactly how this feed
        // broke the first time.
        //
        // Note "downloads" arrives as a real JSON number even on this host, so the
        // string serialisation is specific to totalHits rather than general to the feed.
        string json = """
        {
            "@context": {"@vocab": "http://schema.nuget.org/schema#"},
            "data": [
                {
                    "@id": "",
                    "@type": "Package",
                    "id": "Contoso.Internal.Core",
                    "version": "9.0.0",
                    "description": "Contoso.Internal.Core",
                    "versions": [{"@id": "Contoso.Internal.Core", "downloads": 0, "version": "9.0.0"}],
                    "authors": [],
                    "iconUrl": "",
                    "licenseUrl": "",
                    "projectUrl": null,
                    "registration": "",
                    "summary": null,
                    "tags": [],
                    "title": null
                },
                {
                    "@id": "",
                    "@type": "Package",
                    "id": "Contoso.Internal.Auth",
                    "version": "13.4.0-preview.6",
                    "description": "Contoso.Internal.Auth",
                    "versions": [{"@id": "Contoso.Internal.Auth", "downloads": 0, "version": "13.4.0-preview.6"}],
                    "authors": [],
                    "iconUrl": "",
                    "licenseUrl": "",
                    "projectUrl": null,
                    "registration": "",
                    "summary": null,
                    "tags": [],
                    "title": null
                }
            ],
            "lastReopen": "2026-07-29T01:31:47.7885829Z",
            "index": "PackageIndex",
            "totalHits": "0"
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await NuGetApi.GetSearchResponseAsync(stream, TestContext.Current.CancellationToken);

        // Both hits survive a response whose own count claims there are none, which is
        // the property that matters: Data is load bearing and totalHits is not merely
        // mistyped on this host, it is wrong.
        Assert.NotNull(result);
        Assert.Equal(2, result.Data.Count);
        Assert.Equal("Contoso.Internal.Core", result.Data[0].Id);
        Assert.Equal("9.0.0", result.Data[0].Version);
        Assert.Equal("Contoso.Internal.Auth", result.Data[1].Id);
        Assert.Equal("13.4.0-preview.6", result.Data[1].Version);
    }

    [Fact]
    public async Task GetSearchResponseAsync_StringSerialisedCounts_Parse()
    {
        // Azure DevOps proved a feed will spell a count as a string. Dropping totalHits
        // from the model answered that field and only that field; totalDownloads and
        // versions[].downloads are still counts, and both are Int64 -- the width most
        // often serialised as a string to keep it out of a JavaScript double. A feed
        // spelling either that way used to fail the whole document, taking every result
        // with it. Issue #3417.
        string json = """
        {
            "data": [
                {
                    "id": "Contoso.Internal",
                    "version": "1.2.3",
                    "totalDownloads": "9007199254740993",
                    "versions": [{"version": "1.2.3", "downloads": "42"}]
                }
            ],
            "totalHits": "0"
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await NuGetApi.GetSearchResponseAsync(stream, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        SearchResult hit = Assert.Single(result.Data);

        // Larger than 2^53, so this also pins that the value survives as an Int64 rather
        // than going through a double on the way in -- which is the reason a feed spells
        // it as a string in the first place.
        Assert.Equal(9007199254740993, hit.TotalDownloads);
        Assert.Equal(42, Assert.Single(hit.Versions!).Downloads);
    }

    [Fact]
    public async Task GetSearchResponseAsync_NumericCounts_StillParse()
    {
        // The other direction: nuget.org sends counts as numbers, and tolerating the
        // string spelling must not cost that.
        string json = """
        {"data":[{"id":"Newtonsoft.Json","version":"13.0.3","totalDownloads":5000000,
          "versions":[{"version":"13.0.3","downloads":7}]}]}
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await NuGetApi.GetSearchResponseAsync(stream, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        SearchResult hit = Assert.Single(result.Data);
        Assert.Equal(5000000, hit.TotalDownloads);
        Assert.Equal(7, Assert.Single(hit.Versions!).Downloads);
    }

    [Fact]
    public async Task GetVersionIndexAsync_MalformedJson_ReturnsNull()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{broken"));
        var result = await NuGetApi.GetVersionIndexAsync(stream, TestContext.Current.CancellationToken);
        Assert.Null(result);
    }
}
