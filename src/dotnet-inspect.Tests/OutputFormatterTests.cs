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
        var audit = CreateTestAudit("Test.dll", "net9.0");
        var output = Serialize(audit);

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
    public void SingleAudit_ExcludesSymbols_WhenNoAuditTier()
    {
        var audit = CreateTestAudit("Test.dll", "net9.0");
        var options = new AssemblyOptions { IncludeMetadata = true };
        var output = Serialize(audit, GetAuditExcludeSections(options));

        Assert.DoesNotContain("## Symbols", output);
    }

    [Fact]
    public void SingleAudit_IncludesSymbols_WhenSymbolsTier()
    {
        var audit = CreateTestAudit("Test.dll", "net9.0");
        var options = new AssemblyOptions { IncludeSymbols = true };
        var output = Serialize(audit, GetAuditExcludeSections(options));

        Assert.Contains("## Symbols", output);
    }

    [Fact]
    public void SingleAudit_MetadataIncludesDeterministic()
    {
        var audit = CreateTestAudit("Test.dll", "net9.0");
        audit.IsDeterministic = true;
        audit.HasReproducibleFlag = true;
        var output = Serialize(audit);

        Assert.Contains("Deterministic", output);
        Assert.Contains("Reproducible", output);
    }

    private static AssemblyAudit CreateTestAudit(string fileName, string? tfm)
    {
        return new AssemblyAudit
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

    private static AssemblyAuditReport CreateTestReport(string fileName, params string[] tfms)
    {
        var audits = tfms.Select(tfm => CreateTestAudit(fileName, tfm)).ToList();
        return new AssemblyAuditReport
        {
            Title = Path.GetFileNameWithoutExtension(fileName),
            Assemblies = audits.Select(a => new AssemblyAuditView(a)).ToList()
        };
    }

    private static string Serialize(AssemblyAuditReport report, HashSet<string>? excludeSections = null)
    {
        var context = new MarkoutContext(new MarkoutWriterOptions
        {
            ExcludeSections = excludeSections
        });
        return context.Serialize(report).TrimEnd();
    }

    private static string Serialize(AssemblyAudit audit, HashSet<string>? excludeSections = null)
    {
        var view = new AssemblyAuditView(audit);
        var context = new MarkoutContext(new MarkoutWriterOptions
        {
            ExcludeSections = excludeSections
        });
        return context.Serialize(view).TrimEnd();
    }

    // Mirror the logic from OutputFormatter.GetAuditExcludeSections
    private static HashSet<string>? GetAuditExcludeSections(AssemblyOptions options)
    {
        if (!options.HasAuditTier)
            return ["Symbols", "Source Coverage", "Non-normalized Paths", "Missing Sources"];
        if (!options.IncludeSourcelinkAudit)
            return ["Source Coverage", "Missing Sources"];
        return null;
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
