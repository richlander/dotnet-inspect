using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The fidelity gate for the lowered C# view. Like <see cref="FidelityGateTests"/>,
/// it decompiles every method on <see cref="CfgSampleClass"/>, recompiles it inside a
/// reconstructed shape of its type, and compares the body under compile-back fidelity
/// the fidelity contract — but it renders through <see cref="CSharpPrinter.PrintLowered"/>
/// (the de-sugared SharpLab-style view) instead of the shipped sugared view. Each official
/// C# view earns its own compiler→decompiler→compiler validation, so a regression that turns
/// a lowered method's recompiled IL into a different stream fails CI.
/// </summary>
[Trait("Speed", "Slow")]
[Collection(FidelityGateCollection.Name)]
[Trait("Area", "Fidelity")]
public class LoweredFidelityGateTests
{
    const string FixtureType = "ILInspector.Decompiler.Tests.CfgSampleClass";

    /// <summary>
    /// Methods whose lowered C# still differs under the compile-back fidelity contract — the open
    /// lowered docket. The gate tolerates these but fails if a NEW method joins the set, and
    /// <see cref="DocketRowsStayCheckedDiffs"/> fails if a listed method stops differing.
    /// The lowered view's by-design divergences (for example lowering skipping
    /// IncrementDecrementPass, so a dup-based ++/-- idiom round-trips as an explicit temp rather
    /// than the folded operator) land here alongside the shared sugared docket. Rows are annotated
    /// individually below; #3584 retired the entries that had silently stopped differing.
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
        "GotoCommonExit",
        "ManualAwaitEnumeratorLoop",
        // RuntimeInlineArrayForeach is the runtime-style inline-array enumerator
        // frontier from #1045: the lowered view is representable, but recompiles
        // through an extra span conversion before the element-ref helper.
        "RuntimeInlineArrayForeach",
        // Clustered-case switch with bool arms raised to nested if/else via
        // SlotDiamondPass (#912) — honest comparison-tree over-render, not exact.
        "SlotDiamondDispatch",
        // Newly compile-checkable only once the harness reconstructs sibling
        // properties as property syntax (#1412): a body's `obj.X` /
        // `?.X = v` cannot bind to a bare `get_X`/`set_X`
        // method, so these were silently recompile-failing before. The diffs are
        // honest decompiler over-renders (flipped short-circuit branch with a temp
        // slot; reused-slot temporaries), not harness artifacts.
        "NullConditionalPropertyAssignment",
        "ReusedSlotStringListCount",
        // SwitchStoreThenUse (#1743) is a known opcode-diff in the sibling sugared
        // FidelityGateTests docket — the ConditionalStoreChainPass ternary
        // (sel == 0 ? 11 : ...) re-lowers differently than the per-arm stores. It
        // diffs identically in the lowered view; this entry was missing because the
        // lowered gate is Speed=Slow and only runs in Deep Inspect / publish, not PR CI. Verified
        // pre-existing (fails with MixedShortCircuitChainPass off as well).
        "SwitchStoreThenUse",
        // Lowered output omits ForLoopPass, so protected increment targets retain
        // #2857's valid while-loop fallback. The explicit default declaration
        // before initialization adds an opcode pair on compile-back.
        "Issue2861_NestedProtectedLeaveToOuterIncrement",
        // CharConditionalElementStore (#1784): a pre-existing slow-docket gap from
        // recent main merges (a char ternary element store that spills to temps),
        // an honest valid re-lowering. Surfaced by running the Speed=Slow gate
        // locally; verified independent of MixedShortCircuitChainPass (it does not
        // fold it). See the sibling FidelityGateTests docket.
        "CharConditionalElementStore",
        // PointerStoreUsesOriginalAddress (#2644): same intentional residual as
        // the sugared view. The lowered body keeps the original pointer address
        // in the store target, but compile-back introduces locals around the
        // conditional value before stind.i4, so the canonical opcode stream
        // differs without changing the represented address semantics. Checkability
        // is pinned below.
        "PointerStoreUsesOriginalAddress",
        // Unmasked by the in/out skeleton-parameter fix (#1931): RecompileFail before
        // (the reconstructed CfgSampleClass failed to compile on InOperatorVec's
        // in-parameter operators rendered as illegal `ref`). Now compile-checked,
        // showing honest pre-existing over-renders. Same set as the sugared docket.
        "FloatPositionalPattern",
        // These malformed/shared-graph near misses keep honest Expression.*
        // factory calls, but their parameter-alias locals still recompile through
        // a different stack shape.
        "ManualReusedParameterFactory",
        "ManualDuplicateNameFactory",
        "ManualPositionalPatternLookalike",
        "MergedReferenceSlot",
        "MergedTernaryDeclaration",
        "NullCoalescingAssignStaticProperty",
        "set_SlotMergedDateTimeFormat",
        // Fidelity contract rebaseline: opcode names still match, but canonical
        // operands, symbolic targets, or branch targets differ.
        "DayNumber",
        // MakeConsumerWithTwoLeadingArgs (#3272): the trailing object initializer
        // with TWO preceding constructor arguments folds correctly (Valid + Correct)
        // to `new InitConsumer3(Identity(tag), Identity(a), new InitTarget { ... })`,
        // but ExpressionInliningPass only removes one of the two single-use spill
        // temps, leaving an extra stloc/ldloc pair on compile-back. Honest OpcodeDiff
        // from a separate inliner limitation, not an object-initializer regression
        // (the pre-raise version-copy expansion was also non-exact). The single-arg
        // corpus case (OpenAI GetRealtimeClient) stays byte-exact.
        "MakeConsumerWithTwoLeadingArgs",
    };

    /// <summary>
    /// Docket rows the harness reports as <see cref="FidelityCheck.CompileBackStatus.NotFull"/>:
    /// the body imports below Full fidelity, so no opcode verdict is formed and an
    /// opcode diff is expected rather than a defect. Empty on this rail today; it
    /// exists so <see cref="DocketRowsStayCheckedDiffs"/> can require every other docket
    /// row to remain an actual diff, and so a row that newly drops to NotFull fails as
    /// the validity regression it is instead of landing here silently.
    /// </summary>
    static readonly HashSet<string> KnownNotFull = new(StringComparer.Ordinal);

    /// <summary>
    /// Methods the lowered view keeps exact under the fidelity contract. This is the sugared pinned set minus
    /// ClassicLock, which the lowered view legitimately reshapes (lowering skips LockSugarPass,
    /// emitting an
    /// explicit Monitor.Enter/Exit form the fidelity check shell cannot bind — it lands in the
    /// recompile-fail bucket the gate excludes by design, same as the sugared rail's
    /// shell-attribution failures). DayNumber moved to the V1 difference docket
    /// because its opcode names match while its branch targets differ. AnonNamed
    /// and AnonSingle are below Full and their compiler-generated anonymous-type
    /// ordinals now differ, which remains observable in V1. The remaining pins
    /// guard that de-sugaring loops, ++/--, and
    /// locks does not disturb fixes proven on unrelated constructs (overflow checks, redundant
    /// mask removal, shadowed-field qualification, ctor field-init lifting, stale-field-read
    /// pinning, volatile re-declaration, and EH/return-accumulator sinking).
    /// </summary>
    static readonly string[] PinnedExact =
    {
        "AwaitAcrossVoidCall",
        "AwaitConfiguredTask",
        "AwaitConfiguredValueTask",
        "AwaitForeach",
        "AwaitInArguments",
        "AwaitOnce",
        "AwaitThree",
        "AwaitTwo",
        "AwaitUsingResource",
        "AwaitValueTask",
        "AwaitVoid",
        "NestedAwaitUsingResources",
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
        "SmallStringSwitch",
        "StringSwitchWithJoin",
        "StringSwitchNoDefault",
        "ParseOrZero",
        "ClassifyMode",
        "ClassifyWide",
        "AnonShorthand",
        // Promoted from KnownDiffs by #3584 after they were measured Exact on the
        // current main. The local-function/lambda rows are the benign
        // reconstruction-ordinal class that #3505 retired by canonicalizing
        // synthesized-member ordinals in the oracle — the outcome this rail's
        // ordinal comment anticipated when it pointed at #3503. The Issue2830_* /
        // Issue2861_ForLoopTryAndCatchContinues rows became exact through the
        // try/catch return-sinking work (#3553), and the rest through unrelated
        // raising. They are pinned rather than merely deleted so each fix is now
        // guarded: a docket row that stops diffing gates nothing, a PinnedExact
        // row does.
        "CallOutTarget",
        "CollectionListLiteral",
        "DoubleViaLocalFunction",
        "EnumArgInInlineLocalFunction",
        "EnumArgInLocalFunctionWithLocal",
        "GenericRefKindCallSites",
        "Issue2830_ForLoopEhCleanupContinue",
        "Issue2830_ForLoopLeave",
        "Issue2830_ForLoopNestedContinue",
        "Issue2861_ForLoopTryAndCatchContinues",
        "ManualParameterPlusConstantFactory",
        // #3502: the late inliner now restores an ordered run of stack-held
        // arguments directly into a returned call. Pin the intended comparison
        // fix and every sibling row measured Exact on this rail.
        "ManualCanonicalReturnedAsExpression",
        "ManualCanonicalReturnedAsLambdaExpression",
        "ManualCanonicalReturnedAsObject",
        "ManualConstantOnlyAddFactory",
        "ManualConstantOnlyComparisonFactory",
        "ManualConstantOnlyDivideByZeroFactory",
        "ManualConstantOnlyMultiplyFactory",
        "ManualConstantOnlyRemainderOverflowFactory",
        "ManualConstantOnlySubtractFactory",
        "ManualNestedConstantSubtreeFactory",
        "ManualSimpleExpressionTreeFactory",
        "ManualUnspellableNameFactory",
        "ObjectInitializerArgumentBeforeShortCircuit",
        "OrBoolIntMix",
        "OrBoolUintMix",
        "RecursiveLocalFunction",
        "RefKindCallSites",
        "ReverseCopy",
        "SelectBoolReturn",
        "SimpleExpressionTreeLambda",
        "StaticLocalFunctionCalledTwice",
        "StaticLocalFunctionWithLocal",
        "TwoLocalFunctionQuadrants",
        "WhileNestedContinueKeepsArmExclusive",
    };

    static readonly Lazy<IReadOnlyList<FidelityCheck.CompileBackResult>> Results = new(() =>
    {
        var assembly = typeof(CfgSampleClass).Assembly.Location;
        return FidelityCheck.Evaluate(assembly, lowered: true, type => type == FixtureType)
            .Where(r => r.Type == FixtureType)
            .ToList();
    });

    static IReadOnlyList<FidelityCheck.CompileBackResult> EvaluateFixtures() => Results.Value;

    [Fact]
    public void NoNewFidelityDiffsBeyondKnownDocket()
    {
        var diffResults = EvaluateFixtures()
            .Where(r => r.Status is
                FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff)
            .OrderBy(r => r.Method, StringComparer.Ordinal)
            .ToList();

        var unexpected = diffResults.Where(r => !KnownDiffs.Contains(r.Method)).ToList();
        var details = unexpected.Select(result =>
        {
            string fidelityRows = result.FidelityDiff is { Rows.Length: > 0 } diff
                ? "\n  fidelity:\n" + string.Join('\n', diff.Rows.Take(6).Select(row => $"    {row.Message}"))
                : "";
            return $"{result.Method} [{result.Status}]\n"
                + $"  original : {result.OriginalOpcodes}\n"
                + $"  recompiled: {result.RecompiledOpcodes}\n"
                + $"  detail: {result.Detail}{fidelityRows}";
        });

        Assert.True(unexpected.Count == 0,
            "New lowered fidelity check contract diffs (lowered C# recompiles to different IL):\n"
            + string.Join("\n\n", details)
            + $"\n\nFull current diff set: {string.Join(", ", diffResults.Select(r => r.Method))}");
    }

    /// <summary>
    /// The reverse direction of <see cref="NoNewFidelityDiffsBeyondKnownDocket"/>, which is a
    /// one-directional allow list: it fails when an undocketed method diffs, but never when a
    /// docketed method stops diffing. A row that became <c>Exact</c> silently allows a future
    /// regression it was never meant to cover, and a row that became
    /// <c>RecompileFail</c>/<c>ContextFail</c> silently drops a real regression out of the
    /// gated set. See the sugared rail's <c>DocketRowsStayCheckedDiffs</c> for the full
    /// rationale and the #3505 precedent (#3584).
    /// </summary>
    [Fact]
    public void DocketRowsStayCheckedDiffs()
    {
        var results = EvaluateFixtures();
        var failures = new List<string>();

        foreach (string method in KnownDiffs.OrderBy(m => m, StringComparer.Ordinal))
        {
            var matches = results.Where(r => r.Method == method).ToList();
            if (matches.Count == 0)
            {
                failures.Add($"{method}: docketed, but the lowered fidelity check no longer renders it. "
                    + "Remove the row if the fixture is gone, or restore the fixture.");
                continue;
            }

            foreach (var result in matches)
            {
                if (result.Status is FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff)
                    continue;

                if (result.Status == FidelityCheck.CompileBackStatus.NotFull
                    && KnownNotFull.Contains(method))
                    continue;

                failures.Add(result.Status switch
                {
                    FidelityCheck.CompileBackStatus.Exact =>
                        $"{method}: now recompiles Exact in the lowered view, so its docket row is stale and "
                        + "silently allows a future regression. Move it to PinnedExact so the fix is guarded.",
                    FidelityCheck.CompileBackStatus.NotFull =>
                        $"{method}: dropped to NotFull — it now imports below Full fidelity, so no opcode "
                        + "verdict is formed. That is a validity regression, not a docketed diff.",
                    FidelityCheck.CompileBackStatus.FidelityUnavailable =>
                        $"{method}: the body comparison produced no verdict, so the row gates nothing. "
                        + $"BodyComparisonRemainsAvailable reports the cause.\n  detail: {result.Detail}",
                    _ =>
                        $"{method}: regressed to {result.Status} — its lowered C# no longer recompiles, so it "
                        + $"silently left the diff set the docket gates.\n  detail: {result.Detail}",
                });
            }
        }

        Assert.True(failures.Count == 0,
            "Stale or regressed rows in LoweredFidelityGateTests.KnownDiffs (#3584):\n"
            + string.Join("\n", failures));
    }

    [Fact]
    public void BodyComparisonRemainsAvailable()
    {
        var unavailable = EvaluateFixtures()
            .Where(result => result.Status == FidelityCheck.CompileBackStatus.FidelityUnavailable)
            .Select(result => $"{result.Method}: {result.Detail}")
            .ToArray();

        Assert.True(
            unavailable.Length == 0,
            "Lowered the compile-back fidelity contract was unavailable for: "
            + string.Join(", ", unavailable));
    }

    [Fact]
    public void PinnedFixesStayExact()
    {
        var results = EvaluateFixtures();
        var failures = new List<string>();

        foreach (var method in PinnedExact)
        {
            var matches = results.Where(r => r.Method == method).ToList();
            if (matches.Count == 0)
            {
                failures.Add($"Expected lowered fidelity check to evaluate {method}, but it was not rendered.");
                continue;
            }

            foreach (var result in matches)
            {
                if (result.Status == FidelityCheck.CompileBackStatus.Exact)
                    continue;

                string fidelityRows = result.FidelityDiff is { Rows.Length: > 0 } diff
                    ? "\n  fidelity:\n"
                        + string.Join('\n', diff.Rows.Take(6).Select(row => $"    {row.Message}"))
                    : "";
                failures.Add(
                    $"{method} regressed to {result.Status} in the lowered view: a prior fidelity check fix no longer holds.\n" +
                    $"  original : {result.OriginalOpcodes}\n  recompiled: {result.RecompiledOpcodes}\n" +
                    $"  detail: {result.Detail}{fidelityRows}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    [Fact]
    public void PointerStoreUsesOriginalAddress_StaysCompileBackCheckable()
    {
        var matches = EvaluateFixtures().Where(r => r.Method == "PointerStoreUsesOriginalAddress").ToList();

        Assert.True(matches.Count > 0,
            "Expected the lowered fidelity check to render PointerStoreUsesOriginalAddress, but it was not evaluated.");
        foreach (var result in matches)
            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact
                    or FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff,
                $"PointerStoreUsesOriginalAddress regressed to {result.Status} in the lowered view: the pointer-store "
                    + "residual (#2644) no longer recompiles. Its lowered C# must stay recompilable so the known-diff "
                    + "docket continues to check the address-preservation shape.\n"
                    + $"  original : {result.OriginalOpcodes}\n  recompiled: {result.RecompiledOpcodes}\n"
                    + $"  detail: {result.Detail}");
    }
}
