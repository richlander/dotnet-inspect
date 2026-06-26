using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class PointerAssignmentCastTests
{
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef BytePointer = TypeRef.Pointer(Byte);
    static readonly TypeRef IntPointer = TypeRef.Pointer(Int32);
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "PointerSamples");

    [Fact]
    public void MismatchedPointerLocalInitialization_RendersExplicitCast()
    {
        var output = Print(PointerStoreFunction(BytePointer, IntPointer));

        Assert.Contains("byte* V_1 = null;", output);
        Assert.Contains("int* V_0 = (int*)V_1;", output);
        Assert.DoesNotContain("int* V_0 = V_1;", output);
    }

    [Fact]
    public void SamePointerLocalInitialization_RendersWithoutCast()
    {
        var output = Print(PointerStoreFunction(IntPointer, IntPointer));

        Assert.Contains("int* V_1 = null;", output);
        Assert.Contains("int* V_0 = V_1;", output);
        Assert.DoesNotContain("int* V_0 = (int*)V_1;", output);
    }

    [Fact]
    public void MismatchedPointerAssignment_RendersExplicitCast()
    {
        var output = Print(PointerAssignmentFunction());

        Assert.Contains("int* V_0 = null;", output);
        Assert.Contains("byte* V_1 = null;", output);
        Assert.Contains("V_0 = (int*)V_1;", output);
        Assert.DoesNotContain("V_0 = V_1;", output);
    }

    [Fact]
    public void MismatchedPointerReturn_RendersExplicitCast()
    {
        var output = Print(PointerReturnFunction());

        Assert.Contains("byte* V_0 = null;", output);
        Assert.Contains("return (int*)V_0;", output);
        Assert.DoesNotContain("return V_0;", output);
    }

    static string Print(IrFunction function)
    {
        function.CheckInvariant();
        return CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
    }

    static IrFunction PointerStoreFunction(TypeRef sourcePointer, TypeRef targetPointer)
    {
        var block = new Block();
        block.Add(new StoreLocal(1, sourcePointer, new Constant(null, sourcePointer)));
        block.Add(new StoreLocal(0, targetPointer, new LoadLocal(1, sourcePointer)));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [targetPointer, sourcePointer],
            body);
    }

    static IrFunction PointerAssignmentFunction()
    {
        var block = new Block();
        block.Add(new StoreLocal(0, IntPointer, new Constant(null, IntPointer)));
        block.Add(new StoreLocal(1, BytePointer, new Constant(null, BytePointer)));
        block.Add(new StoreLocal(0, IntPointer, new LoadLocal(1, BytePointer)));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [IntPointer, BytePointer],
            body);
    }

    static IrFunction PointerReturnFunction()
    {
        var block = new Block();
        block.Add(new StoreLocal(0, BytePointer, new Constant(null, BytePointer)));
        block.Add(new Return(new LoadLocal(0, BytePointer)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(IntPointer, [], HasThis: false, GenericParameterCount: 0),
            [BytePointer],
            body);
    }
}
