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
/// <para>Every entry is owed today. <see cref="DelegateConstructionPass"/> raises
/// a method-group delegate — but that is a LocalRewriter <c>DelegateCreationExpression</c>
/// (already Full), not a closure. Nothing recovers a lambda, local function,
/// captured closure, or expression tree, so they render as the synthesized
/// <c>&lt;&gt;c__DisplayClass</c> / <c>g__Local|</c> shapes — a large inferior-form
/// gap that the LocalRewriter register structurally cannot show.</para>
///
/// <para>Synced against dotnet/roslyn
/// <c>src/Compilers/CSharp/Portable/Lowering/ClosureConversion/</c> @ main.</para>
/// </summary>
internal static class ClosureCoverage
{
    [Completeness(CompletenessLevel.None, "x => x + 1 — delegate over a synthesized method (non-capturing caches in <>c)")]
    public static Unhandled Lambda => default!;

    [Completeness(CompletenessLevel.None, "void Local() { } — synthesized g__Local| method (+ ref-struct env if capturing)")]
    public static Unhandled LocalFunction => default!;

    [Completeness(CompletenessLevel.None, "captured locals hoisted into a <>c__DisplayClass environment")]
    public static Unhandled CapturedClosure => default!;

    [Completeness(CompletenessLevel.None, "Expression<Func<...>> built via Expression.Lambda/Call/... (ExpressionLambdaRewriter)")]
    public static Unhandled ExpressionTreeLambda => default!;
}
