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

    // Constant-fold guard: hand-written canonical graphs whose arithmetic operands
    // are all compile-time constant would be folded by the compiler on recompile
    // (`2 + 3` rebuilds as Constant(5), not Add), so recovering them as `x => ...`
    // would change the tree. Each stays in honest factory-call form. Covers every
    // supported operator at the root, a nested constant-only subtree under a
    // parameter-dependent parent, and the Divide/Remainder zero/overflow edges.
    [Theory]
    [InlineData(nameof(CfgSampleClass.ManualConstantOnlyAddFactory), "Expression.Add")]
    [InlineData(nameof(CfgSampleClass.ManualConstantOnlySubtractFactory), "Expression.Subtract")]
    [InlineData(nameof(CfgSampleClass.ManualConstantOnlyMultiplyFactory), "Expression.Multiply")]
    [InlineData(nameof(CfgSampleClass.ManualNestedConstantSubtreeFactory), "Expression.Multiply")]
    [InlineData(nameof(CfgSampleClass.ManualConstantOnlyDivideByZeroFactory), "Expression.Divide")]
    [InlineData(nameof(CfgSampleClass.ManualConstantOnlyRemainderOverflowFactory), "Expression.Modulo")]
    public void ConstantOnlyArithmeticFactory_StaysFactoryCalls(string methodName, string expectedFactory)
    {
        var function = Raised(methodName);

        Assert.Empty(function.Descendants.OfType<Lambda>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<int, int>>", output);
        Assert.Contains(expectedFactory, output);
        Assert.DoesNotContain("=>", output);
    }

    // Positive control: a hand-written graph in which every arithmetic node has at
    // least one parameter-dependent operand (`x * 2 + 3`) folds nothing, so the
    // constant-fold guard leaves it recoverable at Full fidelity.
    [Fact]
    public void ManualParameterDependentFactory_RecoversLambda()
    {
        var function = Raised(nameof(CfgSampleClass.ManualParameterPlusConstantFactory));

        Assert.Single(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("x =>", output);
        Assert.DoesNotContain("Expression.Lambda", output);
    }

    // Real compile-back tree-structure oracle for the constant-fold guard. A source
    // lambda whose body is constant-only arithmetic is folded to a single Constant
    // node (root) or collapses a nested constant subtree — so recovering a
    // constant-only factory graph as such a lambda would NOT rebuild the original
    // Add/Multiply structure. A parameter-dependent body keeps its arithmetic node.
    [Fact]
    public void ConstantFoldOracle_RootConstantArithmetic_FoldsToConstant()
    {
        var body = BuildTreeUnderChecked("x => unchecked(2 + 3)");
        Assert.Equal(ExpressionType.Constant, body.NodeType);
    }

    [Fact]
    public void ConstantFoldOracle_NestedConstantSubtree_Collapses()
    {
        var body = BuildTreeUnderChecked("x => unchecked((2 + 3) * x)");
        var multiply = Assert.IsAssignableFrom<BinaryExpression>(body);
        Assert.Equal(ExpressionType.Multiply, multiply.NodeType);
        // The original Add(Constant(2), Constant(3)) left subtree is gone.
        Assert.Equal(ExpressionType.Constant, multiply.Left.NodeType);
    }

    [Fact]
    public void ConstantFoldOracle_ParameterDependent_KeepsArithmeticNode()
    {
        var body = BuildTreeUnderChecked("x => unchecked(x + 1)");
        Assert.Equal(ExpressionType.Add, body.NodeType);
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

    // Constant-fold guard for comparisons: a hand-written constant-only comparison
    // graph (`GreaterThan(2, 3)`) would be folded to a single Constant(false) by the
    // compiler on recompile (proven by the oracle below), so recovering it as
    // `x => 2 > 3` would change the tree. It stays in honest factory-call form.
    [Fact]
    public void ManualConstantOnlyComparisonFactory_StaysFactoryCalls()
    {
        var function = Raised(nameof(CfgSampleClass.ManualConstantOnlyComparisonFactory));

        Assert.Empty(function.Descendants.OfType<Lambda>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<int, bool>>", output);
        Assert.Contains("Expression.GreaterThan", output);
        Assert.DoesNotContain("=>", output);
    }

    // Real compile-back tree oracle for the comparison subset. A parameter-dependent
    // comparison keeps its 2-arg comparison node with no method/lift (so the
    // recovered `l OP r` rebuilds the identical factory node), while a constant-only
    // comparison is folded to a single Constant — which is exactly why the
    // constant-fold guard declines the constant-only near miss.
    [Theory]
    [InlineData("Func<int, int, bool>", "(x, y) => x > y", ExpressionType.GreaterThan)]
    [InlineData("Func<int, int, bool>", "(x, y) => x == y", ExpressionType.Equal)]
    [InlineData("Func<int, int, bool>", "(x, y) => x != y", ExpressionType.NotEqual)]
    [InlineData("Func<int, int, bool>", "(a, b) => a <= b", ExpressionType.LessThanOrEqual)]
    [InlineData("Func<int, bool>", "x => x > 5", ExpressionType.GreaterThan)]
    public void ComparisonOracle_ParameterDependent_KeepsComparisonNode(string delegateType, string lambda, ExpressionType expected)
    {
        var (nodeType, method, lifted) = BuildPredicateTree(delegateType, lambda);
        Assert.Equal(expected, nodeType);
        // The 2-arg factory the pass matches: no user-defined method, not lifted.
        Assert.Null(method);
        Assert.False(lifted);
    }

    [Fact]
    public void ComparisonOracle_ConstantOnly_FoldsToConstant()
    {
        var (nodeType, _, _) = BuildPredicateTree("Func<int, bool>", "x => 2 > 3");
        Assert.Equal(ExpressionType.Constant, nodeType);
    }

    static (ExpressionType NodeType, System.Reflection.MethodInfo? Method, bool Lifted) BuildPredicateTree(string delegateType, string lambda)
    {
        string source = $$"""
            using System;
            using System.Linq.Expressions;

            public static class PredicateShell
            {
                public static Expression<{{delegateType}}> Make() => {{lambda}};
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "expression-tree-predicate-shell",
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var make = assembly.GetType("PredicateShell")!.GetMethod("Make")!;
        var body = ((LambdaExpression)make.Invoke(null, null)!).Body;
        return body is BinaryExpression binary
            ? (body.NodeType, binary.Method, binary.IsLiftedToNull)
            : (body.NodeType, null, false);
    }
}
