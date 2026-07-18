using ILInspector.Decompiler.Pipeline;
using Xunit;
using System.Linq;

namespace ILInspector.Decompiler.Tests;

public class StackAllocInitializerPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef VoidPointer = TypeRef.Pointer(Void);
    static readonly TypeRef BytePointer = TypeRef.Pointer(Byte);

    [Fact]
    public void MismatchedSize_Declines()
    {
        var function = Build(16, 12, false, false);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void EscapedDestination_Declines()
    {
        var function = Build(12, 12, true, false);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void InterveningWrite_Declines()
    {
        var function = Build(12, 12, false, true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void SpoofedMemoryMarshal_Declines()
    {
        var stackAlloc = new StackAllocate(new Constant(12, Int32));
        var storeSlot = new StoreStackSlot(0, stackAlloc);
        var loadDest = new LoadStackSlot(0, BytePointer);

        var spoofedMarshal = TypeRef.CoreLib("Synthetic", "MemoryMarshal"); // Not System.Runtime.InteropServices.MemoryMarshal
        var getRef = new MethodRef(spoofedMarshal, "GetReference", Byte, [Byte], HasThis: false);
        var call = new Call(getRef, isVirtual: false, [new Constant(0, Byte)]);

        var copyBlock = new CopyBlock(loadDest, call, new Constant(12, Int32));

        var block = new Block(0);
        block.Add(storeSlot);
        block.Add(copyBlock);

        var finalUsage = new StoreLocal(1, BytePointer, new LoadStackSlot(0, BytePointer));
        block.Add(finalUsage);
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);

        var function = new IrFunction("M", Holder, new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0), [], body);

        new StackAllocInitializerPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void SpoofedReadOnlySpan_Declines()
    {
        var stackAlloc = new StackAllocate(new Constant(12, Int32));
        var storeSlot = new StoreStackSlot(0, stackAlloc);
        var loadDest = new LoadStackSlot(0, BytePointer);

        var spoofedSpan = TypeRef.CoreLib("Synthetic", "ReadOnlySpan`1"); // Not System.ReadOnlySpan
        var call = new Call(new MethodRef(spoofedSpan, "get_Item", Byte, [Int32], HasThis: true), isVirtual: false, [new LoadLocal(0, spoofedSpan), new Constant(0, Int32)]);

        var copyBlock = new CopyBlock(loadDest, call, new Constant(12, Int32));

        var block = new Block(0);
        block.Add(storeSlot);
        block.Add(copyBlock);

        var finalUsage = new StoreLocal(1, BytePointer, new LoadStackSlot(0, BytePointer));
        block.Add(finalUsage);
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);

        var function = new IrFunction("M", Holder, new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0), [], body);

        new StackAllocInitializerPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }
    static IrFunction Build(int allocSize, int copySize, bool escapeDest, bool interveningWrite)
    {
        var stackAlloc = new StackAllocate(new Constant(allocSize, Int32));
        var storeSlot = new StoreStackSlot(0, stackAlloc);

        var loadDest = new LoadStackSlot(0, BytePointer);
        var rvaData = new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0 };
        var loadField = new LoadFieldAddress(new FieldRef(TypeRef.CoreLib("Synthetic", "Blob"), "data", Int32), null) { FieldRvaData = rvaData };

        var copyBlock = new CopyBlock(loadDest, loadField, new Constant(copySize, Int32));

        var block = new Block(0);
        block.Add(storeSlot);
        if (escapeDest)
        {
            block.Add(new Call(new MethodRef(Holder, "Escape", Void, [BytePointer], HasThis: false), isVirtual: false, [new LoadStackSlot(0, BytePointer)]));
        }
        if (interveningWrite)
        {
            block.Add(new StoreIndirect(Byte, new LoadStackSlot(0, BytePointer), new Constant(42, Byte)));
        }
        block.Add(copyBlock);

        var finalUsage = new StoreLocal(1, BytePointer, new LoadStackSlot(0, BytePointer));
        block.Add(finalUsage);
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
