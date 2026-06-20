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
    /// fixes land. Tracked defects include StaleFieldRead (issue #605),
    /// branch-polarity in the string-switch lowering (DayNumber,
    /// SmallStringSwitch), and benign codegen choices (BothPositive, NeitherOr).
    /// ClassifyMode is a sparse switch csc lowers to a comparison tree the
    /// structuring pass does not yet raise, so it round-trips through flat gotos
    /// (tracked under the structuring docket — leaves the set when that lands).
    /// GotoCommonExit is the step-2 common-exit fold: the decompiler inlines the
    /// shared return tail into each arm, recompiling to cleaner direct-return IL
    /// than the original goto-and-merge shape — a benign equivalent restructuring.
    /// </summary>
    static readonly HashSet<string> KnownDiffs = new(StringComparer.Ordinal)
    {
        "BothPositive",
        "ClassifyMode",
        "DayNumber",
        "GotoCommonExit",
        "NeitherOr",
        "SmallStringSwitch",
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
    /// </summary>
    static readonly string[] PinnedExact =
    {
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
        "InlineArraySpan",
        "IsPatternGuard",
        "IsPatternConjunction",
        "IsPatternProperty",
        "AnonShorthand",
        "AnonNamed",
        "AnonSingle",
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
