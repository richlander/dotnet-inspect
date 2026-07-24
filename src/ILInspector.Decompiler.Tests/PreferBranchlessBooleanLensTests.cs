using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The opt-in tier-3 style lens
/// <see cref="PrinterOptions.PreferBranchlessBoolean"/> (#3138): re-renders a flat
/// guarded boolean return with a constant arm the default view leaves as
/// <c>if (c) return A; return B;</c> (because the short-circuit fold is not
/// opcode-faithful for a bare-load operand — #3114) into the compact short-circuit
/// "bool hack".
///
/// The knob is byte-divergent (csc recompiles the short-circuit form branchless)
/// but unconditionally behavior-preserving: it keeps the same condition, surviving
/// operand, and short-circuit order. These tests pin BOTH the structural rewrite
/// AND, via <see cref="BoolPairs"/>, executed runtime equivalence over every
/// boolean input. They also pin the two BEHAVIOR guards the lens keeps
/// (user-defined truthiness and a managed by-ref operand) and the deterministic
/// ternary-wins precedence when both lenses are enabled.
/// </summary>
[Trait("Area", "RoundTrip")]
public sealed class PreferBranchlessBooleanLensTests
{
    static string AssemblyPath => typeof(PreferBranchlessBooleanLensTests).Assembly.Location;

    static readonly PrinterOptions LensOptions = new() { PreferBranchlessBoolean = true };

