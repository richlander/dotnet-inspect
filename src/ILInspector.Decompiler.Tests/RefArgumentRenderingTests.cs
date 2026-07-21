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
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");

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

    static IrFunction BuildRefReturn()
    {
        var refInt = TypeRef.ByRef(Int32);
        // ref int M() { return <ref local>; }  — the ref local names a place, so the
        // by-ref return must spell `return ref V_0;` (a bare `return V_0;` is CS8150).
        var block = new Block(0);
        block.Add(new Return(new LoadLocal(0, refInt)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(refInt, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("System", "Holder"), signature, [refInt], container);
    }

    [Fact]
    public void ByRefReturn_RendersReturnRef()
    {
        var output = CSharpPrinter.Print(BuildRefReturn()).Output;

        Assert.Contains("return ref V_0;", output);
        Assert.DoesNotContain("return V_0;", output);
    }

    // Issue #2916: an `Unbox` passed to a `ref`/`out` parameter is the managed
    // pointer into the box, so it must spell as the `Unsafe.Unbox<T>(o)`
    // intrinsic — a genuine ref-place. `ArgumentLvalue` used to exclude `Unbox`,
    // leaving these positions to the default `ref (T)x` spelling, which is
    // CS0445/CS0206. (`Unsafe.Unbox<int>(o)` validity in every ref-place is
    // compile-verified in PrinterPrecedenceTests.ReturnRefUnbox_SpellsUnsafeUnbox
    // and the PR's csc probe.)
    static IrFunction BuildUnboxByRefCall(ArgumentRefKind refKind)
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
        // Exchange(ref/out unbox<int>(o), 5)
        var call = new Call(callee, isVirtual: false, [new Unbox(Int32, new LoadArgument(0, "o", Object)), new Constant(5, Int32)]);
        var block = new Block(0);
        block.Add(new Return(call));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32, [new Parameter("o", Object)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("System", "Holder"), signature, [], container);
    }

    [Fact]
    public void RefArgumentUnbox_SpellsUnsafeUnbox()
    {
        var output = CSharpPrinter.Print(BuildUnboxByRefCall(ArgumentRefKind.Ref)).Output;

        Assert.Contains("Exchange(ref ", output);
        Assert.Contains("Unsafe.Unbox<int>(o)", output);
        Assert.DoesNotContain("ref (int)o", output);
    }

    [Fact]
    public void OutArgumentUnbox_SpellsUnsafeUnbox()
    {
        var output = CSharpPrinter.Print(BuildUnboxByRefCall(ArgumentRefKind.Out)).Output;

        Assert.Contains("Exchange(out ", output);
        Assert.Contains("Unsafe.Unbox<int>(o)", output);
        Assert.DoesNotContain("out (int)o", output);
    }
}
