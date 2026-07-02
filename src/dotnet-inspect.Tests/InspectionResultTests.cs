using DotnetInspector.Models;
using DotnetInspector.Inspectors;
using DotnetInspector.Views;
using DotnetInspector;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using Markout;

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
    public void Tfm_SelectsCorrectFramework(string[] frameworks, string expected)
    {
        var result = new InspectionResult { TargetFrameworks = frameworks.ToList() };

        Assert.Equal(expected, result.Tfm);
    }

    [Fact]
    public void Tfm_EmptyList_ReturnsNull()
    {
        var result = new InspectionResult { TargetFrameworks = [] };

        Assert.Null(result.Tfm);
    }

    [Fact]
    public void Tfm_NullList_ReturnsNull()
    {
        var result = new InspectionResult { TargetFrameworks = null };

        Assert.Null(result.Tfm);
    }

    [Fact]
    public void PackageInfo_RendersHighestTfmAndTfmCount()
    {
        var result = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TargetFrameworks = ["net8.0", "net10.0", "netstandard2.0"]
        };

        var output = MarkoutSerializer.Serialize(new InspectionResultView(result), InspectionContext.Default);

        Assert.Contains("| Highest TFM | net10.0 |", output);
        Assert.Contains("| TFM Count | 3 |", output);
        Assert.Contains("## Target Frameworks", output);
        Assert.Contains("| TFM |", output);
        Assert.DoesNotContain("| TFM | net10.0 |", output);
        Assert.DoesNotContain("| Target Frameworks | 3 |", output);
    }

    [Fact]
    public void PackageInfo_RendersNuspecMetadataDetails()
    {
        var result = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            License = "MIT",
            LicenseUrl = "https://licenses.nuget.org/MIT",
            Repository = "https://github.com/example/test",
            RepositoryType = "git",
            RepositoryCommit = "abcdef",
            HasReadme = true,
            ReadmeFile = "docs/README.md"
        };

        var output = MarkoutSerializer.Serialize(new InspectionResultView(result), InspectionContext.Default);

        Assert.Contains("| License | MIT |", output);
        Assert.Contains("| License URL | https://licenses.nuget.org/MIT |", output);
        Assert.Contains("| Repository | https://github.com/example/test |", output);
        Assert.Contains("| Repository Type | git |", output);
        Assert.Contains("| Repository Commit | abcdef |", output);
        Assert.Contains("| Readme | docs/README.md |", output);
    }

    [Fact]
    public void PackageInfo_RendersFieldsAlphabetically()
    {
        var result = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            Authors = "authors",
            ContentDirectories = ["lib", "analyzers"],
            License = "MIT",
            TargetFrameworks = ["net8.0", "net10.0"]
        };

        var output = MarkoutSerializer.Serialize(new InspectionResultView(result), InspectionContext.Default);
        var packageInfo = output[
            output.IndexOf("## Package Info", StringComparison.Ordinal)..
            output.IndexOf("## Target Frameworks", StringComparison.Ordinal)];

        Assert.True(
            packageInfo.IndexOf("| Authors |", StringComparison.Ordinal) < packageInfo.IndexOf("| Content |", StringComparison.Ordinal)
            && packageInfo.IndexOf("| Content |", StringComparison.Ordinal) < packageInfo.IndexOf("| Highest TFM |", StringComparison.Ordinal)
            && packageInfo.IndexOf("| Highest TFM |", StringComparison.Ordinal) < packageInfo.IndexOf("| License |", StringComparison.Ordinal)
            && packageInfo.IndexOf("| License |", StringComparison.Ordinal) < packageInfo.IndexOf("| Version |", StringComparison.Ordinal),
            output);
    }

    [Fact]
    public void Manifest_RendersManifestVersionWhenPresent()
    {
        var result = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            ManifestVersion = "2013/05"
        };

        var output = MarkoutSerializer.Serialize(new InspectionResultView(result), InspectionContext.Default);

        Assert.Contains("| Info | Manifest Version | 2013/05 | n/a |", output);
    }

    [Theory]
    [InlineData("net6.0", "No")]
    [InlineData("net8.0", "Yes")]
    [InlineData("net10.0", "Yes")]
    [InlineData("netstandard2.0", "Yes")]
    [InlineData("netstandard2.1", "Yes")]
    [InlineData("netstandard1.6", "No")]
    [InlineData("net472", "No")]
    public async Task PackageSignals_SupportedTfm_IsBooleanFromHighestTfm(string tfm, string expected)
    {
        var result = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TargetFrameworks = [tfm]
        };

        await AuditSignalBuilder.PopulatePackageAuditAsync(
            result, new HttpClient(), new VerboseLogger(false));

        var signal = Assert.Single(result.AuditSignals!, s => s.Signal == "Supported TFM");
        Assert.Equal(expected, signal.Value);
        Assert.Equal(tfm, signal.Evidence);
    }

    [Fact]
    public async Task PackageSignals_Symbols_ReportsMsdlPdbSource()
    {
        var result = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            BinarySignals = new PackageBinarySignals
            {
                TotalBinaries = 5,
                SymbolsAvailable = 5,
                SourceLinkAvailable = 5,
                MsdlPdbs = 5,
                MsdlSourceLinkPdbs = 5
            }
        };

        await AuditSignalBuilder.PopulatePackageAuditAsync(
            result, new HttpClient(), new VerboseLogger(false));

        var symbols = Assert.Single(result.AuditSignals!, s => s.Signal == "Symbols");
        Assert.Equal("Yes (5/5)", symbols.Value);
        Assert.Equal("PDBs from msdl.microsoft.com", symbols.Evidence);

        var sourceLink = Assert.Single(result.AuditSignals!, s => s.Signal == "SourceLink");
        Assert.Equal("SourceLink data found in PDBs from msdl.microsoft.com", sourceLink.Evidence);
    }

    [Fact]
    public async Task PackageSignals_Symbols_ReportsMixedPdbSources()
    {
        var result = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            BinarySignals = new PackageBinarySignals
            {
                TotalBinaries = 4,
                SymbolsAvailable = 4,
                EmbeddedPdbs = 1,
                InPackagePdbs = 1,
                SnupkgPdbs = 1,
                MsdlPdbs = 1
            }
        };

        await AuditSignalBuilder.PopulatePackageAuditAsync(
            result, new HttpClient(), new VerboseLogger(false));

        var symbols = Assert.Single(result.AuditSignals!, s => s.Signal == "Symbols");
        Assert.Equal("embedded PDBs (1), PDBs from package (1), PDBs from .snupkg (1), PDBs from msdl.microsoft.com (1)", symbols.Evidence);
    }

    [Fact]
    public async Task PackageSignals_Symbols_ReportsNoPdbsWhenUnavailable()
    {
        var result = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            BinarySignals = new PackageBinarySignals
            {
                TotalBinaries = 5,
                SymbolsAvailable = 0,
                SourceLinkAvailable = 0
            }
        };

        await AuditSignalBuilder.PopulatePackageAuditAsync(
            result, new HttpClient(), new VerboseLogger(false));

        var symbols = Assert.Single(result.AuditSignals!, s => s.Signal == "Symbols");
        Assert.Equal("No (0/5)", symbols.Value);
        Assert.Equal("no PDBs available", symbols.Evidence);
    }

    [Fact]
    public void PackageType_Library_WhenNotTool()
    {
        var result = new InspectionResult { IsToolPackage = false };

        Assert.Equal("Library", new InspectionResultView(result).PackageType);
    }

    [Fact]
    public void PackageType_Tool_WhenIsToolPackage()
    {
        var result = new InspectionResult { IsToolPackage = true };

        Assert.Equal("Tool", new InspectionResultView(result).PackageType);
    }

    [Fact]
    public void PackageType_ToolV2_WhenVersion2ToolFormat()
    {
        var result = new InspectionResult 
        { 
            IsToolPackage = true,
            ToolFormat = "DotNetCliTool Version=\"2\" (RID-specific)"
        };

        Assert.Equal("Tool v2", new InspectionResultView(result).PackageType);
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

        var display = new InspectionResultView(result).VulnerabilitiesDisplay;
        Assert.NotNull(display);
        Assert.Contains("3 known", display);
        Assert.Contains("High", display);
        Assert.Contains("Critical", display);
    }

    [Fact]
    public void VulnerabilitiesDisplay_NoVulnerabilities_ReturnsNull()
    {
        var result = new InspectionResult { Vulnerabilities = null };

        Assert.Null(new InspectionResultView(result).VulnerabilitiesDisplay);
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

        var flat = new InspectionResultView(result).FlatDependencies;
        Assert.NotNull(flat);
        Assert.Equal(2, flat.Count);
        Assert.Equal("net8.0", flat[0].TargetFramework);
        Assert.Equal("netstandard2.0", flat[1].TargetFramework);
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

        var flat = new InspectionResultView(result).FlatDependencies;
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
