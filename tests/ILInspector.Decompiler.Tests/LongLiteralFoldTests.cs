using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The opt-in byte-divergent style lens
/// <see cref="PrinterOptions.PreferLongLiteralSuffix"/> (#3347): a <c>long</c>
/// constant csc emits as <c>ldc.i4(.s) N; conv.i8</c> renders as the idiomatic
/// <c>NL</c> literal instead of the <c>(long)N</c> cast the default view spells.
/// This is a raise-completeness gap, not a fidelity gap — the cast is already
/// opcode-faithful and round-trips; it is simply not fully raised.
///
/// <para>Three claims are pinned here, in the order they matter:</para>
/// <list type="number">
/// <item><description>
/// <b>Default unchanged.</b> Every fixture method and the reference witness
/// <c>CfgSampleClass.InlineArraySpanTernaryConditionValue</c> render exactly
/// today's text with the knob off — the lens is opt-in and the shipped view stays
/// byte-faithful.
/// </description></item>
/// <item><description>
/// <b>The fold fires on exactly the <c>conv.i8</c> shape.</b> With the knob on, a
/// <c>Convert(→Int64, Int32 Constant)</c> becomes <c>NL</c> at a ternary arm, a
/// return, an argument, and a binary operand (including a negative literal, whose
/// unary precedence the fold has to carry itself).
/// </description></item>
/// <item><description>
/// <b>The close negative: a genuine <c>ldc.i8</c> is untouched.</b> That shape
/// reaches the printer as a bare <c>Int64</c> <see cref="Constant"/> with no
/// <see cref="Convert"/> over it, so it cannot match the fold. csc will not emit
/// <c>ldc.i8</c> for a small value, so the small-<c>ldc.i8</c> case is pinned on a
/// hand-built IR function, and the compiled fixture covers the large values csc
/// does encode that way (including one in the very ternary-arm seam the fold fires
/// in). Both must render identically knob-on and knob-off.
/// </description></item>
/// </list>
///
/// <para>The lens is <see cref="StyleOptionDescriptor.ByteDivergent"/>, so it is
/// excluded from <see cref="ByteNeutralityGateTests"/> by construction. Its
/// fidelity claim is narrower and is proven directly: because the fold is
/// opcode-neutral <em>for csc output</em>, the knob-on render of every folded
/// specimen must still compile back <c>Exact</c> — the same anchor the knob-off
/// render earns. That is measured with the product's own compile-back harness
/// (<see cref="FidelityCheck.EvaluateTargets(IReadOnlyList{string}, IReadOnlyList{FidelityCheck.CompileBackTarget}, bool, PrinterOptions?)"/>),
/// not a harness-side reimplementation.</para>
/// </summary>
[Trait("Area", "RoundTrip")]
public sealed class LongLiteralFoldTests
{
    const string KnobId = "prefer-long-literal-suffix";

    static string AssemblyPath => typeof(LongLiteralFoldTests).Assembly.Location;

    static StyleOptionDescriptor Knob => StyleOptionCatalog.Options.Single(o => o.Id == KnobId);

    // Built through the catalog descriptor rather than a raw property set, so these
    // tests exercise the same value-domain plumbing the CLI config resolver uses.
    static PrinterOptions LensOptions => Knob.WithValue(PrinterOptions.Default, "true");

