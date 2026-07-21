using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Collection(ConsoleMutatorCollection.Name)]
public class ValidityCoverageReportingTests
{
    static readonly object ConsoleGate = new();
    static string FixturePath => typeof(LadderRung1.Foundation).Assembly.Location;
    static string DecompilerPath => typeof(IrImporter).Assembly.Location;

    [Fact]
    public void ValidityCheck_CappedRunLabelsSemanticFindingsAsPerSample()
    {
        string output = CaptureConsole(() => ValidityCheck.Run([FixturePath], cap: 1, maxExamples: 0));

        Assert.Contains("Semantic binding (Full + syntactically-valid): compiled 1 of ", output);
        Assert.Contains("compile-cap 1", output);
        Assert.Contains("semantic findings are per-sample, not corpus-wide", output);
    }

    [Fact]
    public void ValidityPredicateScan_PrintsExhaustiveNonCompilerCoverageLane()
    {
        string output = CaptureConsole(() => ValidityPredicateScan.Run([FixturePath], maxExamples: 1, workers: 1, sequential: true));

        Assert.Contains("VALIDITY PREDICATE SCAN", output);
        Assert.Contains("No compilation performed", output);
        Assert.Contains("conditional-arm-numeric-join-cast", output);
        Assert.Contains("conditional-target-numeric-cast", output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void DecompilerAssembly_MissingReturnPopulationIsPinned()
    {
        var actual = ValidityCheck.Evaluate(DecompilerPath)
            .Where(result => result.SemanticDiagnostics.Any(diagnostic => diagnostic.Id == "CS0161"))
            .Select(result => result.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Release census refreshed against the merged head (verified by dumping
        // the live CS0161 population). #2959 removed six scattered-return
        // dispatch defects — the shared return is now duplicated into each guard
        // instead of dropping an edge: TupleSwitchExpressionPass::TryNumericValue,
        // IsPatternPass::IsPatternLocalNull, NullConditionalPass::MemberReceiver,
        // UnionSwitchExpressionPass::SameTailNode,
        // DynamicCallSitePass::TryGuardCacheLoad, and
        // FluentChainRecompositionPass::SinkStatement.
        // YieldBreakLoopIteratorReconstruction::TryNormalizeContinueCondition is a
        // distinct result-temp switch-expression shape the fix does not cover and
        // remains pinned as tracked follow-up.
        string[] expected =
#if DEBUG
        [
            "ILInspector.Decompiler.MemberBodyProducer::DecompileBody",
        ];
#else
        [
            "ILInspector.Decompiler.MemberBodyProducer::DecompileBody",
            "ILInspector.Decompiler.Pipeline.BooleanFoldingPass::IsNullableCoalesceExpressionContext",
            "ILInspector.Decompiler.Pipeline.CSharpPrinter::ForLoopIncrementText",
            "ILInspector.Decompiler.Pipeline.DeconstructionAssignmentPass::TryMatchTupleSeed",
            "ILInspector.Decompiler.Pipeline.FixedArrayRaising::SameLoadPlace",
            "ILInspector.Decompiler.Pipeline.IndexFromEndPass::LengthReceiver",
            "ILInspector.Decompiler.Pipeline.InlineArrayCollectionPass::PlaceFromAddress",
            "ILInspector.Decompiler.Pipeline.YieldBreakLoopIteratorReconstruction::TryNormalizeContinueCondition",
        ];
#endif
        Assert.Equal(expected, actual);
    }

    static string CaptureConsole(Func<int> action)
    {
        lock (ConsoleGate)
        {
            var originalOut = Console.Out;
            using var writer = new StringWriter();
            try
            {
                Console.SetOut(writer);
                int exitCode = action();
                Assert.Equal(0, exitCode);
                return writer.ToString();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
