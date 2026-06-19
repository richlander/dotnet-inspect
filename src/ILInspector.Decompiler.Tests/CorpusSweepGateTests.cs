using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The corpus-sweep ratchet gate (docs/decompiler-quality.md): runs every method
/// of the running runtime's CoreLib through <c>IrImporter → IrPasses →</c> the
/// fidelity/gap classification, and asserts <b>health floors</b>. It is the
/// new-pipeline analog of the corpus no-crash sweep the old stack carried, made
/// objective:
/// <list type="bullet">
/// <item>zero pass-bugs — pins the "exception-safe by construction" guarantee
/// across the whole corpus, not just the curated fixtures;</item>
/// <item><c>Full</c>-fidelity % above a floor — a broad import/print regression
/// drops it;</item>
/// <item>fully-raised % above a floor — a broad structuring regression drops it.</item>
/// </list>
/// Floors, not exact baselines: they tolerate minor runtime-version drift and
/// need no per-method baseline file. They sit a couple points below the measured
/// numbers, so normal drift never flakes CI but a real regression fails it. When
/// the structuring work raises the true numbers, ratchet the floors up to lock
/// the gain in. The fixture fidelity gate (<see cref="FidelityGateTests"/>)
/// remains the depth signal; this is the breadth signal.
/// </summary>
public class CorpusSweepGateTests
{
    // Measured over net11 CoreLib (41,159 methods): 0 pass-bugs, 99.97% Full,
    // 95.75% fully raised. Floors sit a couple points below.
    const double FullFidelityFloor = 99.0;
    const double FullyRaisedFloor = 94.0;

    static string CoreLibPath => typeof(object).Assembly.Location;

    [Fact]
    public void CoreLibSweep_MeetsHealthFloors()
    {
        long total = 0, full = 0, fullyRaised = 0;
        var passBugs = new List<string>();

        using (var source = MetadataSource.Open(CoreLibPath))
        {
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
            {
                total++;
                try
                {
                    IrPasses.Run(function);
                }
                catch (Exception ex)
                {
                    if (passBugs.Count < 20)
                        passBugs.Add($"{typeName}::{methodName} — {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                // An importer crash surfaces as a DEC0001 diagnostic, not an
                // exception (ImportAssembly is exception-safe) — count it as a
                // pass-bug too, since it is the same by-construction violation.
                if (function.Diagnostics.FirstOrDefault().Id == DiagnosticIds.InternalError)
                {
                    if (passBugs.Count < 20)
                        passBugs.Add($"{typeName}::{methodName} — importer bug: {function.Diagnostics.First().Message}");
                    continue;
                }

                if (function.Fidelity == DecompilationFidelity.Full)
                    full++;
                if (Completeness.Residual(function) is null)
                    fullyRaised++;
            }
        }

        Assert.True(total > 10_000, $"Expected a large CoreLib corpus; swept only {total} methods.");

        Assert.True(passBugs.Count == 0,
            $"Pipeline must be exception-safe by construction over the whole corpus, but {passBugs.Count} method(s) failed:\n  "
                + string.Join("\n  ", passBugs));

        double fullPercent = 100.0 * full / total;
        Assert.True(fullPercent >= FullFidelityFloor,
            $"Full-fidelity rate {fullPercent:F2}% ({full}/{total}) fell below the {FullFidelityFloor}% floor — a broad import/print regression. "
                + "If this is an intentional runtime-version shift, re-measure and adjust the floor.");

        double raisedPercent = 100.0 * fullyRaised / total;
        Assert.True(raisedPercent >= FullyRaisedFloor,
            $"Fully-raised rate {raisedPercent:F2}% ({fullyRaised}/{total}) fell below the {FullyRaisedFloor}% floor — a broad structuring regression. "
                + "If this is an intentional runtime-version shift, re-measure and adjust the floor.");
    }
}
