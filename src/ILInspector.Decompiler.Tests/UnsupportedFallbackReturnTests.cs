using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class UnsupportedFallbackReturnTests
{
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Task = TypeRef.CoreLib("System.Threading.Tasks", "Task");

    [Fact]
    public void NonVoidUnsupportedBodyWithoutReturn_EmitsDefaultReturn()
    {
        var output = CSharpPrinter.Print(UnsupportedFunction(Int)).Output ?? "";

        Assert.Contains("Unsupported IL_0000", output);
        Assert.Contains("return default;", output);
    }

    [Fact]
    public void YieldBodyWithUnsupportedNode_DoesNotEmitValueReturn()
    {
        var function = UnsupportedFunction(TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"));
        function.Body.Blocks[0].Add(new YieldReturn(new Constant(1, Int)));

        var output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Contains("yield return 1;", output);
        Assert.DoesNotContain("return default;", output);
    }

    [Fact]
    public void AsyncTaskUnsupportedBody_DoesNotEmitValueReturn()
    {
        var function = UnsupportedFunction(Task);
        function.RequiresAsyncBodyModifier = true;

        var output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Contains("Unsupported IL_0000", output);
        Assert.DoesNotContain("return default;", output);
    }

    [Fact]
    public void VoidUnsupportedBody_DoesNotEmitDefaultReturn()
    {
        var output = CSharpPrinter.Print(UnsupportedFunction(Void)).Output ?? "";

        Assert.Contains("Unsupported IL_0000", output);
        Assert.DoesNotContain("return default;", output);
    }

    static IrFunction UnsupportedFunction(TypeRef returnType)
    {
        var block = new Block(0);
        block.Add(new ExpressionStatement(new UnsupportedNode(0, "probe", "test unsupported")));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "Unsupported"),
            new MethodSignature(returnType, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
