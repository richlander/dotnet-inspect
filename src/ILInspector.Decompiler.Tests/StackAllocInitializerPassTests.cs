using ILInspector.Decompiler.Pipeline;
using Xunit;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;

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
        var function = BuildCanonicalSpan();
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal(12, ((Constant)raised.Count).Value);
    }

    [Fact]
    public void CanonicalRvaPositive_Raises()
    {
        var function = BuildCanonicalRva();
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal(12, ((Constant)raised.Count).Value);
    }

    [Fact]
    public void MismatchedSize_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => {
            b.AllocSize = 16;
        });
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void EscapedDestination_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.EscapeDest = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void InterveningWrite_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.InterveningWrite = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void SharedSpanLiteralMutation_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b => b.SharedSpanLiteral = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void LongerRva_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.LongerRva = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void TruncatedRva_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.TruncatedRva = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void ThrowingSetup_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.ThrowingSetup = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void CrossBlock_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.CrossBlock = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void UnrelatedSpanLiteralSetup_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.UnrelatedSpanLiteralSetup = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void NonConstantSpanLiteral_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b => b.NonConstantSpanLiteral = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void DestinationAliasBefore_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.DestinationAliasBefore = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void DestinationAliasAfter_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.DestinationAliasAfter = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void SpoofedAssembly_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b => b.SpoofedAssembly = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void SpoofedSignature_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b => b.SpoofedSignature = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void FinalSpanCtor_Rva_Raises()
    {
        var function = BuildCanonicalRva(mutate: b => b.FinalSpanCtor = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal(12, ((Constant)raised.Count).Value);
    }

    [Fact]
    public void FinalSpanCtorSpoofed_Rva_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => { b.FinalSpanCtor = true; b.SpoofedFinalSpanCtor = true; });
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }


    [Fact]
    public void SpanGetItemPositive_Raises()
    {
        var function = BuildCanonicalSpan(mutate: b => b.UseGetItem = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal(12, ((Constant)raised.Count).Value);
    }

    [Fact]
    public void SpanGetPinnableReferencePositive_Raises()
    {
        var function = BuildCanonicalSpan(mutate: b => b.UseGetPinnableReference = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal(12, ((Constant)raised.Count).Value);
    }

    [Fact]
    public void MismatchedSpanElement_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b => b.MismatchedSpanElement = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void WrongHasThis_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b => b.WrongHasThis = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void MalformedArgumentCounts_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b => b.MalformedArgumentCounts = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void RvaMisalignment_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => {
            b.AllocSize = 13;
            b.CopySize = 13;
            b.RvaMisaligned = true;
            b.Int32Element = true;
            b.FinalSpanCtor = true;
        });
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void MalformedFinalSpanCtor_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.MalformedFinalSpanCtor = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    class Builder
    {
        public bool IsRva;
        public int AllocSize = 12;
        public int CopySize = 12;
        public bool EscapeDest;
        public bool InterveningWrite;
        public bool SharedSpanLiteral;
        public bool ThrowingSetup;
        public bool LongerRva;
        public bool TruncatedRva;
        public bool CrossBlock;
        public bool UnrelatedSpanLiteralSetup;
        public bool NonConstantSpanLiteral;
        public bool DestinationAliasBefore;
        public bool DestinationAliasAfter;
        public bool SpoofedAssembly;
        public bool SpoofedSignature;
        public bool FinalSpanCtor;
        public bool SpoofedFinalSpanCtor;
        public bool UseGetItem;
        public bool UseGetPinnableReference;
        public bool MismatchedSpanElement;
        public bool WrongHasThis;
        public bool MalformedArgumentCounts;
        public bool RvaMisaligned;
        public bool Int32Element;
        public bool MalformedFinalSpanCtor;

        public IrFunction Build()
        {
            var stackAlloc = new StackAllocate(new Constant(AllocSize, Int32));
            var storeSlot = new StoreStackSlot(0, stackAlloc);

            var loadDest = new LoadStackSlot(0, BytePointer);

            IrExpression copySource;
            IrNode? setup = null;

            if (IsRva)
            {
                byte[] rvaData;
                if (LongerRva) rvaData = new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 0, 0 };
                else if (TruncatedRva) rvaData = new byte[] { 1, 0, 0, 0, 2, 0, 0, 0 };
                else if (RvaMisaligned) rvaData = new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 5 };
                else rvaData = new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0 };

                copySource = new LoadFieldAddress(new FieldRef(TypeRef.CoreLib("Synthetic", "Blob"), "data", Int32), null) { FieldRvaData = rvaData };
            }
            else
            {
                var elements = new List<IrExpression>();
                for(int i = 0; i < CopySize; i++)
                {
                    if (NonConstantSpanLiteral && i == 0) elements.Add(new LoadArgument(0, "arg", Byte));
                    else elements.Add(new Constant(i, Byte));
                }
                var spanLit = new SpanLiteral(Byte, ReadOnlySpanByte, elements);
                setup = new StoreLocal(2, ReadOnlySpanByte, spanLit);

                var marshalType = SpoofedAssembly ? TypeRef.Definition("System.Memory", "System.Runtime.InteropServices", "MemoryMarshal", ValueTypeHint.ReferenceType) : TypeRef.CoreLib("System.Runtime.InteropServices", "MemoryMarshal");
                var returnType = SpoofedSignature ? Byte : TypeRef.ByRef(Byte);
                if (MismatchedSpanElement) returnType = TypeRef.ByRef(Int32);

                var spanType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [Byte]);
                var getRefDecl = marshalType;

                if (UseGetItem)
                {
                    var getItem = new MethodRef(spanType, "get_Item", returnType, [Int32], HasThis: !WrongHasThis)
                    {
                        DeclaringTypeIsTrustedPlatform = SpoofedAssembly ? MetadataFactState.Unknown : MetadataFactState.Yes
                    };
                    var args = MalformedArgumentCounts ? new IrExpression[] { new LoadLocalAddress(2, spanType) } : new IrExpression[] { new LoadLocalAddress(2, spanType), new Constant(0, Int32) };
                    copySource = new Call(getItem, isVirtual: false, args);
                }
                else if (UseGetPinnableReference)
                {
                    var getPin = new MethodRef(spanType, "GetPinnableReference", returnType, [], HasThis: !WrongHasThis)
                    {
                        DeclaringTypeIsTrustedPlatform = SpoofedAssembly ? MetadataFactState.Unknown : MetadataFactState.Yes
                    };
                    var args = MalformedArgumentCounts ? new IrExpression[] { new LoadLocalAddress(2, spanType), new Constant(0, Int32) } : new IrExpression[] { new LoadLocalAddress(2, spanType) };
                    copySource = new Call(getPin, isVirtual: false, args);
                }
                else
                {
                    var getRef = new MethodRef(marshalType, "GetReference", returnType, [spanType], HasThis: WrongHasThis)
                    {
                        DeclaringTypeIsTrustedPlatform = SpoofedAssembly ? MetadataFactState.Unknown : MetadataFactState.Yes,
                        TypeArguments = [MismatchedSpanElement ? Int32 : Byte]
                    };
                    var args = MalformedArgumentCounts ? new IrExpression[] { new LoadLocalAddress(2, spanType), new Constant(0, Int32) } : new IrExpression[] { new LoadLocalAddress(2, spanType) };
                    copySource = new Call(getRef, isVirtual: false, args);
                }
            }

            var copyBlock = new CopyBlock(loadDest, copySource, new Constant(CopySize, Int32));

            var block0 = new Block(0);
            var block1 = new Block(1);
            var activeBlock = block0;

            activeBlock.Add(storeSlot);

            if (DestinationAliasBefore)
            {
                activeBlock.Add(new StoreLocal(5, BytePointer, new LoadStackSlot(0, BytePointer)));
            }

            if (CrossBlock)
            {
                activeBlock.Add(new Branch(1));
                activeBlock = block1;
            }

            if (EscapeDest)
            {
                activeBlock.Add(new Call(new MethodRef(Holder, "Escape", Void, [BytePointer], HasThis: false), isVirtual: false, [new LoadStackSlot(0, BytePointer)]));
            }
            if (InterveningWrite)
            {
                activeBlock.Add(new StoreIndirect(Byte, new LoadStackSlot(0, BytePointer), new Constant(42, Byte)));
            }
            if (ThrowingSetup)
            {
                activeBlock.Add(new StoreLocal(9, Byte, new Binary(BinaryKind.Divide, false, false, new Constant(10, Byte), new Constant(0, Byte))));
            }
            if (UnrelatedSpanLiteralSetup)
            {
                activeBlock.Add(new StoreLocal(10, ReadOnlySpanByte, new SpanLiteral(Byte, ReadOnlySpanByte, [new Constant(0, Byte)])));
            }
            if (setup != null)
            {
                activeBlock.Add(setup);
            }

            activeBlock.Add(copyBlock);

            IrNode finalUsage;
            if (FinalSpanCtor || MalformedFinalSpanCtor)
            {
                var spanCtorType = SpoofedFinalSpanCtor ? TypeRef.Definition("System.Memory", "System", "Span`1", ValueTypeHint.ValueType) : TypeRef.CoreLib("System", "Span`1");
                spanCtorType = TypeRef.GenericInstance(spanCtorType, [Int32Element ? Int32 : Byte]);
                var paramType = MalformedFinalSpanCtor ? Int32 : VoidPointer;
                var ctor = new MethodRef(spanCtorType, ".ctor", Void, [paramType, Int32], HasThis: true)
                {
                    DeclaringTypeIsTrustedPlatform = SpoofedFinalSpanCtor ? MetadataFactState.Unknown : MetadataFactState.Yes
                };
                var args = MalformedFinalSpanCtor ? new IrExpression[] { new Constant(0, Int32), new Constant(CopySize, Int32) } : new IrExpression[] { new LoadStackSlot(0, BytePointer), new Constant(CopySize, Int32) };
                finalUsage = new StoreLocal(1, spanCtorType, new NewObject(ctor, args));
            }
            else
            {
                finalUsage = new StoreLocal(1, BytePointer, new LoadStackSlot(0, BytePointer));
            }
            activeBlock.Add(finalUsage);

            if (DestinationAliasAfter)
            {
                activeBlock.Add(new StoreLocal(6, BytePointer, new LoadStackSlot(0, BytePointer)));
            }

            if (SharedSpanLiteral)
            {
                activeBlock.Add(new Call(new MethodRef(Holder, "Print", Void, [ReadOnlySpanByte], HasThis: false), isVirtual: false, [new LoadLocalAddress(2, ReadOnlySpanByte)]));
            }

            activeBlock.Add(new Return(null));

            var body = new BlockContainer();
            body.Add(block0);
            if (CrossBlock) body.Add(block1);

            return new IrFunction(
                "M",
                Holder,
                new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
                [],
                body);
        }
    }

    static IrFunction BuildCanonicalSpan(System.Action<Builder>? mutate = null)
    {
        var builder = new Builder { IsRva = false };
        mutate?.Invoke(builder);
        return builder.Build();
    }

    static IrFunction BuildCanonicalRva(System.Action<Builder>? mutate = null)
    {
        var builder = new Builder { IsRva = true };
        mutate?.Invoke(builder);
        return builder.Build();
    }
}
