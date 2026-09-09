using ILInspector.Decompiler.Pipeline;
using System.Collections.Immutable;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Guards on <c>BooleanFoldingPass.FoldGuardReturn</c> for the mixed constant-arm
/// shape <c>if (c) return A; return B;</c> where exactly one arm is a bool constant.
/// That fold re-forms a short-circuit <c>&amp;&amp;</c>/<c>||</c> by lifting the condition
/// into the operator's left operand (optionally negated) and keeping the other arm
/// as the surviving right operand — the same lift <see cref="ShortCircuitTernaryPass"/>
/// performs for the nested constant-arm ternary — so it must carry the same
/// opcode-fidelity guards (shared through <c>ShortCircuitFidelity</c>) or it emits C#
/// whose recompilation diverges in branch opcodes, operator tokens, or runtime
/// behavior (#3114). Compiler-shape evidence for the five hazards is the repro table
/// on #3114 (`--dump` at base `e15f41c4`); the corpus compile-back gates guard the
/// exact forms continuously. These fixtures pin the pass-level decline/accept.
/// </summary>
[Trait("Area", "Pass")]
public class FoldGuardReturnFidelityTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_double = TypeRef.CoreLib("System", "Double");
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");
    static readonly TypeRef s_truth = TypeRef.CoreLib("Synthetic", "TruthOver");
    static readonly TypeRef s_holder = TypeRef.CoreLib("Synthetic", "Holder");

    static Constant Bool(bool value) => new(value, s_bool);

    // A computed (non-bare-load) bool operand: a comparison. csc keeps the branch for
    // a computed operand, so raising the short-circuit operator is opcode-exact.
    static Comparison Y()
        => new(ComparisonKind.GreaterThan, false, new LoadLocal(2, s_int), new Constant(0, s_int));

    // A bare bool argument condition (negation is not the proven integer dual).
    static LoadArgument Flag() => new(0, "flag", s_bool);

    // An integer comparison condition; its negation is the proven same-branch dual
    // (`start <= 0` → `start > 0`, both `ble.s`).
    static Comparison StartLessEqualZero()
        => new(ComparisonKind.LessThanOrEqual, false, new LoadArgument(0, "start", s_int), new Constant(0, s_int));

    // A float comparison condition; negating it flips the ordered/unordered sense.
    static Comparison FloatCompare()
        => new(ComparisonKind.LessThan, false,
               new LoadArgument(0, "a", s_double), new LoadArgument(1, "b", s_double));

    // A reference (string) equality condition; negating it flips op_Equality →
    // op_Inequality (a different call token).
    static Comparison StringEquals()
        => new(ComparisonKind.Equal, false,
               new LoadArgument(0, "a", s_string), new LoadArgument(1, "b", s_string));

    // A user-defined-truthiness condition: the `operator true` call csc inserts for a
    // user type in boolean context. Lifting it rebinds the user-defined `|`/`&`.
    static Call UserTruthiness()
        => new(new MethodRef(s_truth, "op_True", s_bool, [s_truth], HasThis: false),
               isVirtual: false, [new LoadArgument(0, "a", s_truth)]);

    // A managed by-ref bool dereference: csc collapses `c && refBool` to a branchless
    // `&`, eagerly dereferencing a location the branch had guarded.
    static LoadIndirect ByRefBoolDeref()
        => new(s_bool, new LoadArgument(1, "y", TypeRef.ByRef(s_bool)));

    // A bool-returning call — a side-effecting barrier csc will not collapse past.
    static Call BoolCall()
        => new(new MethodRef(s_holder, "Call", s_bool, [], HasThis: false), isVirtual: false, []);

    // A bool-returning call taking one bool argument (a by-ref deref confined to a
    // call argument stays guarded and folds faithfully).
    static Call BoolCallWith(IrExpression argument)
        => new(new MethodRef(s_holder, "Sink", s_bool, [s_bool], HasThis: false), isVirtual: false, [argument]);

    // A raw pointer bool dereference: can access-violate, so csc keeps the branch and
    // the raise stays opcode-exact — the negative boundary of the by-ref guard.
    static LoadIndirect PointerBoolDeref()
        => new(s_bool, new LoadArgument(1, "p", TypeRef.Pointer(s_bool)));

    // A bare reference (string) branch condition — the `brtrue`/`brfalse` over a
    // reference the printer spells `value is not null`. Negating it flips only branch
    // polarity (`is null`), so the negating fold stays opcode-exact. This is the
    // `String.IsNullOrEmpty` witness shape carried since before #3114.
    static LoadArgument ReferenceValue()
        => new(0, "value", s_string);

    // An explicit `x != null` reference test; its negation is the exact `x == null`
    // (a branch-polarity flip), not an operator-token change.
    static Comparison ReferenceNotNull()
        => new(ComparisonKind.NotEqual, false,
               new LoadArgument(0, "value", s_string), new Constant(null, s_string));

    // ---- Accept: opcode-exact folds still fire ----

    [Fact]
    public void IntegerComparisonNegate_Case3_FoldsToAnd()
    {
        // if (start <= 0) return false; return y;  ≡  return start > 0 && y;
        var (kind, left, right) = FoldOne(StartLessEqualZero(), Bool(false), Y());
        Assert.Equal(LogicalKind.And, kind);
        Assert.Equal(ComparisonKind.GreaterThan, Assert.IsType<Comparison>(left).Kind);
        Assert.IsType<Comparison>(right);
    }

    [Fact]
    public void PrimitiveBoolCondition_NoNegate_Case3_FoldsToOr()
    {
        // if (flag) return true; return y;  ≡  return flag || y;  (A-form, no negate)
        var (kind, left, right) = FoldOne(Flag(), Bool(true), Y());
        Assert.Equal(LogicalKind.Or, kind);
        Assert.IsType<LoadArgument>(left);   // condition unchanged
        Assert.IsType<Comparison>(right);
    }

    [Fact]
    public void IntegerComparison_Case2_NegateFoldsToOr()
    {
        // if (start <= 0) return y; return true;  ≡  return start > 0 || y;
        var (kind, left, right) = FoldOne(StartLessEqualZero(), Y(), Bool(true));
        Assert.Equal(LogicalKind.Or, kind);
        Assert.Equal(ComparisonKind.GreaterThan, Assert.IsType<Comparison>(left).Kind);
        Assert.IsType<Comparison>(right);
    }

    [Fact]
    public void IntegerComparison_Case2_NoNegateFoldsToAnd()
    {
        // if (start <= 0) return y; return false;  ≡  return start <= 0 && y;
        var (kind, left, right) = FoldOne(StartLessEqualZero(), Y(), Bool(false));
        Assert.Equal(LogicalKind.And, kind);
        Assert.Equal(ComparisonKind.LessThanOrEqual, Assert.IsType<Comparison>(left).Kind);
        Assert.IsType<Comparison>(right);
    }

    [Fact]
    public void PointerDereferenceOperand_StillFolds()
    {
        // if (start <= 0) return false; return *p;  ≡  return start > 0 && *p;
        // A pointer read can access-violate, so csc keeps the branch — raising is exact.
        var (kind, _, right) = FoldOne(StartLessEqualZero(), Bool(false), PointerBoolDeref());
        Assert.Equal(LogicalKind.And, kind);
        Assert.IsType<LoadIndirect>(right);
    }

    [Fact]
    public void ReferenceNullBranchNegate_Case2_FoldsToOr()
    {
        // if (value is not null) return y; return true;  ≡  return value is null || y;
        // The bare reference branch negates to a branch-polarity flip (is null), which
        // re-lowers to the opposite brtrue/brfalse — opcode-exact. This is the
        // `String.IsNullOrEmpty` shape (`value is null || value.Length == 0`).
        var (kind, left, right) = FoldOne(ReferenceValue(), Y(), Bool(true));
        Assert.Equal(LogicalKind.Or, kind);
        Assert.IsType<LogicalNot>(left);   // Negate wraps the bare reference load
        Assert.IsType<Comparison>(right);
    }

    [Fact]
    public void ExplicitNullTestNegate_Case3_FoldsToAnd()
    {
        // if (value != null) return false; return y;  ≡  return value == null && y;
        // An explicit reference null test inverts to its opposite null test, not an
        // operator-token change — opcode-exact.
        var (kind, left, right) = FoldOne(ReferenceNotNull(), Bool(false), Y());
        Assert.Equal(LogicalKind.And, kind);
        Assert.Equal(ComparisonKind.Equal, Assert.IsType<Comparison>(left).Kind);
        Assert.IsType<Comparison>(right);
    }

    // ---- Decline: hazardous folds are left flat ----

    [Fact]
    public void FloatComparisonNegate_Case3_Declines()
    {
        // if (a < b) return false; return y;  →  !(a < b) && y flips ordered/unordered.
        AssertDeclined(FloatCompare(), Bool(false), Y());
    }

    [Fact]
    public void ReferenceEqualityNegate_Case3_Declines()
    {
        // if (a == b) return false; return y;  →  a != b && y flips op_Equality token.
        AssertDeclined(StringEquals(), Bool(false), Y());
    }

    [Fact]
    public void FloatComparisonNegate_Case2_Declines()
    {
        // if (a < b) return y; return true;  →  !(a < b) || y flips ordered/unordered.
        AssertDeclined(FloatCompare(), Y(), Bool(true));
    }

    [Fact]
    public void UserTruthiness_Case3_TrueThen_Declines()
    {
        // if (op_True(a)) return true; return y;  →  a || y rebinds user-defined `|`.
        AssertDeclined(UserTruthiness(), Bool(true), Y());
    }

    [Fact]
    public void UserTruthiness_Case3_FalseThen_Declines()
    {
        // if (op_True(a)) return false; return y;  →  !a && y rebinds user-defined `&`.
        AssertDeclined(UserTruthiness(), Bool(false), Y());
    }

    [Fact]
    public void BoolConditionNegate_Case3_Declines()
    {
        // if (flag) return false; return y;  →  !flag && y. A bare bool is not a
        // comparison, so its negation is not the proven integer dual — decline.
        AssertDeclined(Flag(), Bool(false), Y());
    }

    [Fact]
    public void BareLocalOperand_Case3_Declines()
    {
        // if (start <= 0) return false; return localBool;  →  start > 0 && localBool.
        // csc collapses `c && local` to a branchless `&` — decline. (Integer condition,
        // so the decline is attributable to the operand guard, not the negate gate.)
        AssertDeclined(StartLessEqualZero(), Bool(false), new LoadLocal(1, s_bool));
    }

    [Fact]
    public void BareArgumentOperand_Case3_Declines()
    {
        AssertDeclined(StartLessEqualZero(), Bool(false), new LoadArgument(1, "other", s_bool));
    }

    [Fact]
    public void ByRefDereferenceOperand_Case3_Declines()
    {
        // csc collapses `c && refBool` to a branchless `&` and eagerly dereferences a
        // location the branch had guarded (NRE divergence on a null by-ref).
        AssertDeclined(StartLessEqualZero(), Bool(false), ByRefBoolDeref());
    }

    [Fact]
    public void ByRefDereferenceOperand_Case2_Declines()
    {
        // if (start <= 0) return refBool; return true;  →  start > 0 || refBool.
        AssertDeclined(StartLessEqualZero(), ByRefBoolDeref(), Bool(true));
    }

    // ---- Decline: a by-ref buried in a SAME-KIND logical chain (or under a reducible
    // bool wrapper) that the printer flattens into the emitted operator. #3127's
    // RendersAsBranchlessBarePlace only inspects the top-level operand and misses these;
    // LiftEagerlyDerefsByRef descends the flattened chain (#3114 follow-up). ----

    [Fact]
    public void ByRefBeforeCallInAndChain_Case3_Declines()
    {
        // if (start <= 0) return (*r && Call()); return false;  →  start > 0 && (*r && Call())
        // flattens to `start > 0 && *r && Call()`; the call-free `start > 0 & *r` PREFIX
        // collapses branchless before the call barrier, so *r is dereferenced eagerly (NRE
        // divergence on a null by-ref). Integer condition, so the decline is attributable
        // to the operand chain descent, not the negate gate.
        AssertDeclined(StartLessEqualZero(), new LogicalBinary(LogicalKind.And, ByRefBoolDeref(), BoolCall()), Bool(false));
    }

    [Fact]
    public void ByRefAfterCallInAndChain_Case3_Declines()
    {
        // if (start <= 0) return (Call() && *r); return false;  →  flattens to
        // `start > 0 && Call() && *r`. The trailing `&& *r` (bare by-ref right operand)
        // collapses branchless, dereferencing *r whenever the guarded prefix is false —
        // a by-ref anywhere in the same-kind chain is a hazard, not only before the call.
        AssertDeclined(StartLessEqualZero(), new LogicalBinary(LogicalKind.And, BoolCall(), ByRefBoolDeref()), Bool(false));
    }

    [Fact]
    public void ByRefInOrChain_Case2_OuterOr_Declines()
    {
        // if (start <= 0) return (*r || Call()); return true;  →  start > 0 || (*r || Call())
        // flattens to `start > 0 || *r || Call()`; the `start > 0 | *r` prefix collapses
        // branchless. The Or lift makes the same-kind Or chain the hazard.
        AssertDeclined(StartLessEqualZero(), new LogicalBinary(LogicalKind.Or, ByRefBoolDeref(), BoolCall()), Bool(true));
    }

    [Fact]
    public void ByRefInAndChainUnderEqualsTrueWrapper_Case3_Declines()
    {
        // SYNTHETIC/defensive: csc constant-folds `(*r && Call()) == true` to `*r && Call()`
        // (no `ceq`), so this exact shape is not csc-reachable, but the fixpoint is pre-order
        // and could present the un-reduced `== true` before FoldBoolConstantComparison strips
        // it. Peeling the reducible wrapper BEFORE the same-kind flatten still reaches the
        // buried by-ref.
        var chain = new LogicalBinary(LogicalKind.And, ByRefBoolDeref(), BoolCall());
        var wrapped = new Comparison(ComparisonKind.Equal, false, chain, Bool(true));
        AssertDeclined(StartLessEqualZero(), wrapped, Bool(false));
    }

    // ---- Accept: a by-ref confined to a COMPOUND operand that keeps its branch still
    // folds — the negative boundary of the chain descent. ----

    [Fact]
    public void ByRefInDifferentKindLogicalOperand_Case3_StillFolds()
    {
        // if (start <= 0) return (*r || Call()); return false;  →  start > 0 && (*r || Call()).
        // The Or operand is a different kind from the And lift, so the printer does NOT
        // flatten it; csc keeps the branch and *r stays guarded.
        var (kind, _, right) = FoldOne(StartLessEqualZero(), new LogicalBinary(LogicalKind.Or, ByRefBoolDeref(), BoolCall()), Bool(false));
        Assert.Equal(LogicalKind.And, kind);
        Assert.Equal(LogicalKind.Or, Assert.IsType<LogicalBinary>(right).Kind);
    }

    [Fact]
    public void ByRefInBitwiseAndOperand_Case3_StillFolds()
    {
        // if (start <= 0) return (local & *r); return false;  →  start > 0 && (local & *r).
        // A bitwise `&` (Binary, not LogicalBinary) is a compound operand csc keeps the
        // branch for; the by-ref stays guarded.
        var bitwise = new Binary(BinaryKind.And, isChecked: false, isUnsigned: false, new LoadLocal(0, s_bool), ByRefBoolDeref());
        var (kind, _, right) = FoldOne(StartLessEqualZero(), bitwise, Bool(false));
        Assert.Equal(LogicalKind.And, kind);
        Assert.IsType<Binary>(right);
    }

    [Fact]
    public void ByRefInComparisonOperand_Case3_StillFolds()
    {
        // if (start <= 0) return (*r == local); return false;  →  start > 0 && (*r == local).
        // A comparison is compound (not a reducible bool-constant comparison), so csc keeps
        // the branch and the by-ref stays guarded.
        var comparison = new Comparison(ComparisonKind.Equal, false, ByRefBoolDeref(), new LoadLocal(0, s_bool));
        var (kind, _, right) = FoldOne(StartLessEqualZero(), comparison, Bool(false));
        Assert.Equal(LogicalKind.And, kind);
        Assert.IsType<Comparison>(right);
    }

    [Fact]
    public void ByRefBehindCallArgument_Case3_StillFolds()
    {
        // if (start <= 0) return Sink(*r); return false;  →  start > 0 && Sink(*r).
        // A call keeps the branch, so a by-ref confined to its argument stays guarded.
        var (kind, _, right) = FoldOne(StartLessEqualZero(), BoolCallWith(ByRefBoolDeref()), Bool(false));
        Assert.Equal(LogicalKind.And, kind);
        Assert.IsType<Call>(right);
    }

    // ---- The both-opposite-constant identity/negation fold is a separate
    // readability raise (no operator lift, no surviving operand) and stays ungated. ----

    [Fact]
    public void BothOppositeConstants_StillFoldsToCondition()
    {
        // if (flag) return true; return false;  ≡  return flag;
        var block = Run(Flag(), Bool(true), Bool(false));
        var ret = Assert.IsType<Return>(Assert.Single(block.Children));
        Assert.IsType<LoadArgument>(ret.Value);
    }

    [Fact]
    public void BothOppositeConstants_FloatCondition_StillFolds()
    {
        // if (a < b) return false; return true;  ≡  return !(a < b). The both-constant
        // negation is out of #3114's scope (owned by the identity/negation fold), so it
        // still collapses the guard rather than staying flat.
        var block = Run(FloatCompare(), Bool(false), Bool(true));
        Assert.IsType<Return>(Assert.Single(block.Children));
    }

    // ---- Harness ----

    static (LogicalKind Kind, IrExpression Left, IrExpression Right) FoldOne(
        IrExpression condition, IrExpression thenValue, IrExpression tailValue)
    {
        var block = Run(condition, thenValue, tailValue);
        var ret = Assert.IsType<Return>(Assert.Single(block.Children));
        var logical = Assert.IsType<LogicalBinary>(ret.Value);
        return (logical.Kind, logical.Left, logical.Right);
    }

    static void AssertDeclined(IrExpression condition, IrExpression thenValue, IrExpression tailValue)
    {
        var block = Run(condition, thenValue, tailValue);
        Assert.Equal(2, block.Children.Count);
        Assert.IsType<IfStatement>(block.Children[0]);
        Assert.IsType<Return>(block.Children[1]);
    }

    // Builds `if (condition) return thenValue; return tailValue;`, runs the pass, and
    // returns the containing block for inspection.
    static Block Run(IrExpression condition, IrExpression thenValue, IrExpression tailValue)
    {
        var then = new Block(0);
        then.Add(new Return(thenValue));
        var guard = new IfStatement(condition, then, null);

        var block = new Block(0);
        block.Add(guard);
        block.Add(new Return(tailValue));

        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(
            s_bool, [new Parameter("start", s_int)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", s_holder, signature, [s_bool, s_bool, s_int], body);

        new BooleanFoldingPass().Run(function, PassContext.None);
        function.CheckInvariant();
        return block;
    }
}
