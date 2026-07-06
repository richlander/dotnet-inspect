using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Collection(ConsoleMutatorCollection.Name)]
public class SlotResidualCensusTests
{
    static readonly object ConsoleGate = new();
    static string FixturePath => typeof(LadderRung1.Foundation).Assembly.Location;

    [Fact]
    public void SlotResidualCensus_PrintsPostF2MeasurementCard()
    {
        string output = CaptureConsole(() => SlotResidualCensus.Run([FixturePath], cap: 20, maxExamples: 2));

        Assert.Contains("F2 SLOT RESIDUAL CENSUS", output);
        Assert.Contains("Before late F2", output);
        Assert.Contains("After late F2", output);
        Assert.Contains("Post-F2 residual deferral classes", output);
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
