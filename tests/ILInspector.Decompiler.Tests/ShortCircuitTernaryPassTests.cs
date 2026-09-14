using ILInspector.Decompiler.Pipeline;
using System.Collections.Immutable;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ShortCircuitTernaryPassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_double = TypeRef.CoreLib("System", "Double");
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");
    static readonly TypeRef s_truth = TypeRef.CoreLib("Synthetic", "TruthOver");

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

    // A float comparison condition. Negating it flips the ordered/unordered sense,
    // which the printer cannot spell, so the B-form declines it.
    static Comparison FloatCompare()
        => new(ComparisonKind.LessThan, false,
               new LoadArgument(0, "a", s_double), new LoadArgument(1, "b", s_double));

    // A reference (string) equality condition. Negating it takes the Equal→NotEqual
    // dual, which the printer spells with the inverse operator (op_Equality →
    // op_Inequality) — a different call token — so the B-form declines it.
    static Comparison StringEquals()
        => new(ComparisonKind.Equal, false,
               new LoadArgument(0, "a", s_string), new LoadArgument(1, "b", s_string));

    // A user-defined-truthiness condition: the `operator true` call the compiler
    // inserts for a user type used in boolean context. The printer strips it to the
    // bare user-typed receiver `a`, so lifting `a` into `||`/`&&` would rebind the
    // user-defined conditional operator (op_BitwiseOr/op_BitwiseAnd + op_True) and
    // reselect the overload — the pass declines it for both forms.
    static Call UserTruthiness()
        => new(new MethodRef(s_truth, "op_True", s_bool, [s_truth], HasThis: false),
               isVirtual: false, [new LoadArgument(0, "a", s_truth)]);

    // A managed by-ref (`in`/`ref`/`out`) bool dereference: LoadIndirect over an
    // argument typed `ref bool`. csc treats a managed by-ref as non-null and
    // side-effect-free, so `c && refBool` collapses to a branchless `&` — the pass
    // must decline it just like a bare local.
    static LoadIndirect ByRefBoolDeref()
        => new(s_bool, new LoadArgument(1, "y", TypeRef.ByRef(s_bool)));

    // A raw pointer (`bool*`) dereference: LoadIndirect over an argument typed
    // `bool*`. A pointer read can access-violate, so csc keeps the branch for
    // `c && *p` — the pass raises it (opcode-exact), unlike the managed by-ref.
    static LoadIndirect PointerBoolDeref()
        => new(s_bool, new LoadArgument(1, "p", TypeRef.Pointer(s_bool)));

    [Fact]
    public void TrueThenValue_BecomesShortCircuitOr()
    {
        // A-form `flag ? true : y`  →  `flag || y`. A bare bool condition spells as a
        // primitive bool, so lifting it into `||`-operand position binds the
        // primitive operator (no user-operator rebind) and is opcode-exact.
        var result = RunOn(new Conditional(Flag(), Bool(true), Y()));

        var logical = Assert.IsType<LogicalBinary>(result);
        Assert.Equal(LogicalKind.Or, logical.Kind);
        Assert.IsType<LoadArgument>(logical.Left);    // condition unchanged
        Assert.IsType<Comparison>(logical.Right);    // the surviving arm
    }

    [Fact]
    public void UserTruthinessCondition_AForm_IsNotRewritten()
    {
        // A-form `a ? true : y` where `a` is a user type evaluated through
        // `operator true` would become `a || y`. The printer strips the op_True call
        // to the bare user-typed receiver `a`, so `a || y` rebinds to the
        // user-defined conditional `|` (which requires op_True/op_False), reselecting
        // the overload and changing the call tokens and runtime result — decline.
        var result = RunOn(new Conditional(UserTruthiness(), Bool(true), Y()));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void UserTruthinessCondition_BForm_IsNotRewritten()
    {
        // Same rebind hazard in the B-form: `a ? false : y` → `!a && y` would rebind
        // the user-defined conditional `&`. (The B-form also declines it through the
        // integer-comparison negate gate, but the truthiness gate is the reason.)
        var result = RunOn(new Conditional(UserTruthiness(), Bool(false), Y()));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void BoolCondition_BForm_IsNotRewritten()
    {
        // B-form `flag ? false : y` would become `!flag && y`. A bare bool is not a
        // comparison, so negating it is not the proven integer dual and the printer
        // can fold the negation to a different branch — the B-form declines it. (The
        // A-form leaves a bare bool condition alone: `flag || y` is opcode-exact.)
        var result = RunOn(new Conditional(Flag(), Bool(false), Y()));

        Assert.IsType<Conditional>(result);
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
    public void FloatComparisonCondition_BForm_IsNotRewritten()
    {
        // `(a < b) ? false : y` would become `!(a < b) && y`. Negating a float
        // comparison flips its ordered/unordered sense (blt.s → blt.un.s), which
        // the printer cannot spell — decline so the rewrite stays opcode-exact.
        var result = RunOn(new Conditional(FloatCompare(), Bool(false), Y()));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void FloatComparisonCondition_AForm_StillRewrites()
    {
        // `(a < b) ? true : y` → `(a < b) || y`. The A-form does not negate the
        // condition, so there is no ordered/unordered flip; float conditions are safe.
        var result = RunOn(new Conditional(FloatCompare(), Bool(true), Y()));

        var logical = Assert.IsType<LogicalBinary>(result);
        Assert.Equal(LogicalKind.Or, logical.Kind);
        Assert.IsType<Comparison>(logical.Left);   // condition unchanged
    }

    [Fact]
    public void ReferenceEqualityCondition_BForm_IsNotRewritten()
    {
        // `(a == b) ? false : y` would become `!(a == b) && y`. Negating a reference
        // equality takes the Equal→NotEqual dual, which the printer spells with the
        // inverse operator (op_Equality → op_Inequality) — a different call token —
        // so decline to keep the rewrite opcode-exact.
        var result = RunOn(new Conditional(StringEquals(), Bool(false), Y()));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void ReferenceEqualityCondition_AForm_StillRewrites()
    {
        // `(a == b) ? true : y` → `(a == b) || y`. The A-form leaves the condition
        // untouched (no dual), so the operator token is preserved — safe to rewrite.
        var result = RunOn(new Conditional(StringEquals(), Bool(true), Y()));

        var logical = Assert.IsType<LogicalBinary>(result);
        Assert.Equal(LogicalKind.Or, logical.Kind);
        Assert.IsType<Comparison>(logical.Left);   // condition unchanged
    }

    [Fact]
    public void BareStackSlotOperand_IsNotRewritten()
    {
        // Integer-comparison condition (passes the B-form negate gate) so this
        // isolates the operand guard: a residual stack slot renders as a bare
        // synthetic local, so csc collapses `c && S_0` to a branchless `&` just
        // like a named local — decline.
        var result = RunOn(new Conditional(StartLessEqualZero(), Bool(false), new LoadStackSlot(0, s_bool)));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void BareLocalOperand_IsNotRewritten()
    {
        // csc collapses `c && local` to a branchless `&`; leave the ternary so the
        // raise never trades branch IL for branchless. (Integer condition so the
        // decline is attributable to the operand guard, not the negate gate.)
        var result = RunOn(new Conditional(StartLessEqualZero(), Bool(false), new LoadLocal(1, s_bool)));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void BareArgumentOperand_IsNotRewritten()
    {
        // Same branchless hazard for a bare parameter load in the exact B-form
        // (`c ? false : other` would become `c & other`).
        var result = RunOn(new Conditional(StartLessEqualZero(), Bool(false), new LoadArgument(1, "other", s_bool)));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void ByRefDereferenceOperand_BForm_IsNotRewritten()
    {
        // `c ? false : refBool` would become `!c && refBool`. csc treats a managed
        // by-ref (`in`/`ref`/`out`) as non-null and side-effect-free, so it collapses
        // that to a branchless `&` and eagerly dereferences the location the branch
        // had guarded (an observable NullReferenceException divergence on a null
        // by-ref). Decline so the raise never trades branch IL for branchless.
        var result = RunOn(new Conditional(StartLessEqualZero(), Bool(false), ByRefBoolDeref()));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void ByRefDereferenceOperand_AForm_IsNotRewritten()
    {
        // Same branchless hazard for the A-form: `c ? true : refBool` would become
        // `c || refBool`, which csc collapses to a branchless `|`.
        var result = RunOn(new Conditional(StartLessEqualZero(), Bool(true), ByRefBoolDeref()));

        Assert.IsType<Conditional>(result);
    }

    [Fact]
    public void PointerDereferenceOperand_StillRewrites()
    {
        // `c ? false : *p` → `!c && *p`. A raw pointer read can access-violate, so
        // csc keeps the branch for `c && *p` — raising it is opcode-exact. This is
        // the negative boundary of the by-ref guard: the guard fires for a managed
        // by-ref (which collapses branchless), not for a pointer dereference.
        var result = RunOn(new Conditional(StartLessEqualZero(), Bool(false), PointerBoolDeref()));

        var logical = Assert.IsType<LogicalBinary>(result);
        Assert.Equal(LogicalKind.And, logical.Kind);
        Assert.IsType<LoadIndirect>(logical.Right);
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
