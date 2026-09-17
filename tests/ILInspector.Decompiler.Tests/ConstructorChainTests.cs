using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MethodDefinitionHandle = System.Reflection.Metadata.MethodDefinitionHandle;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Constructor-chain rendering: base/this calls print as body statements, the
/// spilled-this receiver (control-flow argument shapes) canonicalizes back to
/// <c>this</c>, and the implicit parameterless base call is suppressed.
/// </summary>
public class ConstructorChainTests
{
    static (string Body, string? Chain) RaiseCtor(int overloadIndex)
    {
        using var source = MetadataSource.Open(typeof(CtorChainSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CtorChainSamples).FullName!, ".ctor", overloadIndex);
        Assert.NotNull(function);
        IrPasses.Run(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return (result.Output!.ReplaceLineEndings("\n").TrimEnd(), result.ConstructorChain);
    }

    [Fact]
    public void ImplicitParameterlessBase_IsSuppressed()
    {
        // ctor#0: public CtorChainSamples() { } — base() is implicit: no body
        // statement and no initializer.
        var (body, chain) = RaiseCtor(0);
        Assert.Equal("", body);
        Assert.Null(chain);
    }

    [Fact]
    public void BaseCall_LiftsToInitializer()
    {
        // ctor#1: : base(message) — the explicit chain lifts out of the body to
        // a signature initializer (a base(...) body statement is CS0175).
        var (body, chain) = RaiseCtor(1);
        Assert.Equal("base(message)", chain);
        Assert.DoesNotContain("base(", body);
    }

    [Fact]
    public void SpilledCoalesceArgument_LiftsToInitializer()
    {
        // ctor#3: base(message ?? "default") — the ?? spill keeps the base call
        // off the first statement; the constructor-chain argument pass inlines
        // the spill so the call lifts to the signature initializer instead of
        // leaking as an invalid base(temp); body statement (CS0175).
        var (body, chain) = RaiseCtor(3);

        Assert.Equal("base(message ?? \"default\")", chain);
        Assert.DoesNotContain("base(", body);
        Assert.DoesNotContain("..ctor", body);
    }

    [Fact]
    public void SpilledTernaryArgument_LiftsWithArgumentInlined()
    {
        // ctor#2: base(code > 0 ? "positive" : null) — the ternary argument
        // spills to a temp; the pass inlines it into the lifted initializer so
        // the base argument survives (it would otherwise drop on recompile).
        var (body, chain) = RaiseCtor(2);

        Assert.NotNull(chain);
        Assert.StartsWith("base(", chain);
        Assert.Contains("\"positive\"", chain);
        Assert.DoesNotContain("base(", body);
    }

    [Fact]
    public void ThisDelegation_LiftsToInitializer()
    {
        // ctor#4: : this(value.ToString()) — lifts to the signature initializer.
        var (_, chain) = RaiseCtor(4);

        Assert.StartsWith("this(", chain);
        Assert.DoesNotContain("base(", chain);
        Assert.DoesNotContain("..ctor", chain);
    }

    static DecompilerResult RaiseDefaultCtor(Type type)
    {
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, ".ctor");
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function);
        Assert.True(result.Succeeded);
        return result;
    }

    [Fact]
    public void ConstantFieldInitializer_LiftsToFieldDeclaration()
    {
        // CfgSampleClass declares `int _shadowed = 1;`. The compiler emits that
        // store before the (implicit) base call, so it is a field initializer,
        // not a body assignment — lift it to the field declaration and drop it
        // from the body (where it would recompile to AFTER the base call).
        var result = RaiseDefaultCtor(typeof(CfgSampleClass));

        Assert.Contains(("_shadowed", "1"), result.FieldInitializers);
        Assert.DoesNotContain("_shadowed", result.Output);
    }

    [Fact]
    public void NewObjectFieldInitializer_LiftsToFieldDeclaration()
    {
        // LockFixtureSamples declares `readonly object _root = new();`. The
        // `new object()` initializer is self-contained (no this/parameter/local
        // load), so it is legal in field-declaration context and lifts there.
        var result = RaiseDefaultCtor(typeof(LockFixtureSamples));

        Assert.Contains(("_root", "new object()"), result.FieldInitializers);
        Assert.DoesNotContain("_root", result.Output);
    }
}
