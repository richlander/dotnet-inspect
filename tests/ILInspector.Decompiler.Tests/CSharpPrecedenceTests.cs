using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// The precedence model (#2376 phase 1): node-shape precedence rows and the
// Rendered.At wrapping rule. Structural rows only — the composed-string forms
// (BoolToIntegerText's ternary, CoerceText's casts) are pinned at their
// consumers, where the Rendered values are produced.
public class CSharpPrecedenceTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");

    static IrExpression A => new LoadArgument(0, "a", Int32);
    static IrExpression B => new LoadArgument(1, "b", Int32);
    static IrExpression C => new LoadArgument(2, "c", Bool);

    // A minimal two-component tuple switch, matching how TupleSwitchExpressionPass
    // shapes it: one relational arm plus a trailing default.
    static IrExpression TupleSwitch
    {
        get
        {
            var arm = new TupleSwitchExpressionArm(
                subpatterns: [new PositionalPatternSubpattern(ComparisonKind.GreaterThan), new PositionalPatternSubpattern(ComparisonKind.GreaterThan)],
                constants: [new Constant(0, Int32), new Constant(0, Int32)],
                value: new Constant(1, Int32));
            var defaultArm = new TupleSwitchExpressionArm(subpatterns: [], constants: [], value: new Constant(2, Int32));
            return new TupleSwitchExpression([A, B], [arm, defaultArm]);
        }
    }

    public static TheoryData<IrExpression, Precedence> Rows => new()
    {
        { new Conditional(C, A, B), Precedence.Conditional },
        // Issue #2867 follow-up: TupleSwitchExpression must report the same
        // precedence as its SwitchExpression/UnionSwitchExpression siblings —
        // Of() previously fell through to the Primary default for this node
        // type, so every RenderedExpression consumer under-parenthesized it.
        { TupleSwitch, Precedence.Conditional },
        { new Coalesce(A, B), Precedence.NullCoalescing },
        { new LogicalBinary(LogicalKind.Or, C, C), Precedence.ConditionalOr },
        { new LogicalBinary(LogicalKind.And, C, C), Precedence.ConditionalAnd },
        { new Binary(BinaryKind.Or, false, false, A, B), Precedence.BitwiseOr },
        { new Binary(BinaryKind.Xor, false, false, A, B), Precedence.BitwiseXor },
        { new Binary(BinaryKind.And, false, false, A, B), Precedence.BitwiseAnd },
        { new Comparison(ComparisonKind.Equal, false, A, B), Precedence.Equality },
        { new Comparison(ComparisonKind.LessThan, false, A, B), Precedence.Relational },
        { new Binary(BinaryKind.ShiftLeft, false, false, A, B), Precedence.Shift },
        { new Binary(BinaryKind.Add, false, false, A, B), Precedence.Additive },
        { new Binary(BinaryKind.Multiply, false, false, A, B), Precedence.Multiplicative },
        { new LogicalNot(C), Precedence.Unary },
        { new Pipeline.Convert(Int32, false, false, A), Precedence.Unary },
        // Coerce reports its LOOSEST spelling (the truthiness ternary) —
        // conservative wraps are never invalid; exact values travel as
        // Rendered from the composing helpers.
        { new Coerce(Int32, A), Precedence.Conditional },
        { new Constant(1, Int32), Precedence.Primary },
        { new LoadArgument(0, "a", Int32), Precedence.Primary },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Of_ReportsTheStructuralLevel(IrExpression node, Precedence expected)
        => Assert.Equal(expected, CSharpPrecedence.Of(node));

    [Fact]
    public void RenderedAt_WrapsIffContextBindsTighter()
    {
        var ternary = new Rendered("b ? 1 : 0", Precedence.Conditional);
        // The coalesce right demands NullCoalescing — the #2345 residual class.
        Assert.Equal("(b ? 1 : 0)", ternary.At(Precedence.NullCoalescing));
        // A statement/argument position demands nothing.
        Assert.Equal("b ? 1 : 0", ternary.At(Precedence.Assignment));
        // Equal precedence renders bare — the context passes the TIGHTENED
        // demand for its hazard side, so equality means the safe side.
        Assert.Equal("b ? 1 : 0", ternary.At(Precedence.Conditional));
        var cast = new Rendered("(uint)(b ? 1 : 0)", Precedence.Unary);
        Assert.Equal("(uint)(b ? 1 : 0)", cast.At(Precedence.NullCoalescing));
    }
}
