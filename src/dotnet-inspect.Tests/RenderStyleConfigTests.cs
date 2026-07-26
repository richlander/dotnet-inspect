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
            "dotnet_style_qualification_for_property = true\n" +
            "dotnet_style_qualification_for_method = true\n" +
            "dotnet_style_qualification_for_event = true\n",
            origin: "cfg");

        Assert.True(result.Options.QualifyFieldAccess);
        Assert.True(result.Options.QualifyPropertyAccess);
        Assert.True(result.Options.QualifyMethodAccess);
        Assert.True(result.Options.QualifyEventAccess);
        Assert.Empty(result.Warnings);
        Assert.Equal("cfg", result.Origin);
    }

    [Fact]
    public void Parse_PreferConditionalReturnKey_MapsToLensKnob()
    {
        var result = RenderStyleConfig.Parse(
            "dotnet_style_prefer_conditional_expression_over_return = true",
            origin: "cfg");

        Assert.True(result.Options.PreferConditionalExpressionReturn);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_PreferConditionalReturn_DefaultsOffAndToleratesSeverity()
    {
        Assert.False(RenderStyleConfig.Parse("", origin: null).Options.PreferConditionalExpressionReturn);

        var withSeverity = RenderStyleConfig.Parse(
            "dotnet_style_prefer_conditional_expression_over_return = true:suggestion",
            origin: null);
        Assert.True(withSeverity.Options.PreferConditionalExpressionReturn);
        Assert.Empty(withSeverity.Warnings);
    }

    [Fact]
    public void Parse_PreferBranchlessBooleanKey_MapsToLensKnob()
    {
        var result = RenderStyleConfig.Parse(
            "dotnet_inspect_style_prefer_branchless_boolean = true",
            origin: "cfg");

        Assert.True(result.Options.PreferBranchlessBoolean);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_PreferBranchlessBoolean_DefaultsOffAndToleratesSeverity()
    {
        Assert.False(RenderStyleConfig.Parse("", origin: null).Options.PreferBranchlessBoolean);

        var withSeverity = RenderStyleConfig.Parse(
            "dotnet_inspect_style_prefer_branchless_boolean = true:suggestion",
            origin: null);
        Assert.True(withSeverity.Options.PreferBranchlessBoolean);
        Assert.Empty(withSeverity.Warnings);
    }

    [Fact]
    public void Parse_FalseValue_LeavesKnobOff()
    {
        var result = RenderStyleConfig.Parse("dotnet_style_qualification_for_field = false", origin: null);

        Assert.False(result.Options.QualifyFieldAccess);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_VarKeys_MapToTheirIndependentBackingBools()
    {
        // Each csharp_style_var_* key selects only its own bucket; the three are
        // independent (a site falls into exactly one, so any subset can be on).
        var builtIn = RenderStyleConfig.Parse("csharp_style_var_for_built_in_types = true", origin: null);
        Assert.True(builtIn.Options.PreferVarForBuiltInTypes);
        Assert.False(builtIn.Options.PreferVarWhenTypeApparent);
        Assert.False(builtIn.Options.PreferVarElsewhere);
        Assert.Empty(builtIn.Warnings);

        var apparent = RenderStyleConfig.Parse("csharp_style_var_when_type_is_apparent = true", origin: null);
        Assert.True(apparent.Options.PreferVarWhenTypeApparent);
        Assert.False(apparent.Options.PreferVarForBuiltInTypes);

        var elsewhere = RenderStyleConfig.Parse("csharp_style_var_elsewhere = true", origin: null);
        Assert.True(elsewhere.Options.PreferVarElsewhere);
    }

    [Fact]
    public void Parse_VarKeys_DefaultOff_TolerateSeverity_AndClearWithFalse()
    {
        // Shipped default: explicit everywhere (matches dotnet/runtime).
        var empty = RenderStyleConfig.Parse("", origin: null).Options;
        Assert.False(empty.PreferVarForBuiltInTypes);
        Assert.False(empty.PreferVarWhenTypeApparent);
        Assert.False(empty.PreferVarElsewhere);

        // The editorconfig value:severity form copied straight from a real file parses.
        var withSeverity = RenderStyleConfig.Parse("csharp_style_var_elsewhere = true:suggestion", origin: null);
        Assert.True(withSeverity.Options.PreferVarElsewhere);
        Assert.Empty(withSeverity.Warnings);

        // = false clears its own bucket only.
        var mixed = RenderStyleConfig.Parse(
            "csharp_style_var_for_built_in_types = true\n" +
            "csharp_style_var_elsewhere = true\n" +
            "csharp_style_var_for_built_in_types = false\n",
            origin: null);
        Assert.False(mixed.Options.PreferVarForBuiltInTypes);
        Assert.True(mixed.Options.PreferVarElsewhere);
        Assert.Empty(mixed.Warnings);
    }

    [Fact]
    public void Parse_FullTaste_DoesNotEnableVar()
    {
        // var is opt-in only (the runtime endorses explicit), so the "full taste"
        // aggregate must never turn it on.
        var result = RenderStyleConfig.Parse("dotnet_inspect_style_full_taste = true", origin: null);
        Assert.False(result.Options.PreferVarForBuiltInTypes);
        Assert.False(result.Options.PreferVarWhenTypeApparent);
        Assert.False(result.Options.PreferVarElsewhere);
    }

    [Fact]
    public void Parse_FullTasteKey_EnablesTheOracleEndorsedSubset()
    {
        // The "full taste" aggregate turns on the whole oracle-endorsed subset with
        // one key — the four this.-qualifications and the ternary lens — and never
        // the non-endorsed branchless "bool hack".
        var result = RenderStyleConfig.Parse("dotnet_inspect_style_full_taste = true", origin: "cfg");

        Assert.True(result.Options.QualifyFieldAccess);
        Assert.True(result.Options.QualifyPropertyAccess);
        Assert.True(result.Options.QualifyMethodAccess);
        Assert.True(result.Options.QualifyEventAccess);
        Assert.True(result.Options.PreferConditionalExpressionReturn);
        Assert.False(result.Options.PreferBranchlessBoolean);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_FullTasteKey_DefaultsOffAndToleratesSeverity()
    {
        Assert.False(RenderStyleConfig.Parse("", origin: null).Options.QualifyFieldAccess);

        var withSeverity = RenderStyleConfig.Parse(
            "dotnet_inspect_style_full_taste = true:suggestion",
            origin: null);
        Assert.True(withSeverity.Options.QualifyFieldAccess);
        Assert.True(withSeverity.Options.PreferConditionalExpressionReturn);
        Assert.Empty(withSeverity.Warnings);
    }

    [Fact]
    public void Parse_FullTasteFalse_IsRecognizedAndLeavesSubsetOff()
    {
        var result = RenderStyleConfig.Parse("dotnet_inspect_style_full_taste = false", origin: null);

        Assert.False(result.Options.QualifyFieldAccess);
        Assert.False(result.Options.PreferConditionalExpressionReturn);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_FullTasteThenExplicitOverride_LastWriteWins()
    {
        // A later explicit per-knob line overrides the aggregate (file order is
        // last-write-wins), so a user can take "full taste" minus one knob.
        var result = RenderStyleConfig.Parse(
            "dotnet_inspect_style_full_taste = true\n" +
            "dotnet_style_qualification_for_field = false\n",
            origin: null);

        Assert.False(result.Options.QualifyFieldAccess);
        Assert.True(result.Options.QualifyPropertyAccess);
        Assert.True(result.Options.PreferConditionalExpressionReturn);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_FullTasteKey_NonBoolValue_Warns()
    {
        var result = RenderStyleConfig.Parse("dotnet_inspect_style_full_taste = maybe", origin: null);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("expects true/false", warning);
        Assert.False(result.Options.QualifyFieldAccess);
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
            "csharp_style_expression_bodied_methods = true\n" +
            "dotnet_style_qualification_for_field = true\n",
            origin: null);

        Assert.True(result.Options.QualifyFieldAccess);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("unknown key", warning);
        Assert.Contains("csharp_style_expression_bodied_methods", warning);
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

    [Fact]
    public void Collect_WithQualifyMethodAccess_QualifiesInstanceCallAndRecordsEvidence()
    {
        var (code, result) = RenderSpecimenMember(
            nameof(ThisQualificationConfigSpecimen.Doubled),
            PrinterOptions.Default with { QualifyMethodAccess = true });

        Assert.Contains("this.Compute()", code);
        Assert.True(result.EffectiveOptions.QualifyMethodAccess);
        Assert.False(result.EffectiveOptions.QualifyEventAccess);
    }

    [Fact]
    public void Collect_WithoutRenderOptions_RendersBareInstanceCall()
    {
        var (code, _) = RenderSpecimenMember(
            nameof(ThisQualificationConfigSpecimen.Doubled), renderOptions: null);

        Assert.Contains("Compute()", code);
        Assert.DoesNotContain("this.Compute()", code);
    }

    [Fact]
    public void Collect_WithQualifyMethodAccess_QualifiesThisMethodGroup()
    {
        var (code, _) = RenderSpecimenMember(
            nameof(ThisQualificationConfigSpecimen.ComputeGetter),
            PrinterOptions.Default with { QualifyMethodAccess = true });

        Assert.Contains("this.Compute", code);
    }

    [Fact]
    public void Collect_WithQualifyEventAccess_QualifiesEventAndRecordsEvidence()
    {
        var (code, result) = RenderSpecimenMember(
            nameof(ThisQualificationConfigSpecimen.Subscribe),
            PrinterOptions.Default with { QualifyEventAccess = true });

        Assert.Contains("this.Pinged +=", code);
        Assert.True(result.EffectiveOptions.QualifyEventAccess);
        Assert.False(result.EffectiveOptions.QualifyMethodAccess);
    }

    [Fact]
    public void Collect_WithoutRenderOptions_RendersBareEventSubscription()
    {
        var (code, _) = RenderSpecimenMember(
            nameof(ThisQualificationConfigSpecimen.Subscribe), renderOptions: null);

        Assert.Contains("Pinged +=", code);
        Assert.DoesNotContain("this.Pinged", code);
    }

    // An event subscription and a property access both route through the printer's
    // PropertyTarget helper, but they are governed by separate knobs: enabling
    // property qualification must NOT qualify an event subscription (and the
    // event knob, tested above, does).
    [Fact]
    public void Collect_WithQualifyPropertyAccess_DoesNotQualifyEventSubscription()
    {
        var (code, _) = RenderSpecimenMember(
            nameof(ThisQualificationConfigSpecimen.Subscribe),
            PrinterOptions.Default with { QualifyPropertyAccess = true });

        Assert.DoesNotContain("this.Pinged", code);
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
        // A fidelity-only projection consumes no config, so it never marks the
        // warning latch regardless of whether a config was resolved.
        Assert.False(withConfig.StyledProjectionProduced);
        Assert.False(withDefault.StyledProjectionProduced);
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
        Assert.True(code.StyledProjectionProduced);
    }

    // The Applied-Taste-only path (#3158): `member <M> -S "Applied Taste"` without
    // Decompiled Source still renders the styled projection to extract the applied
    // decisions, so the config IS consumed -- but DecompiledResult stays null. The
    // old latch keyed off DecompiledResult and silently swallowed a bad config
    // here; StyledProjectionProduced is the independent signal that fixes it.
    [Fact]
    public void AppliedTasteOnlyRequest_MarksStyledProjectionProduced_ThoughNoDecompiledResult()
    {
        var appliedTasteOnly = new MemberCodeProvider.Request(
            DecompiledSource: false, AnnotatedSource: false, CostOverlay: false,
            SemanticsOverlay: false, IL: false, Attributes: false, Calls: false,
            Callers: false, CallGraph: false, UnsafeOperations: false,
            AppliedTaste: true);

        var code = CollectSpecimenCompute(
            appliedTasteOnly, PrinterOptions.Default with { QualifyFieldAccess = true });

        Assert.Null(code.DecompiledResult);
        Assert.True(code.StyledProjectionProduced);
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

    // GPT adversarial finding (head 32e7c966): a P/Invoke method (extern, no IL
    // body) yields a non-null DecompilerResult whose Output is null -- only a
    // DEC0001 diagnostic renders, no styled C#. The printer never ran, so the
    // config is not consumed; the warning latch must key off a produced Output,
    // not merely a non-null result.
    [Fact]
    public void NoBodyMethod_ProducesResultWithoutOutput_SoNoStyledSource()
    {
        var decompiledSourceRequested = new MemberCodeProvider.Request(
            DecompiledSource: true, AnnotatedSource: false, CostOverlay: false,
            SemanticsOverlay: false, IL: false, Attributes: false, Calls: false,
            Callers: false, CallGraph: false, UnsafeOperations: false);

        string assemblyPath = typeof(SamplePInvokeClass).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        var type = surface.Types.Single(t => t.FullName == typeof(SamplePInvokeClass).FullName);
        var methods = type.Members
            .Where(m => m.Name == nameof(SamplePInvokeClass.GetCurrentProcessId)).ToList();

        var results = MemberCodeProvider.Collect(
            type, methods, assemblyPath, overloadIndex: 0, decompiledSourceRequested,
            renderOptions: PrinterOptions.Default with { QualifyFieldAccess = true });

        var (_, code) = Assert.Single(results);
        Assert.NotNull(code.DecompiledResult);
        Assert.Null(code.DecompiledResult!.Output);
        // The printer never ran (no IL body), so no config was consumed: the latch
        // stays unmarked even though Decompiled Source was requested.
        Assert.False(code.StyledProjectionProduced);
    }

    // The bodyless-method invariant must also hold for an Applied-Taste-only run:
    // no printed body means no config consumption, so no warning, matching the
    // Decompiled Source behavior above.
    [Fact]
    public void AppliedTasteOnly_NoBodyMethod_DoesNotMarkStyledProjection()
    {
        var appliedTasteOnly = new MemberCodeProvider.Request(
            DecompiledSource: false, AnnotatedSource: false, CostOverlay: false,
            SemanticsOverlay: false, IL: false, Attributes: false, Calls: false,
            Callers: false, CallGraph: false, UnsafeOperations: false,
            AppliedTaste: true);

        string assemblyPath = typeof(SamplePInvokeClass).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        var type = surface.Types.Single(t => t.FullName == typeof(SamplePInvokeClass).FullName);
        var methods = type.Members
            .Where(m => m.Name == nameof(SamplePInvokeClass.GetCurrentProcessId)).ToList();

        var results = MemberCodeProvider.Collect(
            type, methods, assemblyPath, overloadIndex: 0, appliedTasteOnly,
            renderOptions: PrinterOptions.Default with { QualifyFieldAccess = true });

        var (_, code) = Assert.Single(results);
        Assert.False(code.StyledProjectionProduced);
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

    private static (string Code, ILInspector.Decompiler.DecompilerResult Result) RenderSpecimenMember(
        string memberName, PrinterOptions? renderOptions)
    {
        string assemblyPath = typeof(ThisQualificationConfigSpecimen).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        var type = surface.Types.Single(t => t.FullName == typeof(ThisQualificationConfigSpecimen).FullName);
        var methods = type.Members.Where(m => m.Name == memberName).ToList();

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

    // ---- catalog is the source of truth ----

    [Fact]
    public void EveryCatalogConfigKey_IsHonoredByParse()
    {
        // The resolver is data-driven from StyleOptionCatalog, so every catalog
        // knob that declares a config key must round-trip through Parse with no
        // warning and set exactly its own option.
        foreach (var knob in StyleOptionCatalog.Options.Where(o => o.ConfigKey is not null))
        {
            var result = RenderStyleConfig.Parse($"{knob.ConfigKey} = true", origin: "cfg");

            Assert.Empty(result.Warnings);
            Assert.True(knob.Get(result.Options), $"'{knob.ConfigKey}' should set {knob.Id}");
        }
    }

    [Fact]
    public void ApiOnlyCatalogKnobs_HaveNoConfigKey()
    {
        // Knobs with no config key (formatting/synthesis) are API-only and must not
        // be reachable through the file vocabulary; a made-up key still warns.
        var apiOnly = StyleOptionCatalog.Options.Where(o => o.ConfigKey is null).ToArray();
        Assert.Contains(apiOnly, o => o.Id == "readable-local-names");

        var result = RenderStyleConfig.Parse("readable_local_names = true", origin: "cfg");
        Assert.False(result.Options.ReadableLocalNames);
        Assert.Contains(result.Warnings, w => w.Contains("unknown key"));
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

    // Instance method call on the implicit this receiver.
    public int Doubled() => Compute() * 2;

    // Method group over the implicit this receiver.
    public System.Func<int> ComputeGetter() => Compute;

#pragma warning disable CS0067 // Pinged is subscribed to via Subscribe; the fixture never raises it.
    public event System.EventHandler? Pinged;
#pragma warning restore CS0067

    // Event subscription (+=) on the implicit this receiver.
    public void Subscribe(System.EventHandler handler) => Pinged += handler;
}

/// <summary>
/// An empty public type whose whole-type decompilation yields no listing, so a
/// <c>type -S "Decompiled Source"</c> render produces no styled source (and thus
/// no config warning).
/// </summary>
public interface IEmptyStyleFixture
{
}
