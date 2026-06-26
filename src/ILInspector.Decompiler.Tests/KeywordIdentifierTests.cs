using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A parameter (or any metadata identifier) whose name is a C# reserved keyword
// must be @-escaped in the rendered body — a bare `delegate` is CS1001
// "Identifier expected". Locals are already synthetic (V_0/S_0) or keyword-
// filtered, so the live case is parameter names.
public class KeywordIdentifierTests
{
    static readonly TypeRef AwaitType = TypeRef.Definition("Synthetic", "", "Await");

    static string Render(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void KeywordParameter_IsEscaped()
    {
        var output = Render(nameof(CfgSampleClass.KeywordParam));

        Assert.Contains("@delegate + 1", output);
        Assert.DoesNotContain(" delegate", output);
    }

    [Fact]
    public void ContextualKeywordParameter_IsEscaped()
    {
        var output = Render(nameof(CfgSampleClass.ContextualKeywordParam));

        Assert.Contains("@await + 1", output);
        Assert.DoesNotContain(" await", output);
    }

    [Fact]
    public void ReadableLocalName_DoesNotEmitBareAwait()
    {
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new StoreLocal(0, AwaitType, new Constant(null, AwaitType)));
        block.Add(new ExpressionStatement(new LoadLocal(0, AwaitType)));
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0),
            [AwaitType],
            body);

        var output = CSharpPrinter.Print(function, new PrinterOptions { ReadableLocalNames = true }).Output!;

        Assert.Contains("V_0", output);
        Assert.DoesNotContain(" await", output);
    }
}
