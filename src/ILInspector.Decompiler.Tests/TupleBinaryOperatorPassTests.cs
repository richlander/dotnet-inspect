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
    public void TupleLiteralComparison_IsNotRaised()
    {
        var function = Raised(nameof(TupleBinaryAdversarialSamples.TupleLiteralEquals), typeof(TupleBinaryAdversarialSamples));

        Assert.Empty(function.Descendants.OfType<TupleBinaryExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("return a == c && b == d;", output);
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
    public static bool TupleLiteralEquals(int a, int b, int c, int d) => (a, b) == (c, d);

    public static bool DirectManualTupleFields((int Sum, int Product) left, (int Sum, int Product) right)
        => left.Sum == right.Sum && left.Product == right.Product;

    public static bool SourceNamedLocalTupleFields((int Sum, int Product) left, (int Sum, int Product) right)
    {
        var leftCopy = left;
        var rightCopy = right;
        return leftCopy.Sum == rightCopy.Sum && leftCopy.Product == rightCopy.Product;
    }
}
