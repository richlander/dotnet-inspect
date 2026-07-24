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
