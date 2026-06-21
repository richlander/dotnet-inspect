using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A by-ref call argument can arrive as a ref-typed stack slot: when a `ref`
// argument (a field/local address) is evaluated before a later side-effecting
// argument, the importer spills the managed pointer into a ref slot that survives
// to the call. That slot still names a place, so the call must render with the
// `ref`/`out` keyword the parameter demands — a bare `M(S_0, ...)` is CS1620.
public class RefArgumentRenderingTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    static IrFunction BuildSlotByRefCall(ArgumentRefKind refKind)
    {
        var refInt = TypeRef.ByRef(Int32);
        var callee = new MethodRef(
            TypeRef.CoreLib("System", "Sample"),
            "Exchange",
            Int32,
            [refInt, Int32],
            HasThis: false)
        {
            ParameterRefKinds = [refKind, ArgumentRefKind.Value],
        };
        // Exchange(<ref slot>, 5)
        var call = new Call(callee, isVirtual: false, [new LoadStackSlot(0, refInt), new Constant(5, Int32)]);
        var block = new Block(0);
        block.Add(new Return(call));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("System", "Holder"), signature, [], container);
    }

    [Fact]
    public void RefArgumentInStackSlot_RendersWithRefKeyword()
    {
        var output = CSharpPrinter.Print(BuildSlotByRefCall(ArgumentRefKind.Ref)).Output;

        Assert.Contains("Exchange(ref S_0, 5)", output);
        Assert.DoesNotContain("Exchange(S_0", output);
    }

    [Fact]
    public void OutArgumentInStackSlot_RendersWithOutKeyword()
    {
        var output = CSharpPrinter.Print(BuildSlotByRefCall(ArgumentRefKind.Out)).Output;

        Assert.Contains("Exchange(out S_0, 5)", output);
        Assert.DoesNotContain("Exchange(S_0", output);
    }
}
