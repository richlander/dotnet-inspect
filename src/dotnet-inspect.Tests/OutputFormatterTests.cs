using DotnetInspector.Models;
using DotnetInspector.Views;
using DotnetInspector;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Tests;

public class OutputFormatterTests
{
    [Fact]
    public void MultiAssemblyReport_HasSingleH1()
    {
        var report = CreateTestReport("Test.dll", "net9.0", "net8.0");
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
        var report = CreateTestReport("Test.dll", "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.Contains("## Libraries", output);
    }

    [Fact]
    public void MultiAssemblyReport_HasH3PerTfm()
    {
        var report = CreateTestReport("Test.dll", "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.Contains("### Test.dll (net9.0)", output);
        Assert.Contains("### Test.dll (net8.0)", output);
    }

    [Fact]
    public void MultiAssemblyReport_HasH4SectionsPerItem()
    {
        var report = CreateTestReport("Test.dll", "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.Contains("#### Library Info", output);
    }

    [Fact]
    public void MultiAssemblyReport_NoCompactLine()
    {
        var report = CreateTestReport("Test.dll", "net9.0", "net8.0");
        var output = Serialize(report);

        // AutoFields=false should suppress the compact "File: ... | Type: ..." line
        Assert.DoesNotContain("File: Test.dll", output);
    }

    [Fact]
    public void MultiAssemblyReport_TitleFromPackageName()
    {
        var report = CreateTestReport("Test.dll", "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.StartsWith("# Test", output.TrimStart());
    }

    [Fact]
    public void SingleAudit_ExcludesSymbols_AtNormalVerbosity()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var options = new AssemblyOptions();
        var output = Serialize(inspection, GetLibraryExcludeSections(options));

        Assert.DoesNotContain("## Symbols", output);
    }

    [Fact]
    public void SingleAudit_IncludesSymbols_AtDetailedVerbosity()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var options = new AssemblyOptions { Verbosity = Verbosity.Detailed };
        var output = Serialize(inspection, GetLibraryExcludeSections(options));

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

    private static LibraryInspectionReport CreateTestReport(string fileName, params string[] tfms)
    {
        var inspections = tfms.Select(tfm => CreateTestAudit(fileName, tfm)).ToList();
        return new LibraryInspectionReport
        {
            Title = Path.GetFileNameWithoutExtension(fileName),
            Assemblies = inspections.Select(a => new LibraryInspectionView(a)).ToList()
        };
    }

    private static string Serialize(LibraryInspectionReport report, HashSet<string>? excludeSections = null)
    {
        var context = new MarkoutContext(new MarkoutWriterOptions
        {
            ExcludeSections = excludeSections
        });
        return context.Serialize(report).TrimEnd();
    }

    private static string Serialize(LibraryInspection inspection, HashSet<string>? excludeSections = null)
    {
        var view = new LibraryInspectionView(inspection);
        var context = new MarkoutContext(new MarkoutWriterOptions
        {
            ExcludeSections = excludeSections
        });
        return context.Serialize(view).TrimEnd();
    }

    // Mirror the logic from OutputFormatter.GetLibraryExcludeSections
    private static HashSet<string>? GetLibraryExcludeSections(AssemblyOptions options)
    {
        HashSet<string> excluded = ["Source Coverage", "Missing Sources"];

        if (options.Verbosity != Verbosity.Detailed)
            excluded.Add("Symbols");

        if (options.IncludeSourcelinkAudit)
        {
            excluded.Remove("Source Coverage");
            excluded.Remove("Missing Sources");
        }

        return excluded.Count > 0 ? excluded : null;
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

        var output = ApiOutputFormatter.RenderFullApiMarkdown(api, options);

        Assert.Contains("Source: NuGet", output);
        Assert.DoesNotContain("## Classes", output);
        Assert.DoesNotContain("Type1", output);
    }

    [Fact]
    public void ApiFullSurface_MinimalMode_ShowsTypeTables()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Minimal };

        var output = ApiOutputFormatter.RenderFullApiMarkdown(api, options);

        Assert.Contains("## Classes", output);
        Assert.Contains("TestLib.Type1", output);
    }

    [Fact]
    public void ApiFullSurface_QuietWithTypeFilter_ShowsTypeTables()
    {
        var api = CreateTestApiSurface();
        // Glob upgrade: quiet + TypeFilter should behave as minimal
        var options = new ApiOptions
        {
            Verbosity = Verbosity.Minimal,  // caller upgrades quiet to minimal for globs
            TypeFilter = "Type1*"
        };

        var output = ApiOutputFormatter.RenderFullApiMarkdown(api, options);

        Assert.Contains("## Classes", output);
        Assert.Contains("TestLib.Type1", output);
    }

    [Fact]
    public void ApiFullSurface_SourceAndTfm_PresentInCompactLine()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var output = ApiOutputFormatter.RenderFullApiMarkdown(api, options);

        Assert.Contains("Source: NuGet", output);
        Assert.Contains("TFM: net10.0", output);
        Assert.Contains("Version: 1.0.0", output);
    }

    [Fact]
    public void ApiTypeView_SourceAndTfm_PresentInCompactLine()
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
    public void ApiTypeView_NullSource_OmitsSourceField()
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
}
