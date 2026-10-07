using System.Reflection;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// #9437: an integer box operand is an exact coercion sink decided before
// printing. Each csc sample is rendered, recompiled, and run; every boxed
// value must have the same runtime type and value as the original method's.
[Trait("Area", "Pass")]
public class BoxExactCoercionTests
{
    [Theory]
    [InlineData(nameof(BoxExactCoercionSamples.NarrowLiterals), "public static object[] M()")]
    [InlineData(nameof(BoxExactCoercionSamples.UnsignedLiterals), "public static object[] M()")]
    [InlineData(nameof(BoxExactCoercionSamples.NativeLiterals), "public static object[] M()")]
    [InlineData(nameof(BoxExactCoercionSamples.WidenedByte), "public static object M(byte b)", (byte)200)]
    [InlineData(nameof(BoxExactCoercionSamples.WidenedChar), "public static object M(char c)", 'A')]
    [InlineData(nameof(BoxExactCoercionSamples.HexChar), "public static string M(char c)", 'A')]
    [InlineData(nameof(BoxExactCoercionSamples.UnboxOverBox), "public static short M()")]
    [InlineData(nameof(BoxExactCoercionSamples.Unchanged), "public static object[] M(int i, long l)", 3, 4L)]
    public void RecompiledBodyBoxesTheSameTypes(string method, string header, params object[] arguments)
    {
        var (function, body) = Render(method);
        Assert.Empty(CoercionInvariant.Check(function));

        object? expected = typeof(BoxExactCoercionSamples).GetMethod(method)!.Invoke(null, arguments);
        object? actual = CompileAndRun(header, body, arguments);

        Assert.Equal(Describe(expected), Describe(actual));
    }

    // The boxes that were already right keep their spelling. Without a
    // metadata context the framework enum constant spells as a cast.
    [Fact]
    public void AlreadyCorrectBoxesKeepTheirSpelling()
    {
        var (_, body) = Render(nameof(BoxExactCoercionSamples.Unchanged));

        Assert.Contains("new object[] { 1, 'c', true, (long)2, i, l, 1.5f, 2.5d, (StringComparison)4, (StringComparison)99 }", body);
    }

    [Fact]
    public void BoxOperandsAreDecidedBeforePrinting()
    {
        var (function, _) = Render(nameof(BoxExactCoercionSamples.NarrowLiterals));

        var boxes = function.Descendants.OfType<Box>().ToList();
        Assert.Equal(5, boxes.Count);
        Assert.All(boxes, box => Assert.Equal(CoercionKind.Exact, Assert.IsType<Coerce>(box.Operand).Kind));
    }

    // Microsoft.CodeAnalysis witnesses from the corpus, all labelled Full
    // before and after: the boxed zero constants and the x4 format argument.
    [Theory]
    [InlineData("Microsoft.CodeAnalysis.Boxes", ".cctor", "Boxes.BoxedByteZero = (byte)0;")]
    [InlineData("Microsoft.CodeAnalysis.Boxes", ".cctor", "Boxes.BoxedUInt64Zero = (ulong)0;")]
    [InlineData("Microsoft.CodeAnalysis.Boxes", ".cctor", "Boxes.BoxedInt32Zero = 0;")]
    [InlineData("Roslyn.Utilities.JsonWriter", "AppendCharAsUnicode", "\"{0:x4}\", (int)c)")]
    public void RealRoslynBoxesSpellTheirType(string typeName, string method, string expected)
    {
        using var source = MetadataSource.Open(typeof(SyntaxTree).Assembly.Location);
        var function = IrImporter.Import(source, typeName, method);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function, reference => IrImporter.Import(source, reference));

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output);
    }

    static (IrFunction Function, string Body) Render(string method)
    {
        using var source = MetadataSource.Open(typeof(BoxExactCoercionSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(BoxExactCoercionSamples).FullName!, method);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function, reference => IrImporter.Import(source, reference));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        return (function, result.Output!);
    }

    static object? CompileAndRun(string header, string body, object[] arguments)
    {
        string text = $$"""
            using System;
            public static class __Gate
            {
                {{header}}
                {
            {{body}}
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "__box_gate",
            [CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview))],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) + "\n--- body ---\n" + body);
        var assembly = Assembly.Load(image.ToArray());
        return assembly.GetType("__Gate")!.GetMethod("M")!.Invoke(null, arguments);
    }

    static string Describe(object? value)
        => value is object[] items
            ? string.Join(", ", items.Select(Describe))
            : value is null ? "null" : $"{value.GetType().Name}:{value}";
}
