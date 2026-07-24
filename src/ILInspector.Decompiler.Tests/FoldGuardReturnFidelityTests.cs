using System.Collections.Immutable;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// #3114: <c>BooleanFoldingPass.FoldGuardReturn</c> re-forms <c>if (c) return A;
/// return B;</c> (with a bool-constant arm) into a short-circuit. Most re-forms are
/// opcode-divergent but VALID readability raises (a block-reordered guard clause, a
/// both-constant <c>return c</c>) and keep folding; only two shapes produce output
/// the correctness bar rejects and are declined: a user-defined-truthiness condition
/// (yields non-compiling C#) and a managed by-ref surviving operand (a branchless
/// deref that faults where the branch had guarded).
/// </summary>
[Trait("Area", "Pass")]
public class FoldGuardReturnFidelityTests
{
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Truth = TypeRef.CoreLib("Synthetic", "TruthOver");

    static Constant Bool(bool value) => new(value, Boolean);

    // An integer comparison condition (the common guard test).
    static Comparison CondCompare()
        => new(ComparisonKind.GreaterThan, isUnsigned: false, new LoadArgument(0, "s", Int32), new Constant(0, Int32));

    // A computed (non-constant, non-bare-place) bool operand: a comparison. csc keeps
    // the branch for a computed operand, so the tailConstant fold is opcode-exact.
    static Comparison Computed()
        => new(ComparisonKind.GreaterThan, isUnsigned: false, new LoadLocal(1, Int32), new Constant(0, Int32));

    // A user-defined-truthiness condition: the `operator true` call the compiler
    // inserts for a user type in boolean context.
    static Call UserTruthiness()
        => new(new MethodRef(Truth, "op_True", Boolean, [Truth], HasThis: false),
               isVirtual: false, [new LoadArgument(0, "t", Truth)]);

    // A managed by-ref (`in`/`ref`/`out`) bool dereference.
    static LoadIndirect ByRefBoolDeref()
        => new(Boolean, new LoadArgument(1, "r", TypeRef.ByRef(Boolean)));

    // A raw pointer (`bool*`) dereference.
    static LoadIndirect PointerBoolDeref()
        => new(Boolean, new LoadArgument(1, "p", TypeRef.Pointer(Boolean)));

    // === accepted readability raises: STILL fold ===

    [Fact]
    public void TailConstant_ComputedOperand_StillFolds()
    {
        // `if (c) return computed; return false;` → `c && computed`. The exact inverse
        // of csc's `&&` lowering: keeps folding.
        var result = RunGuard(CondCompare(), Computed(), Bool(false), [Int32, Int32]);

        var logical = Assert.IsType<LogicalBinary>(result);
        Assert.Equal(LogicalKind.And, logical.Kind);
    }

    [Fact]
    public void ThenConstant_GuardClause_StillFolds()
    {
        // `if (c) return false; return X;` → `!c && X`. Opcode-divergent (block
        // reorder) but valid and readable — an accepted readability raise per #3114.
        var result = RunGuard(CondCompare(), Bool(false), Computed(), [Int32, Int32]);

        var logical = Assert.IsType<LogicalBinary>(result);
        Assert.Equal(LogicalKind.And, logical.Kind);
    }

    [Fact]
    public void BothConstant_StillFolds()
    {
        // `if (c) return true; return false;` → `return c`. Opcode-divergent (csc keeps
        // the branch; `c` is `cgt`) but valid — accepted readability raise.
        var result = RunGuard(CondCompare(), Bool(true), Bool(false), [Int32]);

        Assert.IsType<Comparison>(result);
    }

    [Fact]
    public void TailConstant_BareLocalOperand_StillFolds()
    {
        // `if (c) return local; return false;` → `c && local`. csc would emit a
        // branchless `&` (opcode-divergent), but that is a plain readability raise
        // richlander chose to keep — only the managed by-ref deref is declined.
        var result = RunGuard(CondCompare(), new LoadLocal(1, Boolean), Bool(false), [Int32, Boolean]);

        Assert.IsType<LogicalBinary>(result);
    }

    [Fact]
    public void TailConstant_PointerDereferenceOperand_StillFolds()
    {
        // `if (c) return *p; return false;` → `c && *p`. A pointer read can
        // access-violate, so csc keeps the branch — folding is safe.
        var result = RunGuard(CondCompare(), PointerBoolDeref(), Bool(false), [Int32]);

        Assert.IsType<LogicalBinary>(result);
    }

    // === correctness declines: leave the faithful guarded return ===

    [Fact]
    public void TruthinessCondition_TailConstant_IsNotFolded()
    {
        // `if (t) return computed; return false;` would become `t && computed`, which
        // binds the user-defined `&` (typed TruthOver, not bool) — non-compiling.
        AssertDeclined(UserTruthiness(), Computed(), Bool(false), [Int32]);
    }

    [Fact]
    public void TruthinessCondition_ThenConstant_IsNotFolded()
    {
        // `if (t) return false; return X;` would become `!t && X` — same rebind.
        AssertDeclined(UserTruthiness(), Bool(false), Computed(), [Int32]);
    }

