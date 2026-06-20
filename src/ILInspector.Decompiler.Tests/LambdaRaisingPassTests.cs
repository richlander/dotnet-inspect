using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class LambdaRaisingPassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_func = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`2"), [s_int, s_int]);

    static string PrintRaised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    [Fact]
    public void NonCapturingExpressionBody_RaisesSimpleLambda()
        => Assert.Equal("return x => x + 1;", PrintRaised(nameof(CfgSampleClass.NonCapturingLambda)));

    [Fact]
    public void NonCapturingStatementBody_RaisesBlockLambda()
    {
        string output = PrintRaised(nameof(CfgSampleClass.StatementBodyLambda));

        Assert.Contains("return x => {", output);
        Assert.Contains("Console.WriteLine(x);", output);
        Assert.Contains("return x + 1;", output);
        Assert.DoesNotContain("new Func", output);
    }

    [Fact]
    public void LambdaNameLookalikeWithoutCompilerGeneratedMetadata_IsNotRaised()
    {
        var lambdaMethod = new MethodRef(
            TypeRef.Definition("UserAssembly", "Samples", "Outer+<>c"),
            "<M>b__0_0",
            s_int,
            [s_int],
            HasThis: true);
        var function = FunctionReturningDelegate(lambdaMethod);
        var lambdaBody = LambdaBody(lambdaMethod);
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: method => method == lambdaMethod ? lambdaBody : null);

        new LambdaRaisingPass().Run(function, context);

        Assert.Empty(function.Descendants.OfType<Lambda>());
        Assert.Single(function.Descendants.OfType<DelegateCreation>());
        function.CheckInvariant();
    }

    static IrFunction FunctionReturningDelegate(MethodRef method)
    {
        var block = new Block();
        block.Add(new Return(new DelegateCreation(
            s_func,
            method,
            isVirtual: false,
            new Constant(null, TypeRef.CoreLib("System", "Object")))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Outer"),
            new MethodSignature(s_func, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction LambdaBody(MethodRef method)
    {
        var block = new Block();
        block.Add(new Return(new LoadArgument(1, "x", s_int)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            method.Name,
            method.DeclaringType,
            new MethodSignature(s_int, [new Parameter("x", s_int)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }
}
