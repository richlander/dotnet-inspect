using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using DotnetInspector.Inspectors;
using DotnetInspector.Services;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Covers the tool-owned <c>.dotnet-inspectconfig</c> style surface (issue #3097,
/// slice 2a): the CLI-edge resolver (<see cref="RenderStyleConfig"/>) and the
/// threading of the resolved <see cref="PrinterOptions"/> through
/// <see cref="MemberCodeProvider.Collect"/> into the decompiler. The decompiler
/// library stays a pure function of explicit options; config discovery lives only
/// at this edge.
/// </summary>
public class RenderStyleConfigTests
{
    // ---- parsing ----

    [Fact]
    public void Parse_EmptyText_IsDefaultsWithNoWarnings()
    {
        var result = RenderStyleConfig.Parse("", origin: null);

        Assert.Equal(PrinterOptions.Default, result.Options);
        Assert.False(result.Options.QualifyFieldAccess);
        Assert.False(result.Options.QualifyPropertyAccess);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_KnownKeys_MapToQualificationKnobs()
    {
        var result = RenderStyleConfig.Parse(
            "dotnet_style_qualification_for_field = true\n" +
            "dotnet_style_qualification_for_property = true\n",
            origin: "cfg");

        Assert.True(result.Options.QualifyFieldAccess);
        Assert.True(result.Options.QualifyPropertyAccess);
        Assert.Empty(result.Warnings);
        Assert.Equal("cfg", result.Origin);
    }

    [Fact]
    public void Parse_FalseValue_LeavesKnobOff()
    {
        var result = RenderStyleConfig.Parse("dotnet_style_qualification_for_field = false", origin: null);

        Assert.False(result.Options.QualifyFieldAccess);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_ToleratesEditorConfigSeveritySuffix()
    {
        var result = RenderStyleConfig.Parse("dotnet_style_qualification_for_field = true:suggestion", origin: null);

        Assert.True(result.Options.QualifyFieldAccess);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveForKeysAndValues()
    {
        var result = RenderStyleConfig.Parse("DOTNET_STYLE_QUALIFICATION_FOR_PROPERTY = TRUE", origin: null);

        Assert.True(result.Options.QualifyPropertyAccess);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_IgnoresCommentsAndSectionHeadersWithoutWarning()
    {
        var result = RenderStyleConfig.Parse(
            "# a comment\n" +
            "; another comment\n" +
            "[*.cs]\n" +
            "dotnet_style_qualification_for_field = true\n",
            origin: null);

        Assert.True(result.Options.QualifyFieldAccess);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_UnknownKey_WarnsButKeepsRecognizedKeys()
    {
        var result = RenderStyleConfig.Parse(
            "csharp_style_var_when_type_is_apparent = true\n" +
            "dotnet_style_qualification_for_field = true\n",
            origin: null);

        Assert.True(result.Options.QualifyFieldAccess);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("unknown key", warning);
        Assert.Contains("csharp_style_var_when_type_is_apparent", warning);
    }

    [Fact]
    public void Parse_InvalidBool_WarnsAndLeavesKnobOff()
    {
        var result = RenderStyleConfig.Parse("dotnet_style_qualification_for_field = yes", origin: null);

        Assert.False(result.Options.QualifyFieldAccess);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("expects true/false", warning);
    }

    [Fact]
    public void Parse_MalformedLine_Warns()
    {
        var result = RenderStyleConfig.Parse("this line has no equals sign", origin: null);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("malformed entry", warning);
    }

    // ---- discovery ----

    [Fact]
    public void Discover_ReturnsNull_WhenNoFilePresent()
    {
        var root = CreateTempDirectory();
        try
        {
            Assert.Null(RenderStyleConfig.Discover(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Discover_WalksUpToNearestFile()
    {
        var root = CreateTempDirectory();
        try
        {
            var child = Path.Combine(root, "a", "b", "c");
            Directory.CreateDirectory(child);
            var expected = Path.Combine(root, "a", RenderStyleConfig.FileName);
            File.WriteAllText(expected, "dotnet_style_qualification_for_field = true");

            var found = RenderStyleConfig.Discover(child);

            Assert.Equal(expected, found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Discover_NearestFileWins_OverAncestor()
    {
        var root = CreateTempDirectory();
        try
        {
            var child = Path.Combine(root, "a", "b");
            Directory.CreateDirectory(child);
            File.WriteAllText(Path.Combine(root, RenderStyleConfig.FileName), "dotnet_style_qualification_for_field = false");
            var nearest = Path.Combine(root, "a", RenderStyleConfig.FileName);
            File.WriteAllText(nearest, "dotnet_style_qualification_for_field = true");

            var resolution = RenderStyleConfig.Resolve(child);

            Assert.Equal(nearest, resolution.Origin);
            Assert.True(resolution.Options.QualifyFieldAccess);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_NoFile_IsNone()
    {
        var root = CreateTempDirectory();
        try
        {
            var resolution = RenderStyleConfig.Resolve(root);

            Assert.Same(RenderStyleResolution.None, resolution);
            Assert.Null(resolution.Origin);
            Assert.Equal(PrinterOptions.Default, resolution.Options);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- threading into the decompiler ----

    [Fact]
    public void Collect_WithoutRenderOptions_RendersBareThisMemberAccess()
    {
        var (code, _) = RenderSpecimenCompute(renderOptions: null);

        Assert.Contains("_count", code);
        Assert.Contains("Extra", code);
        Assert.DoesNotContain("this._count", code);
        Assert.DoesNotContain("this.Extra", code);
    }

    [Fact]
    public void Collect_WithQualifyFieldAccess_QualifiesFieldAndRecordsEvidence()
    {
        var (code, result) = RenderSpecimenCompute(
            PrinterOptions.Default with { QualifyFieldAccess = true });

        Assert.Contains("this._count", code);
        Assert.DoesNotContain("this.Extra", code);
        Assert.True(result.EffectiveOptions.QualifyFieldAccess);
        Assert.False(result.EffectiveOptions.QualifyPropertyAccess);
    }

    [Fact]
    public void Collect_WithQualifyPropertyAccess_QualifiesPropertyAndRecordsEvidence()
    {
        var (code, result) = RenderSpecimenCompute(
            PrinterOptions.Default with { QualifyPropertyAccess = true });

        Assert.Contains("this.Extra", code);
        Assert.DoesNotContain("this._count", code);
        Assert.True(result.EffectiveOptions.QualifyPropertyAccess);
        Assert.False(result.EffectiveOptions.QualifyFieldAccess);
    }

    private static (string Code, ILInspector.Decompiler.DecompilerResult Result) RenderSpecimenCompute(
        PrinterOptions? renderOptions)
    {
        string assemblyPath = typeof(ThisQualificationConfigSpecimen).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        var type = surface.Types.Single(t => t.FullName == typeof(ThisQualificationConfigSpecimen).FullName);
        var methods = type.Members.Where(m => m.Name == nameof(ThisQualificationConfigSpecimen.Compute)).ToList();

        var request = new MemberCodeProvider.Request(
            DecompiledSource: true,
            AnnotatedSource: false,
            CostOverlay: false,
            SemanticsOverlay: false,
            IL: false,
            Attributes: false,
            Calls: false,
            Callers: false,
            CallGraph: false,
            UnsafeOperations: false);

        var results = MemberCodeProvider.Collect(
            type, methods, assemblyPath, overloadIndex: 0, request, renderOptions: renderOptions);

        var (_, code) = Assert.Single(results);
        var decompiled = code.DecompiledResult;
        Assert.NotNull(decompiled?.Output);
        return (decompiled!.Output, decompiled);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "dotnet-inspectconfig-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }
}

/// <summary>
/// A method whose body reads an instance field and an instance property through
/// <c>this</c>, so a config-driven qualification knob is observable in the
/// decompiled output.
/// </summary>
public class ThisQualificationConfigSpecimen
{
    private readonly int _count;

    public ThisQualificationConfigSpecimen(int count) => _count = count;

    public int Extra { get; set; }

    public int Compute() => _count + Extra;
}
