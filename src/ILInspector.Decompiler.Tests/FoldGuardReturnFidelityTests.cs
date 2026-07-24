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

    // === by-ref nested in a composition (adversarial review rounds 3–5, #3119).
    // csc collapses `c && OP` to a branchless `c & OP` only when OP renders as a bare
    // place; the printer flattens a same-kind logical chain, so a by-ref deref that
    // becomes a bare operand of the emitted OUTER-kind chain is eagerly dereferenced
    // (NRE divergence) and is declined — but one confined to a compound operand that
    // keeps its branch (a bitwise `&`, a different-kind logical, a call argument) folds
    // faithfully. The csc-REACHABLE shapes below were each verified against SDK-csc
    // /optimize IL plus a null-by-ref runtime probe: the bitwise `a & r`, the comparison
    // `*r > 0`, the by-ref field `r.b`, and the call-barrier same-kind chain `*r && Call()`
    // (a call keeps csc from collapsing the `&&`). The `LogicalBinary` cases built over
    // BARE operands (`a && *r`, `*r || a`) and the `== true`-wrapped cases are SYNTHETIC
    // predicate tests: csc lowers a bare-operand `&&`/`||` to a bitwise `Binary` (so those
    // sources import as the `a & r` case, not `LogicalBinary`) and constant-folds
    // `x == true`. A csc-reachable `LogicalBinary` needs a compound (call) operand
    // (`*r && Call()`, `*r || Call()`); the reachable analog of a wrapped chain is the
    // `!`-negated `!(*r && Call())`. Synthetic status noted per test. ===

    [Fact]
    public void ByRefUnderLogicalAndOperand_TailConstant_IsNotFolded()
    {
        // Synthetic/defensive predicate shape: a same-kind `LogicalBinary(And, a, *r)`.
        // csc pre-lowers the bare-operand source `a && *r` to a BITWISE `a & r` (see
        // ByRefUnderBitwiseAndOperand, which folds faithfully), so this literal
        // LogicalBinary is not what that source imports as — the reachable logical-chain
        // hazard needs a call barrier that keeps the `&&` (ByRefBeforeCallInLogicalAnd-
        // Operand, next test). This test pins the by-ref scan: IF a same-kind
        // `LogicalBinary` reaches the guard, it flattens to `c && a && r`, whose call-free
        // prefix would collapse branchless (eager by-ref deref), so it is declined.
        var operand = new LogicalBinary(LogicalKind.And, new LoadLocal(1, Boolean), ByRefBoolDeref());
        AssertDeclined(CondCompare(), operand, Bool(false), [Int32, Boolean]);
    }

    [Fact]
    public void ByRefBeforeCallInLogicalAndOperand_TailConstant_IsNotFolded()
    {
        // #3119 round-4 finding 1: a trailing call does NOT guard an earlier by-ref.
        // `if (c) return *r && Call(); return false;` → `c && (*r && Call())` flattens to
        // `c && *r && Call()`; the call-free `c && *r` PREFIX collapses branchless
        // (`c & *r`) before the call is reached, so the by-ref is dereferenced eagerly
        // (verified: null-by-ref throws). The by-ref is a bare operand of the flattened
        // And chain even though a call sits later in that chain.
        var call = new Call(
            new MethodRef(Holder, "Call", Boolean, [], HasThis: false), isVirtual: false, []);
        var operand = new LogicalBinary(LogicalKind.And, ByRefBoolDeref(), call);
        AssertDeclined(CondCompare(), operand, Bool(false), [Int32]);
    }

    [Fact]
    public void ByRefInLogicalAndChainUnderEqualsTrueWrapper_TailConstant_IsNotFolded()
    {
        // #3119 round-5 finding 1 / round-6: SYNTHETIC defensive predicate test. csc
        // constant-folds `(*r && Call()) == true` to `*r && Call()` (verified: the wrapped
        // and unwrapped sources emit byte-identical IL, no `ceq`), so this `== true` node
        // is NOT csc-reachable. The csc-REACHABLE analog that drives the same peel-then-
        // recurse path is the `!`-negated chain `!(*r && Call())` (csc keeps the `&&`
        // branch and appends a `ceq`), which the parity-agnostic peel conservatively
        // declines for the same reason as a bare `!*r`. This pins that the peel runs BEFORE
        // the same-kind flatten: IF a reducible wrapper around a same-kind chain reaches
        // the fold, its buried by-ref is still reached (flattening first would treat the
        // wrapper as an opaque compound and miss it).
        var call = new Call(
            new MethodRef(Holder, "Call", Boolean, [], HasThis: false), isVirtual: false, []);
        var chain = new LogicalBinary(LogicalKind.And, ByRefBoolDeref(), call);
        var wrapped = new Comparison(ComparisonKind.Equal, isUnsigned: false, chain, Bool(true));
        AssertDeclined(CondCompare(), wrapped, Bool(false), [Int32]);
    }

    [Fact]
    public void LocalOnlyLogicalAndChainUnderEqualsTrueWrapper_TailConstant_StillFolds()
    {
        // Negative for the peel-then-recurse: the same synthetic wrapped same-kind chain
        // WITHOUT a by-ref still folds. The peel reaches the local `a`, but the by-ref
        // hazard predicate only fires on a by-ref deref, so a local operand is a valid
        // readability raise (a local never faults) and the guarded-return fold keeps it.
        // (Synthetic like the sibling: csc constant-folds `== true`.)
        var call = new Call(
            new MethodRef(Holder, "Call", Boolean, [], HasThis: false), isVirtual: false, []);
        var chain = new LogicalBinary(LogicalKind.And, new LoadLocal(1, Boolean), call);
        var wrapped = new Comparison(ComparisonKind.Equal, isUnsigned: false, chain, Bool(true));
        var result = RunGuard(CondCompare(), wrapped, Bool(false), [Int32, Boolean]);

        Assert.IsType<LogicalBinary>(result);
    }

    [Fact]
    public void ByRefInSameKindLogicalOperand_TailTrue_OuterOr_IsNotFolded()
    {
        // SYNTHETIC same-kind (`Or`) chain under an `Or` lift. A bare `*r || a` lowers to a
        // bitwise `Binary.Or` (faithful, folds), so the csc-reachable same-kind hazard
        // needs a call barrier: `*r || Call()` → `!c || (*r || Call())` flattens to
        // `!c || *r || Call()`, whose `!c || *r` prefix collapses branchless (eager by-ref).
        // This pins the outer-kind-relative decline on the `LogicalBinary(Or)` shape; the
        // SAME shape folds under an `&&` lift (next test).
        var operand = new LogicalBinary(LogicalKind.Or, ByRefBoolDeref(), new LoadLocal(1, Boolean));
        AssertDeclined(CondCompare(), operand, Bool(true), [Int32, Boolean]);
    }

    [Fact]
    public void ByRefInDifferentKindLogicalOperand_TailConstant_StillFolds()
    {
        // #3119 round-4/round-6: a different-kind `LogicalBinary(Or)` operand under an
        // `&&` lift folds. The `||` differs from the `&&` lift, so the printer keeps it a
        // parenthesized compound the outer `&&` branch guards — the by-ref is read only
        // when c is true. (SYNTHETIC IR: a bare `*r || a` lowers to a bitwise `Binary.Or`,
        // = the `ByRefUnderBitwiseAndOperand` case which was verified byte-identical; the
        // csc-reachable different-kind `LogicalBinary` needs a call operand, `*r || Call()`.
        // The predicate's fold decision is identical for either.)
        var operand = new LogicalBinary(LogicalKind.Or, ByRefBoolDeref(), new LoadLocal(1, Boolean));
        var result = RunGuard(CondCompare(), operand, Bool(false), [Int32, Boolean]);

        Assert.IsType<LogicalBinary>(result);
    }

    [Fact]
    public void ByRefUnderBitwiseAndOperand_TailConstant_StillFolds()
    {
        // #3119 round-4 finding 2: the real csc shape. Source `if (c) return a && r;
        // return false;` imports with the inner `a && r` already lowered to a bitwise
        // `a & r`. A bitwise composition is a compound operand csc keeps the `c` branch
        // for, so `c && (a & r)` recompiles to IL byte-identical to the guarded original
        // (verified: null-by-ref does NOT throw). My round-3 fix wrongly declined this;
        // the by-ref is not a bare operand of the And chain.
        var operand = new Binary(BinaryKind.And, isChecked: false, isUnsigned: false,
            new LoadLocal(1, Boolean), ByRefBoolDeref());
        var result = RunGuard(CondCompare(), operand, Bool(false), [Int32, Boolean]);

        Assert.IsType<LogicalBinary>(result);
    }

    [Fact]
    public void ByRefBehindCallOperand_TailConstant_StillFolds()
    {
        // `if (c) return Use(*r); return false;` → `c && Use(*r)`. The by-ref is a call
        // argument, so it lives inside the compound call operand the `c` branch guards —
        // it is not a bare operand of the And chain, and folding stays faithful.
        var operand = new Call(
            new MethodRef(Holder, "Use", Boolean, [Boolean], HasThis: false),
            isVirtual: false, [ByRefBoolDeref()]);
        var result = RunGuard(CondCompare(), operand, Bool(false), [Int32]);

        Assert.IsType<LogicalBinary>(result);
    }

    [Fact]
    public void ByRefInComparisonOperand_TailConstant_StillFolds()
    {
        // `if (c) return *r > 0; return false;` (int by-ref) → `c && (*r > 0)`. The by-ref
        // deref is confined to a comparison, a compound operand csc keeps the `c` branch
        // for, so it is read only when c is true (verified round-4: IL byte-identical to
        // the guarded original, null-by-ref does NOT throw). This pins that the guard does
        // NOT decline a by-ref nested in a comparison — a regression guard against the
        // round-3 subtree scan, which wrongly declined any by-ref anywhere in the operand.
        var byRefInt = new LoadIndirect(Int32, new LoadArgument(1, "r", TypeRef.ByRef(Int32)));
        var operand = new Comparison(ComparisonKind.GreaterThan, isUnsigned: false, byRefInt, new Constant(0, Int32));
        var result = RunGuard(CondCompare(), operand, Bool(false), [Int32]);

        Assert.IsType<LogicalBinary>(result);
    }

    [Fact]
    public void ByRefStructFieldOperand_TailConstant_StillFolds()
    {
        // `if (c) return r.b; return false;` (by-ref struct `r`, bool field `b`) →
        // `c && r.b`. The field access reads through the by-ref address with `ldfld` — it
        // is not a bare `ldind` bool deref, so it is a compound operand csc keeps the `c`
        // branch for and stays foldable (verified round-4: IL byte-identical, no null-by-
        // ref throw). Pins non-decline for a by-ref struct field access.
        var structRef = TypeRef.CoreLib("Synthetic", "S");
        var field = new FieldRef(structRef, "b", Boolean);
        var operand = new LoadField(field, new LoadArgument(1, "r", TypeRef.ByRef(structRef)));
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
