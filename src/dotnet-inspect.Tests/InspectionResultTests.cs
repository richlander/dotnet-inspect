using DotnetInspector;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for InspectionResult computed properties and value formatters.
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
    public void CompactNumberFormatter_FormatsCorrectly(long downloads, string expected)
    {
        var formatter = new CompactNumberFormatter();

        Assert.Equal(expected, formatter.Format(downloads));
    }

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(1_024, "1 KB")]
    [InlineData(1_536, "1.5 KB")]
    [InlineData(1_048_576, "1 MB")]
    [InlineData(2_621_440, "2.5 MB")]
    [InlineData(1_073_741_824, "1 GB")]
    public void ByteSizeFormatter_FormatsCorrectly(long bytes, string expected)
    {
        var formatter = new ByteSizeFormatter();

        Assert.Equal(expected, formatter.Format(bytes));
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