    static ApiType Specimen()
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var api = ApiSurfaceExtractor.Extract(pe);
        return Assert.Single(api.Types, t => t.FullName == typeof(PreferBranchlessBooleanSpecimen).FullName);
    }

    static string Render(string memberName, PrinterOptions? options = null)
    {
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == memberName);
        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null, printerOptions: options);
        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.NotNull(rendered.Text);
        return rendered.Text!;
    }

    [Fact]
    public void AndTailGuard_DefaultFlat_LensFoldsToAnd()
    {
        var defaultText = Render(nameof(PreferBranchlessBooleanSpecimen.AndTailGuard));
        Assert.Contains("return false;", defaultText);
        Assert.DoesNotContain("&&", defaultText);

        var lensText = Render(nameof(PreferBranchlessBooleanSpecimen.AndTailGuard), LensOptions);
        Assert.Contains("=> a && b;", lensText);
        Assert.DoesNotContain("if (", lensText);
    }

    [Fact]
    public void OrTailGuard_LensFoldsToNegatedOr()
    {
        var lensText = Render(nameof(PreferBranchlessBooleanSpecimen.OrTailGuard), LensOptions);
        Assert.Contains("=> !a || b;", lensText);
        Assert.DoesNotContain("if (", lensText);
    }

    [Fact]
    public void OrThenGuard_LensFoldsToOr()
    {
        var lensText = Render(nameof(PreferBranchlessBooleanSpecimen.OrThenGuard), LensOptions);
        Assert.Contains("=> a || b;", lensText);
        Assert.DoesNotContain("if (", lensText);
    }

    [Fact]
    public void AndThenGuard_LensFoldsToNegatedAnd()
    {
        var lensText = Render(nameof(PreferBranchlessBooleanSpecimen.AndThenGuard), LensOptions);
        Assert.Contains("=> !a && b;", lensText);
        Assert.DoesNotContain("if (", lensText);
    }

    [Fact]
    public void BothVariable_LensNoOp_NoConstantArmToLift()
    {
        // No constant arm, so there is no operator to lift the condition into: the
        // branchless "bool hack" does not apply and the lens leaves the flat guard
        // byte-identical to the default (unlike the ternary lens, which would fold).
        var defaultText = Render(nameof(PreferBranchlessBooleanSpecimen.BothVariable));
        var lensText = Render(nameof(PreferBranchlessBooleanSpecimen.BothVariable), LensOptions);
        Assert.Equal(defaultText, lensText);
        Assert.Contains("if (", lensText);
    }

    [Fact]
    public void Plain_LensNoOp_ByteIdenticalToDefault()
    {
        var defaultText = Render(nameof(PreferBranchlessBooleanSpecimen.Plain));
        var lensText = Render(nameof(PreferBranchlessBooleanSpecimen.Plain), LensOptions);
        Assert.Equal(defaultText, lensText);
    }

    [Fact]
    public void UserTruthinessGuard_LensDeclines_StaysFlat()
    {
        // Lifting a user-truthiness condition into `t && b` rebinds to a
        // user-defined `&` that does not exist and changes the runtime result, so
        // the lens must decline and leave the flat guard exactly as the default.
        var defaultText = Render(nameof(PreferBranchlessBooleanSpecimen.UserTruthinessGuard));
        var lensText = Render(nameof(PreferBranchlessBooleanSpecimen.UserTruthinessGuard), LensOptions);
        Assert.Equal(defaultText, lensText);
        Assert.Contains("return false;", lensText);
        Assert.DoesNotContain("&&", lensText);
    }

    [Fact]
    public void ByRefOperandGuard_LensDeclines_StaysFlat()
    {
        // The surviving operand is a managed by-ref dereference; csc's branchless
        // lowering would eagerly dereference it on the guarded path (null-by-ref NRE
        // divergence), so the lens must decline and leave the flat guard.
        var defaultText = Render(nameof(PreferBranchlessBooleanSpecimen.ByRefOperandGuard));
        var lensText = Render(nameof(PreferBranchlessBooleanSpecimen.ByRefOperandGuard), LensOptions);
        Assert.Equal(defaultText, lensText);
        Assert.Contains("return p;", lensText);
        Assert.DoesNotContain("||", lensText);
    }

    [Fact]
    public void TernaryWins_WhenBothLensesEnabled()
    {
        // Deterministic precedence: the oracle-endorsed ternary consumes the shape
        // first, so the non-endorsed branchless form never fires. OrThenGuard is a
        // case where the two lenses DIFFER (`a || b` vs `a ? true : b`).
        var both = new PrinterOptions
        {
            PreferConditionalExpressionReturn = true,
            PreferBranchlessBoolean = true,
        };
        var text = Render(nameof(PreferBranchlessBooleanSpecimen.OrThenGuard), both);
        Assert.Contains("=> a ? true : b;", text);
        Assert.DoesNotContain("||", text);
    }

    // Executed behavioral-equivalence gate for the tier-3 lens: the specimen
    // methods run their compiled (source) semantics; the short-circuit form is the
    // lens's rendering. Proving `method(inputs) == (bool hack over inputs)` for
    // every boolean input pins that the rewrite preserves behavior — the contract
    // this tier trades byte-fidelity for.
    [Theory]
    [MemberData(nameof(BoolPairs))]
    public void AndTailGuard_ShortCircuitIsBehaviorEquivalent(bool a, bool b)
        => Assert.Equal(a && b, PreferBranchlessBooleanSpecimen.AndTailGuard(a, b));

    [Theory]
    [MemberData(nameof(BoolPairs))]
    public void OrTailGuard_ShortCircuitIsBehaviorEquivalent(bool a, bool b)
        => Assert.Equal(!a || b, PreferBranchlessBooleanSpecimen.OrTailGuard(a, b));

    [Theory]
    [MemberData(nameof(BoolPairs))]
    public void OrThenGuard_ShortCircuitIsBehaviorEquivalent(bool a, bool b)
        => Assert.Equal(a || b, PreferBranchlessBooleanSpecimen.OrThenGuard(a, b));

    [Theory]
    [MemberData(nameof(BoolPairs))]
    public void AndThenGuard_ShortCircuitIsBehaviorEquivalent(bool a, bool b)
        => Assert.Equal(!a && b, PreferBranchlessBooleanSpecimen.AndThenGuard(a, b));

    public static TheoryData<bool, bool> BoolPairs()
    {
        var data = new TheoryData<bool, bool>();
        foreach (var a in new[] { false, true })
        foreach (var b in new[] { false, true })
            data.Add(a, b);
        return data;
    }
}
