using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// An equality test between a same-width signed/unsigned integer pair — `ceq`/`bne.un`
// on a (ulong, long) or (nuint, nint) — has no C# common type (CS0034), yet the IL
// compares the raw bits regardless of sign. csc emits this shape from its own
// lowerings (e.g. ulong.CreateTruncating(x) != (long)i in Enum.AreSequentialFromZero),
// not from directly spellable source, so these are constructed at the IR level. The
// printer must reconcile the operands to one C# type so the rendered C# binds:
// equality reinterprets the signed operand as unsigned (a same-width no-op cast),
// while a signed ordering (`clt`/`cgt`) reinterprets the unsigned operand as signed
// to preserve the signed comparison (#1476).
public class MixedSignComparisonTests
{
    static readonly TypeRef ULong = TypeRef.CoreLib("System", "UInt64");
    static readonly TypeRef Long = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");

    static string Render(ComparisonKind kind, TypeRef leftType, TypeRef rightType)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(new Comparison(kind, isUnsigned: false,
            new LoadArgument(0, "a", leftType),
            new LoadArgument(1, "b", rightType))));
        body.Add(block);
        var function = new IrFunction(
            "M", owner,
            new MethodSignature(Bool,
                [new Parameter("a", leftType), new Parameter("b", rightType)],
                HasThis: false, GenericParameterCount: 0),
            [], body);
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void EqualULongLong_ReinterpretsSignedOperandAsUnsigned()
    {
        // `ulong == long` is CS0034; the signed operand reinterprets to unsigned.
        Assert.Contains("a == (ulong)b", Render(ComparisonKind.Equal, ULong, Long));
    }

    [Fact]
    public void NotEqualLongULong_ReinterpretsSignedOperandAsUnsigned()
    {
        // Same reconciliation when the signed operand is on the left.
        Assert.Contains("(ulong)a != b", Render(ComparisonKind.NotEqual, Long, ULong));
    }

    [Fact]
    public void EqualULongULong_LeavesSameTypePairUntouched()
    {
        // Positive canary: a same-type pair needs no cast and must not gain one.
        var output = Render(ComparisonKind.Equal, ULong, ULong);
        Assert.Contains("a == b", output);
        Assert.DoesNotContain("(ulong)", output);
    }

    [Fact]
    public void LessThanULongLong_ReinterpretsUnsignedOperandAsSigned()
    {
        // A signed ordering (`clt`) between a same-width signed/unsigned pair is
        // CS0034. Unlike equality, the unsigned operand reinterprets to SIGNED so
        // the signed comparison the IL performs is preserved.
        var output = Render(ComparisonKind.LessThan, ULong, Long);
        Assert.Contains("(long)a < b", output);
        Assert.DoesNotContain("(ulong)", output);
    }

    [Fact]
    public void GreaterThanLongULong_ReinterpretsUnsignedOperandAsSigned()
    {
        // Same reconciliation when the unsigned operand is on the right.
        var output = Render(ComparisonKind.GreaterThan, Long, ULong);
        Assert.Contains("a > (long)b", output);
        Assert.DoesNotContain("(ulong)", output);
    }

    [Fact]
    public void LessThanLongLong_LeavesSameTypeOrderingUntouched()
    {
        // Positive canary: a same-type ordering pair needs no cast and must not gain one.
        var output = Render(ComparisonKind.LessThan, Long, Long);
        Assert.Contains("a < b", output);
        Assert.DoesNotContain("(long)", output);
    }

    [Fact]
    public void SignedOrdering_NestedUnsignedRenderingOperand_CastsWholeSubtree()
    {
        // A nested mixed-sign arithmetic operand (`long a + ulong b`) renders
        // unsigned (`(ulong)a + b`) though its IR ResultType is signed, so the
        // signed-ordering reconcile must cast the whole rendered subtree —
        // otherwise `ulong < long` stays CS0034.
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var sum = new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false,
            new LoadArgument(0, "a", Long), new LoadArgument(1, "b", ULong));
        var block = new Block(0);
        block.Add(new Return(new Comparison(ComparisonKind.LessThan, isUnsigned: false,
            sum, new LoadArgument(2, "c", Long))));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction("M", owner,
            new MethodSignature(Bool,
                [new Parameter("a", Long), new Parameter("b", ULong), new Parameter("c", Long)],
                HasThis: false, GenericParameterCount: 0),
            [], body);

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("(long)((ulong)a + b) < c", output);
    }
}
