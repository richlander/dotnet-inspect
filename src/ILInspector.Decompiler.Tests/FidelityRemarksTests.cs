using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class FidelityRemarksTests
{
    [Fact]
    public void Collect_UnsupportedNode_ReportsDec0004AtItsOffset()
    {
        var i32 = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var entry = new Block(0x00);
        entry.Add(new Return(new UnsupportedNode(0x05, "calli", "opcode is outside the slice")));
        container.Add(entry);

        var signature = new MethodSignature(i32, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("Unsupported", TypeRef.CoreLib("System", "Object"), signature, [], container);

        var remark = Assert.Single(FidelityRemarks.Collect(function));
        Assert.Equal(DiagnosticIds.UnsupportedConstruct, remark.Code);  // DEC0004
        Assert.Equal(0x05, remark.Offset);                              // the node's own IL offset
        Assert.Contains("calli", remark.Reason);
    }

    [Fact]
    public void Collect_UnrepresentableSignatureType_ReportsDec0005AtSignature()
    {
        var i32 = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var entry = new Block(0x00);
        entry.Add(new Return(null));
        container.Add(entry);

        // A parameter the slice cannot represent caps fidelity from the signature,
        // which belongs to no block — so the remark offset is -1.
        var signature = new MethodSignature(
            i32,
            [new Parameter("p", TypeRef.Unsupported("refanytype"))],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("BadSig", TypeRef.CoreLib("System", "Object"), signature, [], container);

        Assert.Contains(FidelityRemarks.Collect(function), r =>
            r.Code == DiagnosticIds.UnsupportedType && r.Offset == -1);
    }

    [Fact]
    public void Collect_FullFidelityFunction_HasNoRemarks()
    {
        var i32 = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var entry = new Block(0x00);
        entry.Add(new Return(new Constant(0, i32)));
        container.Add(entry);

        var signature = new MethodSignature(i32, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("Clean", TypeRef.CoreLib("System", "Object"), signature, [], container);

        Assert.Empty(FidelityRemarks.Collect(function));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void Collect_UnrepresentableMetadataName_ReportsDec0009()
    {
        var voidType = TypeRef.CoreLib("System", "Void");
        var closure = TypeRef.Definition("Synthetic", "Samples", "<>c__DisplayClass0_0");
        var container = new BlockContainer();
        var entry = new Block(0x00);
        container.Add(entry);
        entry.Add(new StoreLocal(0, closure, new Constant(null, closure)));
        entry.Add(new Return(null));

        var signature = new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("BadName", TypeRef.CoreLib("System", "Object"), signature, [closure], container);

        var remark = Assert.Single(FidelityRemarks.Collect(function),
            r => r.Code == DiagnosticIds.UnrepresentableMetadataName && r.Offset == -1);
        Assert.Equal(-1, remark.Offset);
        Assert.Contains("<>c__DisplayClass0_0", remark.Reason);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void Collect_UnverifiedAccessorLikeCall_ReportsDec0009()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var location = TypeRef.Definition("Synthetic", "Samples", "Location");
        var accessorLike = new MethodRef(holder, "get_Location", location, [], HasThis: true)
        {
            IsSpecialName = true,
            AccessorKind = AccessorKind.Unknown,
        };
        var container = new BlockContainer();
        var entry = new Block(0x10);
        container.Add(entry);
        entry.Add(new Return(new Call(accessorLike, isVirtual: true, [new LoadArgument(0, "self", holder)])));

        var signature = new MethodSignature(location, [new Parameter("self", holder)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("AccessorLike", holder, signature, [], container);

        var remark = Assert.Single(FidelityRemarks.Collect(function),
            r => r.Code == DiagnosticIds.UnrepresentableMetadataName);
        Assert.Equal(0x10, remark.Offset);
        Assert.Contains("accessor metadata was unavailable", remark.Reason);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void Collect_ResolvedNonAccessorGetMethod_RemainsFull()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var int32 = TypeRef.CoreLib("System", "Int32");
        var nonAccessor = new MethodRef(holder, "get_Count", int32, [], HasThis: true)
        {
            IsSpecialName = true,
            AccessorKind = AccessorKind.None,
        };
        var container = new BlockContainer();
        var entry = new Block(0x10);
        container.Add(entry);
        entry.Add(new Return(new Call(nonAccessor, isVirtual: true, [new LoadArgument(0, "self", holder)])));

        var signature = new MethodSignature(int32, [new Parameter("self", holder)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("NonAccessor", holder, signature, [], container);

        Assert.Empty(FidelityRemarks.Collect(function));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void Collect_NameInferredAccessorLikeMemberRef_RemainsFullUntilMetadataResolves()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var int32 = TypeRef.CoreLib("System", "Int32");
        var ambiguous = new MethodRef(holder, "get_Count", int32, [], HasThis: true)
        {
            IsSpecialName = true,
            IsSpecialNameInferred = true,
            AccessorKind = AccessorKind.Unknown,
        };
        var container = new BlockContainer();
        var entry = new Block(0x10);
        container.Add(entry);
        entry.Add(new Return(new Call(ambiguous, isVirtual: true, [new LoadArgument(0, "self", holder)])));

        var signature = new MethodSignature(int32, [new Parameter("self", holder)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("AmbiguousMemberRef", holder, signature, [], container);

        Assert.Empty(FidelityRemarks.Collect(function));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }
}
