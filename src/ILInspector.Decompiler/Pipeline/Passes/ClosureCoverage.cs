namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The ClosureConversion register of the lowering ledger (the LocalRewriter
/// register is <see cref="LoweringCoverage"/>). Roslyn's
/// <c>Lowering/ClosureConversion/</c> is a single transform implemented across a
/// handful of files (<c>ClosureConversion.cs</c>, <c>SynthesizedClosureMethod.cs</c>,
/// <c>ExpressionLambdaRewriter.cs</c>, …), not a per-construct file set like
/// <c>LocalRewriter_*.cs</c>. So this register is <b>curated</b> — anchored to the
/// phase, enumerated by hand — rather than drift-checked against a directory. Same
/// two axes as <see cref="LoweringCoverage"/>: mechanism is the property type,
/// completeness is the <see cref="CompletenessAttribute"/>.
///
/// <para><see cref="DelegateConstructionPass"/> raises a method-group delegate —
/// but that is a LocalRewriter <c>DelegateCreationExpression</c> (already Full),
/// not a closure. <see cref="LambdaRaisingPass"/> recovers the first slice of a
/// real closure: a non-capturing, expression-bodied lambda (<c>x =&gt; x + 1</c>),
/// after <see cref="LambdaCachePass"/> strips its lazy <c>&lt;&gt;c</c> cache.
/// Capturing lambdas, local functions, and expression trees are still owed, so
/// they render as the synthesized <c>&lt;&gt;c__DisplayClass</c> / <c>g__Local|</c>
/// shapes — a large inferior-form gap the LocalRewriter register cannot show.</para>
///
/// <para>Synced against dotnet/roslyn
/// <c>src/Compilers/CSharp/Portable/Lowering/ClosureConversion/</c> @ main.</para>
/// </summary>
internal static class ClosureCoverage
{
    [Completeness(CompletenessLevel.Partial, "non-capturing, expression-bodied only (x => x + 1); capturing / statement / local-bound bodies still owed")]
    public static LambdaRaisingPass Lambda => new();

    [Completeness(CompletenessLevel.None, "void Local() { } — synthesized g__Local| method (+ ref-struct env if capturing)")]
    public static Unhandled LocalFunction => default!;

    [Completeness(CompletenessLevel.None, "captured locals hoisted into a <>c__DisplayClass environment")]
    public static Unhandled CapturedClosure => default!;

    [Completeness(CompletenessLevel.None, "Expression<Func<...>> built via Expression.Lambda/Call/... (ExpressionLambdaRewriter)")]
    public static Unhandled ExpressionTreeLambda => default!;
}
