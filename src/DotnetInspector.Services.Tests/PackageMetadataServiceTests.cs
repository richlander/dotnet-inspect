using System.Text.Json;
using DotnetInspector.Core;

namespace DotnetInspector.Services.Tests;

[Collection(CoreCacheCollection.Name)]
public class PackageMetadataServiceTests : IDisposable
{
    public PackageMetadataServiceTests()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        CoreCache.Clear("metadata");
    }

    public void Dispose()
    {
        CoreCache.Clear("metadata");
    }

    [Fact]
    public void ParseDeprecation_WithAllFields()
    {
        var json = """
        {
            "reasons": ["Legacy", "CriticalBugs"],
            "message": "Use the new package instead",
            "alternatePackage": { "id": "NewPackage" }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var deprecation = PackageMetadataService.ParseDeprecation(doc.RootElement);

        Assert.Equal(2, deprecation.Reasons!.Count);
        Assert.Contains("Legacy", deprecation.Reasons);
        Assert.Contains("CriticalBugs", deprecation.Reasons);
        Assert.Equal("Use the new package instead", deprecation.Message);
        Assert.Equal("NewPackage", deprecation.AlternatePackageId);
    }

    [Fact]
    public void ParseDeprecation_ReasonsOnly()
    {
        var json = """{ "reasons": ["Other"] }""";

        using var doc = JsonDocument.Parse(json);
        var deprecation = PackageMetadataService.ParseDeprecation(doc.RootElement);

        Assert.Single(deprecation.Reasons!);
        Assert.Equal("Other", deprecation.Reasons![0]);
        Assert.Null(deprecation.Message);
        Assert.Null(deprecation.AlternatePackageId);
    }

    [Fact]
    public void ParseDeprecation_EmptyObject()
    {
        var json = """{}""";

        using var doc = JsonDocument.Parse(json);
        var deprecation = PackageMetadataService.ParseDeprecation(doc.RootElement);

        Assert.Null(deprecation.Reasons);
        Assert.Null(deprecation.Message);
        Assert.Equal("Deprecated", deprecation.Summary);
    }

    [Fact]
    public void PackageDeprecation_Summary_WithReasons()
    {
        var dep = new PackageDeprecation
        {
            Reasons = ["Legacy"],
            AlternatePackageId = "NewPkg"
        };
        Assert.Equal("Legacy - use NewPkg", dep.Summary);
    }

    [Fact]
    public void PackageDeprecation_Summary_WithMessage()
    {
        var dep = new PackageDeprecation
        {
            Reasons = ["CriticalBugs"],
            Message = "Security issue"
        };
        Assert.Equal("CriticalBugs - Security issue", dep.Summary);
    }

    [Fact]
    public void PackageDeprecation_Summary_Empty()
    {
        var dep = new PackageDeprecation();
        Assert.Equal("Deprecated", dep.Summary);
    }

    [Theory]
    [InlineData("1.0.0", "[1.0.0, 2.0.0)", true)]
    [InlineData("2.0.0", "[1.0.0, 2.0.0)", false)]
    [InlineData("1.5.0", "[1.0.0, 2.0.0)", true)]
    [InlineData("0.9.0", "[1.0.0, 2.0.0)", false)]
    [InlineData("1.0.0", "1.0.0", true)]
    public void IsVersionInRange_VariousRanges(string version, string range, bool expected)
    {
        var nugetVersion = NuGet.Versioning.NuGetVersion.Parse(version);
        Assert.Equal(expected, PackageMetadataService.IsVersionInRange(nugetVersion, range));
    }

    [Theory]
    [InlineData(0, "Low")]
    [InlineData(1, "Moderate")]
    [InlineData(2, "High")]
    [InlineData(3, "Critical")]
    [InlineData(99, "Unknown")]
    public void SeverityToString_MapsCorrectly(int severity, string expected)
    {
        Assert.Equal(expected, PackageMetadataService.SeverityToString(severity));
    }

    [Fact]
    public void PackageMetadata_DefaultValues()
    {
        var metadata = new PackageMetadata();

        Assert.Null(metadata.Published);
        Assert.Null(metadata.TotalDownloads);
        Assert.Null(metadata.VersionDownloads);
        Assert.Null(metadata.VersionCount);
        Assert.Null(metadata.PackageSize);
        Assert.Null(metadata.IsVerified);
        Assert.Null(metadata.Owners);
        Assert.Null(metadata.Deprecation);
        Assert.Null(metadata.Vulnerabilities);
    }

    [Fact]
    public async Task GetPublishedDateAsync_WithCachedMetadata_DoesNotFetch()
    {
        var published = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        MetadataFieldCache.Set("testpackage@1.0.0", new PackageMetadata { Published = published });

        var result = await PackageMetadataService.GetPublishedDateAsync(
            new HttpClient(new FailingHandler()), "TestPackage", "1.0.0", log: null);

        Assert.Equal(published, result);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException($"Network access not allowed in test: {request.RequestUri}");
        }
    }
}
