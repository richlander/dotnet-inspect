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
}
