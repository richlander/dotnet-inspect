using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Issue #2497: calls to a static member declared on the current type were
// over-qualified with the declaring type name (SelfType.M(...)) instead of the
// source-faithful unqualified form M(...). Cross-type static calls must stay
// qualified.
public class SelfQualifyPrinterTests
{
    [Fact]
    public void SelfTypeStaticCall_RendersUnqualified_CrossTypeStaysQualified()
    {
        string body = RenderFixture(nameof(SelfQualifySamples.CallsSelfAndCross));

        // Self-type static call is unqualified.
        Assert.Contains("Helper(", body);
        Assert.DoesNotContain("SelfQualifySamples.Helper", body);

        // Near-miss: a call to another type's static member stays qualified.
        Assert.Contains("SelfQualifyOther.External(", body);
    }

    static string RenderFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(SelfQualifySamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(SelfQualifySamples).FullName!, methodName);
        Assert.NotNull(function);
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
        var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
        Assert.NotNull(result.Output);
        return result.Output!;
    }
}

public static class SelfQualifySamples
{
    public static int Helper(int x) => x + 1;

    public static int CallsSelfAndCross(int x)
    {
        int a = Helper(x);
        int b = SelfQualifyOther.External(a);
        return a + b;
    }
}

public static class SelfQualifyOther
{
    public static int External(int x) => x * 2;
}
