using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// #3009 sub-part 3: the opt-in PrinterOptions.WrapSplittableExpressions taste knob
// also breaks a long associative bitwise |/&/^ chain one operand per continuation
// line — but with the operator LEADING each broken line (the flags-accumulation
// house style), the opposite placement from the short-circuit &&/|| wrapper.
// Wrapping is whitespace-only: the broken body still compiles and carries the same
// tokens as the inline form. Off by default.
public sealed class BitwiseChainWrapTests
{
    static readonly TypeRef Holder = TypeRef.Definition("synthetic", "", "Holder");
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");

    static readonly string[] Flags =
    [
        "firstCapabilityBit",
        "secondCapabilityBit",
        "thirdCapabilityBit",
        "fourthCapabilityBit",
        "fifthCapabilityBit",
        "sixthCapabilityBit",
    ];

    static LoadArgument Flag(int index) => new(index, Flags[index], Int);

    // firstCapabilityBit | ... | sixthCapabilityBit — a left-associative chain well
    // past the 120-column wrap width once `return ...;` is added.
    static Binary LongChain(BinaryKind kind = BinaryKind.Or)
    {
        IrExpression chain = Flag(0);
        for (int i = 1; i < Flags.Length; i++)
            chain = new Binary(kind, isChecked: false, isUnsigned: false, chain, Flag(i));
        return (Binary)chain;
    }

    static string Render(IrExpression returnValue, PrinterOptions? options = null)
        => CSharpPrinter.Print(Function(returnValue), options).Output!;

    static DecompilerResult Result(IrExpression returnValue, PrinterOptions options)
        => CSharpPrinter.Print(Function(returnValue), options);

    static IrFunction Function(IrExpression returnValue)
    {
        var block = new Block(0);
        block.Add(new Return(returnValue));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int, ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Holder, signature, [], container);
    }

    static readonly PrinterOptions Wrap = new() { WrapSplittableExpressions = true };

    [Fact]
    public void LongOrChain_WithOption_BreaksOneOperandPerLine_LeadingOperator()
    {
        string body = Render(LongChain(), Wrap);

        // Head operand on the return line; every later operand on its own
        // continuation-indented line with the operator LEADING it, the last operand
        // closing with the statement semicolon.
        Assert.Contains(
            "return firstCapabilityBit\n"
                + "    | secondCapabilityBit\n"
                + "    | thirdCapabilityBit\n"
                + "    | fourthCapabilityBit\n"
                + "    | fifthCapabilityBit\n"
                + "    | sixthCapabilityBit;",
            body);
        Assert.DoesNotContain("firstCapabilityBit | secondCapabilityBit", body);
    }

    [Fact]
    public void LongAndChain_WithOption_BreaksOneOperandPerLine_LeadingOperator()
    {
        string body = Render(LongChain(BinaryKind.And), Wrap);

        Assert.Contains(
            "return firstCapabilityBit\n"
                + "    & secondCapabilityBit\n"
                + "    & thirdCapabilityBit\n"
                + "    & fourthCapabilityBit\n"
                + "    & fifthCapabilityBit\n"
                + "    & sixthCapabilityBit;",
            body);
    }

    [Fact]
    public void LongChain_DefaultOptions_StaysInline()
    {
        string body = Render(LongChain());

        Assert.Contains(
            "return firstCapabilityBit | secondCapabilityBit | thirdCapabilityBit "
                + "| fourthCapabilityBit | fifthCapabilityBit | sixthCapabilityBit;",
            body);
        Assert.DoesNotContain("\n    |", body);
    }

    [Fact]
    public void ShortChain_WithOption_StaysInline()
    {
        // A two-operand chain is far under the wrap width, so it stays inline even
        // with the option enabled.
        var chain = new Binary(BinaryKind.Or, isChecked: false, isUnsigned: false, Flag(0), Flag(1));

        string body = Render(chain, Wrap);

        Assert.Contains("return firstCapabilityBit | secondCapabilityBit;", body);
        Assert.DoesNotContain("\n    |", body);
    }

    // The broken form is whitespace-only: its whitespace-delimited token stream is
    // identical to the inline chain's (same tokens, so identical IL).
    [Fact]
    public void WrappedChain_IsTokenIdenticalToInline()
    {
        string wrapped = Render(LongChain(), Wrap);
        string inline = Render(LongChain());

        Assert.Contains("\n    |", wrapped);
        Assert.DoesNotContain("\n    |", inline);
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
        string parameters = string.Join(", ", Flags.Select(f => $"int {f}"));
        string source = $$"""
            using System;
            static class __Gate
            {
                public static int M({{parameters}})
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
