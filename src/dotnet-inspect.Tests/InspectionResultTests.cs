using DotnetInspector;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for InspectionResult formatting and computed properties.
/// </summary>
public class InspectionResultTests
{
    [Theory]
    [InlineData(500, "500")]
    [InlineData(1_000, "1K")]
    [InlineData(1_500, "1.5K")]
    [InlineData(1_000_000, "1M")]
    [InlineData(2_500_000, "2.5M")]
    [InlineData(1_000_000_000, "1B")]
    [InlineData(5_100_000_000, "5.1B")]
    public void DownloadsDisplay_FormatsCorrectly(long downloads, string expected)
    {
        var result = new InspectionResult { TotalDownloads = downloads };

        Assert.Equal(expected, result.DownloadsDisplay);
    }

    [Fact]
    public void DownloadsDisplay_NullDownloads_ReturnsNull()
    {
        var result = new InspectionResult { TotalDownloads = null };

        Assert.Null(result.DownloadsDisplay);
    }

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(1_024, "1 KB")]
    [InlineData(1_536, "1.5 KB")]
    [InlineData(1_048_576, "1 MB")]
    [InlineData(2_621_440, "2.5 MB")]
    [InlineData(1_073_741_824, "1 GB")]
    public void PackageSizeDisplay_FormatsCorrectly(long bytes, string expected)
    {
        var result = new InspectionResult { PackageSize = bytes };

        Assert.Equal(expected, result.PackageSizeDisplay);
    }

    [Fact]
    public void PackageSizeDisplay_NullSize_ReturnsNull()
    {
        var result = new InspectionResult { PackageSize = null };

        Assert.Null(result.PackageSizeDisplay);
    }

    [Theory]
    [InlineData(new[] { "net8.0" }, "net8.0")]
    [InlineData(new[] { "net8.0", "net9.0" }, "net9.0")]
    [InlineData(new[] { "netstandard2.0", "net8.0" }, "net8.0")]
    [InlineData(new[] { "net462", "netstandard2.0" }, "netstandard2.0")]
    [InlineData(new[] { "netcoreapp3.1", "net5.0" }, "net5.0")]
    [InlineData(new[] { "net462", "net472", "netstandard2.0", "net8.0" }, "net8.0")]
    public void NewestTfm_SelectsCorrectFramework(string[] frameworks, string expected)
    {
        var result = new InspectionResult { TargetFrameworks = frameworks.ToList() };

        Assert.Equal(expected, result.NewestTfm);
    }

    [Fact]
    public void NewestTfm_EmptyList_ReturnsNull()
    {
        var result = new InspectionResult { TargetFrameworks = [] };

        Assert.Null(result.NewestTfm);
    }

    [Fact]
    public void NewestTfm_NullList_ReturnsNull()
    {
        var result = new InspectionResult { TargetFrameworks = null };

        Assert.Null(result.NewestTfm);
    }

    [Fact]
    public void PackageType_Library_WhenNotTool()
    {
        var result = new InspectionResult { IsToolPackage = false };

        Assert.Equal("Library", result.PackageType);
    }

    [Fact]
    public void PackageType_Tool_WhenIsToolPackage()
    {
        var result = new InspectionResult { IsToolPackage = true };

        Assert.Equal("Tool", result.PackageType);
    }

    [Fact]
    public void PackageType_ToolV2_WhenVersion2ToolFormat()
    {
        var result = new InspectionResult 
        { 
            IsToolPackage = true,
            ToolFormat = "DotNetCliTool Version=\"2\" (RID-specific)"
        };

        Assert.Equal("Tool v2", result.PackageType);
    }

    [Fact]
    public void OwnersDisplay_FormatsAsList()
    {
        var result = new InspectionResult 
        { 
            Owners = ["owner1", "owner2", "owner3"]
        };

        Assert.Equal("owner1, owner2, owner3", result.OwnersDisplay);
    }

    [Fact]
    public void OwnersDisplay_EmptyList_ReturnsNull()
    {
        var result = new InspectionResult { Owners = [] };

        Assert.Null(result.OwnersDisplay);
    }

    [Fact]
    public void VulnerabilitiesDisplay_ShowsCountAndSeverities()
    {
        var result = new InspectionResult 
        { 
            Vulnerabilities = 
            [
                new PackageVulnerability { Severity = "High", CveId = "CVE-2024-001" },
                new PackageVulnerability { Severity = "Critical", CveId = "CVE-2024-002" },
                new PackageVulnerability { Severity = "High", CveId = "CVE-2024-003" }
            ]
        };

        var display = result.VulnerabilitiesDisplay;
        Assert.NotNull(display);
        Assert.Contains("3 known", display);
        Assert.Contains("High", display);
        Assert.Contains("Critical", display);
    }

    [Fact]
    public void VulnerabilitiesDisplay_NoVulnerabilities_ReturnsNull()
    {
        var result = new InspectionResult { Vulnerabilities = null };

        Assert.Null(result.VulnerabilitiesDisplay);
    }

    [Fact]
    public void TargetFrameworksSummary_JoinsWithComma()
    {
        var result = new InspectionResult 
        { 
            TargetFrameworks = ["net8.0", "netstandard2.0"]
        };

        Assert.Equal("net8.0, netstandard2.0", result.TargetFrameworksSummary);
    }

    [Fact]
    public void SupportedRidsSummary_JoinsWithComma()
    {
        var result = new InspectionResult 
        { 
            SupportedRids = ["win-x64", "linux-x64", "osx-arm64"]
        };

        Assert.Equal("win-x64, linux-x64, osx-arm64", result.SupportedRidsSummary);
    }

    [Fact]
    public void ToolCommandsSummary_JoinsWithComma()
    {
        var result = new InspectionResult 
        { 
            ToolCommands = ["dotnet-tool", "other-command"]
        };

        Assert.Equal("dotnet-tool, other-command", result.ToolCommandsSummary);
    }

    [Fact]
    public void FlatDependencies_FlattensGroupsCorrectly()
    {
        var result = new InspectionResult
        {
            DependencyGroups = 
            [
                new DependencyGroup 
                { 
                    TargetFramework = "net8.0",
                    Dependencies = 
                    [
                        new PackageDependency { Id = "System.Text.Json", Version = "8.0.0" }
                    ]
                },
                new DependencyGroup 
                { 
                    TargetFramework = "netstandard2.0",
                    Dependencies = 
                    [
                        new PackageDependency { Id = "Newtonsoft.Json", Version = "13.0.1" }
                    ]
                }
            ]
        };

        var flat = result.FlatDependencies;
        Assert.NotNull(flat);
        Assert.Equal(2, flat.Count);
        // netstandard should come first (lower TFM priority)
        Assert.Equal("netstandard2.0", flat[0].TargetFramework);
        Assert.Equal("net8.0", flat[1].TargetFramework);
    }

    [Fact]
    public void FlatDependencies_SortsDependenciesById()
    {
        var result = new InspectionResult
        {
            DependencyGroups = 
            [
                new DependencyGroup 
                { 
                    TargetFramework = "net8.0",
                    Dependencies = 
                    [
                        new PackageDependency { Id = "Zebra", Version = "1.0.0" },
                        new PackageDependency { Id = "Alpha", Version = "1.0.0" },
                        new PackageDependency { Id = "Middle", Version = "1.0.0" }
                    ]
                }
            ]
        };

        var flat = result.FlatDependencies;
        Assert.NotNull(flat);
        Assert.Equal(3, flat.Count);
        Assert.Equal("Alpha", flat[0].Id);
        Assert.Equal("Middle", flat[1].Id);
        Assert.Equal("Zebra", flat[2].Id);
    }

    [Fact]
    public void PublishedDisplay_FormatsDateCorrectly()
    {
        var result = new InspectionResult 
        { 
            Published = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero)
        };

        Assert.Equal("2024-06-15", result.PublishedDisplay);
    }

    [Fact]
    public void PublishedDisplay_NullDate_ReturnsNull()
    {
        var result = new InspectionResult { Published = null };

        Assert.Null(result.PublishedDisplay);
    }

    [Fact]
    public void ContentSummary_JoinsDirectories()
    {
        var result = new InspectionResult 
        { 
            ContentDirectories = ["lib", "tools", "runtimes"]
        };

        Assert.Equal("lib, tools, runtimes", result.ContentSummary);
    }

    [Fact]
    public void PackageDeprecation_Summary_CombinesParts()
    {
        var deprecation = new PackageDeprecation
        {
            Reasons = ["Legacy", "CriticalBugs"],
            AlternatePackageId = "NewPackage",
            Message = "Please migrate"
        };

        var summary = deprecation.Summary;
        Assert.Contains("Legacy", summary);
        Assert.Contains("CriticalBugs", summary);
        Assert.Contains("use NewPackage", summary);
        Assert.Contains("Please migrate", summary);
    }

    [Fact]
    public void PackageDeprecation_Summary_EmptyReasons_StillWorks()
    {
        var deprecation = new PackageDeprecation
        {
            AlternatePackageId = "NewPackage"
        };

        var summary = deprecation.Summary;
        Assert.Equal("use NewPackage", summary);
    }

    [Fact]
    public void PackageDeprecation_Summary_NoDetails_ReturnsDeprecated()
    {
        var deprecation = new PackageDeprecation();

        Assert.Equal("Deprecated", deprecation.Summary);
    }
}
