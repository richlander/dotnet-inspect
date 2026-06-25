using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// An equality test between a same-width signed/unsigned integer pair — `ceq`/`bne.un`
// on a (ulong, long) or (nuint, nint) — has no C# common type (CS0034), yet the IL
// compares the raw bits regardless of sign. csc emits this shape from its own
// lowerings (e.g. ulong.CreateTruncating(x) != (long)i in Enum.AreSequentialFromZero),
// not from directly spellable source, so these are constructed at the IR level. The
// printer must reinterpret the signed operand as unsigned (a same-width no-op cast)
// so the rendered C# binds.
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
    public void LessThanULongLong_LeavesOrderingUntouched()
    {
        // Scope boundary: a signed ordering comparison must NOT be reinterpreted as
        // unsigned (that would change its meaning). Only equality is reconciled here;
        // the ordering/compound CS0034 variants are tracked separately.
        var output = Render(ComparisonKind.LessThan, ULong, Long);
        Assert.DoesNotContain("(ulong)b", output);
    }
}
