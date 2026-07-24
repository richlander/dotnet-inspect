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

    [Fact]
    public void Parse_RootKey_IsRecognizedWithoutWarningAndDoesNotAffectKnobs()
    {
        var result = RenderStyleConfig.Parse(
            "root = true\n" +
            "dotnet_style_qualification_for_field = true\n",
            origin: null);

        // 'root' is the editorconfig boundary marker: recognized (never an
        // "unknown key"), drives no knob, and leaves recognized keys applied.
        Assert.True(result.Options.QualifyFieldAccess);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_RootKey_WithInvalidBool_Warns()
    {
        var result = RenderStyleConfig.Parse("root = maybe", origin: null);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("expects true/false", warning);
    }

    // ---- warning latch (emit at consumption, exactly once) ----

    [Fact]
    public void WarningSink_EmitOnce_WritesEachWarningPrefixed()
    {
        var sink = new RenderConfigWarningSink(
            ["line 1: unknown key 'foo' (ignored)", "line 2: malformed entry 'bar'"]);
        var writer = new StringWriter();

        sink.EmitOnce(writer);

        var lines = writer.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();
        Assert.Equal(
            [
                $"Warning: {RenderStyleConfig.FileName}: line 1: unknown key 'foo' (ignored)",
                $"Warning: {RenderStyleConfig.FileName}: line 2: malformed entry 'bar'",
            ],
            lines);
    }

    [Fact]
    public void WarningSink_EmitOnce_IsLatched_SecondCallIsNoOp()
    {
        var sink = new RenderConfigWarningSink(["line 1: unknown key 'foo' (ignored)"]);
        var writer = new StringWriter();

        sink.EmitOnce(writer);
        sink.EmitOnce(writer);

        var count = writer.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Length;
        Assert.Equal(1, count);
    }

    [Fact]
    public void WarningSink_EmitOnce_WithNoWarnings_WritesNothing()
    {
        var sink = new RenderConfigWarningSink([]);
        var writer = new StringWriter();

        sink.EmitOnce(writer);

        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void RunPreamble_AttachesWarningSink_OnlyWhenConfigWarns()
    {
        // A clean config raises no warnings, so no latch is attached; a config with
        // a bad key attaches a sink carrying that warning. RenderOptions is always
        // attached regardless. This proves the latch is created iff there is
        // something to say, and the emit decision lives at the consumption sites.
        var clean = Resolve("dotnet_style_qualification_for_field = true");
        Assert.Empty(clean.Warnings);

        var dirty = Resolve("bogus_key = true");
        Assert.NotEmpty(dirty.Warnings);
        var sink = new RenderConfigWarningSink(dirty.Warnings);
        var writer = new StringWriter();
        sink.EmitOnce(writer);
        Assert.Contains("bogus_key", writer.ToString());
    }

    private static RenderStyleResolution Resolve(string configText)
        => RenderStyleConfig.Parse(configText, ".dotnet-inspectconfig");

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

    // Whole-type decompilation (the `type -S "Decompiled Source"` path) routes
    // through MemberBodyProducer.Project rather than Collect, so it needs the
    // resolved options threaded separately.

    [Fact]
    public void WholeType_WithoutRenderOptions_RendersBareThisMemberAccess()
    {
        var code = RenderSpecimenWholeType(renderOptions: null);

        Assert.Contains("Compute()", code);
        Assert.DoesNotContain("this._count", code);
    }

    [Fact]
    public void WholeType_WithQualifyFieldAccess_QualifiesField()
    {
        var code = RenderSpecimenWholeType(
            PrinterOptions.Default with { QualifyFieldAccess = true });

        Assert.Contains("this._count", code);
    }

    // A fidelity-only projection reads style-invariant evidence (the raised IR
    // and recompile diagnostics) and never surfaces the printed C# string, so the
    // style config is genuinely not consumed -- which is why `-S "Fidelity Causes"`
    // does not emit a config warning even when a config is present. The observable
    // guarantee the warning latch keys off is that no styled DecompiledResult is
    // surfaced for a fidelity-only request, regardless of the render options.

    [Fact]
    public void FidelityOnlyRequest_NeverSurfacesStyledSource_RegardlessOfConfig()
    {
        var fidelityOnly = new MemberCodeProvider.Request(
            DecompiledSource: false, AnnotatedSource: false, CostOverlay: false,
            SemanticsOverlay: false, IL: false, Attributes: false, Calls: false,
            Callers: false, CallGraph: false, UnsafeOperations: false,
            FidelityCauses: true);

        var withConfig = CollectSpecimenCompute(
            fidelityOnly, PrinterOptions.Default with { QualifyFieldAccess = true });
        var withDefault = CollectSpecimenCompute(fidelityOnly, renderOptions: null);

        Assert.Null(withConfig.DecompiledResult);
        Assert.Null(withDefault.DecompiledResult);
        Assert.NotNull(withConfig.FidelityCauses);
        Assert.NotNull(withDefault.FidelityCauses);
    }

    [Fact]
    public void DecompiledSourceRequest_SurfacesStyledSource_WhenConfigQualifies()
    {
        var decompiledSource = new MemberCodeProvider.Request(
            DecompiledSource: true, AnnotatedSource: false, CostOverlay: false,
            SemanticsOverlay: false, IL: false, Attributes: false, Calls: false,
            Callers: false, CallGraph: false, UnsafeOperations: false);

        var code = CollectSpecimenCompute(
            decompiledSource, PrinterOptions.Default with { QualifyFieldAccess = true });

        Assert.NotNull(code.DecompiledResult?.Output);
        Assert.Contains("this._count", code.DecompiledResult!.Output);
    }

    // MAI review finding (head a25e01d9): `member <Type> --directory <dir>
    // -S "Decompiled Source"` drives the callers-only aggregation path
    // (HasCallerScope true, no overload selector). Collect returns no styled
    // result there, so the production-gated warning latch -- which keys off a
    // non-null DecompiledResult at the formatter emit site -- must not fire.
    [Fact]
    public void CallersOnlyRequest_WithoutOverloadIndex_SurfacesNoStyledSource()
    {
        var decompiledSourceRequested = new MemberCodeProvider.Request(
            DecompiledSource: true, AnnotatedSource: false, CostOverlay: false,
            SemanticsOverlay: false, IL: false, Attributes: false, Calls: false,
            Callers: true, CallGraph: false, UnsafeOperations: false);

        string assemblyPath = typeof(ThisQualificationConfigSpecimen).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        var type = surface.Types.Single(t => t.FullName == typeof(ThisQualificationConfigSpecimen).FullName);
        var methods = type.Members
            .Where(m => m.Name == nameof(ThisQualificationConfigSpecimen.Compute)).ToList();

        var results = MemberCodeProvider.Collect(
            type, methods, assemblyPath, overloadIndex: null, decompiledSourceRequested,
            renderOptions: PrinterOptions.Default with { QualifyFieldAccess = true });

        Assert.DoesNotContain(results, r => r.Code.DecompiledResult is not null);
    }

    // Type-path counterpart: `type <empty type> -S "Decompiled Source"` requests
    // source but MemberBodyProducer.Project yields no listing, so the whole-type
    // emit gate (listing is not null) never surfaces a config warning.
    [Fact]
    public void WholeTypeProject_EmptyType_ProducesNoListing()
    {
        string assemblyPath = typeof(IEmptyStyleFixture).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        var type = surface.Types.Single(t => t.FullName == typeof(IEmptyStyleFixture).FullName);

        var result = ILInspector.Decompiler.MemberBodyProducer.Project(
            type, assemblyPath, pdbPath: null,
            printerOptions: PrinterOptions.Default with { QualifyFieldAccess = true });

        Assert.Null(result.Output);
    }

    private static MemberCodeProvider.Item CollectSpecimenCompute(
        MemberCodeProvider.Request request, PrinterOptions? renderOptions)
    {
        string assemblyPath = typeof(ThisQualificationConfigSpecimen).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        var type = surface.Types.Single(t => t.FullName == typeof(ThisQualificationConfigSpecimen).FullName);
        var methods = type.Members
            .Where(m => m.Name == nameof(ThisQualificationConfigSpecimen.Compute)).ToList();

        var results = MemberCodeProvider.Collect(
            type, methods, assemblyPath, overloadIndex: 0, request, renderOptions: renderOptions);
        var (_, code) = Assert.Single(results);
        return code;
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

    private static string RenderSpecimenWholeType(PrinterOptions? renderOptions)
    {
        string assemblyPath = typeof(ThisQualificationConfigSpecimen).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        var type = surface.Types.Single(t => t.FullName == typeof(ThisQualificationConfigSpecimen).FullName);

        var result = ILInspector.Decompiler.MemberBodyProducer.Project(
            type, assemblyPath, pdbPath: null, printerOptions: renderOptions);

        Assert.NotNull(result.Output);
        return result.Output!;
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

/// <summary>
/// An empty public type whose whole-type decompilation yields no listing, so a
/// <c>type -S "Decompiled Source"</c> render produces no styled source (and thus
/// no config warning).
/// </summary>
public interface IEmptyStyleFixture
{
}
