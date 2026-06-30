using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The fidelity gate for the lowered C# view. Like <see cref="FidelityGateTests"/>,
/// it decompiles every method on <see cref="CfgSampleClass"/>, recompiles it inside a
/// reconstructed shape of its type, and compares the canonical opcode stream against the
/// originally compiled fixture — but it renders through <see cref="CSharpPrinter.PrintLowered"/>
/// (the de-sugared SharpLab-style view) instead of the shipped sugared view. Each official
/// C# view earns its own compiler→decompiler→compiler validation, so a regression that turns
/// a lowered method's recompiled IL into a different stream fails CI.
/// </summary>
[Trait("Speed", "Slow")]
public class LoweredFidelityGateTests
{
    const string FixtureType = "ILInspector.Decompiler.Tests.CfgSampleClass";

    /// <summary>
    /// Methods whose lowered C# still recompiles to a different opcode stream — the open
    /// lowered docket. The gate tolerates these but fails if a NEW method joins the set.
    /// Beyond the shared sugared docket (BothPositive, GotoCommonExit,
    /// NeitherOr, SelectBoolReturn), the lowered view adds ReverseCopy:
    /// lowering deliberately skips
    /// IncrementDecrementPass, so the dup-based ++/-- idiom round-trips as an explicit temp
    /// rather than the folded operator — a benign by-design divergence for this view.
    /// </summary>
    static readonly HashSet<string> KnownDiffs = new(StringComparer.Ordinal)
    {
        // AllOuterMatchInner, CachedStaticMethodGroup, and
        // CompoundAssignDictionaryIndexer were previously recompile failures: the
        // skeleton lacked the System.Linq / System.Collections.Generic usings the
        // product printer's short names assume, so they never compiled to be
        // compared. The widened skeleton using set (changed-method missing-symbol
        // work) now compiles them, surfacing pre-existing over-renders (LINQ All,
        // static method-group caching, compound dictionary-indexer double access)
        // that were masked, not introduced. Triage tracked separately.
        "AllOuterMatchInner",
        "CachedStaticMethodGroup",
        "CompoundAssignDictionaryIndexer",
        "BothPositive",
        // ByteRangeSearchTree is the #1084 comparison-tree bool-arm fixture:
        // now fully raised by ComparisonTreeBoolArmPass, but still recompiles to
        // different branch structure like the sparse-switch over-render docket.
        "ByteRangeSearchTree",
        // CollectionListLiteral is raised by InlineArrayCollectionPass, which the
        // lowered view keeps for collection-expression validity. Compile-back
        // Roslyn emits a hidden index temp instead of the SDK csc's constant
        // Span<T> indices for the same source idiom.
        "CollectionListLiteral",
        "GotoCommonExit",
        "NeitherOr",
        "ReverseCopy",
        // RuntimeInlineArrayForeach is the runtime-style inline-array enumerator
        // frontier from #1045: the lowered view is representable, but recompiles
        // through an extra span conversion before the element-ref helper.
        "RuntimeInlineArrayForeach",
        "SelectBoolReturn",
        // Clustered-case switch with bool arms raised to nested if/else via
        // SlotDiamondPass (#912) — honest comparison-tree over-render, not exact.
        "SlotDiamondDispatch",
        // Newly compile-checkable only once the harness reconstructs sibling
        // properties as property syntax (#1412): a body's `obj.X` /
        // object-initializer / `?.X = v` cannot bind to a bare `get_X`/`set_X`
        // method, so these were silently recompile-failing before. The diffs are
        // honest decompiler over-renders (flipped short-circuit branch with a temp
        // slot; reused-slot temporaries), not harness artifacts.
        "NullConditionalPropertyAssignment",
        "ObjectInitializerArgumentBeforeShortCircuit",
        "ReusedSlotStringListCount",
        // OrBoolIntMix / OrBoolUintMix are the #1452 bool-operand materialization:
        // a bitwise `bool | int` is CS0019, so BitwiseBoolOperandPass rewrites the
        // bool operand to `(cond ? 1 : 0)`; in the slot-form shapes that ternary
        // recompiles to a branch-select differing from the original branchless 0/1
        // — valid C#, not opcode-exact. OrBoolUintMix additionally takes the
        // `(uint)` mixed-sign reinterpret. The And* siblings stay opcode-exact.
        "OrBoolIntMix",
        "OrBoolUintMix",
        // SwitchStoreThenUse (#1743) is a known opcode-diff in the sibling sugared
        // FidelityGateTests docket — the ConditionalStoreChainPass ternary
        // (sel == 0 ? 11 : ...) re-lowers differently than the per-arm stores. It
        // diffs identically in the lowered view; this entry was missing because the
        // lowered gate is Speed=Slow and only runs in the daily, not PR CI. Verified
        // pre-existing (fails with MixedShortCircuitChainPass off as well).
        "SwitchStoreThenUse",
        // CharConditionalElementStore (#1784) and WhileNestedContinueKeepsArmExclusive:
        // pre-existing slow-docket gaps from recent main merges (a char ternary
        // element store that spills to temps; a nested-continue loop structuring),
        // both honest valid re-lowerings. Surfaced by running the Speed=Slow gate
        // locally; verified independent of MixedShortCircuitChainPass (it folds
        // neither). See the sibling FidelityGateTests docket.
        "CharConditionalElementStore",
        "WhileNestedContinueKeepsArmExclusive",
        // Unmasked by the in/out skeleton-parameter fix (#1931): RecompileFail before
        // (the reconstructed CfgSampleClass failed to compile on InOperatorVec's
        // in-parameter operators rendered as illegal `ref`). Now compile-checked,
        // showing honest pre-existing over-renders. Same set as the sugared docket.
        "CallOutTarget",
        "FloatPositionalPattern",
        "GenericRefKindCallSites",
        // Expression-tree factory fixtures are valid and Full, but SDK preview6
        // compile-back reshapes the manually emitted Expression.* calls around
        // stack-slot/local temporaries. Same preview drift as the sugared gate.
        "ManualSimpleExpressionTreeFactory",
        "SimpleExpressionTreeLambda",
        "ManualPositionalPatternLookalike",
        "MergedReferenceSlot",
        "MergedTernaryDeclaration",
        "NullCoalescingAssignStaticProperty",
        "RefKindCallSites",
        "set_SlotMergedDateTimeFormat",
    };

