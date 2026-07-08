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

    [Fact]
    public void StackSlotUnifierTelemetry_UnifiesReferenceCoalesceAtObjectTarget()
    {
        var obj = TypeRef.CoreLib("System", "Object");
        var str = TypeRef.CoreLib("System", "String");
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner", ValueTypeHint.ReferenceType);
        var voidType = TypeRef.CoreLib("System", "Void");

        var block = new Block();
        block.Add(new StoreStackSlot(0, new Coalesce(
            new LoadLocal(0, str),
            new LoadArgument(0, "fallback", obj))));
        block.Add(new ExpressionStatement(new Call(
            new MethodRef(owner, "Use", voidType, [obj], HasThis: false),
            isVirtual: false,
            [new LoadStackSlot(0, obj)])));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [
                new Parameter("fallback", obj),
            ], HasThis: false, GenericParameterCount: 0),
            [str],
            body);

        var telemetry = CSharpPrinter.CollectStackSlotUnifierTelemetry(function);
        var output = CSharpPrinter.Print(function).Output;

        Assert.Equal(0, telemetry.UnunifiedSplitSlots);
        Assert.DoesNotContain("S_0_1", output);
        Assert.Contains("object S_0 = V_0 ?? fallback;", output);
        Assert.Contains("Use(S_0);", output);
    }

    [Fact]
    public void StackSlotUnifierTelemetry_DoesNotUnifyObjectCoalesceToStringTarget()
    {
        var obj = TypeRef.CoreLib("System", "Object");
        var str = TypeRef.CoreLib("System", "String");
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner", ValueTypeHint.ReferenceType);
        var voidType = TypeRef.CoreLib("System", "Void");

        var block = new Block();
        block.Add(new StoreStackSlot(0, new Coalesce(
            new LoadArgument(0, "left", obj),
            new LoadArgument(1, "right", str))));
        block.Add(new ExpressionStatement(new Call(
            new MethodRef(owner, "Use", voidType, [str], HasThis: false),
            isVirtual: false,
            [new LoadStackSlot(0, str)])));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [
                new Parameter("left", obj),
                new Parameter("right", str),
            ], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        var telemetry = CSharpPrinter.CollectStackSlotUnifierTelemetry(function);
        var output = CSharpPrinter.Print(function).Output;

        Assert.Equal(1, telemetry.UnunifiedSplitSlots);
        Assert.Contains("object S_0 = left ?? right;", output);
        Assert.Contains("Use(S_0_1);", output);
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
