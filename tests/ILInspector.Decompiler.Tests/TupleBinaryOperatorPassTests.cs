using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
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

    [Theory]
    [InlineData(nameof(CfgSampleClass.TupleLiteralEquals))]
    [InlineData(nameof(CfgSampleClass.TupleLiteralNotEquals))]
    [InlineData(nameof(CfgSampleClass.TupleLiteralEquals3))]
    [InlineData(nameof(CfgSampleClass.TupleLiteralEquals4))]
    [InlineData(nameof(CfgSampleClass.TupleNestedLiteralEquals))]
    [InlineData(nameof(CfgSampleClass.TupleNestedLiteralEquals2))]
    public void ReconstructedTupleTypesRetainMetadataArity(string methodName)
    {
        var function = Raised(methodName);
        var tuples = function.Descendants.OfType<TupleExpression>().ToArray();

        Assert.NotEmpty(tuples);
        Assert.All(tuples, tuple =>
        {
            Assert.False(tuple.TupleType.HasUnrenderableGenericArity);
            Assert.Equal($"ValueTuple`{tuple.Elements.Count}", tuple.TupleType.ElementType!.Name);
            Assert.Equal(tuple.Elements.Count, tuple.TupleType.TypeArguments.Length);
        });
        Assert.DoesNotContain(FidelityRemarks.CollectCauses(function),
            cause => cause.Discriminator == "generic-arity-mismatch");
    }

    [Theory]
    [InlineData(nameof(WideTupleComparisonSamples.CompareEight), nameof(WideTupleComparisonSamples.EightType))]
    [InlineData(nameof(WideTupleComparisonSamples.CompareNine), nameof(WideTupleComparisonSamples.NineType))]
    [InlineData(nameof(WideTupleComparisonSamples.CompareFifteen), nameof(WideTupleComparisonSamples.FifteenType))]
    public void WideTupleTypesMatchCompilerSignature(string comparisonMethod, string typeWitnessMethod)
    {
        using var source = MetadataSource.Open(typeof(WideTupleComparisonSamples).Assembly.Location);
        var typeWitness = IrImporter.Import(source, typeof(WideTupleComparisonSamples).FullName!, typeWitnessMethod);
        Assert.NotNull(typeWitness);
        var expectedType = typeWitness.Signature.ReturnType;
        var function = Raised(comparisonMethod, typeof(WideTupleComparisonSamples));
        var comparison = Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());

        Assert.Equal(expectedType, comparison.TupleType);
        Assert.Equal(expectedType, Assert.IsType<TupleExpression>(comparison.Left).TupleType);
        Assert.Equal(expectedType, Assert.IsType<TupleExpression>(comparison.Right).TupleType);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    [InlineData(false)]
    [InlineData(true)]
    public void WideTupleComparisonCompileBack_RemainsExact(bool lowered)
    {
        var results = FidelityCheck.Evaluate(
            typeof(WideTupleComparisonSamples).Assembly.Location,
            lowered: lowered,
            type => type == typeof(WideTupleComparisonSamples).FullName)
            .Where(result => result.Method.StartsWith("Compare", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, results.Length);
        Assert.All(results, result => Assert.True(
            result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status} — {result.Detail}"));
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
    public void TupleLiteralEqualityArity4_RaisesAllElements()
    {
        var function = Raised(nameof(CfgSampleClass.TupleLiteralEquals4));

        var tupleBinary = Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        Assert.Equal(4, ((TupleExpression)tupleBinary.Left).Elements.Count);
        Assert.Equal(4, ((TupleExpression)tupleBinary.Right).Elements.Count);
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return (a, b, c, d) == (e, f, g, h);", output);
    }

    [Fact]
    public void NestedTupleLiteralEquality_IsNotFlattenedToWrongArity()
    {
        var function = Raised(nameof(CfgSampleClass.TupleNestedLiteralEquals));

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("return (a, (b, c)) == (d, (e, f));", output);
        Assert.DoesNotContain("return (a, b, c) == (d, e, f);", output);
    }

    [Fact]
    public void NestedTupleLiteralEquality2_IsNotFlattenedToWrongArity()
    {
        var function = Raised(nameof(CfgSampleClass.TupleNestedLiteralEquals2));

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("return ((a, b), (c, d)) == ((e, f), (g, h));", output);
        Assert.DoesNotContain("return (a, b, c, d) == (e, f, g, h);", output);
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
        Assert.Contains("Tick(a), Tick(b)) == (Tick(c), Tick(d)", output);
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
    public void TupleEqualsAndedWithComparison_IsNotCollapsedToHigherArity()
    {
        // `(a, b) == (c, d) && e == f` is one arity-2 tuple comparison AND a separate
        // scalar comparison. The trailing `e == f` is lazy and unbacked by the eager
        // spill prologue; collapsing it into `(a, b, e) == (c, d, f)` invents a third
        // element. Only the genuine arity-2 tuple may raise.
        var function = Raised(nameof(TupleBinaryAdversarialSamples.TupleEqualsAndedWithComparison),
            typeof(TupleBinaryAdversarialSamples));

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("&&", output);
        Assert.DoesNotContain(", e)", output);
        foreach (var tupleBinary in function.Descendants.OfType<TupleBinaryExpression>())
            if (tupleBinary.Left is TupleExpression literal)
                Assert.Equal(2, literal.Elements.Count);
    }

    [Fact]
    public void TupleEqualsAndedWithSideEffectComparison_PreservesShortCircuit()
    {
        // The collapse would evaluate SideEffect(e)/SideEffect(f) unconditionally
        // rather than only when the tuple compares equal. The trailing comparison
        // must stay behind the `&&`.
        var function = Raised(nameof(TupleBinaryAdversarialSamples.TupleEqualsAndedWithSideEffectComparison),
            typeof(TupleBinaryAdversarialSamples));

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("&&", output);
        foreach (var tupleBinary in function.Descendants.OfType<TupleBinaryExpression>())
            if (tupleBinary.Left is TupleExpression literal)
                Assert.Equal(2, literal.Elements.Count);
    }

    [Fact]
    public void SideEffectComparisonAndedWithTupleEquals_IsNotCollapsed()
    {
        // The prepended scalar comparison guards the tuple comparison. Raising the
        // inner tuple comparison is fine, but folding the scalar guard into a
        // nested tuple element would drop the short-circuit.
        var function = Raised(nameof(TupleBinaryAdversarialSamples.SideEffectComparisonAndedWithTupleEquals),
            typeof(TupleBinaryAdversarialSamples));

        var tupleBinary = Assert.Single(function.Descendants.OfType<TupleBinaryExpression>());
        Assert.Equal(2, ((TupleExpression)tupleBinary.Left).Elements.Count);
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("if (SideEffect(e) == SideEffect(f))", output);
        Assert.Contains("return (a, b) == (c, d);", output);
        Assert.DoesNotContain("(e, (a, b))", output);
        Assert.DoesNotContain("(f, (c, d))", output);
    }

    [Fact]
    public void TupleEqualsAndedWithTwoComparisons_IsNotCollapsed()
    {
        var function = Raised(nameof(TupleBinaryAdversarialSamples.TupleEqualsAndedWithTwoComparisons),
            typeof(TupleBinaryAdversarialSamples));

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("&&", output);
        foreach (var tupleBinary in function.Descendants.OfType<TupleBinaryExpression>())
            if (tupleBinary.Left is TupleExpression literal)
                Assert.Equal(2, literal.Elements.Count);
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

    // A genuine arity-2 tuple comparison AND-ed with an unrelated scalar comparison.
    // csc spills the tuple operands eagerly, then lowers the whole expression to
    // `a == c && b == d && e == f`. The eager prologue belongs ONLY to the tuple
    // operands; `e == f` is a separate, lazily evaluated source comparison. The pass
    // must NOT collapse all three comparisons into `(a, b, e) == (c, d, f)`.
    public static bool TupleEqualsAndedWithComparison(int a, int b, int c, int d, int e, int f)
        => (a, b) == (c, d) && e == f;

    // The soundness twin: the trailing comparison has observable side effects. An
    // arity-3 collapse would call SideEffect(e)/SideEffect(f) unconditionally instead
    // of only when the tuple compares equal, changing short-circuit behavior.
    public static bool TupleEqualsAndedWithSideEffectComparison(int a, int b, int c, int d, int e, int f)
        => (a, b) == (c, d) && SideEffect(e) == SideEffect(f);

    public static bool SideEffectComparisonAndedWithTupleEquals(int a, int b, int c, int d, int e, int f)
        => SideEffect(e) == SideEffect(f) && (a, b) == (c, d);

    // Two appended comparisons: the flattened chain ends in `g == h`, also unbacked
    // by the prologue, so `(a, b, e, g) == (c, d, f, h)` must not be raised either.
    public static bool TupleEqualsAndedWithTwoComparisons(int a, int b, int c, int d, int e, int f, int g, int h)
        => (a, b) == (c, d) && e == f && g == h;

    static int s_ticks;
    static int SideEffect(int v) { s_ticks++; return v; }
}

// Compiler-produced CLR tuple signatures independently witness the storage type
// of flat tuple operands, including the nested rest after every seven elements.
public static class WideTupleComparisonSamples
{
    public static (int, int, int, int, int, int, int, int) EightType() => default;

    public static (int, int, int, int, int, int, int, int, int) NineType() => default;

    public static (int, int, int, int, int, int, int, int, int, int, int, int, int, int, int) FifteenType() => default;

    public static bool CompareEight(int[] left, int[] right)
        => (left[0], left[1], left[2], left[3], left[4], left[5], left[6], left[7])
            == (right[0], right[1], right[2], right[3], right[4], right[5], right[6], right[7]);

    public static bool CompareNine(int[] left, int[] right)
        => (left[0], left[1], left[2], left[3], left[4], left[5], left[6], left[7], left[8])
            == (right[0], right[1], right[2], right[3], right[4], right[5], right[6], right[7], right[8]);

    public static bool CompareFifteen(int[] left, int[] right)
        => (left[0], left[1], left[2], left[3], left[4], left[5], left[6], left[7], left[8], left[9], left[10], left[11], left[12], left[13], left[14])
            == (right[0], right[1], right[2], right[3], right[4], right[5], right[6], right[7], right[8], right[9], right[10], right[11], right[12], right[13], right[14]);
}
