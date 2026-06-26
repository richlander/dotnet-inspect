using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class NonFiniteConstantPrinterTests
{
    static readonly TypeRef Double = TypeRef.CoreLib("System", "Double");
    static readonly TypeRef Single = TypeRef.CoreLib("System", "Single");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");

    [Fact]
    public void NonFiniteFloatAndDoubleConstants_RenderAsFrameworkConstants_AndCompile()
    {
        AssertRenderAndCompile(Double, new Constant(double.NaN, Double), "public static double M()", "return double.NaN;");
        AssertRenderAndCompile(Double, new Constant(double.PositiveInfinity, Double), "public static double M()", "return double.PositiveInfinity;");
        AssertRenderAndCompile(Double, new Constant(double.NegativeInfinity, Double), "public static double M()", "return double.NegativeInfinity;");
        AssertRenderAndCompile(Single, new Constant(float.NaN, Single), "public static float M()", "return float.NaN;");
        AssertRenderAndCompile(Single, new Constant(float.PositiveInfinity, Single), "public static float M()", "return float.PositiveInfinity;");
        AssertRenderAndCompile(Single, new Constant(float.NegativeInfinity, Single), "public static float M()", "return float.NegativeInfinity;");
    }

    [Fact]
    public void ImportedNonFiniteFloatAndDoubleConstants_RenderAsFrameworkConstants_AndCompile()
    {
        AssertImportedFixture(nameof(CfgSampleClass.DoubleNaN), "public static double M()", "return double.NaN;");
        AssertImportedFixture(nameof(CfgSampleClass.DoublePositiveInfinity), "public static double M()", "return double.PositiveInfinity;");
        AssertImportedFixture(nameof(CfgSampleClass.DoubleNegativeInfinity), "public static double M()", "return double.NegativeInfinity;");
        AssertImportedFixture(nameof(CfgSampleClass.FloatNaN), "public static float M()", "return float.NaN;");
        AssertImportedFixture(nameof(CfgSampleClass.FloatPositiveInfinity), "public static float M()", "return float.PositiveInfinity;");
        AssertImportedFixture(nameof(CfgSampleClass.FloatNegativeInfinity), "public static float M()", "return float.NegativeInfinity;");
        AssertImportedFixture(nameof(CfgSampleClass.DoubleNaNComparison), "public static bool M(double x)", "return x == double.NaN;");
        AssertImportedFixture(nameof(CfgSampleClass.DoublePositiveInfinityAdd), "public static double M(double x)", "return x + double.PositiveInfinity;");
    }

    [Fact]
    public void InlineNonFiniteFloatAndDoubleConstants_RenderAsFrameworkConstants_AndCompile()
    {
        AssertRenderAndCompile(
            Bool,
            new Comparison(ComparisonKind.Equal, isUnsigned: false, new LoadArgument(0, "x", Double), new Constant(double.NaN, Double)),
            "public static bool M(double x)",
            "return x == double.NaN;",
            new Parameter("x", Double));

        AssertRenderAndCompile(
            Double,
            new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadArgument(0, "x", Double), new Constant(double.PositiveInfinity, Double)),
            "public static double M(double x)",
            "return x + double.PositiveInfinity;",
            new Parameter("x", Double));
    }

    [Fact]
    public void FiniteFloatAndDoubleConstants_KeepLiteralSpelling_AndCompile()
    {
        AssertRenderAndCompile(Double, new Constant(double.MaxValue, Double), "public static double M()", "return 1.7976931348623157E+308d;");
        AssertRenderAndCompile(Single, new Constant(float.MaxValue, Single), "public static float M()", "return 3.4028235E+38f;");
        AssertRenderAndCompile(Single, new Constant(-0.0f, Single), "public static float M()", "return -0f;");
        AssertRenderAndCompile(Double, new Constant(1.5d, Double), "public static double M()", "return 1.5d;");
    }

    static void AssertRenderAndCompile(TypeRef returnType, IrExpression expression, string methodHeader, string expected, params Parameter[] parameters)
    {
        string body = Render(returnType, expression, parameters.ToImmutableArray()).Trim();
        AssertBodyAndCompile(methodHeader, expected, body);
    }

    static void AssertImportedFixture(string methodName, string methodHeader, string expected)
    {
        string body = RenderFixture(methodName).Trim();
        AssertBodyAndCompile(methodHeader, expected, body);
    }

    static void AssertBodyAndCompile(string methodHeader, string expected, string body)
    {
        Assert.Equal(expected, body);

        var errors = Recompile(methodHeader, body)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Rendered body must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static string RenderFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    static string Render(TypeRef returnType, IrExpression expression, ImmutableArray<Parameter> parameters)
    {
        var block = new Block(0);
        block.Add(new Return(expression));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(returnType, parameters, HasThis: false, GenericParameterCount: 0),
            ImmutableArray<TypeRef>.Empty,
            body);
        return CSharpPrinter.Print(function).Output!;
    }

    static ImmutableArray<Diagnostic> Recompile(string methodHeader, string body)
    {
        string source = $$"""
            using System;
            static class __Gate
            {
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "__gate",
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return compilation.GetDiagnostics();
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (string path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            try { references.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }
        return references.ToImmutable();
    }
}
