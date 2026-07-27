using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The corpus-sweep ratchet gate (docs/decompiler-quality.md): runs every method
/// of the running runtime's CoreLib through <c>IrImporter → IrPasses →</c> the
/// fidelity/gap classification once (<see cref="SweepCoreLib"/>) and asserts two
/// kinds of property over the result. It is the new-pipeline analog of the corpus
/// no-crash sweep the old stack carried, made objective.
/// <para>
/// <b>Absolute, zero-tolerance properties</b>
/// (<see cref="CoreLibSweep_IsExceptionSafeAndSemanticallyValid"/>) run
/// <em>per-PR</em> — they are deliberately NOT <c>Speed=Slow</c>:
/// </para>
/// <list type="bullet">
/// <item>zero pass-bugs — pins the "exception-safe by construction" guarantee
/// across the whole corpus, not just the curated fixtures;</item>
/// <item>zero semantic-invariant violations — every method's final IR satisfies
/// the semantic invariants (local-slot range) that only hold on fully-formed
/// output, so a pass that leaves a dangling local slot fails here (#3241). This
/// gate is the <em>only</em> place semantic invariants execute — the unit-test
/// host runs the structural level only — so it must run per-PR, or a PR that
/// breaks one would go green until the next weekly corpus run.</item>
/// </list>
/// <para>
/// <b>Drifty health floors</b> (<see cref="CoreLibSweep_MeetsHealthFloors"/>) are
/// <c>Speed=Slow</c>, runtime-version-sensitive, and run in the corpus gate:
/// </para>
/// <list type="bullet">
/// <item><c>Full</c>-fidelity % above a floor — a broad import/print regression
/// drops it;</item>
/// <item>fully-raised % above a floor — a broad structuring regression drops it.</item>
/// </list>
/// Floors, not exact baselines: they tolerate minor runtime-version drift and
/// need no per-method baseline file. They sit a couple points below the measured
/// numbers, so normal drift never flakes CI but a real regression fails it. A
/// floor that is not ratcheted when the metric improves silently decays into a
/// no-op — the same shape of problem this stack exists to fix — so when the
/// structuring work raises the true numbers, ratchet the floors up to lock the
/// gain in. The fixture fidelity gate (<see cref="FidelityGateTests"/>) remains
/// the depth signal; this is the breadth signal.
/// </summary>
[Trait("Area", "Corpus")]
public class CorpusSweepGateTests
{
    // Measured over net11 CoreLib (41,952 methods): 0 pass-bugs, 0 semantic
    // violations, 98.18% Full (41,190) after residual unspeakable metadata names
    // degrade honestly, 97.96% fully raised (41,095). Floors sit ~2 points below
    // the measured values — loose enough that a runtime patch bump does not flake
    // CI, tight enough that a real breadth regression fails it. Ratchet up when
    // structuring work raises the true numbers.
    const double FullFidelityFloor = 96.0;
    const double FullyRaisedFloor = 96.0;

    // Cap on the per-failure sample kept for the assertion message. The reported
    // count is the true, uncapped population (see SweepResult) so a catastrophic
    // run reads "12,000 method(s) (showing first 20)", not "20 method(s)".
    const int SampleCap = 20;

    static string CoreLibPath => typeof(object).Assembly.Location;

    /// <summary>
    /// The result of one whole-corpus sweep. <c>PassBugCount</c> and
    /// <c>SemanticViolationCount</c> are the true, uncapped populations; the
    /// paired lists are bounded samples for the failure message.
    /// </summary>
    sealed class SweepResult
    {
        public long Total;
        public long Full;
        public long FullyRaised;
        public long PassBugCount;
        public long SemanticViolationCount;
        public readonly List<string> PassBugs = [];
        public readonly List<string> SemanticViolations = [];
    }