    [Fact]
    public void TruthinessCondition_BothConstant_IsNotFolded()
    {
        // `if (t) return true; return false;` would become `return t`, which needs a
        // nonexistent TruthOver→bool conversion — non-compiling.
        AssertDeclined(UserTruthiness(), Bool(true), Bool(false), []);
    }

    [Fact]
    public void ByRefDereferenceOperand_TailConstant_IsNotFolded()
    {
        // `if (c) return *r; return false;` would become `c && *r`, which csc
        // collapses to a branchless `&` that eagerly dereferences a managed by-ref the
        // branch had guarded — a NullReferenceException divergence.
        AssertDeclined(CondCompare(), ByRefBoolDeref(), Bool(false), [Int32]);
    }

    [Fact]
    public void ByRefDereferenceOperand_ThenConstant_IsNotFolded()
    {
        // `if (c) return false; return *r;` would become `!c && *r` — same eager-deref
        // divergence on the surviving operand.
        AssertDeclined(CondCompare(), Bool(false), ByRefBoolDeref(), [Int32]);
    }

    // === correctness declines: the wrapped forms the fold's own downstream
    // transforms would strip or invert back to a bare hazard (adversarial review,
    // #3119). The peel is a CONSERVATIVE, negation-parity-agnostic over-approximation:
    // it follows a hazard through any chain of logical negations and bool-constant
    // comparisons (both directions), so it also declines the branch-preserving `!x`
    // forms (`== false` / `!= true`). Declining an extra readable raise is sound;
    // modeling parity exactly would re-implement FoldBoolConstantComparison + Negate. ===

    [Fact]
    public void NegatedTruthinessCondition_TailConstant_IsNotFolded()
    {
        // `if (!t) return computed; return true;` lifts the condition through
        // Conditions.Negate on the `||` arm, which unwraps the LogicalNot and re-exposes
        // the bare `op_True(t)` — the printer then strips it to `return t || computed;`,
        // which binds the user-defined `|` (typed TruthOver, not bool): non-compiling.
        // The look-through peel must see the truthiness call under the negation.
        AssertDeclined(new LogicalNot(UserTruthiness()), Computed(), Bool(true), [Int32]);
    }

    [Fact]
    public void TruthinessUnderEqualsTrueComparison_TailConstant_IsNotFolded()
    {
        // `if (t == true) return computed; return false;` reduces `op_True(t) == true`
        // to the bare `op_True(t)`, which the printer strips to `return t && computed;` —
        // the user-defined `&` rebind, non-compiling. The peel must see the truthiness
        // call under the identity comparison the fixpoint strips.
        var truthEqualsTrue = new Comparison(
            ComparisonKind.Equal, isUnsigned: false, UserTruthiness(), Bool(true));
        AssertDeclined(truthEqualsTrue, Computed(), Bool(false), [Int32]);
    }

    [Fact]
    public void TruthinessUnderEqualsFalseComparison_TailConstant_IsNotFolded()
    {
        // `if (t == false) return computed; return true;` lifts through Conditions.Negate
        // on the `||` arm, which inverts `op_True(t) == false` to `op_True(t) != false`;
        // the fixpoint reduces that to the bare `op_True(t)` → `t || computed`, the same
        // rebind. Both comparison directions must be peeled because the arm decides the
        // negation.
        var truthEqualsFalse = new Comparison(
            ComparisonKind.Equal, isUnsigned: false, UserTruthiness(), Bool(false));
        AssertDeclined(truthEqualsFalse, Computed(), Bool(true), [Int32]);
    }

    [Fact]
    public void ByRefDereferenceUnderEqualsTrueComparison_IsNotFolded()
    {
        // `if (c) return *r == true; return false;` folds to `c && (*r == true)`, then
        // BooleanFoldingPass's fixpoint reduces `*r == true` to the bare `*r` — the same
        // branchless eager-deref divergence the raw-operand guard catches. The peel must
        // see the by-ref deref under the identity comparison the pass will strip.
        var byRefEqualsTrue = new Comparison(
            ComparisonKind.Equal, isUnsigned: false, ByRefBoolDeref(), Bool(true));
        AssertDeclined(CondCompare(), byRefEqualsTrue, Bool(false), [Int32]);
    }

    [Fact]
    public void ByRefDereferenceUnderDoubleNegatedComparison_IsNotFolded()
    {
        // `if (c) return (*r == false) == false; return false;`: the outer Negate inverts
        // the inner comparison and the fixpoint reduces the whole chain back to the bare
        // `*r` — a branchless eager deref. A single identity peel stops at the outer
        // negating comparison; following the non-constant side through the full chain is
        // what reaches the hidden by-ref deref (adversarial review, #3119).
        var innerEqualsFalse = new Comparison(
            ComparisonKind.Equal, isUnsigned: false, ByRefBoolDeref(), Bool(false));
        var doubleNegated = new Comparison(
            ComparisonKind.Equal, isUnsigned: false, innerEqualsFalse, Bool(false));
        AssertDeclined(CondCompare(), doubleNegated, Bool(false), [Int32]);
    }

