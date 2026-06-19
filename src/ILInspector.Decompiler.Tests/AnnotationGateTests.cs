using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The annotation gate (docs/design/hidden-fact-annotations.md): the
/// breadth signal for the hidden-fact annotations, the analyzer analog of the
/// <see cref="FidelityGateTests"/> depth signal. It runs <c>--annotation-check</c>
/// over the running runtime's CoreLib and grades every annotation's IL offset
/// against the raw opcode read independently with <c>ILReader</c>:
/// <list type="bullet">
/// <item><b>precision == 0 violations</b> — a wrong fact (an <c>alloc.box</c> not
/// sitting on a <c>box</c>) is always an importer-typing or classifier bug, never
/// runtime drift, so it is gated absolutely;</item>
/// <item><b>0 import crashes</b> — pins the exception-safe-by-construction
/// guarantee across the whole corpus, not just the curated fixtures;</item>
/// <item><b>recall above a floor</b> — every unambiguous witness opcode
/// (<c>box</c>/<c>newarr</c>/<c>localloc</c>/<c>calli</c>) and every confirmed
/// reference-type <c>newobj</c> should produce its annotation; a floor (not an
/// exact baseline) tolerates runtime-version drift while a broad completeness
/// regression still fails it. (A <c>newobj</c>'s constructed type is resolved
/// from metadata independently of the importer; value-type and unresolvable
/// cross-assembly newobjs are excluded — see <c>AnnotationCheck.ResolveNewObjKind</c>.)</item>
/// </list>
/// The corpus check guards against a vacuous green: a refactor that silently stops
/// producing annotations would make a zero-violation assertion meaningless, so the
/// gate also requires a large checked population.
/// </summary>
public class AnnotationGateTests
{
    // Measured over net11 CoreLib (41,012 methods): 100% precision (19,425
    // graded — every annotation offset plus the value-type-newobj no-allocation
    // checks, 0 violations), 100% recall (14,488 witnesses, including 8,986
    // confirmed reference-type newobjs). The recall floor sits a couple points
    // below; precision and crashes are absolute.
    const double RecallFloor = 98.0;

    static string CoreLibPath => typeof(object).Assembly.Location;

    [Fact]
    public void CoreLib_AnnotationsAgreeWithRawIL()
    {
        var result = AnnotationCheck.Evaluate(CoreLibPath);

        Assert.True(result.Methods > 10_000,
            $"Expected a large CoreLib corpus; swept only {result.Methods} methods.");

        // Non-vacuous: the sweep must actually grade a broad annotation population,
        // or a zero-violation result would prove nothing.
        Assert.True(result.PrecisionChecked > 1_000,
            $"Expected a broad annotation population to grade; only {result.PrecisionChecked} were checked. "
                + "A classifier or importer change may have silently stopped producing annotations.");

        Assert.True(result.ImportCrashes == 0,
            $"The annotation sweep must be exception-safe over the whole corpus, but {result.ImportCrashes} method(s) crashed:\n  "
                + string.Join("\n  ", result.ImportCrashExamples));

        Assert.True(result.PrecisionViolations == 0,
            $"Annotation precision must be exact: {result.PrecisionViolations} annotation(s) sit on an IL offset whose raw "
                + "opcode contradicts the claim — an importer-typing or classifier bug:\n  "
                + string.Join("\n  ", result.Precision.SelectMany(c => c.Examples)));

        double recallPercent = result.RecallChecked == 0
            ? 0
            : 100.0 * (result.RecallChecked - result.RecallViolations) / result.RecallChecked;
        Assert.True(recallPercent >= RecallFloor,
            $"Annotation recall {recallPercent:F2}% ({result.RecallChecked - result.RecallViolations}/{result.RecallChecked}) "
                + $"fell below the {RecallFloor}% floor — a broad completeness regression (an unambiguous witness opcode "
                + "stopped producing its annotation). If this is an intentional runtime-version shift, re-measure and adjust the floor.\n  "
                + string.Join("\n  ", result.Recall.SelectMany(c => c.Examples)));
    }
}
