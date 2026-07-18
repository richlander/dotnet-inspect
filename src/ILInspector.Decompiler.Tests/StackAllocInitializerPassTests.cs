using ILInspector.Decompiler.Pipeline;


using Xunit;
using System.Linq;
using System.Collections.Generic;

namespace ILInspector.Decompiler.Tests;

public class StackAllocInitializerPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef VoidPointer = TypeRef.Pointer(Void);
    static readonly TypeRef BytePointer = TypeRef.Pointer(Byte);
    static readonly TypeRef ReadOnlySpanByte = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [Byte]);

    [Fact]
    public void CanonicalSpanPositive_Raises()
    {
        var function = Build(12, 12, false, false, false, false, false);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal(12, ((Constant)raised.Count).Value);
    }

    [Fact]
    public void CanonicalRvaPositive_Raises()
    {
        var function = Build(12, 12, false, false, false, false, true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal(12, ((Constant)raised.Count).Value);
    }

    [Fact]
    public void MismatchedSize_Declines()
    {
        var function = Build(16, 12, false, false, false, false, false);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void EscapedDestination_Declines()
    {
        var function = Build(12, 12, true, false, false, false, false);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void InterveningWrite_Declines()
    {
        var function = Build(12, 12, false, true, false, false, false);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void SharedSpanLiteralMutation_Declines()
    {
        var function = Build(12, 12, false, false, true, false, false);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void LongerRva_Declines()
    {
        var function = Build(12, 12, false, false, false, false, true, true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void SpoofedMemoryMarshal_Declines()
    {
        var function = BuildSpoofed("MemoryMarshal");
        new StackAllocInitializerPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void SpoofedReadOnlySpan_Declines()
    {
        var function = BuildSpoofed("ReadOnlySpan`1");
        new StackAllocInitializerPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    static IrFunction Build(int allocSize, int copySize, bool escapeDest, bool interveningWrite, bool sharedSpanLiteral, bool throwingSetup, bool useRva, bool longerRva = false)
    {
        var stackAlloc = new StackAllocate(new Constant(allocSize, Int32));
        var storeSlot = new StoreStackSlot(0, stackAlloc);

        var loadDest = new LoadStackSlot(0, BytePointer);

        IrExpression copySource;
        IrNode? setup = null;

        if (useRva)
        {
            var rvaData = longerRva ? new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 0, 0 } : new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0 };
            copySource = new LoadFieldAddress(new FieldRef(TypeRef.CoreLib("Synthetic", "Blob"), "data", Int32), null) { FieldRvaData = rvaData };
        }
        else
        {
            var elements = new List<IrExpression>();
            for(int i = 0; i < copySize; i++) elements.Add(new Constant(i, Byte));
            var spanLit = new SpanLiteral(Byte, ReadOnlySpanByte, elements);
            setup = new StoreLocal(2, ReadOnlySpanByte, spanLit);

            var getRef = new MethodRef(TypeRef.CoreLib("System.Runtime.InteropServices", "MemoryMarshal"), "GetReference", Byte, [ReadOnlySpanByte], HasThis: false);
            copySource = new Call(getRef, isVirtual: false, [new LoadLocalAddress(2, ReadOnlySpanByte)]);
        }

        var copyBlock = new CopyBlock(loadDest, copySource, new Constant(copySize, Int32));

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
        if (setup != null)
        {
            block.Add(setup);
        }
        block.Add(copyBlock);

        var finalUsage = new StoreLocal(1, BytePointer, new LoadStackSlot(0, BytePointer));
        block.Add(finalUsage);

        if (sharedSpanLiteral)
        {
            block.Add(new Call(new MethodRef(Holder, "Print", Void, [ReadOnlySpanByte], HasThis: false), isVirtual: false, [new LoadLocalAddress(2, ReadOnlySpanByte)]));
        }

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

    static IrFunction BuildSpoofed(string spoofType)
    {
        var stackAlloc = new StackAllocate(new Constant(12, Int32));
        var storeSlot = new StoreStackSlot(0, stackAlloc);
        var loadDest = new LoadStackSlot(0, BytePointer);

        IrExpression copySource;
        if (spoofType == "MemoryMarshal")
        {
            var spoofedMarshal = TypeRef.CoreLib("Synthetic", "MemoryMarshal"); // Not System.Runtime.InteropServices.MemoryMarshal
            var getRef = new MethodRef(spoofedMarshal, "GetReference", Byte, [Byte], HasThis: false);
            copySource = new Call(getRef, isVirtual: false, [new Constant(0, Byte)]);
        }
        else
        {
            var spoofedSpan = TypeRef.CoreLib("Synthetic", "ReadOnlySpan`1"); // Not System.ReadOnlySpan
            copySource = new Call(new MethodRef(spoofedSpan, "get_Item", Byte, [Int32], HasThis: true), isVirtual: false, [new LoadLocal(0, spoofedSpan), new Constant(0, Int32)]);
        }

        var copyBlock = new CopyBlock(loadDest, copySource, new Constant(12, Int32));

        var block = new Block(0);
        block.Add(storeSlot);
        block.Add(copyBlock);

        var finalUsage = new StoreLocal(1, BytePointer, new LoadStackSlot(0, BytePointer));
        block.Add(finalUsage);
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);

        return new IrFunction("M", Holder, new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0), [], body);
    }
}