    static string Render(System.Type declaringType, string memberName, PrinterOptions? options = null)
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, t => t.FullName == declaringType.FullName);
        var member = Assert.Single(type.Members, m => m.Name == memberName);
        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null, printerOptions: options);
        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.NotNull(rendered.Text);
        return rendered.Text!.Trim();
    }

    static string Fixture(string memberName, PrinterOptions? options = null)
        => Render(typeof(LongLiteralFoldFixture), memberName, options);

    // ---- 1. the default view is unchanged ----

    public static TheoryData<string, string> DefaultRenders() => new()
    {
        { nameof(LongLiteralFoldFixture.TernaryArms), "public static long TernaryArms(bool c, long tail) => (c ? ((long)10) : ((long)20)) + tail;" },
        { nameof(LongLiteralFoldFixture.SmallReturn), "public static long SmallReturn() => (long)42;" },
        { nameof(LongLiteralFoldFixture.SmallArgument), "public static long SmallArgument() => Consume((long)7);" },
        { nameof(LongLiteralFoldFixture.BinaryOperand), "public static long BinaryOperand(long x) => x * (long)3;" },
        { nameof(LongLiteralFoldFixture.NegativeBinaryOperand), "public static long NegativeBinaryOperand(long x) => x * (long)-1;" },
        { nameof(LongLiteralFoldFixture.Zero), "public static long Zero() => (long)0;" },
        { nameof(LongLiteralFoldFixture.MinusOne), "public static long MinusOne() => (long)-1;" },
        { nameof(LongLiteralFoldFixture.IntMinValue), "public static long IntMinValue() => (long)-2147483648;" },
        { nameof(LongLiteralFoldFixture.IntMaxValue), "public static long IntMaxValue() => (long)2147483647;" },
        { nameof(LongLiteralFoldFixture.LargeReturn), "public static long LargeReturn() => 5000000000;" },
        { nameof(LongLiteralFoldFixture.LargeTernaryArms), "public static long LargeTernaryArms(bool c, long tail) => (c ? 5000000000 : 6000000000) + tail;" },
    };

    [Theory]
    [MemberData(nameof(DefaultRenders))]
    public void Default_IsUnchanged(string member, string expected)
    {
        // The knob defaults to false, so PrinterOptions.Default and "no options at all"
        // must both produce today's text — the opt-in contract, pinned as exact text
        // rather than a containment check so a stray space or paren fails here.
        Assert.Equal(expected, Fixture(member));
        Assert.Equal(expected, Fixture(member, PrinterOptions.Default));
    }

    [Fact]
    public void ReferenceWitness_Default_IsUnchanged()
    {
        Assert.Equal(
            "public static long InlineArraySpanTernaryConditionValue(object a, object b) "
            + "=> (AnyObjectSpan([a, b]) ? ((long)10) : ((long)20)) + Environment.TickCount64;",
            Render(typeof(CfgSampleClass), nameof(CfgSampleClass.InlineArraySpanTernaryConditionValue)));
    }

    // ---- 2. the lens folds exactly the conv.i8-sourced constants ----

    public static TheoryData<string, string> LensRenders() => new()
    {
        // The reference witness's shape: both arms fold, and the folded literal drops
        // the arm parentheses the cast needed without disturbing the enclosing `+`.
        { nameof(LongLiteralFoldFixture.TernaryArms), "public static long TernaryArms(bool c, long tail) => (c ? 10L : 20L) + tail;" },
        { nameof(LongLiteralFoldFixture.SmallReturn), "public static long SmallReturn() => 42L;" },
        { nameof(LongLiteralFoldFixture.SmallArgument), "public static long SmallArgument() => Consume(7L);" },
        { nameof(LongLiteralFoldFixture.BinaryOperand), "public static long BinaryOperand(long x) => x * 3L;" },
        // `-1L` is a UNARY expression, not a primary one; at the right operand of `*`
        // the demand is exactly Unary, so it stays bare and still binds correctly.
        { nameof(LongLiteralFoldFixture.NegativeBinaryOperand), "public static long NegativeBinaryOperand(long x) => x * -1L;" },
        { nameof(LongLiteralFoldFixture.Zero), "public static long Zero() => 0L;" },
        { nameof(LongLiteralFoldFixture.MinusOne), "public static long MinusOne() => -1L;" },
        { nameof(LongLiteralFoldFixture.IntMinValue), "public static long IntMinValue() => -2147483648L;" },
        { nameof(LongLiteralFoldFixture.IntMaxValue), "public static long IntMaxValue() => 2147483647L;" },
    };

    [Theory]
    [MemberData(nameof(LensRenders))]
    public void Lens_FoldsConvI8Constants(string member, string expected)
        => Assert.Equal(expected, Fixture(member, LensOptions));

    [Fact]
    public void ReferenceWitness_Lens_FoldsBothArms()
    {
        // The endpoint the issue names.
        Assert.Equal(
            "public static long InlineArraySpanTernaryConditionValue(object a, object b) "
            + "=> (AnyObjectSpan([a, b]) ? 10L : 20L) + Environment.TickCount64;",
            Render(typeof(CfgSampleClass), nameof(CfgSampleClass.InlineArraySpanTernaryConditionValue), LensOptions));
    }

    // ---- 3. the close negative: a genuine ldc.i8 source is untouched ----

    [Theory]
    [InlineData(nameof(LongLiteralFoldFixture.LargeReturn))]
    [InlineData(nameof(LongLiteralFoldFixture.LargeTernaryArms))]
    [InlineData(nameof(LongLiteralFoldFixture.JustPastIntMaxValue))]
    public void LdcI8Sources_RenderIdenticallyWithTheLensOnOrOff(string member)
    {
        // `LargeReturn`/`LargeTernaryArms` are real `ldc.i8` bodies; `JustPastIntMaxValue`
        // is csc's `ldc.i4 <int.MinValue bits>; conv.u8` zero-extension trick, whose target
        // is UInt64 and so is not the fold's shape either. None may change, and equality —
        // not an expected-text pin — is the claim, so this stays honest even where the
        // shipped render of a shape is imperfect (the conv.u8 body is such a case; its
        // pre-existing spelling is out of this lens's scope).
        Assert.Equal(Fixture(member), Fixture(member, LensOptions));
    }

    [Fact]
    public void SmallLdcI8_RendersIdenticallyWithTheLensOnOrOff()
    {
        // The case the corpus cannot supply: csc never encodes a SMALL long constant as
        // `ldc.i8`, but a hand-authored or non-csc assembly can, and that is a distinct
        // opcode from `ldc.i4.s; conv.i8`. Such a body reaches the printer as a bare
        // Int64 Constant with no Convert over it, so the fold cannot see it — this is the
        // opcode-fidelity guard the issue asks for, and it is structural, not heuristic.
        var off = Print(SmallInt64ConstantReturn(), options: null);
        var on = Print(SmallInt64ConstantReturn(), LensOptions);

        Assert.Equal(off, on);
        Assert.Contains("return 10;", off);
        Assert.DoesNotContain("10L", on);
    }

    [Fact]
    public void SyntheticConvI8_Folds_ProvingTheLdcI8PinIsNotVacuous()
    {
        // Non-vacuity partner of the pin above: the SAME hand-built shape with the
        // `conv.i8` widening in place DOES fold, so the identical-render assertion there
        // is a real discrimination between the two opcode sources rather than a lens that
        // silently does nothing on synthetic input.
        Assert.Contains("return (long)10;", Print(SmallConvertedInt32ConstantReturn(), options: null));
        Assert.Contains("return 10L;", Print(SmallConvertedInt32ConstantReturn(), LensOptions));
    }

    static string Print(IrFunction function, PrinterOptions? options)
    {
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function, options);
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    // `return <Int64 Constant 10>;` — the IR shape a genuine `ldc.i8 10` imports as.
    static IrFunction SmallInt64ConstantReturn()
        => Int64ReturningFunction("SmallLdcI8", new Constant(10L, TypeRef.CoreLib("System", "Int64")));

    // `return <Convert(Int64, Int32 Constant 10)>;` — the shape `ldc.i4.s 10; conv.i8`
    // imports as, and the only one the fold accepts.
    static IrFunction SmallConvertedInt32ConstantReturn()
        => Int64ReturningFunction(
            "SmallConvI8",
            new Pipeline.Convert(
                TypeRef.CoreLib("System", "Int64"),
                isChecked: false,
                isUnsigned: false,
                new Constant(10, TypeRef.CoreLib("System", "Int32"))));

    static IrFunction Int64ReturningFunction(string name, IrExpression value)
    {
        var int64 = TypeRef.CoreLib("System", "Int64");
        var holder = TypeRef.Definition("Synthetic", "Samples", "LongLiterals");
        var body = new BlockContainer();
        var block = new Block();
        body.Add(block);
        block.Add(new Return(value));
        // No locals are declared because none are referenced: a hand-built function must
        // carry the local table its body indexes (AGENTS.md, semantic IR invariants).
        return new IrFunction(
            name,
            holder,
            new MethodSignature(int64, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    // ---- catalog wiring ----

    [Fact]
    public void Knob_IsRegisteredAsANonEndorsedLensWithAToolOwnedConfigKey()
    {
        var knob = Knob;

        Assert.Equal(StyleOptionTier.Lens, knob.Tier);
        Assert.True(knob.ByteDivergent);
        Assert.False(knob.OracleEndorsed);
        Assert.False(knob.CorpusEndorsed);
        Assert.Equal("false", knob.DefaultValue);
        Assert.False(PrinterOptions.Default.PreferLongLiteralSuffix);

        // Tool-owned vocabulary: `dotnet_style_*` is reserved for oracle-endorsed values
        // (StyleOptionCatalogTests.EndorsedValuesWithAConfigKey_UseTheEditorconfigVocabulary).
        Assert.Equal("dotnet_inspect_style_prefer_long_literal_suffix", knob.ConfigKey);
        Assert.True(LensOptions.PreferLongLiteralSuffix);

        // Not part of the "full taste" aggregate, since no value on the axis is endorsed.
        Assert.False(StyleOptionCatalog.ApplyFullTaste(PrinterOptions.Default).PreferLongLiteralSuffix);
    }

    // ---- compile-back: the folded NL output round-trips ----

    // Every specimen the fold fires on, with the harness signature that identifies it.
    static readonly IReadOnlyList<(string Method, string Signature)> FoldedSpecimens =
    [
        (nameof(LongLiteralFoldFixture.TernaryArms), "(corelib:System.Boolean, corelib:System.Int64) -> corelib:System.Int64"),
        (nameof(LongLiteralFoldFixture.SmallReturn), "() -> corelib:System.Int64"),
        (nameof(LongLiteralFoldFixture.SmallArgument), "() -> corelib:System.Int64"),
        (nameof(LongLiteralFoldFixture.BinaryOperand), "(corelib:System.Int64) -> corelib:System.Int64"),
        (nameof(LongLiteralFoldFixture.NegativeBinaryOperand), "(corelib:System.Int64) -> corelib:System.Int64"),
        (nameof(LongLiteralFoldFixture.Zero), "() -> corelib:System.Int64"),
        (nameof(LongLiteralFoldFixture.MinusOne), "() -> corelib:System.Int64"),
        (nameof(LongLiteralFoldFixture.IntMinValue), "() -> corelib:System.Int64"),
        (nameof(LongLiteralFoldFixture.IntMaxValue), "() -> corelib:System.Int64"),
    ];

    [Fact]
    [Trait("Speed", "Slow")]
    public void FoldedOutput_CompilesBackExactly()
    {
        // The lens is byte-divergent as a CLASSIFICATION — a non-csc `ldc.i8 <small>`
        // source would make `NL` and the source opcode disagree — but on csc output the
        // fold is opcode-neutral, and that is the property this proves: the knob-on render
        // recompiles to the original IL exactly, the same anchor the knob-off render earns.
        // Measured with the product's own compile-back harness (the seam the byte-neutrality
        // gate uses), never a harness-side reimplementation.
        var targets = FoldedSpecimens
            .Select(s => new FidelityCheck.CompileBackTarget(
                AssemblyPath, typeof(LongLiteralFoldFixture).FullName!, s.Method, Overload: 0, Signature: s.Signature))
            .ToArray();

        var off = Evaluate(targets, options: null);
        var on = Evaluate(targets, LensOptions);

        foreach (var (method, _) in FoldedSpecimens)
        {
            Assert.True(off[method].Status == FidelityCheck.CompileBackStatus.Exact,
                $"{method}: knob-off compile-back is {off[method].Status} ({off[method].Detail}); the baseline anchor must be Exact.");
            Assert.True(on[method].Status == FidelityCheck.CompileBackStatus.Exact,
                $"{method}: knob-on compile-back is {on[method].Status} ({on[method].Detail}); the folded NL literal did not round-trip.");

            // Byte identity of the two recompiled bodies, so "both Exact" cannot be two
            // different exactness verdicts against different originals.
            Assert.Equal(off[method].RecompiledOpcodes, on[method].RecompiledOpcodes);
        }

        // Non-vacuity: the knob-on renders must actually differ from knob-off, otherwise
        // this would be comparing a render with itself.
        Assert.All(FoldedSpecimens, s => Assert.NotEqual(Fixture(s.Method), Fixture(s.Method, LensOptions)));
    }

    static IReadOnlyDictionary<string, FidelityCheck.CompileBackResult> Evaluate(
        IReadOnlyList<FidelityCheck.CompileBackTarget> targets, PrinterOptions? options)
        => FidelityCheck.EvaluateTargets([AssemblyPath], targets, lowered: false, options)
            .ToDictionary(r => r.Method, r => r, StringComparer.Ordinal);
}