    /// <summary>
    /// Methods the lowered view keeps opcode-exact. This is the sugared pinned set minus the
    /// two methods the lowered view legitimately reshapes: ReverseCopy (now an expected diff,
    /// see <see cref="KnownDiffs"/>) and ClassicLock (lowering skips LockSugarPass, emitting an
    /// explicit Monitor.Enter/Exit form the fidelity check shell cannot bind — it lands in the
    /// recompile-fail bucket the gate excludes by design, same as the sugared rail's
    /// shell-attribution failures). The remaining pins guard that de-sugaring loops, ++/--, and
    /// locks does not disturb fixes proven on unrelated constructs (overflow checks, redundant
    /// mask removal, shadowed-field qualification, ctor field-init lifting, stale-field-read
    /// pinning, volatile re-declaration, and EH/return-accumulator sinking).
    /// </summary>
    static readonly string[] PinnedExact =
    {
        "CheckedAdd",
        // MixedOrAndArms (#1175): mixed ||/&& fold stays opcode-exact in the
        // lowered view too — pinned so a regression trips the always-run gate.
        "MixedOrAndArms",
        "UnsignedShift",
        "Shadowed",
        ".ctor",
        "StaleFieldRead",
        "ReadVolatileFlag",
        "TryFinallyAdd",
        "TryFinallyTwoReturns",
        "CatchEverything",
        "ManualDisposeAsyncInFinally",
        "SumPinnedArray",
        "SumTwoPinned",
        "ConstantUIntSpan",
        "InlineArraySpan",
        "InlineArrayFieldAsSpan",
        "InlineArrayFieldAsReadOnlySpan",
        "RuntimeInlineArrayIndexer",
        "IsPatternGuard",
        "IsPatternConjunction",
        "IsPatternConjunctionVariableBound",
        "IsPatternProperty",
        "IsPatternPropertyGreater",
        "IsPatternPropertyAtMost",
        "DayNumber",
        "SmallStringSwitch",
        "StringSwitchWithJoin",
        "StringSwitchNoDefault",
        "ParseOrZero",
        "ClassifyMode",
        "ClassifyWide",
        "AnonShorthand",
        "AnonNamed",
        "AnonSingle",
    };

    static IReadOnlyList<FidelityCheck.CompileBackResult> EvaluateFixtures()
    {
        var assembly = typeof(CfgSampleClass).Assembly.Location;
        return FidelityCheck.Evaluate(assembly, lowered: true)
            .Where(r => r.Type == FixtureType)
            .ToList();
    }

    [Fact]
    public void NoNewOpcodeDiffsBeyondKnownDocket()
    {
        var diffs = EvaluateFixtures()
            .Where(r => r.Status == FidelityCheck.CompileBackStatus.OpcodeDiff)
            .Select(r => r.Method)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

        var unexpected = diffs.Where(m => !KnownDiffs.Contains(m)).ToList();

        Assert.True(unexpected.Count == 0,
            $"New lowered fidelity check opcode diffs (lowered C# recompiles to different IL): " +
            $"{string.Join(", ", unexpected)}. Full current diff set: {string.Join(", ", diffs)}");
    }

    [Fact]
    public void PinnedFixesStayOpcodeExact()
    {
        var results = EvaluateFixtures();

        foreach (var method in PinnedExact)
        {
            var matches = results.Where(r => r.Method == method).ToList();
            Assert.True(matches.Count > 0,
                $"Expected lowered fidelity check to evaluate {method}, but it was not rendered.");
            foreach (var result in matches)
                Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
                    $"{method} regressed to {result.Status} in the lowered view: a prior fidelity check fix no longer holds.\n" +
                    $"  original : {result.OriginalOpcodes}\n  recompiled: {result.RecompiledOpcodes}");
        }
    }
}
