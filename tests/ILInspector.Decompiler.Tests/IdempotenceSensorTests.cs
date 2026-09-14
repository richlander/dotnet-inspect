using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class IdempotenceSensorTests
{
    // A trivial `return 1` reaches its fixed point on the first pipeline run, so
    // the second run must rewrite nothing — the sensor's stable-method baseline.
    // This also exercises the two-run-with-one-seam contract the sensor relies on.
    [Fact]
    public void SecondRunChangedPasses_IdempotentFunction_ReportsNoRewrites()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var block = new Block();
        block.Add(new Return(new Constant(1, intType)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        var changed = IdempotenceSensor.SecondRunChangedPasses(function, _ => null);

        Assert.Empty(changed);
    }
}
