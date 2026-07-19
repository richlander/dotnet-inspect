using System.Collections.Generic;
using System.Linq;
using ILInspector.Decompiler.Pipeline;
using Xunit;

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
        var function = BuildCanonicalRva(mutate: b =>
        {
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
        var function = BuildCanonicalRva(mutate: b =>
        {
            b.FinalSpanCtor = true;
            b.SpoofedFinalSpanCtor = true;
        });
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
        var function = BuildCanonicalRva(mutate: b =>
        {
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

    [Fact]
    public void WrongRefKind_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b =>
        {
            b.UseGetItem = true;
            b.WrongRefKind = true;
        });
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void StoreTypeMismatch_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b => b.StoreTypeMismatch = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void MethodGenericArgMismatch_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b => b.MethodGenericArgMismatch = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void DeclaringGenericArgMismatch_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b =>
        {
            b.UseGetPinnableReference = true;
            b.DeclaringGenericArgMismatch = true;
        });
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void NonZeroGetItem_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b =>
        {
            b.UseGetItem = true;
            b.NonZeroGetItem = true;
        });
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void CtorZeroDeclaringArgs_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.CtorZeroDeclaringArgs = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void CtorWrongHasThis_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.CtorWrongHasThis = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void CtorWrongReturn_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.CtorWrongReturn = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void CtorWrongPointer_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.CtorWrongPointer = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void CtorWrongArgCount_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.CtorWrongArgCount = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void CtorWrongArgOrder_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.CtorWrongArgOrder = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void CtorMismatchedDeclaringT_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.CtorMismatchedDeclaringT = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }



    [Fact]
    public void VolatileCpblk_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.VolatileCpblk = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void SharedSpanLiteralValueMutation_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b => b.SharedSpanLiteralValue = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void WrongTypedZeroGetItem_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b => { b.UseGetItem = true; b.WrongTypedZeroGetItem = true; });
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void EffectfulGetItem_Declines()
    {
        var function = BuildCanonicalSpan(mutate: b => { b.UseGetItem = true; b.EffectfulGetItem = true; });
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void CtorMultipleDeclaringArgs_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.CtorMultipleDeclaringArgs = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void CtorMethodGenericArgs_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.CtorMethodGenericArgs = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void WrongLengthConstantType_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.WrongLengthConstantType = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void CtorWrongDestType_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.CtorWrongDestType = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void CtorWrongRefKind_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.CtorWrongRefKind = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void WrongCopySizeType_Declines()
    {
        var function = BuildCanonicalRva(mutate: b => b.WrongCopySizeType = true);
        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void NestedFunctionScope_Untouched()
    {
        var function = BuildCanonicalRva();
        var innerFunction = BuildCanonicalRva();
        var innerBody = innerFunction.Body;
        innerBody.Detach();
        var localFunc = new LocalFunctionStatement("L", Void, System.Collections.Immutable.ImmutableArray<Parameter>.Empty, false, System.Collections.Immutable.ImmutableArray<TypeRef>.Empty, System.Collections.Immutable.ImmutableArray<string?>.Empty, false, false, innerBody);

        function.Descendants.OfType<Block>().First().Add(localFunc);

        new StackAllocInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();

        int outerArrays = 0;
        int innerArrays = 0;
        foreach (var s in function.Descendants.OfType<StackAllocArray>())
        {
            bool inLocalFunc = false;
            IrNode? parent = s.Parent;
            while (parent != null) {
                if (parent == localFunc) { inLocalFunc = true; break; }
                parent = parent.Parent;
            }
            if (inLocalFunc) innerArrays++;
            else outerArrays++;
        }
        Assert.Equal(1, outerArrays);
        Assert.Equal(0, innerArrays);

        int innerAllocs = 0;
        foreach (var s in function.Descendants.OfType<StackAllocate>())
        {
            bool inLocalFunc = false;
            IrNode? parent = s.Parent;
            while (parent != null) {
                if (parent == localFunc) { inLocalFunc = true; break; }
                parent = parent.Parent;
            }
            if (inLocalFunc) innerAllocs++;
        }
        Assert.True(innerAllocs > 0);
    }

    [Fact]
    public void LoadProperty_CompilerBacked_FoldsSuccessfully()
    {
        using var source = ILInspector.Decompiler.Pipeline.MetadataSource.Open(DotnetInspector.Fixtures.FixtureCatalog.DecompilerUnsafeNew.AssemblyPath());
        var function = ILInspector.Decompiler.Pipeline.IrImporter.Import(source, "ILInspector.Decompiler.Fixtures.NewUnsafe.StackallocInitializerResiduals", "StackallocPointerInitializer");

        var context = PassContext.None;
        foreach (var pass in ILInspector.Decompiler.Pipeline.IrPasses.Default)
        {
            if (pass is StackAllocInitializerPass)
                break;
            pass.Run(function!, context);
        }

        var copyBlock = function!.Descendants.OfType<CopyBlock>().First();
        var loadProp = Assert.IsType<LoadProperty>(copyBlock.Source);
        Assert.Equal(0, ((Constant)loadProp.IndexArguments[0]).Value);

        new StackAllocInitializerPass().Run(function, context);

        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal(3, ((Constant)raised.Count).Value);
    }


    public class Builder
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
        public bool UseGetItemProperty;
        public bool UseGetPinnableReference;
        public bool MismatchedSpanElement;
        public bool WrongHasThis;
        public bool MalformedArgumentCounts;
        public bool RvaMisaligned;
        public bool Int32Element;
        public bool MalformedFinalSpanCtor;
        public bool WrongRefKind;
        public bool StoreTypeMismatch;
        public bool MethodGenericArgMismatch;
        public bool DeclaringGenericArgMismatch;
        public bool NonZeroGetItem;
        public bool CtorZeroDeclaringArgs;
        public bool CtorWrongHasThis;
        public bool CtorWrongReturn;
        public bool CtorWrongPointer;
        public bool CtorWrongArgCount;
        public bool CtorWrongArgOrder;
        public bool CtorMismatchedDeclaringT;
        public bool VolatileCpblk;
        public bool SharedSpanLiteralValue;
        public bool WrongTypedZeroGetItem;
        public bool EffectfulGetItem;
        public bool CtorMultipleDeclaringArgs;
        public bool CtorMethodGenericArgs;
        public bool WrongLengthConstantType;
        public bool CtorWrongDestType;
        public bool CtorWrongRefKind;
        public bool WrongCopySizeType;


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
                for (int i = 0; i < CopySize; i++)
                {
                    if (NonConstantSpanLiteral && i == 0) elements.Add(new LoadArgument(0, "arg", Byte));
                    else elements.Add(new Constant(i, Byte));
                }
                var spanLit = new SpanLiteral(Byte, ReadOnlySpanByte, elements);
                setup = new StoreLocal(2, StoreTypeMismatch ? TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [Int32]) : ReadOnlySpanByte, spanLit);

                var marshalType = SpoofedAssembly ? TypeRef.Definition("System.Memory", "System.Runtime.InteropServices", "MemoryMarshal", ValueTypeHint.ReferenceType) : TypeRef.CoreLib("System.Runtime.InteropServices", "MemoryMarshal");
                var returnType = SpoofedSignature ? Byte : TypeRef.ByRef(Byte);
                if (MismatchedSpanElement) returnType = TypeRef.ByRef(Int32);

                var spanType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [Byte]);
                var getRefDecl = marshalType;

                if (UseGetItem || UseGetItemProperty)
                {
                    var getItem = new MethodRef(spanType, "get_Item", returnType, [Int32], HasThis: !WrongHasThis)
                    {
                        DeclaringTypeIsTrustedPlatform = SpoofedAssembly ? MetadataFactState.Unknown : MetadataFactState.Yes,
                        ParameterRefKindsFacts = ParameterRefKindFacts.NotRequired,
                        ParameterRefKinds = WrongRefKind ? System.Collections.Immutable.ImmutableArray.Create(ArgumentRefKind.Ref) : System.Collections.Immutable.ImmutableArray<ArgumentRefKind>.Empty,
                        TypeArguments = MethodGenericArgMismatch ? [Byte] : []
                    };
                    if (UseGetItemProperty)
                    {
                        var indexArgs = MalformedArgumentCounts ? System.Array.Empty<IrExpression>() : new IrExpression[] { new Constant(NonZeroGetItem ? 1 : 0, Int32) };
                        copySource = new LoadProperty(getItem, new LoadLocalAddress(2, spanType), indexArgs);
                    }
                    else
                    {
                        var args = MalformedArgumentCounts ? new IrExpression[] { new LoadLocalAddress(2, spanType) } : new IrExpression[] { new LoadLocalAddress(2, spanType), EffectfulGetItem ? new Call(new MethodRef(Holder, "GetIndex", Int32, [], HasThis: false), isVirtual: false, []) : new Constant(NonZeroGetItem ? 1 : 0, WrongTypedZeroGetItem ? Byte : Int32) };
                        copySource = new Call(getItem, isVirtual: false, args);
                    }
                }
                else if (UseGetPinnableReference)
                {
                    var getPin = new MethodRef(DeclaringGenericArgMismatch ? TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [Int32]) : spanType, "GetPinnableReference", returnType, [], HasThis: !WrongHasThis)
                    {
                        DeclaringTypeIsTrustedPlatform = SpoofedAssembly ? MetadataFactState.Unknown : MetadataFactState.Yes,
                        ParameterRefKindsFacts = ParameterRefKindFacts.NotRequired,
                        ParameterRefKinds = WrongRefKind ? System.Collections.Immutable.ImmutableArray.Create(ArgumentRefKind.Ref) : System.Collections.Immutable.ImmutableArray<ArgumentRefKind>.Empty,
                        TypeArguments = MethodGenericArgMismatch ? [Byte] : []
                    };
                    var args = MalformedArgumentCounts ? new IrExpression[] { new LoadLocalAddress(2, spanType), new Constant(0, Int32) } : new IrExpression[] { new LoadLocalAddress(2, spanType) };
                    copySource = new Call(getPin, isVirtual: false, args);
                }
                else
                {
                    var getRef = new MethodRef(marshalType, "GetReference", returnType, [spanType], HasThis: WrongHasThis)
                    {
                        DeclaringTypeIsTrustedPlatform = SpoofedAssembly ? MetadataFactState.Unknown : MetadataFactState.Yes,
                        TypeArguments = [MismatchedSpanElement ? Int32 : Byte],
                        ParameterRefKindsFacts = WrongRefKind ? ParameterRefKindFacts.Unknown : ParameterRefKindFacts.NotRequired
                    };
                    if (MethodGenericArgMismatch) getRef = getRef with { TypeArguments = [Byte, Int32] };
                    var args = MalformedArgumentCounts ? new IrExpression[] { new LoadLocalAddress(2, spanType), new Constant(0, Int32) } : new IrExpression[] { new LoadLocalAddress(2, spanType) };
                    copySource = new Call(getRef, isVirtual: false, args);
                }
            }

            var copyBlock = new CopyBlock(loadDest, copySource, new Constant(WrongCopySizeType ? CopySize / (Int32Element || CtorMismatchedDeclaringT ? 4 : 1) : CopySize, WrongCopySizeType ? Byte : Int32)) { IsVolatile = VolatileCpblk };

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
            if (FinalSpanCtor || MalformedFinalSpanCtor || CtorZeroDeclaringArgs || CtorWrongHasThis || CtorWrongReturn || CtorWrongPointer || CtorWrongArgCount || CtorWrongArgOrder || CtorMismatchedDeclaringT || CtorMultipleDeclaringArgs || CtorMethodGenericArgs || CtorWrongDestType || CtorWrongRefKind || WrongLengthConstantType || WrongCopySizeType)
            {
                var spanCtorType = SpoofedFinalSpanCtor ? TypeRef.Definition("System.Memory", "System", "Span`1", ValueTypeHint.ValueType) : TypeRef.CoreLib("System", "Span`1");
                var typeArgs = CtorZeroDeclaringArgs ? System.Array.Empty<TypeRef>() : (CtorMultipleDeclaringArgs ? new[] { Byte, Int32 } : new[] { CtorMismatchedDeclaringT ? Int32 : (Int32Element ? Int32 : Byte) });
                spanCtorType = TypeRef.GenericInstance(spanCtorType, System.Collections.Immutable.ImmutableArray.Create(typeArgs));
                var paramType = MalformedFinalSpanCtor ? Int32 : (CtorWrongPointer ? TypeRef.Pointer(Int32) : VoidPointer);
                var retType = CtorWrongReturn ? Int32 : Void;
                var ctor = new MethodRef(spanCtorType, ".ctor", retType, [paramType, Int32], HasThis: !CtorWrongHasThis)
                {
                    TypeArguments = CtorMethodGenericArgs ? [Int32] : [],
                    DeclaringTypeIsTrustedPlatform = SpoofedFinalSpanCtor ? MetadataFactState.Unknown : MetadataFactState.Yes,
                    ParameterRefKindsFacts = ParameterRefKindFacts.NotRequired,
                    ParameterRefKinds = CtorWrongRefKind ? System.Collections.Immutable.ImmutableArray.Create(ArgumentRefKind.Ref) : System.Collections.Immutable.ImmutableArray<ArgumentRefKind>.Empty
                };

                IrExpression[] args;
                if (MalformedFinalSpanCtor) args = [new Constant(0, Int32), new Constant(WrongLengthConstantType ? CopySize : (CopySize / (Int32Element || CtorMismatchedDeclaringT ? 4 : 1)), WrongLengthConstantType ? Byte : Int32)];
                else if (CtorWrongArgCount) args = [new LoadStackSlot(0, VoidPointer)];
                else if (CtorWrongArgOrder) args = [new Constant(CopySize / (Int32Element || CtorMismatchedDeclaringT ? 4 : 1), Int32), new LoadStackSlot(0, VoidPointer)];
                else args = [new LoadStackSlot(0, CtorWrongDestType ? Int32 : VoidPointer), new Constant(WrongLengthConstantType ? CopySize : (CopySize / (Int32Element || CtorMismatchedDeclaringT ? 4 : 1)), WrongLengthConstantType ? Byte : Int32)];

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

            if (SharedSpanLiteralValue)
            {
                activeBlock.Add(new Call(new MethodRef(Holder, "PrintVal", Void, [ReadOnlySpanByte], HasThis: false), isVirtual: false, [new LoadLocal(2, ReadOnlySpanByte)]));
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

    public static IrFunction BuildCanonicalRvaForCheck(System.Action<Builder>? mutate = null)
    {
        var builder = new Builder { IsRva = true, FinalSpanCtor = true };
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
