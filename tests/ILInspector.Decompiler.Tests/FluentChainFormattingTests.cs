using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// #2935: once a re-composed fluent chain is long, CSharpPrinter breaks it one
// call per line under a continuation indent; a chain that still fits stays
// inline. Breaking is whitespace-only, so the broken body still compiles and
// carries the same tokens as the inline form.
public sealed class FluentChainFormattingTests
{
    static readonly TypeRef Builder = TypeRef.Definition("synthetic", "", "Builder");
    static readonly TypeRef Holder = TypeRef.Definition("synthetic", "", "Holder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    static readonly MethodRef New = new(Builder, "Create", Builder, [], HasThis: false);

    static Call Head() => new(New, isVirtual: false, []);

    static Call Segment(string name, IrExpression receiver, int arg)
        => new(new MethodRef(Builder, name, Builder, [Int32], HasThis: true), isVirtual: false, [receiver, new Constant(arg, Int32)]);

    // Create().AppendFirstMeasuredValue(1)...AppendFourthMeasuredValue(4) — well
    // past the 120-column wrap width.
    static Call LongChain() =>
        Segment("AppendFourthMeasuredValue",
            Segment("AppendThirdMeasuredValue",
                Segment("AppendSecondMeasuredValue",
                    Segment("AppendFirstMeasuredValue", Head(), 1),
                    2),
                3),
            4);

    static string Render(IrExpression rootValue)
    {
        var block = new Block(0);
        block.Add(new ExpressionStatement(rootValue));
        block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Void, ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Holder, signature, [], container);
        return CSharpPrinter.Print(function).Output!;
    }

    static string RenderWithOptions(IrExpression rootValue, PrinterOptions options)
    {
        var block = new Block(0);
        block.Add(new ExpressionStatement(rootValue));
        block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Void, ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Holder, signature, [], container);
        return CSharpPrinter.Print(function, options).Output!;
    }

    // Issue #3185: the general one-liner opt-out (DisableOneLinerWrapping) suppresses
    // the always-on fluent-chain wrapper, so an over-width chain stays on one line.
    [Fact]
    public void LongChain_DisableOneLinerWrapping_StaysInline()
    {
        string body = RenderWithOptions(LongChain(), PrinterOptions.Default with { DisableOneLinerWrapping = true });

        Assert.Contains(
            "Create().AppendFirstMeasuredValue(1).AppendSecondMeasuredValue(2)"
                + ".AppendThirdMeasuredValue(3).AppendFourthMeasuredValue(4);",
            string.Concat(body.Split('\n').Select(line => line.Trim())));
        Assert.DoesNotContain("\n    .Append", body);
    }

    [Fact]
    public void LongChain_BreaksOneCallPerLine()
    {
        string body = Render(LongChain());

        // The head call is alone on the first line; each chained call lands on its
        // own continuation-indented line (no two segments share a line).
        Assert.Contains("Create()\n", body);
        Assert.Contains("\n    .AppendFirstMeasuredValue(1)\n", body);
        Assert.Contains("\n    .AppendSecondMeasuredValue(2)\n", body);
        Assert.Contains("\n    .AppendThirdMeasuredValue(3)\n", body);
        Assert.Contains("\n    .AppendFourthMeasuredValue(4);", body);
        Assert.DoesNotContain("(1).AppendSecondMeasuredValue", body);
    }

    [Fact]
    public void ShortChain_StaysInline()
    {
        var chain = Segment("A", Segment("B", Head(), 2), 1);

        string body = Render(chain);

        Assert.Contains("Create().B(2).A(1);", body);
        Assert.DoesNotContain("\n    .", body);
    }

    // The broken form is whitespace-only: collapsing its lines back to one line
    // reproduces the inline chain exactly (same tokens, so identical IL).
    [Fact]
    public void BrokenChain_IsTokenIdenticalToInline()
    {
        string body = Render(LongChain());
        string collapsed = string.Concat(body.Split('\n').Select(line => line.Trim()));

        Assert.Contains(
            "Create().AppendFirstMeasuredValue(1).AppendSecondMeasuredValue(2)"
                + ".AppendThirdMeasuredValue(3).AppendFourthMeasuredValue(4);",
            collapsed);
    }

    [Fact]
    public void BrokenChain_Compiles()
    {
        string body = Render(LongChain());

        var errors = Recompile(body)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Broken chain must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<Diagnostic> Recompile(string body)
    {
        string builder = """
            public sealed class Builder
            {
                public static Builder Create() => new Builder();
                public Builder AppendFirstMeasuredValue(int v) => this;
                public Builder AppendSecondMeasuredValue(int v) => this;
                public Builder AppendThirdMeasuredValue(int v) => this;
                public Builder AppendFourthMeasuredValue(int v) => this;
            }
            """;
        string source = $$"""
            using System;
            {{builder}}
            static class __Gate
            {
                public static void M()
                {
            {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "__gate",
            [tree],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetDiagnostics();
    }
}
