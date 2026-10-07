using System.Text.Json;
using System.Runtime.Versioning;
using DotnetInspect.Web.Interop.Package;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class EcosystemPackageExportsTests
{
    [Theory]
    [InlineData("System.Linq", "4.3.0", true)]
    [InlineData("System.Text.Json", "9.0.0", true)]
    [InlineData("System.Text.Json", "99.0.0", false)]
    [InlineData("System.Text.Json", null, null)]
    public void ProductionInventoryPreservesVersionSpecificPruning(string id, string? version, bool? expected)
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot(), "inspect-web", "assets", "platform-index.json")));
        var target = catalog.RootElement.GetProperty("targets").EnumerateArray()
            .Single(value => value.GetProperty("tfm").GetString() == "net10.0");
        var results = Classify(id, version, "net10.0", target.GetRawText());
        Assert.Equal("ecosystem.runtime", results[0].EcosystemId);
        Assert.Equal("DotNetRuntime", results[0].PlatformLayer);
        Assert.Equal(expected, results[0].IsPruned);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"tfm\":\"net10.0\",\"version\":\"10.0.12\",\"supplies\":null}")]
    [InlineData("{\"tfm\":\"net9.0\",\"version\":\"9.0.0\",\"supplies\":[]}")]
    public void UnavailableInventoryRetainsRecognizedOwnerWithoutBadge(string inventory)
    {
        var results = Classify("System.Text.Json", "9.0.0", "net10.0", inventory);
        Assert.Equal("ecosystem.runtime", results[0].EcosystemId);
        Assert.Null(results[0].PlatformLayer);
        Assert.Null(results[0].IsPruned);
    }

    [Fact]
    public void NonEcosystemAndUnsupportedFamilyDoNotInventEvidence()
    {
        Assert.Null(Classify("Example.External", "1.0.0", "net10.0", "null")[0].EcosystemId);
        Assert.Throws<ArgumentException>(() => Classify("Example.External", "1.0.0", "net10.0",
            "{\"tfm\":\"net10.0\",\"version\":\"10.0.12\",\"supplies\":[{\"family\":\"Unknown\",\"package\":\"Example.External\",\"version\":\"1.0.0\"}]}"));
    }

    static BrowserEcosystemPackageClassification[] Classify(string id, string? version, string tfm, string inventory)
    {
        var json = PackageExports.ClassifyEcosystemPackages(tfm,
            JsonSerializer.Serialize(new[] { new BrowserEcosystemPackageCandidate(id, version) },
                BrowserPackageJsonContext.Default.BrowserEcosystemPackageCandidateArray), inventory);
        return JsonSerializer.Deserialize(json,
            BrowserPackageJsonContext.Default.BrowserEcosystemPackageClassificationArray)!;
    }

    static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
