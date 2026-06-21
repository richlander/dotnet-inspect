using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class TupleBinaryOperatorPassTests
{
    static IrFunction Raised(string methodName, Type? type = null)
    {
        type ??= typeof(CfgSampleClass);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void TupleValueEquality_RaisesToTupleBinaryExpression()
    {
        var function = Raised(nameof(CfgSampleClass.TupleValueEquals));

        var tupleBinary = Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        Assert.True(tupleBinary.IsEquality);
        Assert.StartsWith("ValueTuple<", tupleBinary.TupleType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<Conditional>());
    }

    [Fact]
    public void TupleValueInequality_RaisesToTupleBinaryExpression()
    {
        var function = Raised(nameof(CfgSampleClass.TupleValueNotEquals));

        var tupleBinary = Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        Assert.False(tupleBinary.IsEquality);
        Assert.Empty(function.Descendants.OfType<Conditional>());
    }

    [Fact]
    public void PrintRaised_RendersTupleEqualityOperator()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.TupleValueEquals))).Output;

        Assert.NotNull(output);
        Assert.Contains("return left == right;", output);
        Assert.DoesNotContain(".Item", output);
        Assert.DoesNotContain(" ? ", output);
    }

    [Fact]
    public void PrintRaised_RendersTupleInequalityOperator()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.TupleValueNotEquals))).Output;

        Assert.NotNull(output);
        Assert.Contains("return left != right;", output);
        Assert.DoesNotContain(".Item", output);
        Assert.DoesNotContain(" ? ", output);
    }

    [Fact]
    public void WholeTupleArity3Equality_RaisesToTupleBinaryExpression()
    {
        var function = Raised(nameof(CfgSampleClass.TupleValueEquals3));

        var tupleBinary = Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        Assert.True(tupleBinary.IsEquality);
        Assert.StartsWith("ValueTuple<", tupleBinary.TupleType.ToDisplayString());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return left == right;", output);
        Assert.DoesNotContain(".Item", output);
        Assert.DoesNotContain("&&", output);
    }

    [Fact]
    public void TupleLiteralEquality_RaisesToTupleBinaryExpression()
    {
        var function = Raised(nameof(CfgSampleClass.TupleLiteralEquals));

        var tupleBinary = Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        Assert.True(tupleBinary.IsEquality);
        Assert.IsType<TupleExpression>(tupleBinary.Left);
        Assert.IsType<TupleExpression>(tupleBinary.Right);
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return (a, b) == (c, d);", output);
        Assert.DoesNotContain("&&", output);
    }

    [Fact]
    public void TupleLiteralInequality_RaisesToTupleBinaryExpression()
    {
        var function = Raised(nameof(CfgSampleClass.TupleLiteralNotEquals));

        var tupleBinary = Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        Assert.False(tupleBinary.IsEquality);
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return (a, b) != (c, d);", output);
        Assert.DoesNotContain("||", output);
    }

    [Fact]
    public void TupleLiteralEqualityArity3_RaisesAllElements()
    {
        var function = Raised(nameof(CfgSampleClass.TupleLiteralEquals3));

        var tupleBinary = Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        Assert.Equal(3, ((TupleExpression)tupleBinary.Left).Elements.Count);
        Assert.Equal(3, ((TupleExpression)tupleBinary.Right).Elements.Count);
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return (a, b, c) == (d, e, f);", output);
    }

    [Fact]
    public void TupleMixedLiteralLeft_RaisesLiteralAgainstVariable()
    {
        var function = Raised(nameof(CfgSampleClass.TupleMixedLiteralLeft));

        var tupleBinary = Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        Assert.IsType<TupleExpression>(tupleBinary.Left);
        Assert.IsNotType<TupleExpression>(tupleBinary.Right);
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return (a, b) == pair;", output);
        Assert.DoesNotContain(".Item", output);
    }

    [Fact]
    public void TupleMixedLiteralRight_RaisesVariableAgainstLiteral()
    {
        var function = Raised(nameof(CfgSampleClass.TupleMixedLiteralRight));

        var tupleBinary = Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        Assert.IsNotType<TupleExpression>(tupleBinary.Left);
        Assert.IsType<TupleExpression>(tupleBinary.Right);
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return pair == (a, b);", output);
        Assert.DoesNotContain(".Item", output);
    }

    [Fact]
    public void TupleMixedInequality_RaisesLiteralAgainstVariable()
    {
        var function = Raised(nameof(CfgSampleClass.TupleMixedNotEquals));

        var tupleBinary = Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        Assert.False(tupleBinary.IsEquality);
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return (a, b) != pair;", output);
    }

    [Fact]
    public void TupleLiteralWithSideEffects_RaisesAndPreservesElementOrder()
    {
        var function = Raised(nameof(CfgSampleClass.TupleLiteralSideEffectOrder));

        Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        var output = CSharpPrinter.Print(function).Output;
        // Elements stay in source order; the fidelity gate proves the spill order
        // round-trips (a reorder would recompile to a different opcode stream).
        Assert.Contains("Tick(a), CfgSampleClass.Tick(b)) == (CfgSampleClass.Tick(c), CfgSampleClass.Tick(d)", output);
    }

    [Fact]
    public void TupleLiteralWithConstElement_Raises()
    {
        var function = Raised(nameof(CfgSampleClass.TupleLiteralConstElement));

        Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return (a, 5) == (c, d);", output);
    }

    [Fact]
    public void LazyShortCircuitComparison_IsNotRaised()
    {
        var function = Raised(nameof(TupleBinaryAdversarialSamples.LazyAndComparison), typeof(TupleBinaryAdversarialSamples));

        Assert.Empty(function.Descendants.OfType<TupleBinaryExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("return a == c && b == d;", output);
    }

    [Fact]
    public void LazyOrComparison_IsNotRaised()
    {
        var function = Raised(nameof(TupleBinaryAdversarialSamples.LazyOrComparison), typeof(TupleBinaryAdversarialSamples));

        Assert.Empty(function.Descendants.OfType<TupleBinaryExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return a != c || b != d;", output);
    }

    [Fact]
    public void HandWrittenArity3ShortCircuit_IsNotRaised()
    {
        var function = Raised(nameof(TupleBinaryAdversarialSamples.LazyAndComparison3), typeof(TupleBinaryAdversarialSamples));

        Assert.Empty(function.Descendants.OfType<TupleBinaryExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return a == c && b == d && e == f;", output);
    }

    [Fact]
    public void BitwiseAndComparison_IsNotRaised()
    {
        // A non-short-circuit `&` evaluates its operands in a different order than a
        // tuple `==` would, so raising it would reorder side effects. The pass must
        // decline it.
        var function = Raised(nameof(CfgSampleClass.BitwiseAndSideEffectComparison));

        Assert.Empty(function.Descendants.OfType<TupleBinaryExpression>());
    }

    [Fact]
    public void HandWrittenMixedTupleFieldComparison_IsNotRaised()
    {
        var function = Raised(nameof(TupleBinaryAdversarialSamples.LazyMixedLiteralVariableComparison), typeof(TupleBinaryAdversarialSamples));

        Assert.Empty(function.Descendants.OfType<TupleBinaryExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("pair.Item1", output);
        Assert.Contains("pair.Item2", output);
        Assert.Contains("&&", output);
    }

    [Fact]
    public void DirectManualTupleFieldComparison_IsNotRaised()
    {
        var function = Raised(nameof(TupleBinaryAdversarialSamples.DirectManualTupleFields), typeof(TupleBinaryAdversarialSamples));

        Assert.Empty(function.Descendants.OfType<TupleBinaryExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains(".Item1", output);
        Assert.Contains(".Item2", output);
    }

    [Fact]
    public void SourceNamedLocalTupleFieldComparison_IsNotRaised()
    {
        var function = Raised(nameof(TupleBinaryAdversarialSamples.SourceNamedLocalTupleFields), typeof(TupleBinaryAdversarialSamples));

        Assert.Empty(function.Descendants.OfType<TupleBinaryExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains(".Item1", output);
        Assert.Contains(".Item2", output);
    }
}

public static class TupleBinaryAdversarialSamples
{
    // Genuine hand-written short-circuit `&&` over bare parameters: csc lowers it
    // lazily (no eager operand spills), so it must NOT raise to a tuple operator.
    public static bool LazyAndComparison(int a, int b, int c, int d) => a == c && b == d;

    // The `!=` twin: hand-written `||` short-circuits and leaves no spill prologue.
    public static bool LazyOrComparison(int a, int b, int c, int d) => a != c || b != d;

    // Hand-written arity-3 short-circuit chain: still lazy, still no spills, so the
    // N-ary literal matcher must not mistake it for `(a, b, e) == (c, d, f)`.
    public static bool LazyAndComparison3(int a, int b, int c, int d, int e, int f) => a == c && b == d && e == f;

    // Hand-written mixed literal-vs-variable spelling: source-like `a,b` on one
    // side and tuple fields on the other, but no eager hidden operand spills.
    public static bool LazyMixedLiteralVariableComparison(int a, int b, (int Sum, int Product) pair)
        => a == pair.Sum && b == pair.Product;

    public static bool DirectManualTupleFields((int Sum, int Product) left, (int Sum, int Product) right)
        => left.Sum == right.Sum && left.Product == right.Product;

    public static bool SourceNamedLocalTupleFields((int Sum, int Product) left, (int Sum, int Product) right)
    {
        var leftCopy = left;
        var rightCopy = right;
        return leftCopy.Sum == rightCopy.Sum && leftCopy.Product == rightCopy.Product;
    }
}
