using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The fidelity gate: decompile every method on <see cref="CfgSampleClass"/>,
/// recompile it inside a reconstructed shape of its type, and compare the body under
/// compile-back fidelity contract V1. A method that recompiles to a different body
/// changed the measured program shape — the worst decompiler failure class,
/// invisible to parse/bind checks. This pins the green set so a regression that turns
/// an exact method into a diff fails CI, while the documented baseline records the
/// methods that still diverge (the open docket) so the gate stays green on main.
/// </summary>
[Trait("Speed", "Slow")]
[Collection(FidelityGateCollection.Name)]
[Trait("Area", "Fidelity")]
public class FidelityGateTests
{
    const string FixtureType = "ILInspector.Decompiler.Tests.CfgSampleClass";

    /// <summary>
    /// Methods that still differ under compile-back fidelity contract V1 — the open
    /// decompiler docket. Each is a tracked defect or a benign over-render; the gate
    /// tolerates these but fails if a NEW method joins the set. Shrink this list as
    /// fixes land. Tracked defects include StaleFieldRead (issue #605) and
    /// benign codegen choices (BothPositive). NeitherOr left the docket when
    /// #3114 taught FoldGuardReturn to decline the negated `a && b` guard fold
    /// (`if (a && b) return false; return c;`) whose recompile diverged.
    /// GotoCommonExit is the step-2 common-exit fold: the decompiler inlines the
    /// shared return tail into each arm, recompiling to cleaner direct-return IL
    /// than the original goto-and-merge shape — a benign equivalent restructuring.
    /// SelectBoolReturn is a benign branch-polarity inversion in a bool-select
    /// ternary (orig <c>bgt</c> vs recmp <c>ble</c>), the same class as
    /// BothPositive; it was previously hidden in the recompile-fail
    /// bucket because its <c>System.GC.KeepAlive</c> call recompiled the short
    /// <c>GC</c> name with no <c>using System;</c> in the skeleton, and the
    /// harness now emits that using.
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
        // CollectionListLiteral is the #1142 PDB-discriminated List<T>
        // collection-expression raise. The original SDK csc lowering uses
        // constant Span<T> indices, while the Roslyn compile-back package emits a
        // hidden index temp for the same `return [a, b, 42];` source idiom.
        "CollectionListLiteral",
        "GotoCommonExit",
        // This hand-written await-enumerator loop recompiles through the same
        // runtime-async shape but schedules the receiver load after the
        // enumerator-local initialization rather than before it.
        "ManualAwaitEnumeratorLoop",
        // RuntimeInlineArrayForeach is the runtime-style inline-array enumerator
        // frontier from #1045: helper cleanup makes the body representable, but
        // recompiling `(V_1)[i]` reintroduces an extra span conversion call before
        // the element-ref helper. Exact recovery needs a higher-level inline-array
        // foreach/indexer raise.
        "RuntimeInlineArrayForeach",
        "SelectBoolReturn",
        // SlotDiamondDispatch is a clustered-case switch with bool-computing arms
        // (#912): SlotDiamondPass folds each returned slot diamond so the dispatch
        // raises into nested if/else, the same honest comparison-tree over-render
        // as the other sparse-switch entries — valid, not opcode-exact.
        "SlotDiamondDispatch",
        // The next three became compile-checkable only when the fidelity harness
        // began reconstructing sibling properties as property syntax (#1412): a
        // body's `obj.X` / object-initializer / `?.X = v` cannot bind to a bare
        // `get_X`/`set_X` method, so these were silently recompile-failing before.
        // The newly-visible diffs are honest decompiler over-renders, not harness
        // artifacts (verified by opcode dump): a flipped short-circuit branch with
        // a temp slot, and reused-slot temporaries — valid C#, not opcode-exact.
        "NullConditionalPropertyAssignment",
        "ObjectInitializerArgumentBeforeShortCircuit",
        "ReusedSlotStringListCount",
        // OrBoolIntMix / OrBoolUintMix are the #1452 bool-operand materialization:
        // a bitwise `bool | int` is CS0019, so BitwiseBoolOperandPass rewrites the
        // bool operand to `(cond ? 1 : 0)`. In the slot-form shapes that ternary
        // recompiles to a branch-select that differs from the original branchless
        // 0/1 — valid C# (no CS0019), not opcode-exact. OrBoolUintMix additionally
        // takes the `(uint)` mixed-sign reinterpret. The siblings AndBoolIntMix,
        // AndNestedConditionalBool, and AndNestedBoolIntMix stay opcode-exact, so
        // they are not docketed.
        "OrBoolIntMix",
        "OrBoolUintMix",
        // SwitchStoreThenUse is the #1710 ConditionalStoreChainPass fold: a
        // compare-chain switch assigning a local folds to a nested conditional
        // store, which csc re-lowers differently than the original per-arm stores
        // (push-then-store-once vs store-per-arm) — valid C#, not opcode-exact.
        // Pinned here (plus SwitchStoreFold_StaysCompileBackCheckable) so the fold
        // is compile-back-checked on every fidelity-gate run rather than left to
        // the sampled corpus.
        "SwitchStoreThenUse",
        // The #2861 nested-loop fixture deliberately retains #2857's valid
        // outer while-loop fallback because one protected leave targeting its
        // increment belongs to the nested loop. Its explicit default declaration
        // adds an opcode pair on compile-back, so the fallback is not opcode-exact.
        "Issue2861_NestedProtectedLeaveToOuterIncrement",
        // CharConditionalElementStore (#1784): the char ternary element store
        // spills to temps and recompiles to a different (valid) stream. Honest
        // over-render. Pre-existing slow-docket gap surfaced by running the gate
        // locally — the lowered/sugared gates are Speed=Slow (Deep Inspect / publish only).
        "CharConditionalElementStore",
        // PointerStoreUsesOriginalAddress (#2644): the raised source preserves
        // the original pointer address across the argument reassignment
        // (`*S_... =`, never `*ptr =`), but compile-back introduces locals around
        // the conditional value before the indirect store. Valid C#, not
        // opcode-exact; the address-preservation shape is pinned separately by
        // Rung6PointerStore_PreservesOriginalAddressAcrossArgumentStore and
        // checkability is pinned below.
        "PointerStoreUsesOriginalAddress",
        // Unmasked by the in/out skeleton-parameter fix (#1931): these CfgSampleClass
        // methods were RecompileFail before — the whole reconstructed type failed to
        // compile because InOperatorVec's in-parameter operators rendered as illegal
        // `ref`. With the type compiling, they are finally compile-checked and show
        // honest pre-existing body over-renders (ref/in/out call sites, positional
        // patterns, slot/ternary merges, null-coalescing assignment).
        "CallOutTarget",
        "FloatPositionalPattern",
        "GenericRefKindCallSites",
        // Expression-tree factory fixtures are valid and Full, but SDK preview6
        // compile-back reshapes the manually emitted Expression.* calls around
        // stack-slot/local temporaries. This is toolchain drift in an existing
        // over-render frontier, not a new daily product regression.
        "ManualSimpleExpressionTreeFactory",
        "SimpleExpressionTreeLambda",
        // The parameter-dependent manual factory graph recovers to a lambda, so it
        // recompiles to the compiler's expression-tree lowering rather than the
        // hand-written factory calls — the same recovery-induced diff as
        // SimpleExpressionTreeLambda.
        "ManualParameterPlusConstantFactory",
        // The manual factory fixtures below DECLINE and decompile to honest
        // Expression.* factory-call C# with clean locals: the malformed/shared-graph
        // near misses (reused parameter identity, duplicate names, unspellable name),
        // the canonical graphs returned through Expression/LambdaExpression/object
        // sinks, and the constant-only arithmetic graphs Roslyn would fold. Like
        // ManualSimpleExpressionTreeFactory, their emitted Expression.* calls
        // recompile through SDK preview compile-back reshaping of stack-slot/local
        // temporaries — a valid over-render, not opcode-exact. Verified by running
        // the Speed=Slow fidelity gate locally.
        "ManualCanonicalReturnedAsExpression",
        "ManualCanonicalReturnedAsLambdaExpression",
        "ManualCanonicalReturnedAsObject",
        "ManualReusedParameterFactory",
        "ManualDuplicateNameFactory",
        "ManualUnspellableNameFactory",
        "ManualConstantOnlyAddFactory",
        "ManualConstantOnlySubtractFactory",
        "ManualConstantOnlyMultiplyFactory",
        "ManualNestedConstantSubtreeFactory",
        "ManualConstantOnlyDivideByZeroFactory",
        "ManualConstantOnlyRemainderOverflowFactory",
        // ManualConstantOnlyComparisonFactory is the comparison sibling of the
        // constant-only family above, added with the #2864 comparison-predicate
        // recovery (#3053) but never docketed — both fidelity gates are
        // Speed=Slow, so PR CI never observed it. Same family cause: the
        // declined constant-fold keeps honest Expression.* factory calls, whose
        // compile-back reshapes stack-slot temporaries. Measured OpcodeDiff adds
        // exactly stloc@0x44, stloc@0x4f, ldloc@0x50, ldloc@0x51 — a temporary
        // pair, no operand or control-flow change.
        "ManualConstantOnlyComparisonFactory",
        "ManualPositionalPatternLookalike",
        "MergedReferenceSlot",
        "MergedTernaryDeclaration",
        "NullCoalescingAssignStaticProperty",
        "RefKindCallSites",
        "set_SlotMergedDateTimeFormat",
        // Compile-back fidelity contract V1 reclassifies these previously
        // opcode-exact rows because a value, symbolic target, or branch target
        // differs after recompilation.
        "BreakWithSideEffect",
        "CachedDelegateArgument",
        "CachedDelegateChain",
        "CapturingLambda",
        "CapturingLocalBodyLambda",
        // #2945: outer-body reads of a hoisted capture field are substituted back
        // to the captured source and the display class elides, so this fully
        // raises; recompiling regenerates a fresh display class whose synthesized
        // ordinals differ under contract V1.
        "CapturedParamReadInOuterBody",
        "ClosureCapture",
        "CountPositive",
        "DayNumber",
        "DoubleViaLocalFunction",
        // #2983: the enum-argument-in-nested-local-function fixture. Its raised
        // body prints through a freshly reconstructed nested scope and now spells
        // the enum constant by member (the fix under test); the call opcodes stay
        // identical (`ldarg call ret`), but recompiling reassigns the local
        // function's synthesized ordinal (g__Classify|N_0), which contract V1
        // observes — the same benign reconstruction-ordinal diff as its sibling
        // StaticLocalFunctionWithLocal below.
        "EnumArgInLocalFunctionWithLocal",
        // #2983 (inline path): the enum-argument-in-inline-local-function fixture.
        // Its raised body has no locals or stack slots, so it prints inline through
        // the enclosing scope and now spells the enum constant by member (the fix
        // under test); the call opcodes stay identical (`ldarg call ret`), but
        // recompiling reassigns the local function's synthesized ordinal, the same
        // benign reconstruction-ordinal diff as its sibling below.
        "EnumArgInInlineLocalFunction",
        "InvokeLocalCapture",
        "JustBreak",
        "LocalBodyLambda",
        "NonCapturingLambda",
        "RecursiveLocalFunction",
        "StatementBodyLambda",
        "StaticLocalFunctionCalledTwice",
        "StaticLocalFunctionWithLocal",
        // Tuple-switch fixture additions changed the reconstructed source ordinal
        // of both local functions from |5_* to |31_*. The call opcodes remain
        // identical, but contract V1 observes the changed symbolic targets.
        "TwoLocalFunctionQuadrants",
        // The iterator raises added by #2884 recover the source bodies and retain
        // the same outer factory opcodes, but recompiling the reconstructed large
        // fixture type assigns different synthesized state-machine ordinals
        // (d__600/601 -> d__575/576). Those constructor and field targets remain
        // observable symbolic identities under contract V1.
        "SwitchYield",
        "WhileTrueYieldBreak",
        "TwoCaptureLambda",
        "ValidNestedIf",
        "YieldCollectionExpressionSpread",
        "YieldEach",
        "YieldEnumerator",
        "YieldGrid",
        "YieldIf",
        "YieldPairs",
        "YieldRange",
        "YieldSquares",
        "YieldStrings",
        "YieldThree",
        "YieldTwo",
    };

    /// <summary>
    /// Methods a prior fidelity check fix turned exact under contract V1. Pinning them guards the
    /// fix durably: CheckedAdd must keep the overflow check (#604), UnsignedShift
    /// must keep dropping the redundant width mask (#606), Shadowed must keep
    /// qualifying the shadowed this.field load (#607), .ctor must keep lifting
    /// its field initializer ahead of the base call to the field declaration
    /// (#614), StaleFieldRead must keep pinning a field read taken before a
    /// store to that field (#605), ReverseCopy must keep folding the dup-based
    /// ++/-- idiom back into the operator at the use site, ReadVolatileFlag must
    /// keep re-declaring the volatile field so the read keeps its volatile.
    /// prefix, and the return-accumulator elimination must
    /// keep sinking the result temp out of an EH region or lock (TryFinallyAdd,
    /// TryFinallyTwoReturns, CatchEverything, ClassicLock,
    /// ManualDisposeAsyncInFinally), and SumPinnedArray and SumTwoPinned must keep
    /// raising the csc pin lowering into one or more `fixed` statements whose
    /// derived pointers recompile opcode-exact (#622 item F; multi-pin #697).
    /// SmallStringSwitch and DayNumber must keep raising switch-on-string
    /// lowerings (small op_Equality chain and hash-bucket dispatch) back into a
    /// `switch` statement that recompiles opcode-exact (the flat goto form
    /// inverts later branch polarities).
    /// ClassifyMode must keep raising csc's sparse switch-on-int binary-search
    /// dispatch (relational pivots over linear == chains) back into a `switch`.
    /// NullCoalescingAssignStaticField and NullCoalescingAssignInstanceField must
    /// keep folding the field null-test diamond into `field ??= fallback` whose
    /// two member loads recompile opcode-exact. LazyFieldGetter must keep folding
    /// csc's expression-valued lazy field getter into `return field ??= fallback`,
    /// preserving the dup/null-test/result-slot lowering opcode-exact. WhileTrueWithReturns and
    /// WhileTrueWithBreak must keep raising csc's unconditional back-edge into a
    /// `while (true)` loop whose break/return exits recompile opcode-exact.
    /// SubIntPromotionToUInt must keep re-inserting the `(uint)` cast C# requires
    /// on a sub-int (char/byte/short) binary result stored into a uint slot —
    /// the cast is a same-width reinterpret that emits no conv, so it recompiles
    /// opcode-exact.
    /// TupleRest must keep flattening the eight-element nested-TRest construction
    /// into one `(…)` literal, which recompiles to the same nested ValueTuple
    /// construction opcode-exact.
    /// OrChainDiamond must keep folding csc's OR-chain diamond (two conditionals to
    /// a shared true arm, the else arm jumping past it to the merge) into one `||`
    /// guard so the structuring pass raises the if/else; the `a < 0 || b < 0` guard
    /// recompiles to the same two-branch IL.
    /// InterpolationWithBackslashFormat must keep escaping the interpolation
    /// format clause (the backslash-escaped `h\:mm\:ss` TimeSpan spelling) so the
    /// non-verbatim `$"…"` is valid C# and the format constant recompiles
    /// opcode-exact — a raw `\:` is CS1009.
    /// CrossAssemblyEnumSwitch must keep casting each `switch` case label to the
    /// (cross-assembly, Unknown-shape) enum governing type — `case (DayOfWeek)1:`
    /// — since a bare `case 1:` over an enum is CS0266 (#1031 CS0266 slice).
    /// SpanElementCompoundAdd must keep declining the address-compound fold for a
    /// ref-returning indexer getter (Span&lt;int&gt;.this[int], a LoadProperty
    /// address the printer's SameLValue cannot fold): rendering the captured ref as
    /// a `ref` local keeps a single get_Item evaluation and recompiles opcode-exact,
    /// where the fold would leak `s[i] = (s[i]) + v` and call the getter twice
    /// (#1011 byref-provenance audit).
    /// ArrayElementAddEffectfulIndex must similarly keep a call-indexed array
    /// address as a captured `ref` local; cloning `a[RecordIndex()]` into read and
    /// write would double-evaluate the index (#1382).
    /// GenericNullConditionalToString must keep folding csc's generic
    /// null-conditional invocation (the default(T)-box two-stage null test with a
    /// reload) back into `value?.ToString() ?? "none"`, which recompiles to the
    /// same two-stage test opcode-exact.
    /// ParseOrZero must keep treating the verified `out` argument as a definite
    /// local assignment so the printer does not emit a dead `= default` store the
    /// original IL never carried.
    /// SharedCaptureLambdas is below Full and now reports `NotFull` because its
    /// opcode names match but its V1 body comparison differs. CapturingLocalFunction
    /// is also below Full; fixture additions changed its compiler-generated display
    /// class and local-function ordinals, which remain observable in V1.
    /// AnonNamed, AnonSingle, AnonNested, and AnonDeepNested are likewise below
    /// Full with changed anonymous-type ordinals. DayNumber and
    /// DoubleViaLocalFunction moved to the V1 difference docket above.
    /// The wide-index / unary-negate block (#2981, PR #3012) pins the idiomatic
    /// long/ulong array-index and enum-negate rendering, including the
    /// cross-assembly (Unknown-shape enum) width fallback exercised by the
    /// Neg*External* fixtures.
    /// </summary>
    static readonly string[] PinnedExact =
    {
        // #3161: a `switch` with TWO loop-bearing sections — an Array-like arm
        // (length guard + a `foreach`) and an Object-like arm (count guard + a
        // `while` with an in-loop early return) — plus scalar arms and a default,
        // shaped for high likeness to the platform method that motivated the fix
        // (System.Text.Json.JsonElement.DeepEquals, whose Array and Object arms each
        // loop). csc emits a dense `switch` opcode over cases 0..4 and lowers both
        // loops to back-edges, so neither case section is a straight-line
        // single-entry region. Before #3161 SwitchRaisingPass left the whole switch
        // flat; it now owns both loop-bearing sections and the raise recompiles
        // opcode-exact. This pins the transformation's IL fidelity on a shape we own
        // — the same fidelity that cannot be checked on DeepEquals itself, since its
        // internal System.Text.Json surface is not recompilable by the compile-back
        // oracle (tracked for a future cross-assembly compile-back capability).
        "SwitchWithTwoLoopingCaseSections",
        // #3166: the value-swap idiom. csc lowers the tuple swap `(a, b) = (b, a)`
        // (and the equivalent manual `temp = b; b = a; a = temp;`) to a single
        // dup-slot save plus the two cross-stores; SwapIdiomPass raises that
        // surviving carrier back to `(a, b) = (b, a)`. Because the tuple swap and
        // the one-temp sequence share IL, the raise is opcode-exact — this pins it.
        // Mirrors System.Text.Json.JsonElement.DeepEquals, which swaps two
        // JsonElement structs through one such temp but cannot itself be fed to the
        // compile-back oracle (internal System.Text.Json surface).
        "SwapStructPair",
        // #3164: the inline-Current foreach. On its Array arm DeepEquals runs a
        // compiler `foreach` whose single-use iteration variable csc hoists as
        // `item = e.Current`; ExpressionInliningPass folds that store into its one
        // use before ForeachStatementPass runs, so the hidden enumerator survives
        // referenced only by MoveNext and one inline `e.Current`. The pass rebinds
        // that inline read to a fresh foreach variable. Because the raised
        // `foreach` re-lowers to csc's original hoist, the transform is
        // opcode-exact — this pins it. Mirrors System.Text.Json.JsonElement.
        // DeepEquals, whose Array arm iterates one enumerator by `foreach` while a
        // second is advanced manually, but which cannot itself be fed to the
        // compile-back oracle (internal System.Text.Json surface, #3197).
        "ForeachSingleUseWithParallelEnumerator",
        // The compile-back oracle replays the fixture's runtime-async feature,
        // so these methods must retain the same lowering rather than merely
        // remaining recompilable.
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
        // MixedOrAndArms (#1175): the mixed ||/&& fold raises and recompiles
        // opcode-exact; pinned so a regression to flat/RecompileFail is caught
        // on every fidelity-gate run, not left to the sampled corpus.
        "MixedOrAndArms",
        "CheckedAdd",
        "UnsignedShift",
        "Shadowed",
        ".ctor",
        "StaleFieldRead",
        "ReverseCopy",
        "ReadVolatileFlag",
        "TryFinallyAdd",
        "TryFinallyTwoReturns",
        "CatchEverything",
        "ClassicLock",
        "ManualDisposeAsyncInFinally",
        "SumPinnedArray",
        "SumTwoPinned",
        "ConstantUIntSpan",
        "ConstantByteSpan",
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
        "ClassifyMode",
        "ClassifyWide",
        "AnonShorthand",
        "TupleRest",
        "TupleLiteralEquals4",
        "TupleNestedLiteralEquals",
        "TupleNestedLiteralEquals2",
        "NthFromEnd",
        "NthFromEndComputed",
        "NthCharFromEnd",
        "NullCoalescingAssignStaticField",
        "NullCoalescingAssignInstanceField",
        "LazyFieldGetter",
        "SubIntPromotionToUInt",
        "Issue2830_ForLoopEhCleanupContinue",
        "Issue2830_ForLoopLeave",
        "Issue2830_ForLoopNestedContinue",
        "Issue2861_ForLoopTryAndCatchContinues",
        "WhileTrueWithReturns",
        "WhileTrueWithBreak",
        "KeywordParam",
        "ReadKeywordField",
        "WriteKeywordField",
        "ReadKeywordFieldNullConditional",
        "InitializeKeywordField",
        "IsNotNullReference",
        "LineSeparatorLiteral",
        "InterpolationWithBackslashFormat",
        "GenericNullConditionalToString",
        "ParseOrZero",
        "NegativeNativeInt",
        "OrChainDiamond",
        "OrLongIntoULong",
        "MulLongIntoULong",
        "MulIntIntoUInt",
        "NestedMixedSignArithmetic",
        "RefEnumMask",
        "RvaIntArray",
        "ArrayElementAddEffectfulIndex",
        "SpanElementCompoundAdd",
        "BoolBitwiseOrWidened",
        "CrossAssemblyEnumSwitch",
        // Wide (long/ulong) array-index and unary-negate rendering (#2981, PR #3012):
        // each decompiles to an idiomatic bare or single-cast index that must
        // recompile opcode-exact, so a regression to checked((nint)i) or a lost/extra
        // signed reinterpret is caught on every fidelity-gate run, not left to the
        // sampled corpus. The Neg*External* trio additionally pins the cross-assembly
        // (Unknown-shape enum) width fallback, including the 8-byte `long` arm that no
        // core-library enum can exercise (ExternalULong/ExternalLong are 8-byte-backed).
        "CheckedULongSumIndexAsSigned",
        "FirstElement",
        "LongArrayIndex",
        "LongArrayIndexAsUnsigned",
        "LongArrayIndexExpr",
        "LongArrayIndexRef",
        "LongArrayIndexStore",
        "LongCheckedSumIndexBare",
        "LongEnumArrayIndex",
        "LongEnumIndexChecked",
        "LongEnumRefArrayIndex",
        "LongIndexAsUnsignedChecked",
        "LongShlIndexBare",
        "NegCrossAssemblyEnumElementIndex",
        "NegEnumLongElementToLong",
        "NegEnumUIntElementToInt",
        "NegEnumULongElementIndexAsSigned",
        "NegExternalLongEnumElementToLong",
        "NegExternalUIntEnumElementToInt",
        "NegExternalULongEnumElementIndex",
        "NegLongIndexBare",
        "NegLongIndexInChecked",
        "NegNuintElementToNint",
        "NegULongElementIndexAsSigned",
        "NegULongElementToLong",
        "NotLongIndexBare",
        "NotULongElementIndexAsSigned",
        "ULongArrayElementIndex",
        "ULongArrayIndex",
        "ULongArrayIndexAsSigned",
        "ULongDivIndexAsSigned",
        "ULongElementSumIndexAsSigned",
        "ULongIndexAsSignedChecked",
        "ULongRefIndexAsSigned",
        "ULongRemIndexAsSigned",
        "ULongShrIndexAsSigned",
        "ULongSumIndexAsSigned",
        "ULongSumIndexBare",
        "Finalize",
    };

    static readonly Lazy<IReadOnlyList<FidelityCheck.CompileBackResult>> Results = new(() =>
    {
        var assembly = typeof(CfgSampleClass).Assembly.Location;
        return FidelityCheck.Evaluate(assembly, type => type == FixtureType)
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

        var unexpected = diffResults.Where(result => !KnownDiffs.Contains(result.Method)).ToList();
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
            "New fidelity check contract V1 diffs (decompiled C# recompiles to different IL):\n"
            + string.Join("\n\n", details)
            + $"\n\nFull current diff set: {string.Join(", ", diffResults.Select(result => result.Method))}");
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
            "Compile-back fidelity contract V1 was unavailable for: "
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
                failures.Add($"Expected fidelity check to evaluate {method}, but it was not rendered.");
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
                    $"{method} regressed to {result.Status}: a prior fidelity check fix no longer holds.\n" +
                    $"  original : {result.OriginalOpcodes}\n  recompiled: {result.RecompiledOpcodes}\n" +
                    $"  detail: {result.Detail}{fidelityRows}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    /// <summary>
    /// The #1710 switch-store fold (<c>ConditionalStoreChainPass</c>) recovers an
    /// intentional opcode-diff (the nested ternary re-lowers differently), so it
    /// cannot be pinned exact. But its danger mode is a <em>silent</em> regression
    /// that produces uncompilable output: that would drop it out of the opcode-diff
    /// set, so <see cref="NoNewFidelityDiffsBeyondKnownDocket"/> would not catch it.
    /// Pin that the fold's fixture stays compile-back-<em>checkable</em> — rendered
    /// and recompilable (<c>Exact</c>, <c>OpcodeDiff</c>, or <c>OperandDiff</c>), never
    /// <c>RecompileFail</c>/<c>ContextFail</c> — so a regression that breaks
    /// recompilation fails the always-run fidelity gate rather than only the
    /// sampled corpus.
    /// </summary>
    [Fact]
    public void SwitchStoreFold_StaysCompileBackCheckable()
    {
        var matches = EvaluateFixtures().Where(r => r.Method == "SwitchStoreThenUse").ToList();

        Assert.True(matches.Count > 0,
            "Expected the fidelity check to render SwitchStoreThenUse, but it was not evaluated.");
        foreach (var result in matches)
            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact
                    or FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff,
                $"SwitchStoreThenUse regressed to {result.Status}: the switch-store fold (#1710) no longer "
                    + "recompiles. Its decompiled C# must stay recompilable.\n"
                    + $"  original : {result.OriginalOpcodes}\n  recompiled: {result.RecompiledOpcodes}\n"
                    + $"  detail: {result.Detail}");
    }

    [Fact]
    public void PointerStoreUsesOriginalAddress_StaysCompileBackCheckable()
    {
        var matches = EvaluateFixtures().Where(r => r.Method == "PointerStoreUsesOriginalAddress").ToList();

        Assert.True(matches.Count > 0,
            "Expected the fidelity check to render PointerStoreUsesOriginalAddress, but it was not evaluated.");
        foreach (var result in matches)
            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact
                    or FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff,
                $"PointerStoreUsesOriginalAddress regressed to {result.Status}: the pointer-store residual (#2644) "
                    + "no longer recompiles. Its decompiled C# must stay recompilable so the known-diff docket "
                    + "continues to check the address-preservation shape.\n"
                    + $"  original : {result.OriginalOpcodes}\n  recompiled: {result.RecompiledOpcodes}\n"
                    + $"  detail: {result.Detail}");
    }
}
