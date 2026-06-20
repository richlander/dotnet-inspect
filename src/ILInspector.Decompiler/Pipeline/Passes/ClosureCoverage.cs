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
/// not a closure. <see cref="LambdaRaisingPass"/> recovers zero-local lambdas,
/// non-capturing (on the <c>&lt;&gt;c</c> holder, after <see cref="LambdaCachePass"/>
/// strips its lazy cache) and capturing (a folded <c>&lt;&gt;c__DisplayClass</c>
/// environment whose hoisted fields are substituted back into the body). Local
/// functions, local-bound lambda bodies, and expression trees are still owed, so
/// they render as synthesized <c>&lt;&gt;c__DisplayClass</c> / <c>g__Local|</c>
/// shapes — an inferior-form gap the LocalRewriter register cannot show.</para>
///
/// <para>Synced against dotnet/roslyn
/// <c>src/Compilers/CSharp/Portable/Lowering/ClosureConversion/</c> @ main.</para>
/// </summary>
internal static class ClosureCoverage
{
    [Completeness(CompletenessLevel.Partial, "zero-local expression or simple block bodies, capturing or not; local-bound bodies still owed")]
    public static LambdaRaisingPass Lambda => new();

    [Completeness(CompletenessLevel.None, "void Local() { } — synthesized g__Local| method (+ ref-struct env if capturing)")]
    public static Unhandled LocalFunction => default!;

    [Completeness(CompletenessLevel.Partial, "a lambda's captured variables, substituted back from a folded <>c__DisplayClass environment; a display class spread across statements, or captured by a local function, is still owed")]
    public static LambdaRaisingPass CapturedClosure => new();

    [Completeness(CompletenessLevel.None, "Expression<Func<...>> built via Expression.Lambda/Call/... (ExpressionLambdaRewriter)")]
    public static Unhandled ExpressionTreeLambda => default!;
}
