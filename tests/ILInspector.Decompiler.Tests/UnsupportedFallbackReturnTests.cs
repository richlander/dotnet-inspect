using System.Collections.Immutable;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class UnsupportedFallbackReturnTests
{
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Task = TypeRef.CoreLib("System.Threading.Tasks", "Task");
    static readonly TypeRef ValueTask = TypeRef.CoreLib("System.Threading.Tasks", "ValueTask");
    static readonly TypeRef TaskInt = TypeRef.GenericInstance(TypeRef.CoreLib("System.Threading.Tasks", "Task`1"), [Int]);
    static readonly TypeRef ValueTaskInt = TypeRef.GenericInstance(TypeRef.CoreLib("System.Threading.Tasks", "ValueTask`1"), [Int]);
    static readonly TypeRef FuncInt = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`1"), [Int]);

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
    public void AsyncValueTaskUnsupportedBody_DoesNotEmitValueReturn()
    {
        var function = UnsupportedFunction(ValueTask);
        function.RequiresAsyncBodyModifier = true;

        var output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Contains("Unsupported IL_0000", output);
        Assert.DoesNotContain("return default;", output);
    }

    [Fact]
    public void AsyncGenericTaskUnsupportedBody_EmitsDefaultReturn()
    {
        var function = UnsupportedFunction(TaskInt);
        function.RequiresAsyncBodyModifier = true;

        var output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Contains("Unsupported IL_0000", output);
        Assert.Contains("return default;", output);
    }

    [Fact]
    public void AsyncGenericValueTaskUnsupportedBody_EmitsDefaultReturn()
    {
        var function = UnsupportedFunction(ValueTaskInt);
        function.RequiresAsyncBodyModifier = true;

        var output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Contains("Unsupported IL_0000", output);
        Assert.Contains("return default;", output);
    }

    [Fact]
    public void VoidUnsupportedBody_DoesNotEmitDefaultReturn()
    {
        var output = CSharpPrinter.Print(UnsupportedFunction(Void)).Output ?? "";

        Assert.Contains("Unsupported IL_0000", output);
        Assert.DoesNotContain("return default;", output);
    }

    [Fact]
    public void LocalFunctionUnsupportedBody_EmitsDefaultReturn()
    {
        var local = new LocalFunctionStatement(
            "Local",
            Int,
            [],
            isStatic: false,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            UnsupportedBody());
        var block = new Block(0);
        block.Add(local);
        var output = CSharpPrinter.Print(WrapVoid(block)).Output ?? "";

        Assert.Contains("int Local()", output);
        Assert.Contains("return default;", output);
    }

    [Fact]
    public void LambdaUnsupportedBody_EmitsDefaultReturn()
    {
        var lambda = new Lambda(
            FuncInt,
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            UnsupportedBody());
        var block = new Block(0);
        block.Add(new StoreLocal(0, FuncInt, lambda));
        var output = CSharpPrinter.Print(WrapVoid(block, [FuncInt])).Output ?? "";

        Assert.Contains("=>", output);
        Assert.Contains("return default;", output);
    }

    static IrFunction UnsupportedFunction(TypeRef returnType)
    {
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "Unsupported"),
            new MethodSignature(returnType, [], HasThis: false, GenericParameterCount: 0),
            [],
            UnsupportedBody());
    }

    static BlockContainer UnsupportedBody()
    {
        var block = new Block(0);
        block.Add(new ExpressionStatement(new UnsupportedNode(0, "probe", "test unsupported")));
        var body = new BlockContainer();
        body.Add(block);
        return body;
    }

    static IrFunction WrapVoid(Block block, ImmutableArray<TypeRef> locals = default)
    {
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "Unsupported"),
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            locals.IsDefault ? [] : locals,
            body);
    }
}
