using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class AwaitRecoveryPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    [Fact]
    public void Await_RecoversValueProducingAwait()
    {
        var function = Raised(nameof(CfgSampleClass.AwaitOnce));

        Assert.Single(function.Descendants.OfType<AwaitExpression>());
        // The synthetic AsyncHelpers.Await call is gone.
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Await");
    }

    [Fact]
    public void Await_RecoversVoidAwait()
    {
        var function = Raised(nameof(CfgSampleClass.AwaitVoid));

        var await = Assert.Single(function.Descendants.OfType<AwaitExpression>());
        // Non-generic AsyncHelpers.Await returns void.
        Assert.Equal("void", await.ResultType?.ToDisplayString());
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Await");
    }

    [Fact]
    public void PrintValueAwait_RendersAwaitKeyword()
    {
        var output = Print(nameof(CfgSampleClass.AwaitOnce));

        Assert.Contains("await", output);
        Assert.DoesNotContain("AsyncHelpers", output);
    }

    [Fact]
    public void PrintVoidAwait_RendersAwaitStatement()
    {
        var output = Print(nameof(CfgSampleClass.AwaitVoid));

        Assert.Contains("await t", output);
        Assert.DoesNotContain("AsyncHelpers", output);
    }

    [Fact]
    public void NonAsyncMethod_HasNoAwaitExpression()
    {
        var function = Raised(nameof(CfgSampleClass.ToByte));

        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
    }
}
