using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class TypeOfRenderingTests
{
    [Fact]
    public void TypeGetTypeFromHandle_UserLookalike_IsNotFolded()
    {
        var owner = TypeRef.Definition("Synthetic", "Tests", "Owner");
        var target = TypeRef.Definition("Synthetic", "Tests", "Target");
        var getTypeFromHandle = new MethodRef(
            TypeRef.Definition("UserAssembly", "System", "Type"),
            "GetTypeFromHandle",
            TypeRef.CoreLib("System", "Object"),
            [TypeRef.CoreLib("System", "RuntimeTypeHandle")],
            HasThis: false);
        var function = Returning(
            new Call(
                getTypeFromHandle,
                isVirtual: false,
                [new LoadToken(RuntimeTokenKind.Type, target, "Synthetic.Tests.Target")]),
            returnType: TypeRef.CoreLib("System", "Object"),
            owner);

        new TypeOfFoldingPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<TypeOf>());
        Assert.Single(function.Descendants.OfType<Call>());
        function.CheckInvariant();
    }

    [Fact]
    public void TypeOf_OpenGenericDefinition_RendersUnboundGenericType()
    {
        var output = CSharpPrinter.Print(Returning(new TypeOf(TypeRef.CoreLib("System.Collections.Generic", "List`1")))).Output;

        Assert.Contains("return typeof(List<>);", output);
        Assert.DoesNotContain("typeof(List)", output);
    }

    [Fact]
    public void TypeOf_OpenGenericDefinitionArityTwo_RendersAllCommas()
    {
        var output = CSharpPrinter.Print(Returning(new TypeOf(TypeRef.CoreLib("System.Collections.Generic", "Dictionary`2")))).Output;

        Assert.Contains("return typeof(Dictionary<,>);", output);
        Assert.DoesNotContain("typeof(Dictionary)", output);
    }

    static IrFunction Returning(IrExpression expression, TypeRef? returnType = null, TypeRef? owner = null)
    {
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new Return(expression));
        body.Add(block);
        return new IrFunction(
            "M",
            owner ?? TypeRef.Definition("Synthetic", "Tests", "Owner"),
            new MethodSignature(returnType ?? TypeRef.CoreLib("System", "Type"), [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
