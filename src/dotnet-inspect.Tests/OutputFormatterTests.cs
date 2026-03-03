using DotnetInspector.Models;
using DotnetInspector.Views;
using DotnetInspector;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspector.Tests;

public class OutputFormatterTests
{
    [Fact]
    public void MultiAssemblyReport_HasSingleH1()
    {
        var report = CreateTestReport("Test.dll", false, "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.Single(output.Split('\n'), l => l.StartsWith("# "));
    }

    [Fact]
    public void SingleAssemblyAudit_HasSingleH1()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var output = Serialize(inspection);

        Assert.Single(output.Split('\n'), l => l.StartsWith("# "));
    }

    [Fact]
    public void MultiAssemblyReport_HasH2AssembliesSection()
    {
        var report = CreateTestReport("Test.dll", false, "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.Contains("## Libraries", output);
    }

    [Fact]
    public void MultiAssemblyReport_HasH3PerTfm()
    {
        var report = CreateTestReport("Test.dll", false, "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.Contains("### Test.dll (net9.0)", output);
        Assert.Contains("### Test.dll (net8.0)", output);
    }

    [Fact]
    public void MultiAssemblyReport_HasH4SectionsPerItem()
    {
        var report = CreateTestReport("Test.dll", false, "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.Contains("#### Library Info", output);
    }

    [Fact]
    public void MultiAssemblyReport_HasCompactLine()
    {
        var report = CreateTestReport("Test.dll", true, "net9.0", "net8.0");
        var output = Serialize(report);

        // AutoFieldsCount = 7 renders the first 7 scalar properties as a compact hero line
        Assert.Contains("Name: Test", output);
    }

    [Fact]
    public void MultiAssemblyReport_TitleFromPackageName()
    {
        var report = CreateTestReport("Test.dll", false, "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.StartsWith("# Test", output.TrimStart());
    }

    [Fact]
    public void SingleAudit_ExcludesSymbols_AtNormalVerbosity()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var pipeline = LibrarySections.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(inspection, Verbosity.Normal);
        var output = SerializeWithInclude(inspection, includeSections);

        Assert.DoesNotContain("## Symbols", output);
    }

    [Fact]
    public void SingleAudit_IncludesSymbols_AtDetailedVerbosity()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var pipeline = LibrarySections.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(inspection, Verbosity.Detailed);
        var output = SerializeWithInclude(inspection, includeSections);

        Assert.Contains("## Symbols", output);
    }

    [Fact]
    public void SingleAudit_MetadataIncludesDeterministic()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.IsDeterministic = true;
        inspection.HasReproducibleFlag = true;
        var output = Serialize(inspection);

        Assert.Contains("Deterministic", output);
        Assert.Contains("Reproducible", output);
    }

    private static LibraryInspection CreateTestAudit(string fileName, string? tfm)
    {
        return new LibraryInspection
        {
            FileName = fileName,
            FileType = "dll",
            Tfm = tfm,
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = Path.GetFileNameWithoutExtension(fileName),
                AssemblyVersion = "1.0.0.0",
                TargetFramework = tfm != null ? $".NETCoreApp,Version=v{tfm[3..]}" : null,
                Architecture = "AnyCPU"
            }
        };
    }

    private static LibraryInspectionReport CreateTestReport(string fileName, bool topFieldsOnly, params string[] tfms)
    {
        var inspections = tfms.Select(tfm => CreateTestAudit(fileName, tfm)).ToList();
        return new LibraryInspectionReport
        {
            Title = Path.GetFileNameWithoutExtension(fileName),
            Assemblies = inspections.Select(a => new LibraryInspectionView(a, topFieldsOnly)).ToList()
        };
    }

    private static string Serialize(LibraryInspectionReport report)
    {
        var context = new MarkoutContext();
        return context.Serialize(report).TrimEnd();
    }

    private static string Serialize(LibraryInspection inspection, bool topFieldsOnly = false)
    {
        var view = new LibraryInspectionView(inspection, topFieldsOnly);
        var context = new MarkoutContext();
        return context.Serialize(view).TrimEnd();
    }

