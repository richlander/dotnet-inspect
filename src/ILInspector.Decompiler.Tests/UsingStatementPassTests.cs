using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class UsingStatementPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void ReferenceTypeUsingWithDisposeGuard_RaisesToUsingStatement()
    {
        var function = Raised(nameof(CfgSampleClass.NormalUsing));

        var usingStatement = Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Equal("StringReader", usingStatement.ResourceType.ToDisplayString());
        Assert.IsType<NewObject>(usingStatement.Resource);
        Assert.Empty(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void PrintRaised_RendersUsingHeaderAndBody()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NormalUsing))).Output;

        Assert.NotNull(output);
        Assert.Contains("using (StringReader reader = new StringReader(s))", output);
        Assert.Contains("return reader.Read();", output);
        Assert.DoesNotContain("finally", output);
    }

    [Fact]
    public void FinallyWithExtraWork_IsLeftAsTryFinally()
    {
        var function = Raised(nameof(CfgSampleClass.FinallyWithExtraWork));

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }
}
