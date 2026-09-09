using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class UnspellableTypeFidelityTests
{
    [Fact]
    public void PrivateImplementationDetailsDeclaringTypeAlone_DoesNotDegradeMethodBody()
    {
        var voidType = TypeRef.CoreLib("System", "Void");
        var hiddenType = TypeRef.Definition("System.Private.CoreLib", "", "<PrivateImplementationDetails>");

        var block = new Block();
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "Helper",
            hiddenType,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(FidelityRemarks.Collect(function));
    }

    [Fact]
    public void PrivateImplementationDetailsType_DegradesToPartial()
    {
        var voidType = TypeRef.CoreLib("System", "Void");
        var hiddenType = TypeRef.Definition("System.Private.CoreLib", "", "<PrivateImplementationDetails>");
        var helper = new MethodRef(hiddenType, "Helper", voidType, [], HasThis: false);

        var block = new Block();
        block.Add(new ExpressionStatement(new Call(helper, isVirtual: false, [])));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Object"),
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var remark = Assert.Single(FidelityRemarks.Collect(function), r => r.Code == DiagnosticIds.UnsupportedType);
        Assert.Contains("<PrivateImplementationDetails>", remark.Reason);
    }
}
