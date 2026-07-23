using ILInspector.Decompiler.Pipeline;
using System.Collections.Immutable;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ShortCircuitTernaryPassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");

    static Constant Bool(bool value) => new(value, s_bool);

    // A computed (non-constant, non-bare-load) bool operand for the surviving
    // short-circuit arm: a comparison. csc keeps the branch for a computed
    // operand, so raising the ternary is opcode-exact. A bare local/argument load
    // is the one operand csc would collapse to a branchless `&`/`|`, so those are
    // covered by the decline negatives below, not here.
    static Comparison Y()
        => new(ComparisonKind.GreaterThan, false, new LoadLocal(2, s_int), new Constant(0, s_int));

    // A plain bool condition (negation wraps it in LogicalNot).
    static LoadArgument Flag() => new(0, "flag", s_bool);

    // A comparison condition (negation takes the IL dual, <= becomes >).
    static Comparison StartLessEqualZero()
        => new(ComparisonKind.LessThanOrEqual, false, new LoadArgument(0, "start", s_int), new Constant(0, s_int));

    [Fact]
    public void TrueThenValue_BecomesShortCircuitOr()
    {
        var result = RunOn(new Conditional(Flag(), Bool(true), Y()));

        var logical = Assert.IsType<LogicalBinary>(result);
        Assert.Equal(LogicalKind.Or, logical.Kind);
        Assert.IsType<LoadArgument>(logical.Left);   // condition unchanged
        Assert.IsType<Comparison>(logical.Right);     // the surviving arm
    }

    [Fact]
    public void FalseThenValue_BecomesNegatedShortCircuitAnd()
    {
        var result = RunOn(new Conditional(Flag(), Bool(false), Y()));

        var logical = Assert.IsType<LogicalBinary>(result);
        Assert.Equal(LogicalKind.And, logical.Kind);
        Assert.IsType<LogicalNot>(logical.Left);      // condition negated
        Assert.IsType<Comparison>(logical.Right);
    }

    [Fact]
    public void ValueThenTrue_WhenFalseConstant_IsNotRewritten()
    {
        // C-form `c ? y : true`. csc lays this out with the opposite branch polarity
        // than `!c || y`, so re-forming the operator is not opcode-exact — decline.
        var result = RunOn(new Conditional(Flag(), Y(), Bool(true)));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void ValueThenFalse_WhenFalseConstant_IsNotRewritten()
    {
        // D-form `c ? y : false`. Same branch-polarity mismatch against `c && y`.
        var result = RunOn(new Conditional(Flag(), Y(), Bool(false)));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void WitnessShape_NegatesComparisonToItsDual()
    {
        // `start <= 0 ? false : y`  →  `start > 0 && y` (the flagship witness).
        var result = RunOn(new Conditional(StartLessEqualZero(), Bool(false), Y()));

        var logical = Assert.IsType<LogicalBinary>(result);
        Assert.Equal(LogicalKind.And, logical.Kind);
        var comparison = Assert.IsType<Comparison>(logical.Left);
        Assert.Equal(ComparisonKind.GreaterThan, comparison.Kind);
        Assert.IsType<Comparison>(logical.Right);
    }

    [Fact]
    public void BareLocalOperand_IsNotRewritten()
    {
        // csc collapses `!c && local` to a branchless `&`; leave the ternary so the
        // raise never trades branch IL for branchless.
        var result = RunOn(new Conditional(Flag(), Bool(false), new LoadLocal(1, s_bool)));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void BareArgumentOperand_IsNotRewritten()
    {
        // Same branchless hazard for a bare parameter load in the exact B-form
        // (`c ? false : other` would become `!c & other`).
        var result = RunOn(new Conditional(Flag(), Bool(false), new LoadArgument(1, "other", s_bool)));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void BothArmsConstant_IsNotRewritten()
    {
        // `c ? true : false` is an identity, owned by other folds — leave it.
        var result = RunOn(new Conditional(Flag(), Bool(true), Bool(false)));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void BothArmsConstantInverted_IsNotRewritten()
    {
        var result = RunOn(new Conditional(Flag(), Bool(false), Bool(true)));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void NonConstantArms_AreNotRewritten()
    {
        var result = RunOn(new Conditional(Flag(), new LoadLocal(0, s_bool), Y()));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void NonBooleanConstantArm_IsNotRewritten()
    {
        // `c ? 1 : x` is an int-valued select, not a short-circuit boolean.
        var conditional = new Conditional(Flag(), new Constant(1, s_int), new LoadLocal(1, s_int));
        var function = Wrap(conditional, s_int, [s_int, s_int]);

        new ShortCircuitTernaryPass().Run(function, PassContext.None);

        var ret = Assert.IsType<Return>(function.Body.Blocks[0].Children[0]);
        Assert.IsType<Conditional>(ret.Value);
        function.CheckInvariant();
    }

    static IrExpression RunOn(Conditional conditional)
    {
        var function = Wrap(conditional, s_bool, [s_bool, s_bool, s_int]);

        new ShortCircuitTernaryPass().Run(function, PassContext.None);

        var ret = Assert.IsType<Return>(function.Body.Blocks[0].Children[0]);
        function.CheckInvariant();
        return ret.Value!;
    }

    static IrFunction Wrap(IrExpression value, TypeRef returnType, ImmutableArray<TypeRef> locals)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(value));
        container.Add(block);
        var signature = new MethodSignature(
            returnType, [new Parameter("start", s_int)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, locals, container);
    }
}
