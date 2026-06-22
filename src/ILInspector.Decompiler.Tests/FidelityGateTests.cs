using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The fidelity gate: decompile every method on <see cref="CfgSampleClass"/>,
/// recompile it inside a reconstructed shape of its type, and compare the canonical
/// opcode stream against the originally compiled fixture. A method that recompiles
/// to a different stream changed the program — the worst decompiler failure class,
/// invisible to parse/bind checks. This pins the green set so a regression that turns
/// an exact method into a diff fails CI, while the documented baseline records the
/// methods that still diverge (the open docket) so the gate stays green on main.
/// </summary>
public class FidelityGateTests
{
    const string FixtureType = "ILInspector.Decompiler.Tests.CfgSampleClass";

    /// <summary>
    /// Methods that still recompile to a different opcode stream — the open
    /// decompiler docket. Each is a tracked defect or a benign over-render; the gate
    /// tolerates these but fails if a NEW method joins the set. Shrink this list as
    /// fixes land. Tracked defects include StaleFieldRead (issue #605) and
    /// benign codegen choices (BothPositive, NeitherOr).
    /// GotoCommonExit is the step-2 common-exit fold: the decompiler inlines the
    /// shared return tail into each arm, recompiling to cleaner direct-return IL
    /// than the original goto-and-merge shape — a benign equivalent restructuring.
    /// SelectBoolReturn is a benign branch-polarity inversion in a bool-select
    /// ternary (orig <c>bgt</c> vs recmp <c>ble</c>), the same class as
    /// BothPositive/NeitherOr; it was previously hidden in the recompile-fail
    /// bucket because its <c>System.GC.KeepAlive</c> call recompiled the short
    /// <c>GC</c> name with no <c>using System;</c> in the skeleton, and the
    /// harness now emits that using.
    /// </summary>
    static readonly HashSet<string> KnownDiffs = new(StringComparer.Ordinal)
    {
        "BothPositive",
        "GotoCommonExit",
        "NeitherOr",
        "SelectBoolReturn",
        // SlotDiamondDispatch is a clustered-case switch with bool-computing arms
        // (#912): SlotDiamondPass folds each returned slot diamond so the dispatch
        // raises into nested if/else, the same honest comparison-tree over-render
        // as the other sparse-switch entries — valid, not opcode-exact.
        "SlotDiamondDispatch",
    };

    /// <summary>
    /// Methods a prior fidelity check fix turned opcode-exact. Pinning them guards the
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
    /// two member loads recompile opcode-exact. WhileTrueWithReturns and
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
    /// SpanElementCompoundAdd must keep declining the address-compound fold for a
    /// ref-returning indexer getter (Span&lt;int&gt;.this[int], a LoadProperty
    /// address the printer's SameLValue cannot fold): rendering the captured ref as
    /// a `ref` local keeps a single get_Item evaluation and recompiles opcode-exact,
    /// where the fold would leak `s[i] = (s[i]) + v` and call the getter twice
    /// (#1011 byref-provenance audit).
    /// GenericNullConditionalToString must keep folding csc's generic
    /// null-conditional invocation (the default(T)-box two-stage null test with a
    /// reload) back into `value?.ToString() ?? "none"`, which recompiles to the
    /// same two-stage test opcode-exact.
    /// </summary>
    static readonly string[] PinnedExact =
    {
        "SharedCaptureLambdas",
        "DoubleViaLocalFunction",
        "CapturingLocalFunction",
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
        "ClassifyMode",
        "ClassifyWide",
        "AnonShorthand",
        "AnonNamed",
        "AnonSingle",
        "AnonNested",
        "AnonDeepNested",
        "TupleRest",
        "NthFromEnd",
        "NthFromEndComputed",
        "NthCharFromEnd",
        "NullCoalescingAssignStaticField",
        "NullCoalescingAssignInstanceField",
        "SubIntPromotionToUInt",
        "WhileTrueWithReturns",
        "WhileTrueWithBreak",
        "KeywordParam",
        "IsNotNullReference",
        "LineSeparatorLiteral",
        "InterpolationWithBackslashFormat",
        "GenericNullConditionalToString",
        "NegativeNativeInt",
        "OrChainDiamond",
        "OrLongIntoULong",
        "MulLongIntoULong",
        "MulIntIntoUInt",
        "NestedMixedSignArithmetic",
        "RefEnumMask",
        "RvaIntArray",
        "SpanElementCompoundAdd",
        "BoolBitwiseOrWidened",
        "Finalize",
    };

    static IReadOnlyList<FidelityCheck.CompileBackResult> EvaluateFixtures()
    {
        var assembly = typeof(CfgSampleClass).Assembly.Location;
        return FidelityCheck.Evaluate(assembly)
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
            $"New fidelity check opcode diffs (decompiled C# recompiles to different IL): " +
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
                $"Expected fidelity check to evaluate {method}, but it was not rendered.");
            foreach (var result in matches)
                Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
                    $"{method} regressed to {result.Status}: a prior fidelity check fix no longer holds.\n" +
                    $"  original : {result.OriginalOpcodes}\n  recompiled: {result.RecompiledOpcodes}");
        }
    }
}
