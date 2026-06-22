using System.Linq.Expressions;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins the expression-tree lambda frontier (issue #1142). A C# expression-tree
/// lambda (<c>Expression&lt;Func&lt;…&gt;&gt; f = x =&gt; …;</c>) lowers to a run of
/// <c>System.Linq.Expressions</c> factory calls (<c>Expression.Parameter</c>,
/// <c>Expression.Add</c>, <c>Expression.Lambda</c>, …). The decompiler renders
/// those factory calls faithfully — it does <em>not</em> recover the original
/// lambda syntax, which would require re-deriving the lambda from the tree it
/// builds (a separate, design-gated effort).
///
/// The factory-call form is faithful, valid C# whenever every node it builds has
/// a C# spelling, so a simple arithmetic lambda stays <c>Full</c>. A lambda that
/// reads a member is different: the compiler captures the member with
/// <c>ldtoken &lt;field/method&gt;</c> (passed to <c>GetFieldFromHandle</c>), and a
/// member token has no C# expression spelling (unlike a type token, which is
/// <c>typeof(T)</c>). That node degrades honestly to <c>DEC0010</c>, so a
/// member-bodied expression-tree lambda is <c>Partial</c>. These tests lock that
/// behavior in so neither the factory-call rendering nor the honest degradation
/// can silently regress.
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
    public void SimpleArithmeticLambda_RendersFactoryCalls_StaysFull()
    {
        var function = Raised(nameof(ExpressionTreeSamples.Simple));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(FidelityRemarks.Collect(function));

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        // Faithful factory-call rendering, not recovered `x => x + 1` lambda syntax.
        Assert.Contains("Expression.Lambda<Func<int, int>>", output);
        Assert.Contains("Expression.Add(", output);
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
}

public static class ExpressionTreeSamples
{
    public static Expression<Func<int, int>> Simple() => x => x + 1;

    public static Expression<Func<ExpressionTreeNode, bool>> Member()
        => n => n.Name != null && n.Count > 0;
}

public sealed class ExpressionTreeNode
{
    public string? Name;
    public int Count;
}
