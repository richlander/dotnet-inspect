using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class TypeOfRenderingTests
{
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

    static IrFunction Returning(IrExpression expression)
    {
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new Return(expression));
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Tests", "Owner"),
            new MethodSignature(TypeRef.CoreLib("System", "Type"), [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
