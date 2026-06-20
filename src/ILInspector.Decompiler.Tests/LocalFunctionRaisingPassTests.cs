using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class LocalFunctionRaisingPassTests
{
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
    public void StaticLocalFunction_RecoveredAsDeclarationAndUnqualifiedCall()
    {
        string output = PrintRaised(nameof(CfgSampleClass.DoubleViaLocalFunction));

        Assert.Contains("return Twice(x);", output);                  // call rendered unqualified
        Assert.Contains("static int Twice(int v) => v * 2;", output); // declaration emitted
        Assert.DoesNotContain("g__", output);                          // no synthesized name
        Assert.DoesNotContain("CfgSampleClass.Twice", output);         // not the qualified mis-binding
    }
}