    [Fact]
    public void ByRefDereferenceUnderEqualsFalseComparison_IsNotFolded()
    {
        // `if (c) return *r == false; return false;` reduces to `c && !*r`. The `!*r`
        // keeps a branch, so folding here would be sound — but the conservative,
        // parity-agnostic peel declines it anyway rather than re-simulate the reduction
        // to distinguish the bare `*r` (hazard) from `!*r` (safe). An extra decline of a
        // rare, readable by-ref raise is sound.
        var byRefEqualsFalse = new Comparison(
            ComparisonKind.Equal, isUnsigned: false, ByRefBoolDeref(), Bool(false));
        AssertDeclined(CondCompare(), byRefEqualsFalse, Bool(false), [Int32]);
    }

    // === correctness declines: a managed by-ref deref nested inside a raised
    // logical/bitwise composition (adversarial review round 3, #3119). A leaf-only
    // peel stops at the composition node and misses the deref; the subtree scan
    // reaches it. csc branchless-collapses `c && OP` whenever OP is side-effect-free,
    // so any by-ref deref in a call-free OP becomes an eager deref of a guarded
    // location — the same NullReferenceException divergence as the bare operand. ===

    [Fact]
    public void ByRefUnderLogicalAndOperand_TailConstant_IsNotFolded()
    {
        // `if (c) return a && *r; return false;` → `c && (a && *r)`, which flattens and
        // recompiles branchless (`c & a & r`), eagerly dereferencing the guarded by-ref.
        // The old leaf-only peel stopped at the LogicalBinary and missed the nested deref.
        var operand = new LogicalBinary(LogicalKind.And, new LoadLocal(1, Boolean), ByRefBoolDeref());
        AssertDeclined(CondCompare(), operand, Bool(false), [Int32, Boolean]);
    }

    [Fact]
    public void ByRefUnderLogicalOrOperand_TailConstant_IsNotFolded()
    {
        // `if (c) return *r || a; return false;` → `c && (*r || a)` → branchless
        // `c & (r | a)`, the same eager deref with the by-ref on the `||` left.
        var operand = new LogicalBinary(LogicalKind.Or, ByRefBoolDeref(), new LoadLocal(1, Boolean));
        AssertDeclined(CondCompare(), operand, Bool(false), [Int32, Boolean]);
    }

    [Fact]
    public void ByRefUnderBitwiseAndOperand_TailConstant_IsNotFolded()
    {
        // The real csc shape: source `if (c) return a && r; return false;` imports with
        // the inner `a && r` already lowered to a bitwise `a & r` (both operands simple).
        // Lifting `c && (a & r)` recompiles branchless → eager by-ref deref. The subtree
        // scan reaches the deref under the bitwise node.
        var operand = new Binary(BinaryKind.And, isChecked: false, isUnsigned: false,
            new LoadLocal(1, Boolean), ByRefBoolDeref());
        AssertDeclined(CondCompare(), operand, Bool(false), [Int32, Boolean]);
    }

    [Fact]
    public void ByRefBehindCallOperand_TailConstant_StillFolds()
    {
        // `if (c) return Use(*r); return false;` → `c && Use(*r)`. The call is a
        // side-effect barrier csc will not hoist past the lift, so it keeps the branch
        // and the by-ref deref stays guarded — a faithful, foldable readability raise.
        // The scan must NOT over-decline a by-ref that only appears inside a call.
        var operand = new Call(
            new MethodRef(Holder, "Use", Boolean, [Boolean], HasThis: false),
            isVirtual: false, [ByRefBoolDeref()]);
        var result = RunGuard(CondCompare(), operand, Bool(false), [Int32]);

        Assert.IsType<LogicalBinary>(result);
    }

    static IrExpression RunGuard(
        IrExpression condition, IrExpression thenValue, IrExpression tailValue, ImmutableArray<TypeRef> locals)
    {
        var function = BuildGuard(condition, thenValue, tailValue, locals);
        new BooleanFoldingPass().Run(function, PassContext.None);
        function.CheckInvariant();
        var ret = Assert.IsType<Return>(Assert.Single(function.Body.Blocks[0].Children));
        return ret.Value!;
    }

    static void AssertDeclined(
        IrExpression condition, IrExpression thenValue, IrExpression tailValue, ImmutableArray<TypeRef> locals)
    {
        var function = BuildGuard(condition, thenValue, tailValue, locals);
        var block = function.Body.Blocks[0];
        new BooleanFoldingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(2, block.Children.Count);
        Assert.IsType<IfStatement>(block.Children[0]);
        Assert.IsType<Return>(block.Children[1]);
    }

    static IrFunction BuildGuard(
        IrExpression condition, IrExpression thenValue, IrExpression tailValue, ImmutableArray<TypeRef> locals)
    {
        var then = new Block(0);
        then.Add(new Return(thenValue));
        var guard = new IfStatement(condition, then, null);

        var block = new Block(0);
        block.Add(guard);
        block.Add(new Return(tailValue));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(
                Boolean, [new Parameter("s", Int32)], HasThis: false, GenericParameterCount: 0),
            locals,
            body);
    }
}
