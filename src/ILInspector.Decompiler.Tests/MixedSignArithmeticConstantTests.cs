using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// #1467: mixed-sign arithmetic over two integer constants where the signed side is
// out of the unsigned range is a C# *constant expression*, evaluated in a checked
// context — so the out-of-range cast and the arithmetic overflow are CS0220/CS0221
// errors unless the whole expression is `unchecked(...)`. (The negative-constant
// shape is reachable via IL but not natural C#, which folds `(uint)(-1)`, so these
// are synthetic-IR fixtures.) The rendered forms below were verified to compile.
public class MixedSignArithmeticConstantTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_uint = TypeRef.CoreLib("System", "UInt32");
    static readonly TypeRef s_ulong = TypeRef.CoreLib("System", "UInt64");

    static string Render(IrExpression value, TypeRef? returnType = null)
    {
        var block = new Block(0);
        block.Add(new Return(value));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(returnType ?? s_uint, [new Parameter("x", s_uint)], HasThis: false, GenericParameterCount: 0),
            ImmutableArray<TypeRef>.Empty,
            body);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static Binary Arith(BinaryKind kind, IrExpression left, IrExpression right)
        => new(kind, isChecked: false, isUnsigned: false, left, right);

    [Fact]
    public void NegativeConstantPlusUnsigned_WrapsWholeBinaryInUnchecked()
        => Assert.Equal(
            "return unchecked((uint)-1 + 1);",
            Render(Arith(BinaryKind.Add, new Constant(-1, s_int), new Constant(1u, s_uint))));

    [Fact]
    public void UnsignedMinusNegativeConstant_WrapsWholeBinaryInUnchecked()
        => Assert.Equal(
            "return unchecked(1 - (uint)-1);",
            Render(Arith(BinaryKind.Subtract, new Constant(1u, s_uint), new Constant(-1, s_int))));

    [Fact]
    public void NegativeConstantTimesUnsigned_WrapsWholeBinaryInUnchecked()
        => Assert.Equal(
            "return unchecked((uint)-2 * 3);",
            Render(Arith(BinaryKind.Multiply, new Constant(-2, s_int), new Constant(3u, s_uint))));

    // A negative constant added to a non-constant unsigned operand is not a constant
    // expression, so it cannot overflow at compile time: the per-operand
    // `unchecked((uint)-1)` cast is enough and the whole binary is not wrapped.
    [Fact]
    public void NegativeConstantPlusVariable_KeepsPerOperandUncheckedOnly()
        => Assert.Equal(
            "return unchecked((uint)-1) + x;",
            Render(Arith(BinaryKind.Add, new Constant(-1, s_int), new LoadArgument(0, "x", s_uint))));

    // Nested constant arithmetic: only the OUTERMOST constant binary wraps, so the
    // whole constant expression is covered once (no nested `unchecked`), and the
    // surrounding constant `+`/`*` cannot overflow at compile time.
    [Fact]
    public void NestedConstantArithmetic_WrapsOnlyOutermost_Left()
        => Assert.Equal(
            "return unchecked(((uint)-1 * 2) + 3);",
            Render(Arith(BinaryKind.Add,
                Arith(BinaryKind.Multiply, new Constant(-1, s_int), new Constant(2u, s_uint)),
                new Constant(3u, s_uint))));

    [Fact]
    public void NestedConstantArithmetic_WrapsOnlyOutermost_Right()
        => Assert.Equal(
            "return unchecked((uint)-1 + (2 * (uint)-3));",
            Render(Arith(BinaryKind.Add,
                new Constant(-1, s_int),
                Arith(BinaryKind.Multiply, new Constant(2u, s_uint), new Constant(-3, s_int)))));

    // An out-of-range constant reinterpreted by an explicit `(uint)` cast (not the
    // mixed-sign reconciliation) is also covered: the whole constant binary wraps so
    // the surrounding `+ 1` cannot overflow at compile time.
    [Fact]
    public void OutOfRangeConvertCastConstant_WholeBinaryIsUnchecked()
    {
        var output = Render(Arith(BinaryKind.Add,
            new ILInspector.Decompiler.Pipeline.Convert(s_uint, isChecked: false, isUnsigned: false, new Constant(-1, s_int)),
            new Constant(1, s_int)));

        Assert.StartsWith("return unchecked(", output);
        Assert.Contains("(uint)", output);
    }

    [Fact]
    public void PureUnsignedOverflowingAdd_WrapsWholeBinaryInUnchecked()
        => Assert.Equal(
            "return unchecked(4294967295u + 1u);",
            Render(Arith(BinaryKind.Add, new Constant(uint.MaxValue, s_uint), new Constant(1u, s_uint))));

    [Fact]
    public void PureUnsignedOverflowingSubtract_WrapsWholeBinaryInUnchecked()
        => Assert.Equal(
            "return unchecked(0u - 1u);",
            Render(Arith(BinaryKind.Subtract, new Constant(0u, s_uint), new Constant(1u, s_uint))));

    [Fact]
    public void PureUnsignedNonOverflowingAdd_StaysPlain()
        => Assert.Equal(
            "return 4294967295 + 0;",
            Render(Arith(BinaryKind.Add, new Constant(uint.MaxValue, s_uint), new Constant(0u, s_uint))));

    [Fact]
    public void PureSignedOverflowingAdd_WrapsWholeBinaryInUnchecked()
        => Assert.Equal(
            "return unchecked(2147483647 + 1);",
            Render(Arith(BinaryKind.Add, new Constant(int.MaxValue, s_int), new Constant(1, s_int)), s_int));

    [Fact]
    public void PureULongOverflowingAdd_WrapsWholeBinaryInUnchecked()
        => Assert.Equal(
            "return unchecked(18446744073709551615UL + 1UL);",
            Render(Arith(BinaryKind.Add, new Constant(ulong.MaxValue, s_ulong), new Constant(1UL, s_ulong)), s_ulong));
}
