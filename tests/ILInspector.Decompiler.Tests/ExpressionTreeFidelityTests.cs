using System.Linq.Expressions;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins the expression-tree lambda frontier (issues #1142 / #2864). A C#
/// expression-tree lambda (<c>Expression&lt;Func&lt;…&gt;&gt; f = x =&gt; …;</c>)
/// lowers to a run of <c>System.Linq.Expressions</c> factory calls
/// (<c>Expression.Parameter</c>, <c>Expression.Add</c>, <c>Expression.Lambda</c>,
/// …). For the fully-owned homogeneous-<c>Int32</c> arithmetic slice,
/// <see cref="ExpressionTreeLambdaRaisingPass"/> rewrites that factory graph back
/// to the source <c>p =&gt; e</c> lambda: a semantics-preserving rewrite over the
/// exact canonical shape (single-source parameter identity, arithmetic/constant
/// body only). The recovered lambda stays <c>Full</c>.
///
/// Everything outside that slice stays in the faithful factory-call form. A
/// non-<c>Int32</c> body (promotion/literal-suffix subtleties are owed), a manual
/// factory alias (two value sources for the parameter), and a member-reading body
/// all remain as factory calls. A member body is different again: the compiler
/// captures the member with <c>ldtoken &lt;field/method&gt;</c> (passed to
/// <c>GetFieldFromHandle</c>), and a member token has no C# expression spelling
/// (unlike a type token, which is <c>typeof(T)</c>). That node degrades honestly
/// to <c>DEC0010</c>, so a member-bodied expression-tree lambda is <c>Partial</c>.
/// These tests lock the recovery, the honest factory-call fallback, and the honest
/// degradation so none can silently regress.
/// </summary>
public class ExpressionTreeFidelityTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(ExpressionTreeSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(ExpressionTreeSamples).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void SimpleArithmeticLambda_RecoversLambda_StaysFull()
    {
        var function = Raised(nameof(ExpressionTreeSamples.Simple));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(FidelityRemarks.Collect(function));

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        // Recovered source lambda, not the factory-call scaffolding. The
        // overflow-prone add is spelled unchecked so a checked consuming project
        // still rebuilds the unchecked Expression.Add node (blocker: checkedness
        // identity).
        Assert.Contains("return x => unchecked(x + 1);", output);
        Assert.DoesNotContain("Expression.Lambda", output);
        Assert.DoesNotContain("Expression.Add", output);
        Assert.DoesNotContain("Expression.Parameter", output);
    }

    [Fact]
    public void MultiParamArithmeticLambda_RecoversLambda_StaysFull()
    {
        var function = Raised(nameof(ExpressionTreeSamples.MultiParam));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(FidelityRemarks.Collect(function));

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("return (a, b) => unchecked(a * b - 1);", output);
        Assert.DoesNotContain("Expression.Lambda", output);
        Assert.DoesNotContain("Expression.Multiply", output);
    }

    [Fact]
    public void NonIntArithmeticLambda_StaysFactoryCalls()
    {
        var function = Raised(nameof(ExpressionTreeSamples.NonIntArithmetic));

        // Outside the Int32-only slice: no recovered lambda, honest factory calls.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("Expression.Lambda<Func<double, double>>", output);
        Assert.DoesNotContain("=>", output);
    }

    [Fact]
    public void CheckedArithmeticLambda_StaysFactoryCalls()
    {
        var function = Raised(nameof(ExpressionTreeSamples.CheckedArithmetic));

        // A checked body lowers to Expression.AddChecked, outside the unchecked
        // arithmetic subset the pass matches: no recovered lambda, honest factory
        // calls, and no plain `x + 1` that would rebuild as unchecked Add.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("Expression.AddChecked", output);
        Assert.DoesNotContain("=>", output);
    }

    [Fact]
    public void MemberBodyLambda_DegradesToPartial_OnMemberToken()
    {
        var function = Raised(nameof(ExpressionTreeSamples.Member));

        // The member access lowers to `ldtoken <field>` + GetFieldFromHandle, a
        // runtime member token with no C# expression spelling, so the body is
        // honestly Partial rather than a plausible-but-unspellable recovery.
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.Contains(FidelityRemarks.Collect(function),
            r => r.Code == DiagnosticIds.UnsupportedRuntimeToken);

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("GetFieldFromHandle", output);
    }
    [Fact]
    public void ComparisonPredicate_RecoversLambda_StaysFull()
    {
        var function = Raised(nameof(ExpressionTreeSamples.GreaterThanComparison));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(FidelityRemarks.Collect(function));

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("return (x, y) => x > y;", output);
        Assert.DoesNotContain("Expression.Lambda", output);
        Assert.DoesNotContain("Expression.GreaterThan", output);
        Assert.DoesNotContain("Expression.Parameter", output);
    }

    [Fact]
    public void EqualityComparisonPredicate_RecoversLambda_StaysFull()
    {
        var function = Raised(nameof(ExpressionTreeSamples.EqualityComparison));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(FidelityRemarks.Collect(function));

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return (x, y) => x == y;", output);
        Assert.DoesNotContain("Expression.Equal", output);
    }

    [Fact]
    public void ComparisonOverArithmetic_RecoversLambda_KeepsUncheckedOperand()
    {
        var function = Raised(nameof(ExpressionTreeSamples.ComparisonOverArithmetic));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(FidelityRemarks.Collect(function));

        var output = CSharpPrinter.Print(function).Output;
        // The comparison itself carries no overflow form, but its int-arithmetic
        // operand recompiles to the unchecked Expression.Add, so it stays unchecked.
        Assert.Contains("return (x, y) => unchecked(x + 1) > y;", output);
        Assert.DoesNotContain("Expression.GreaterThan", output);
        Assert.DoesNotContain("Expression.Add", output);
    }

    [Fact]
    public void LogicalCompositionPredicate_StaysFactoryCalls()
    {
        var function = Raised(nameof(ExpressionTreeSamples.LogicalCompositionPredicate));

        // A boolean AndAlso composition root is outside the single-comparison
        // subset: no recovered lambda, honest factory calls.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<int, bool>>", output);
        Assert.DoesNotContain("=>", output);
    }

    [Fact]
    public void LongComparisonPredicate_StaysFactoryCalls()
    {
        var function = Raised(nameof(ExpressionTreeSamples.LongComparison));

        // Non-int parameters are outside the int slice: honest factory calls.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<long, long, bool>>", output);
        Assert.DoesNotContain("=>", output);
    }
}

public static class ExpressionTreeSamples
{
    public static Expression<Func<int, int>> Simple() => x => x + 1;

    // Multi-parameter homogeneous-Int32 arithmetic: still fully owned, recovers
    // to `(a, b) => a * b - 1`.
    public static Expression<Func<int, int, int>> MultiParam() => (a, b) => a * b - 1;

    // Near-miss: non-Int32 arithmetic. Slice 1 is Int32-only (sub-int/float
    // promotion and literal-suffix subtleties are owed), so this stays in honest
    // factory-call form rather than an unproven recovery.
    public static Expression<Func<double, double>> NonIntArithmetic() => x => x + 1.0;

    // Near-miss: a checked-context body lowers to the checked Expression.AddChecked
    // factory, which the pass never matches (only the unchecked Add/Subtract/Multiply
    // names). A checked tree must not be recovered as plain `x => x + 1` (that would
    // rebuild as unchecked Add), so it stays in honest factory-call form.
    public static Expression<Func<int, int>> CheckedArithmetic() => x => checked(x + 1);

    public static Expression<Func<ExpressionTreeNode, bool>> Member()
        => n => n.Name != null && n.Count > 0;

    // Comparison predicate slice: a single int comparison body recovers to the
    // source `l OP r`. Each operator recompiles to exactly the 2-arg
    // Expression.<Cmp> factory it matched, so the round-trip tree is identical.
    public static Expression<Func<int, int, bool>> GreaterThanComparison() => (x, y) => x > y;

    public static Expression<Func<int, int, bool>> LessThanOrEqualComparison() => (a, b) => a <= b;

    public static Expression<Func<int, int, bool>> EqualityComparison() => (x, y) => x == y;

    public static Expression<Func<int, int, bool>> InequalityComparison() => (x, y) => x != y;

    // A comparison against a constant: recovers `x => x > 5` (the constant operand
    // keeps identity because the other operand is parameter-dependent).
    public static Expression<Func<int, bool>> ComparisonAgainstConstant() => x => x > 5;

    // A comparison whose left operand is unchecked int arithmetic: the arithmetic
    // operand still needs the explicit unchecked(...) spelling (it recompiles to the
    // unchecked Expression.Add), while the comparison itself carries no overflow form.
    public static Expression<Func<int, int, bool>> ComparisonOverArithmetic() => (x, y) => x + 1 > y;

    // Near-miss: a boolean composition (AndAlso) root, not a single comparison.
    // Outside the comparison subset, so it stays in honest factory-call form.
    public static Expression<Func<int, bool>> LogicalCompositionPredicate() => x => x > 0 && x < 10;

    // Near-miss: a non-int (long) comparison. The int-parameter guard rejects it, so
    // it stays in honest factory-call form rather than an unproven recovery.
    public static Expression<Func<long, long, bool>> LongComparison() => (x, y) => x > y;
}

public sealed class ExpressionTreeNode
{
    public string? Name;
    public int Count;
}
