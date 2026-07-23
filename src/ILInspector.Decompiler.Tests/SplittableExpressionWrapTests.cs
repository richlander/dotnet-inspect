using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// #3067: the opt-in PrinterOptions.WrapSplittableExpressions taste knob breaks a
// long short-circuit &&/|| chain one operand per continuation line (operator
// trailing each broken line), the boolean analog of the always-on fluent-chain
// wrapper. Wrapping is whitespace-only: the broken body still compiles and
// carries the same tokens as the inline form. Off by default.
public sealed class SplittableExpressionWrapTests
{
    static readonly TypeRef Holder = TypeRef.Definition("synthetic", "", "Holder");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");

    static readonly string[] Flags =
    [
        "firstConditionFlag",
        "secondConditionFlag",
        "thirdConditionFlag",
        "fourthConditionFlag",
        "fifthConditionFlag",
        "sixthConditionFlag",
    ];

    static LoadArgument Flag(int index) => new(index, Flags[index], Bool);

    // firstConditionFlag && ... && sixthConditionFlag — a left-associative chain
    // well past the 120-column wrap width once `return ...;` is added.
    static LogicalBinary LongChain(LogicalKind kind = LogicalKind.And)
    {
        IrExpression chain = Flag(0);
        for (int i = 1; i < Flags.Length; i++)
            chain = new LogicalBinary(kind, chain, Flag(i));
        return (LogicalBinary)chain;
    }

    static string Render(IrExpression returnValue, PrinterOptions? options = null)
    {
        var block = new Block(0);
        block.Add(new Return(returnValue));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Bool, ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Holder, signature, [], container);
        return CSharpPrinter.Print(function, options).Output!;
    }

    static DecompilerResult Result(IrExpression returnValue, PrinterOptions options)
    {
        var block = new Block(0);
        block.Add(new Return(returnValue));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Bool, ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Holder, signature, [], container);
        return CSharpPrinter.Print(function, options);
    }

    static readonly PrinterOptions Wrap = new() { WrapSplittableExpressions = true };

    [Fact]
    public void LongChain_WithOption_BreaksOneOperandPerLine()
    {
        string body = Render(LongChain(), Wrap);

        // Head operand on the return line with a trailing operator; every later
        // operand on its own continuation-indented line, operator trailing each
        // broken line, the last operand closing with the statement semicolon.
        Assert.Contains(
            "return firstConditionFlag &&\n"
                + "    secondConditionFlag &&\n"
                + "    thirdConditionFlag &&\n"
                + "    fourthConditionFlag &&\n"
                + "    fifthConditionFlag &&\n"
                + "    sixthConditionFlag;",
            body);
        Assert.DoesNotContain("firstConditionFlag && secondConditionFlag", body);
    }

    [Fact]
    public void LongOrChain_WithOption_BreaksOneOperandPerLine()
    {
        string body = Render(LongChain(LogicalKind.Or), Wrap);

        Assert.Contains(
            "return firstConditionFlag ||\n"
                + "    secondConditionFlag ||\n"
                + "    thirdConditionFlag ||\n"
                + "    fourthConditionFlag ||\n"
                + "    fifthConditionFlag ||\n"
                + "    sixthConditionFlag;",
            body);
    }

    [Fact]
    public void LongChain_DefaultOptions_StaysInline()
    {
        string body = Render(LongChain());

        Assert.Contains(
            "return firstConditionFlag && secondConditionFlag && thirdConditionFlag "
                + "&& fourthConditionFlag && fifthConditionFlag && sixthConditionFlag;",
            body);
        Assert.DoesNotContain(" &&\n", body);
    }

    [Fact]
    public void ShortChain_WithOption_StaysInline()
    {
        // A two-operand chain is far under the wrap width, so it stays inline even
        // with the option enabled.
        var chain = new LogicalBinary(LogicalKind.And, Flag(0), Flag(1));

        string body = Render(chain, Wrap);

        Assert.Contains("return firstConditionFlag && secondConditionFlag;", body);
        Assert.DoesNotContain(" &&\n", body);
    }

    // The broken form is whitespace-only: its whitespace-delimited token stream is
    // identical to the inline chain's (same tokens, so identical IL).
    [Fact]
    public void WrappedChain_IsTokenIdenticalToInline()
    {
        string wrapped = Render(LongChain(), Wrap);
        string inline = Render(LongChain());

        Assert.Contains(" &&\n", wrapped);
        Assert.DoesNotContain(" &&\n", inline);
        Assert.Equal(Tokens(inline), Tokens(wrapped));
    }

    static string[] Tokens(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void WrappedChain_EmitsTasteDecision()
    {
        DecompilerResult result = Result(LongChain(), Wrap);

        Assert.Contains(
            result.Decisions,
            d => d is { RuleId: "expression.wrap-splittable-chain", Category: "taste" });
        Assert.True(result.EffectiveOptions.WrapSplittableExpressions);
    }

    [Fact]
    public void LongChain_DefaultOptions_EmitsNoWrapDecision()
    {
        DecompilerResult result = Result(LongChain(), PrinterOptions.Default);

        Assert.DoesNotContain(
            result.Decisions,
            d => d.RuleId == "expression.wrap-splittable-chain");
        Assert.False(result.EffectiveOptions.WrapSplittableExpressions);
    }

    [Fact]
    public void WrappedChain_Compiles()
    {
        string body = Render(LongChain(), Wrap);

        var errors = Recompile(body)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Wrapped chain must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<Diagnostic> Recompile(string body)
    {
        string parameters = string.Join(", ", Flags.Select(f => $"bool {f}"));
        string source = $$"""
            using System;
            static class __Gate
            {
                public static bool M({{parameters}})
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
