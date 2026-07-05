using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class ValidityCoverageReportingTests
{
    static readonly object ConsoleGate = new();
    static string FixturePath => typeof(LadderRung1.Foundation).Assembly.Location;

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
