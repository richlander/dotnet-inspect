using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Issue #2497: calls to a static member declared on the current type were
// over-qualified with the declaring type name (SelfType.M(...)) instead of the
// source-faithful unqualified form M(...). Cross-type static calls, a different
// instantiation of the enclosing generic type, and names shadowed by a local or
// parameter must stay qualified so the call cannot rebind.
public class SelfQualifyPrinterTests
{
    [Fact]
    public void SelfTypeStaticCall_RendersUnqualified_CrossTypeStaysQualified()
    {
        string body = RenderFixture(typeof(SelfQualifySamples), nameof(SelfQualifySamples.CallsSelfAndCross));

        Assert.Contains("Helper(", body);
        Assert.DoesNotContain("SelfQualifySamples.Helper", body);
        Assert.Contains("SelfQualifyOther.External(", body);
    }

    [Fact]
    public void GenericSelfInstantiation_Unqualified_DifferentInstantiationStaysQualified()
    {
        // A call to the enclosing generic type at its own instantiation is
        // unqualified; a call to a different instantiation (C<string> from C<T>)
        // must stay qualified or it rebinds to C<T>'s method.
        Assert.Contains("return Helper(x);", RenderFixture(typeof(SelfQualifyGeneric<>), nameof(SelfQualifyGeneric<int>.SelfCall)));

        string cross = RenderFixture(typeof(SelfQualifyGeneric<>), nameof(SelfQualifyGeneric<int>.CrossInstantiation));
        Assert.Contains("SelfQualifyGeneric<string>.Tagged(x)", cross);
        Assert.DoesNotContain("return Tagged(x);", cross);
    }

    [Fact]
    public void ShadowedStaticCall_StaysQualified()
    {
        // A local that shadows the static method name means an unqualified call
        // would bind to the local, so the type qualifier is retained.
        string body = RenderFixture(typeof(SelfQualifyShadow), nameof(SelfQualifyShadow.CallsShadowed));

        Assert.Contains("SelfQualifyShadow.Ping(Ping)", body);
        Assert.DoesNotContain("return Ping(Ping)", body);
    }

    static string RenderFixture(System.Type type, string methodName)
    {
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, methodName);
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

public class SelfQualifyGeneric<T>
{
    public static int Helper(int x) => x + 1;
    public static int Tagged(int x) => typeof(T) == typeof(string) ? x + 100 : x;
    public int SelfCall(int x) => Helper(x);
    public int CrossInstantiation(int x) => SelfQualifyGeneric<string>.Tagged(x);
}

public class SelfQualifyShadow
{
    public static int Ping(int value) => value + 1;

    // The parameter name shadows the static method name, so an unqualified
    // Ping(...) would be CS0149 (or bind to the parameter); the type qualifier
    // must be retained.
    public int CallsShadowed(int Ping) => SelfQualifyShadow.Ping(Ping);
}
