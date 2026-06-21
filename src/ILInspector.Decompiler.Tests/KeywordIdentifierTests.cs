using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A parameter (or any metadata identifier) whose name is a C# reserved keyword
// must be @-escaped in the rendered body — a bare `delegate` is CS1001
// "Identifier expected". Locals are already synthetic (V_0/S_0) or keyword-
// filtered, so the live case is parameter names.
public class KeywordIdentifierTests
{
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
}
