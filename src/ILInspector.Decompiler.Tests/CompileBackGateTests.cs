using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The compile-back gate: decompile every method on <see cref="CfgSampleClass"/>,
/// recompile it inside a reconstructed shape of its type, and compare the canonical
/// opcode stream against the originally compiled fixture. A method that recompiles
/// to a different stream changed the program — the worst decompiler failure class,
/// invisible to parse/bind checks. This pins the green set so a regression that turns
/// an exact method into a diff fails CI, while the documented baseline records the
/// methods that still diverge (the open docket) so the gate stays green on main.
/// </summary>
public class CompileBackGateTests
{
    const string FixtureType = "ILInspector.Decompiler.Tests.CfgSampleClass";

    /// <summary>
    /// Methods that still recompile to a different opcode stream — the open
    /// decompiler docket. Each is a tracked defect or a benign over-render; the gate
    /// tolerates these but fails if a NEW method joins the set. Shrink this list as
    /// fixes land. Tracked defects include StaleFieldRead (issue #605), Shadowed
    /// (a dropped this.field load), the .ctor field-initializer ordering, and the
    /// definite-assignment default-init over-render (TryFinallyAdd, PowerOfTwo,
    /// CatchEverything, SwitchCase, WhileLoop, ClassicLock, TryFinallyTwoReturns,
    /// ManualDisposeAsyncInFinally).
    /// </summary>
    static readonly HashSet<string> KnownDiffs = new(StringComparer.Ordinal)
    {
        ".ctor",
        "BothPositive",
        "CatchEverything",
        "ClassicLock",
        "DayNumber",
        "ManualDisposeAsyncInFinally",
        "NeitherOr",
        "PowerOfTwo",
        "ReadVolatileFlag",
        "ReverseCopy",
        "SmallStringSwitch",
        "StaleFieldRead",
        "SwitchCase",
        "TryFinallyAdd",
        "TryFinallyTwoReturns",
        "WhileLoop",
    };

    /// <summary>
    /// Methods a prior compile-back fix turned opcode-exact. Pinning them guards the
    /// fix durably: CheckedAdd must keep the overflow check (#604), UnsignedShift
    /// must keep dropping the redundant width mask (#606), and Shadowed must keep
    /// qualifying the shadowed this.field load (#607).
    /// </summary>
    static readonly string[] PinnedExact = { "CheckedAdd", "UnsignedShift", "Shadowed" };

    static IReadOnlyList<CompileBack.CompileBackResult> EvaluateFixtures()
    {
        var assembly = typeof(CfgSampleClass).Assembly.Location;
        return CompileBack.Evaluate(assembly)
            .Where(r => r.Type == FixtureType)
            .ToList();
    }

    [Fact]
    public void NoNewOpcodeDiffsBeyondKnownDocket()
    {
        var diffs = EvaluateFixtures()
            .Where(r => r.Status == CompileBack.CompileBackStatus.OpcodeDiff)
            .Select(r => r.Method)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

        var unexpected = diffs.Where(m => !KnownDiffs.Contains(m)).ToList();

        Assert.True(unexpected.Count == 0,
            $"New compile-back opcode diffs (decompiled C# recompiles to different IL): " +
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
                $"Expected compile-back to evaluate {method}, but it was not rendered.");
            foreach (var result in matches)
                Assert.True(result.Status == CompileBack.CompileBackStatus.Exact,
                    $"{method} regressed to {result.Status}: a prior compile-back fix no longer holds.\n" +
                    $"  original : {result.OriginalOpcodes}\n  recompiled: {result.RecompiledOpcodes}");
        }
    }
}
