using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// #1425: when an overflow-checked op (add.ovf/sub.ovf/mul.ovf/conv.ovf.*) is
// spelled with `checked(...)`, the printer pushes a checked context around the
// operands. A plain (non-overflow) arithmetic or narrowing-conversion child nested
// inside that lexical `checked` region must be wrapped in `unchecked(...)`, or it
// silently acquires `.ovf` semantics on recompile (e.g. the inner `mul` of
// `checked(a + (b * 2))` flips to `mul.ovf`). This is the symmetric insert to the
// existing checked-collapse: the checked path drops a redundant nested wrapper,
// this path inserts the missing unchecked one. All rendered forms compile.
public class CheckedUncheckedInsertTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_long = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef s_short = TypeRef.CoreLib("System", "Int16");

    static string Render(IrExpression value)
    {
        var block = new Block(0);
        block.Add(new Return(value));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                s_int,
                [
                    new Parameter("a", s_int),
                    new Parameter("b", s_int),
                    new Parameter("c", s_int),
                    new Parameter("d", s_long),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            ImmutableArray<TypeRef>.Empty,
            body);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static LoadArgument A => new(0, "a", s_int);
    static LoadArgument B => new(1, "b", s_int);
    static LoadArgument C => new(2, "c", s_int);
    static LoadArgument D => new(3, "d", s_long);

    static Binary Checked(BinaryKind kind, IrExpression left, IrExpression right)
        => new(kind, isChecked: true, isUnsigned: false, left, right);

    static Binary Plain(BinaryKind kind, IrExpression left, IrExpression right)
        => new(kind, isChecked: false, isUnsigned: false, left, right);

    static ILInspector.Decompiler.Pipeline.Convert PlainConvert(TypeRef target, IrExpression operand)
        => new(target, isChecked: false, isUnsigned: false, operand);

    static ILInspector.Decompiler.Pipeline.Convert CheckedConvert(TypeRef target, IrExpression operand)
        => new(target, isChecked: true, isUnsigned: false, operand);

    // checked(a + unchecked(b * 2)) — the inner plain mul must keep an unchecked
    // wrapper or it recompiles as mul.ovf.
    [Fact]
    public void CheckedAdd_PlainMulChild_WrapsInnerUnchecked()
        => Assert.Equal(
            "return checked(a + unchecked(b * 2));",
            Render(Checked(BinaryKind.Add, A, Plain(BinaryKind.Multiply, B, new Constant(2, s_int)))));

    // checked(unchecked(a * b) + c) — plain mul on the left operand.
    [Fact]
    public void CheckedAdd_PlainMulLeftChild_WrapsInnerUnchecked()
        => Assert.Equal(
            "return checked(unchecked(a * b) + c);",
            Render(Checked(BinaryKind.Add, Plain(BinaryKind.Multiply, A, B), C)));

    // checked(a + unchecked(b - c)) — plain sub nested in checked add.
    [Fact]
    public void CheckedAdd_PlainSubChild_WrapsInnerUnchecked()
        => Assert.Equal(
            "return checked(a + unchecked(b - c));",
            Render(Checked(BinaryKind.Add, A, Plain(BinaryKind.Subtract, B, C))));

    // checked(a + unchecked((short)b)) — a plain narrowing conversion nested in a
    // checked region recompiles to conv.ovf.i2 unless wrapped.
    [Fact]
    public void CheckedAdd_PlainNarrowingConvertChild_WrapsInnerUnchecked()
        => Assert.Equal(
            "return checked(a + unchecked((short)b));",
            Render(Checked(BinaryKind.Add, A, PlainConvert(s_short, B))));

    // checked((short)unchecked(a + b)) — the outer narrowing conversion is checked
    // (conv.ovf.i2); its plain add operand must keep an unchecked wrapper.
    [Fact]
    public void CheckedNarrowingConvert_PlainAddChild_WrapsInnerUnchecked()
        => Assert.Equal(
            "return checked((short)unchecked(a + b));",
            Render(CheckedConvert(s_short, Plain(BinaryKind.Add, A, B))));

    // Positive canary: an implicit widening conversion (int -> long) never flips to
    // conv.ovf inside checked, so it is left bare — no unchecked wrapper (the cast's
    // usual operand parens are unchanged by #1425).
    [Fact]
    public void CheckedAdd_PlainWideningConvertChild_StaysBare()
        => Assert.Equal(
            "return checked(d + ((long)a));",
            Render(Checked(BinaryKind.Add, D, PlainConvert(s_long, A))));

    // Positive canary: an all-checked expression keeps a single outer wrapper and
    // does not gain any spurious unchecked — every child is itself checked.
    [Fact]
    public void CheckedAdd_AllCheckedChildren_SingleWrapperNoUnchecked()
        => Assert.Equal(
            "return checked(a + (b * c));",
            Render(Checked(BinaryKind.Add, A, Checked(BinaryKind.Multiply, B, C))));

    // Positive canary: a plain add with no enclosing checked context is untouched.
    [Fact]
    public void PlainAdd_NoCheckedContext_Untouched()
        => Assert.Equal(
            "return a + b;",
            Render(Plain(BinaryKind.Add, A, B)));

    // Positive canary: nested checked-in-checked still collapses to a single
    // wrapper (the inner add keeps its usual operand parens, unchanged by #1425).
    [Fact]
    public void CheckedAdd_NestedCheckedChild_CollapsesToSingleWrapper()
        => Assert.Equal(
            "return checked((a + b) + c);",
            Render(Checked(BinaryKind.Add, Checked(BinaryKind.Add, A, B), C)));
}