    static SweepResult SweepCoreLib()
    {
        var result = new SweepResult();

        using var source = MetadataSource.Open(CoreLibPath);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            result.Total++;
            try
            {
                IrPasses.Run(function);
            }
            catch (Exception ex)
            {
                result.PassBugCount++;
                if (result.PassBugs.Count < SampleCap)
                    result.PassBugs.Add($"{typeName}::{methodName} — {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            // An importer crash surfaces as a DEC0001 diagnostic, not an
            // exception (ImportAssembly is exception-safe) — count it as a
            // pass-bug too, since it is the same by-construction violation.
            if (function.Diagnostics.FirstOrDefault().Id == DiagnosticIds.InternalError)
            {
                result.PassBugCount++;
                if (result.PassBugs.Count < SampleCap)
                    result.PassBugs.Add($"{typeName}::{methodName} — importer bug: {function.Diagnostics.First().Message}");
                continue;
            }

            // Semantic teeth over the corpus (#3241). Called explicitly rather
            // than relying on the global level: this gate counts violations
            // instead of throwing, and an explicit call keeps it hermetic and
            // independent of what a host or an operator configured. Since #3302
            // the semantic level is also armed by default, so the per-pass hooks
            // already sweep intermediate trees; this remains the FINAL-tree
            // check, and the one that reports a count.
            try
            {
                function.CheckInvariant(includeSemantics: true);
            }
            catch (InvalidOperationException ex)
            {
                result.SemanticViolationCount++;
                if (result.SemanticViolations.Count < SampleCap)
                    result.SemanticViolations.Add($"{typeName}::{methodName} — {ex.Message}");
            }

            if (function.Fidelity == DecompilationFidelity.Full)
                result.Full++;
            if (Completeness.Residual(function) is null)
                result.FullyRaised++;
        }

        return result;
    }

    static string Sample(long trueCount, List<string> shown) =>
        shown.Count < trueCount
            ? $"{trueCount} method(s) (showing first {shown.Count}):\n  " + string.Join("\n  ", shown)
            : $"{trueCount} method(s):\n  " + string.Join("\n  ", shown);

    /// <summary>
    /// Absolute properties that must hold on every commit, so this is deliberately
    /// NOT <c>Speed=Slow</c> and runs in the per-PR test lane. A ~12s corpus walk
    /// is a cheap price for per-PR teeth on the exception-safety guarantee and —
    /// critically — the semantic invariants, which execute nowhere else.
    /// </summary>
    [Fact]
    public void CoreLibSweep_IsExceptionSafeAndSemanticallyValid()
    {
        var r = SweepCoreLib();

        Assert.True(r.Total > 10_000, $"Expected a large CoreLib corpus; swept only {r.Total} methods.");

        Assert.True(r.PassBugCount == 0,
            "Pipeline must be exception-safe by construction over the whole corpus, but "
                + Sample(r.PassBugCount, r.PassBugs));

        Assert.True(r.SemanticViolationCount == 0,
            "Every method's final IR must satisfy the semantic invariants (local-slot range), but "
                + Sample(r.SemanticViolationCount, r.SemanticViolations));
    }

    /// <summary>
    /// Breadth health floors — runtime-version-sensitive and drifty, so
    /// <c>Speed=Slow</c> and run in the corpus gate rather than per-PR.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void CoreLibSweep_MeetsHealthFloors()
    {
        var r = SweepCoreLib();

        Assert.True(r.Total > 10_000, $"Expected a large CoreLib corpus; swept only {r.Total} methods.");

        double fullPercent = 100.0 * r.Full / r.Total;
        Assert.True(fullPercent >= FullFidelityFloor,
            $"Full-fidelity rate {fullPercent:F2}% ({r.Full}/{r.Total}) fell below the {FullFidelityFloor}% floor — a broad import/print regression. "
                + "If this is an intentional runtime-version shift, re-measure and adjust the floor.");

        double raisedPercent = 100.0 * r.FullyRaised / r.Total;
        Assert.True(raisedPercent >= FullyRaisedFloor,
            $"Fully-raised rate {raisedPercent:F2}% ({r.FullyRaised}/{r.Total}) fell below the {FullyRaisedFloor}% floor — a broad structuring regression. "
                + "If this is an intentional runtime-version shift, re-measure and adjust the floor.");
    }
}
