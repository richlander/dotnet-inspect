using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;

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

    [Fact]
    public void SlotUnifierCensus_PrintsPrinterTelemetryCard()
    {
        string output = CaptureConsole(() => SlotUnifierCensus.Run([FixturePath], cap: 20, maxExamples: 2));

        Assert.Contains("STACK-SLOT UNIFIER CENSUS", output);
        Assert.Contains("Un-unified split slots", output);
        Assert.Contains("Multi-candidate slots unified by printer", output);
        Assert.Contains("Stack-slot declarations emitted", output);
    }

    [Fact]
    public void StackSlotUnifierTelemetry_RecordsUnunifiedSplitSlots()
    {
        var obj = TypeRef.CoreLib("System", "Object");
        var str = TypeRef.CoreLib("System", "String");
        var i32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");

        var block = new Block();
        block.Add(new StoreStackSlot(0, new Constant(null, obj)));
        block.Add(new StoreStackSlot(0, new Constant("x", str)));
        block.Add(new StoreStackSlot(0, new Constant(1, i32)));
        block.Add(new Return(new LoadStackSlot(0, obj)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        var telemetry = CSharpPrinter.CollectStackSlotUnifierTelemetry(function);

        Assert.Equal(3, telemetry.StoreNodes);
        Assert.Equal(1, telemetry.LoadNodes);
        Assert.Equal(1, telemetry.DistinctSlots);
        Assert.Equal(1, telemetry.UnunifiedSplitSlots);
        Assert.Equal(0, telemetry.MultiCandidateUnifiedSlots);
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
