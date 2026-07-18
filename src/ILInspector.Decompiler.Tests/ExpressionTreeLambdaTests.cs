using System.Collections.Immutable;
using System.IO;
using System.Linq.Expressions;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class ExpressionTreeLambdaTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    static IrFunction RaisedFromFixture(string assemblyPath, string typeName, string methodName)
    {
        using var source = MetadataSource.Open(assemblyPath);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    static void AssertStaysFactory(IrFunction function, string expectedLambdaGeneric)
    {
        Assert.Empty(function.Descendants.OfType<Lambda>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains(expectedLambdaGeneric, output);
        Assert.DoesNotContain("=>", output);
    }

    [Fact]
    public void SimpleExpressionTreeLambda_RecoversLambda()
    {
        var function = Raised(nameof(CfgSampleClass.SimpleExpressionTreeLambda));

        var lambda = Assert.Single(function.Descendants.OfType<Lambda>());
        Assert.Single(lambda.Parameters);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return x => unchecked(x + 1);", output);
        Assert.DoesNotContain("Expression.Lambda", output);
        Assert.DoesNotContain("Expression.Add", output);
        Assert.DoesNotContain("Expression.Parameter", output);
    }

    [Fact]
    public void ManualExpressionTreeFactory_StaysFactoryCalls()
    {
        var function = Raised(nameof(CfgSampleClass.ManualSimpleExpressionTreeFactory));

        // The manual alias reads the parameter through two independent value
        // sources (a stack slot in the body, a local in the array), so the
        // single-source identity guard rejects it: no fabricated lambda.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<int, int>>", output);
        Assert.Contains("Expression.Add", output);
        Assert.Contains("Expression.Parameter(typeof(int), \"x\")", output);
        Assert.DoesNotContain("=>", output);
    }

    [Fact]
    public void ManualReusedParameterFactory_StaysFactoryCalls()
    {
        var function = Raised(nameof(CfgSampleClass.ManualReusedParameterFactory));

        // One ParameterExpression backs both array entries; recovering `(x, x)`
        // would be invalid C#. The distinct-owning-local guard declines it.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<int, int, int>>", output);
        Assert.DoesNotContain("=>", output);
    }

    [Fact]
    public void ManualDuplicateNameFactory_StaysFactoryCalls()
    {
        var function = Raised(nameof(CfgSampleClass.ManualDuplicateNameFactory));

        // Two distinct ParameterExpressions share the name "x"; recovering would
        // emit duplicate declarations. The distinct-name guard declines it.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<int, int, int>>", output);
        Assert.DoesNotContain("=>", output);
    }

    [Fact]
    public void ManualUnspellableNameFactory_StaysFactoryCalls()
    {
        var function = Raised(nameof(CfgSampleClass.ManualUnspellableNameFactory));

        // The name "bad-name" is not a C#-spellable identifier; sanitizing it would
        // change ParameterExpression.Name. The escapable-name guard declines it.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<int, int>>", output);
        Assert.DoesNotContain("=>", output);
    }

    [Fact]
    public void ManualCanonicalReturnedAsExpression_StaysFactoryCalls()
    {
        // The canonical graph returned as the non-generic Expression: recovering
        // `return x => x + 1;` would be CS8917-invalid, so the return-sink guard
        // keeps the honest factory calls.
        AssertStaysFactory(
            Raised(nameof(CfgSampleClass.ManualCanonicalReturnedAsExpression)),
            "Expression.Lambda<Func<int, int>>");
    }

    [Fact]
    public void ManualCanonicalReturnedAsLambdaExpression_StaysFactoryCalls()
    {
        AssertStaysFactory(
            Raised(nameof(CfgSampleClass.ManualCanonicalReturnedAsLambdaExpression)),
            "Expression.Lambda<Func<int, int>>");
    }

    [Fact]
    public void ManualCanonicalReturnedAsObject_StaysFactoryCalls()
    {
        AssertStaysFactory(
            Raised(nameof(CfgSampleClass.ManualCanonicalReturnedAsObject)),
            "Expression.Lambda<Func<int, int>>");
    }

    // Assembly-identity spoof: an unsigned assembly literally named
    // System.Linq.Expressions exposing a lookalike Expression factory family, whose
    // consumer builds the exact canonical inline graph returned as
    // Expression<Func<int,int>>. Every namespace/name/kind, the corelib Func, the
    // arity, the return sink, and the body shape match — so simple-name identity
    // would raise it. The token-verified DeclaringTypeIsTrustedPlatform gate must
    // decline, because the factory calls resolve through a reference with no
    // framework public-key token. This fixture would recover if that gate were
    // removed, so it guards the gate itself, not an incidental shape mismatch.
    [Fact]
    public void LookalikeExpressionAssembly_StaysFactoryCalls()
    {
        var function = RaisedFromFixture(
            FixtureCatalog.DecompilerExpressionTreeSpoof.AssemblyPath(),
            "ExpressionTreeSpoof.ExpressionTreeSpoofer",
            "Spoofed");

        // No fabricated lambda: the lookalike Expression factory is not trusted.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<int, int>>", output);
        Assert.DoesNotContain("=>", output);
    }

    // Direct compile-validity oracle for the return-sink gate: a bare lambda
    // literal `x => x + 1` converts only to the generic Expression<Func<int,int>>
    // (and delegate) targets. Returning it as Expression, LambdaExpression, or
    // object is CS8917, which is exactly why recovery must gate on the return sink.
    [Theory]
    [InlineData("System.Linq.Expressions.Expression<System.Func<int, int>>", true)]
    [InlineData("System.Linq.Expressions.Expression", false)]
    [InlineData("System.Linq.Expressions.LambdaExpression", false)]
    [InlineData("object", false)]
    public void ReturnSink_LambdaLiteralConvertibility_MatchesGate(string returnType, bool compiles)
    {
        var errors = CompileReturnLambda(returnType);

        if (compiles)
            Assert.Empty(errors);
        else
            Assert.Contains("CS8917", errors);
    }

    static string[] CompileReturnLambda(string returnType)
    {
        string source = $$"""
            using System;
            using System.Linq.Expressions;

            public static class ReturnSinkShell
            {
                public static {{returnType}} Make() => x => x + 1;
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "expression-tree-return-sink-shell",
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
        => RoslynTestReferences.TrustedPlatform;

    // Real-run tree-identity oracle for the checkedness blocker. Compiled with the
    // checked overflow default on, the recovered `x => unchecked(x + 1)` rebuilds
    // the unchecked Expression.Add node the source graph had; the un-wrapped
    // `x => x + 1` would instead rebuild the checked AddChecked node — a different
    // tree. That divergence is exactly why the printer wraps the recovered body in
    // unchecked(...), so the rewrite preserves tree identity regardless of the
    // consuming project's CheckForOverflowUnderflow setting.
    [Theory]
    [InlineData("x => unchecked(x + 1)", ExpressionType.Add)]
    [InlineData("x => x + 1", ExpressionType.AddChecked)]
    public void CheckedContext_UncheckedWrapper_PreservesUncheckedTreeNode(string lambdaBody, ExpressionType expected)
    {
        var body = BuildTreeUnderChecked(lambdaBody);
        Assert.Equal(expected, body.NodeType);
    }

    static Expression BuildTreeUnderChecked(string lambdaBody)
    {
        string source = $$"""
            using System;
            using System.Linq.Expressions;

            public static class CheckedShell
            {
                public static Expression<Func<int, int>> Make() => {{lambdaBody}};
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "expression-tree-checked-shell",
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithOverflowChecks(true));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var make = assembly.GetType("CheckedShell")!.GetMethod("Make")!;
        var lambda = (LambdaExpression)make.Invoke(null, null)!;
        return lambda.Body;
    }
}
