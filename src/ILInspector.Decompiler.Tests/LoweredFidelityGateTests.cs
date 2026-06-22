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
        "BothPositive",
        // ByteRangeSearchTree is the #1081 comparison-tree blocker fixture:
        // valid Full-fidelity output, but the shared false return tail leaves a
        // flat range-search tree that recompiles to different branch structure.
        "ByteRangeSearchTree",
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
