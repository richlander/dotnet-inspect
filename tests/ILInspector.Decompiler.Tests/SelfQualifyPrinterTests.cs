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
        // A local/parameter that shadows the static method name means an
        // unqualified call would bind to it, so the type qualifier is retained.
        string body = RenderFixture(typeof(SelfQualifyShadow), nameof(SelfQualifyShadow.CallsShadowed));

        Assert.Contains("SelfQualifyShadow.Ping(Ping)", body);
        Assert.DoesNotContain("return Ping(Ping)", body);
    }

    [Fact]
    public void KeywordShadowedStaticCall_StaysQualified()
    {
        // The shadowing check compares escaped C# spellings, so a keyword-named
        // parameter (@event) still shadows the same-named static method.
        string body = RenderFixture(typeof(SelfQualifyKeywordShadow), nameof(SelfQualifyKeywordShadow.Call));

        Assert.Contains("SelfQualifyKeywordShadow.@event(@event)", body);
    }

    [Fact]
    public void LambdaParameterShadowedStaticCall_StaysQualified()
    {
        // A nested lambda parameter that shadows the static method name keeps the
        // call qualified (the shadow scope includes nested lambda parameters).
        string body = RenderAnyFidelity(typeof(SelfQualifyLambdaShadow), nameof(SelfQualifyLambdaShadow.Uses));

        Assert.Contains("SelfQualifyLambdaShadow.Op(Op)", body);
        Assert.DoesNotContain("=> Op(Op)", body);
    }

    [Fact]
    public void OuterScopeShadowedStaticCall_InsideNestedLambda_StaysQualified()
    {
        // A lambda with its own locals renders through a nested printer; an outer
        // parameter that shadows the static method name must still keep the call
        // qualified (the nested printer inherits outer scope names).
        string body = RenderAnyFidelity(typeof(SelfQualifyOuterShadow), nameof(SelfQualifyOuterShadow.Uses));

        Assert.Contains("SelfQualifyOuterShadow.M()", body);
        Assert.DoesNotContain("int x = M();", body);
    }

    [Fact]
    public void SyntheticLocalShadowedStaticCall_StaysQualified()
    {
        // A static method whose name collides with a printer-synthetic local (a
        // stack slot S_n) keeps the qualifier so the call does not bind to the local.
        string body = RenderAnyFidelity(typeof(SelfQualifySyntheticShadow), nameof(SelfQualifySyntheticShadow.Uses));

        Assert.Contains("SelfQualifySyntheticShadow.S_0()", body);
    }

    [Fact]
    public void MethodGenericParameterShadowedStaticCall_StaysQualified()
    {
        // A method type parameter shares the declaration space with the declaring
        // type's members, so it shadows a same-named static member; the call must
        // stay qualified or it binds to the type parameter (CS0119).
        string body = RenderAnyFidelity(typeof(SelfQualifyGenericParamShadow), nameof(SelfQualifyGenericParamShadow.G));

        Assert.Contains("SelfQualifyGenericParamShadow.M()", body);
        Assert.DoesNotContain("return M();", body);
    }

    static string RenderFixture(System.Type type, string methodName)
    {
        var (function, source) = Import(type, methodName);
        using (source)
        {
            Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
            return Render(function, source);
        }
    }

    static string RenderAnyFidelity(System.Type type, string methodName)
    {
        var (function, source) = Import(type, methodName);
        using (source)
            return Render(function, source);
    }

    static (IrFunction Function, MetadataSource Source) Import(System.Type type, string methodName)
    {
        var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);
        return (function!, source);
    }

    static string Render(IrFunction function, MetadataSource source)
    {
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

public class SelfQualifyKeywordShadow
{
    public static int @event(int value) => value + 1;

    public int Call(int @event) => SelfQualifyKeywordShadow.@event(@event);
}

public class SelfQualifyLambdaShadow
{
    public static int Op(int x) => x + 1;

    public int Uses(int seed)
    {
        System.Func<int, int> f = Op => SelfQualifyLambdaShadow.Op(Op);
        return f(seed);
    }
}

public class SelfQualifyOuterShadow
{
    public static int M() => 1;

    public int Uses(int M)
    {
        System.Func<int, int> f = seed => { int x = SelfQualifyOuterShadow.M(); return seed + x; };
        return f(M);
    }
}

public class SelfQualifySyntheticShadow
{
    public static int S_0() => 2;

    public static int Side() => 5;

    // The ternary condition calls a method, so it is impure and stays spilled to
    // a synthetic S_0 slot (rather than inlining), keeping the name collision
    // with the static S_0() call that this test exercises.
    public static int Uses(int a, int b) => S_0() + (Side() > b ? a : b);
}

public class SelfQualifyGenericParamShadow
{
    public static int M() => 1;

    public static int G<M>() => SelfQualifyGenericParamShadow.M();
}