    // ===== API Output Formatter Tests =====

    private static ApiSurface CreateTestApiSurface(int typeCount = 3)
    {
        var types = Enumerable.Range(1, typeCount).Select(i => new ApiType
        {
            Namespace = "TestLib",
            Name = $"Type{i}",
            Kind = "class",
            Members = [new ApiMember { Name = "Method1", Kind = "method", Signature = "void Method1()" }]
        }).ToList();

        return new ApiSurface
        {
            Name = "TestLib",
            Source = "NuGet",
            Version = "1.0.0",
            Tfm = "net10.0",
            Types = types,
            PublicTypeCount = types.Count,
            PublicMethodCount = types.Count,
            PublicPropertyCount = 0
        };
    }

    [Fact]
    public void ApiFullSurface_QuietMode_SuppressesTypeTables()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var output = RenderFullApi(api, options);

        Assert.Contains("Source: NuGet", output);
        Assert.DoesNotContain("## Classes", output);
        Assert.DoesNotContain("Type1", output);
    }

    [Fact]
    public void ApiFullSurface_MinimalMode_ShowsTypeTables()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Minimal };

        var output = RenderFullApi(api, options);

        Assert.Contains("## Classes", output);
        Assert.Contains("TestLib.Type1", output);
    }

    [Fact]
    public void ApiFullSurface_QuietWithTypeFilter_ShowsTypeTables()
    {
        var api = CreateTestApiSurface();
        // Glob upgrade: quiet + TypeFilter should behave as minimal
        var options = new TypeOptions
        {
            Verbosity = Verbosity.Minimal,  // caller upgrades quiet to minimal for globs
            TypeFilter = "Type1*"
        };

        var output = RenderFullApi(api, options);

        Assert.Contains("## Classes", output);
        Assert.Contains("TestLib.Type1", output);
    }

    [Fact]
    public void ApiFullSurface_SourceAndTfm_PresentInCompactLine()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var output = RenderFullApi(api, options);

        Assert.Contains("Source: NuGet", output);
        Assert.Contains("TFM: net10.0", output);
        Assert.Contains("Version: 1.0.0", output);
    }

    [Fact]
    public void TypeView_SourceAndTfm_PresentInCompactLine()
    {
        var type = new ApiType
        {
            Namespace = "TestLib",
            Name = "MyClass",
            Kind = "class",
            Members = [new ApiMember { Name = "Run", Kind = "method", Signature = "void Run()" }]
        };
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var output = ApiOutputFormatter.RenderTypeMarkdown(type, "TestLib", "TestLib", "1.0.0", "NuGet", "net10.0", options);

        Assert.Contains("Source: NuGet", output);
        Assert.Contains("TFM: net10.0", output);
    }

    [Fact]
    public void TypeView_NullSource_OmitsSourceField()
    {
        var type = new ApiType
        {
            Namespace = "TestLib",
            Name = "MyClass",
            Kind = "class",
            Members = []
        };
        var options = new ApiOptions { Verbosity = Verbosity.Minimal };

        var output = ApiOutputFormatter.RenderTypeMarkdown(type, "TestLib", null, null, null, null, options);

        Assert.DoesNotContain("Source:", output);
        Assert.DoesNotContain("TFM:", output);
    }

    // ===== Quiet Output Tests =====

    [Fact]
    public void LibraryQuiet_ThreeLines()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var pipeline = LibrarySections.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(
            inspection, Verbosity.Quiet);
        var output = SerializeWithInclude(inspection, includeSections, topFieldsOnly: true);
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.None);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("# ", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Contains("Name: ", lines[2]);
        Assert.Contains(" | ", lines[2]);
        Assert.DoesNotContain("## ", output);
    }

    [Fact]
    public void PackageQuiet_ThreeLines()
    {
        var result = CreateTestPackageResult();
        var view = new InspectionResultView(result);
        var context = new MarkoutContext(new MarkoutWriterOptions
        {
            IncludeSections = [PackageSections.Summary],
            IncludeDescription = false
        });
        var output = context.Serialize(view).TrimEnd();
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.None);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("# ", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Contains(" | ", lines[2]);
        Assert.DoesNotContain("## ", output);
    }

    [Fact]
    public void ApiQuiet_ThreeLines()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var output = RenderFullApi(api, options).TrimEnd();
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.None);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("# ", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Contains(" | ", lines[2]);
        Assert.DoesNotContain("## ", output);
    }

    private static string RenderFullApi(ApiSurface api, ApiOptions options)
    {
        var (view, truncatedCount) = ApiOutputFormatter.BuildFullApiView(api, options);
        var writerOptions = ApiOutputFormatter.BuildWriterOptions(api, options);
        var writer = new MarkoutWriter(new MarkdownFormatter(), writerOptions);
        new MarkoutContext().Serialize(view, writer);
        if (truncatedCount > 0)
            writer.WriteParagraph($"... *and {truncatedCount} more types*");
        return writer.ToString().TrimEnd();
    }

    private static string SerializeWithInclude(LibraryInspection inspection, HashSet<string>? includeSections, bool topFieldsOnly = false)
    {
        var view = new LibraryInspectionView(inspection, topFieldsOnly);
        var context = new MarkoutContext(new MarkoutWriterOptions
        {
            IncludeSections = includeSections
        });
        return context.Serialize(view).TrimEnd();
    }

    private static InspectionResult CreateTestPackageResult()
    {
        return new InspectionResult
        {
            PackageName = "TestPackage",
            Version = "1.0.0",
            PackageTypes = ["Library"],
            Published = DateTimeOffset.Parse("2025-01-15"),
        };
    }

    [Fact]
    public void LibraryCompactView_AllSourcePaths_ShowSameFields()
    {
        var modified = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var assemblyInfo = new AssemblyInfo
        {
            AssemblyName = "TestLib",
            AssemblyVersion = "10.0.0.0",
            TargetFramework = ".NETCoreApp,Version=v10.0",
            Architecture = "AnyCPU"
        };

        var platform = new LibraryInspection
        {
            FileName = "TestLib.dll",
            FileType = "dll",
            AssemblyInfo = assemblyInfo,
            FileSize = 1024,
            Source = SourceKind.Platform,
            PlatformVersion = "10.0.1",
            LastModified = modified
        };

        var nuget = new LibraryInspection
        {
            FileName = "TestLib.dll",
            FileType = "dll",
            AssemblyInfo = assemblyInfo,
            FileSize = 1024,
            Source = "NuGet",
            LastModified = modified
        };

        var file = new LibraryInspection
        {
            FileName = "TestLib.dll",
            FileType = "dll",
            AssemblyInfo = assemblyInfo,
            FileSize = 1024,
            Source = "File",
            LastModified = modified
        };

        var platformOutput = Serialize(platform, topFieldsOnly: true);
        var nugetOutput = Serialize(nuget, topFieldsOnly: true);
        var fileOutput = Serialize(file, topFieldsOnly: true);

        // Extract field names from compact line (format: "Name: value | Name: value | ...")
        static HashSet<string> ExtractFieldNames(string output)
        {
            var compactLine = output.Split('\n').First(l => l.Contains('|'));
            return compactLine.Split('|')
                .Select(f => f.Trim().Split(':')[0].Trim())
                .ToHashSet();
        }

        var platformFields = ExtractFieldNames(platformOutput);
        var nugetFields = ExtractFieldNames(nugetOutput);
        var fileFields = ExtractFieldNames(fileOutput);

        Assert.Equal(platformFields, nugetFields);
        Assert.Equal(platformFields, fileFields);

        // Verify expected fields are present
        Assert.Contains("Name", platformFields);
        Assert.Contains("Version", platformFields);
        Assert.Contains("TFM", platformFields);
        Assert.Contains("Arch", platformFields);
        Assert.Contains("Size", platformFields);
        Assert.Contains("Source", platformFields);
        Assert.Contains("Modified", platformFields);
    }
}
