using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The opt-in tier-3 style lens
/// <see cref="PrinterOptions.PreferConditionalExpressionReturn"/> (#3138):
/// re-renders a flat guarded boolean return the default view leaves as
/// <c>if (c) return A; return B;</c> (because no short-circuit fold is
/// opcode-faithful — #3114) into the IDE0046 conditional expression
/// <c>return c ? A : B;</c>.
///
/// The knob is byte-divergent (the ternary recompiles to a different branch
/// stream) but unconditionally behavior-preserving: the ternary is the canonical
/// desugaring of the guarded return. These tests pin BOTH properties — the
/// structural rewrite AND, via <see cref="Equivalence"/>, executed runtime
/// equivalence over every boolean input.
/// </summary>
[Trait("Area", "RoundTrip")]
public sealed class PreferConditionalReturnLensTests
{
    static string AssemblyPath => typeof(PreferConditionalReturnLensTests).Assembly.Location;

    static readonly PrinterOptions LensOptions = new() { PreferConditionalExpressionReturn = true };

    static ApiType Specimen()
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var api = ApiSurfaceExtractor.Extract(pe);
        return Assert.Single(api.Types, t => t.FullName == typeof(PreferConditionalReturnSpecimen).FullName);
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
    public void NeitherOr_DefaultLeavesGuardedReturnFlat()
    {
        var text = Render(nameof(PreferConditionalReturnSpecimen.NeitherOr));
        Assert.Contains("return false;", text);
        Assert.Contains("return c;", text);
        Assert.DoesNotContain("?", text);
    }

    [Fact]
    public void NeitherOr_LensRewritesToConditionalExpression()
    {
        var text = Render(nameof(PreferConditionalReturnSpecimen.NeitherOr), LensOptions);
        // csc compiled `a && b` to the branchless `a & b`; the ternary keeps it.
        Assert.Contains("a & b ? false : c", text);
        // The flat guarded pair is gone.
        Assert.DoesNotContain("if (", text);
        Assert.DoesNotContain("return false;", text);
    }

    [Fact]
    public void GuardBothVariable_DefaultLeavesGuardedReturnFlat()
    {
        var text = Render(nameof(PreferConditionalReturnSpecimen.GuardBothVariable));
        Assert.Contains("return b;", text);
        Assert.Contains("return c;", text);
        Assert.DoesNotContain("?", text);
    }

    [Fact]
    public void GuardBothVariable_LensRewritesToConditionalExpression()
    {
        var text = Render(nameof(PreferConditionalReturnSpecimen.GuardBothVariable), LensOptions);
        Assert.Contains("a ? b : c", text);
        Assert.DoesNotContain("if (", text);
    }

    [Fact]
    public void OrShapedGuard_LensKeepsLiteralArm_NotSimplifiedToOr()
    {
        // The default leaves this flat (no opcode-faithful fold); the lens rewrites
        // it to the IDE0046 form `a ? true : b` and does NOT further collapse to
        // `a || b` (that IDE0075 simplification is a separate, deferred knob).
        var defaultText = Render(nameof(PreferConditionalReturnSpecimen.OrShapedGuard));
        Assert.Contains("return true;", defaultText);
        Assert.DoesNotContain("?", defaultText);

        var lensText = Render(nameof(PreferConditionalReturnSpecimen.OrShapedGuard), LensOptions);
        Assert.Contains("a ? true : b", lensText);
        Assert.DoesNotContain("||", lensText);
    }

    [Fact]
    public void And_LensNoOp_ByteIdenticalToDefault()
    {
        // No guarded return: the lens finds nothing and leaves the output alone.
        var defaultText = Render(nameof(PreferConditionalReturnSpecimen.And));
        var lensText = Render(nameof(PreferConditionalReturnSpecimen.And), LensOptions);
        Assert.Equal(defaultText, lensText);
        Assert.DoesNotContain("?", lensText);
    }

    [Fact]
    public void AndShapedGuard_LensSpellsShortCircuit_AndStaysValid()
    {
        // `if (c) return value; return false;` -> conditional `c ? value : false`,
        // which the printer idiomatically renders as `c && value`. Behavior-faithful
        // and valid C# for a primitive-bool condition.
        var defaultText = Render(nameof(PreferConditionalReturnSpecimen.AndShapedGuard));
        Assert.Contains("return false;", defaultText);

        var lensText = Render(nameof(PreferConditionalReturnSpecimen.AndShapedGuard), LensOptions);
        Assert.Contains("c && value", lensText);
        Assert.DoesNotContain("if (", lensText);
    }

    [Fact]
    public void UserTruthinessGuard_LensDeclines_StaysFlatAndValid()
    {
        // The condition is a user-defined operator-true call with no user `&`, so a
        // short-circuit lift would be uncompilable. The lens must decline and leave
        // the flat guard exactly as the default renders it.
        var defaultText = Render(nameof(PreferConditionalReturnSpecimen.UserTruthinessGuard));
        var lensText = Render(nameof(PreferConditionalReturnSpecimen.UserTruthinessGuard), LensOptions);
        Assert.Equal(defaultText, lensText);
        Assert.Contains("return false;", lensText);
        Assert.DoesNotContain("&&", lensText);
    }

    // Executed behavioral-equivalence gate for the tier-3 lens: the specimen
    // methods run their compiled (source) semantics; the ternary is the lens's
    // rendering. Proving `method(inputs) == (ternary over inputs)` for every
    // boolean input pins that the lens rewrite preserves behavior — the contract
    // this tier trades byte-fidelity for.
    [Theory]
    [MemberData(nameof(BoolTriples))]
    public void NeitherOr_TernaryIsBehaviorEquivalent(bool a, bool b, bool c)
        => Assert.Equal((a && b) ? false : c, PreferConditionalReturnSpecimen.NeitherOr(a, b, c));

    [Theory]
    [MemberData(nameof(BoolTriples))]
    public void GuardBothVariable_TernaryIsBehaviorEquivalent(bool a, bool b, bool c)
        => Assert.Equal(a ? b : c, PreferConditionalReturnSpecimen.GuardBothVariable(a, b, c));

    public static TheoryData<bool, bool, bool> BoolTriples()
    {
        var data = new TheoryData<bool, bool, bool>();
        foreach (var a in new[] { false, true })
        foreach (var b in new[] { false, true })
        foreach (var c in new[] { false, true })
            data.Add(a, b, c);
        return data;
    }
}
