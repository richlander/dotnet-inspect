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
/// not a closure. <see cref="LambdaRaisingPass"/> recovers non-capturing
/// lambdas, including local/stack-slot body temporaries (on the <c>&lt;&gt;c</c>
/// holder, after <see cref="LambdaCachePass"/> strips its lazy cache), plus
/// zero-local capturing lambdas (a folded <c>&lt;&gt;c__DisplayClass</c>
/// environment whose hoisted fields are substituted back into the body), and
/// <see cref="LocalFunctionRaisingPass"/> recovers static non-capturing local
/// functions. Capturing local-bound lambda bodies are still owed, so they render
/// as synthesized <c>&lt;&gt;c__DisplayClass</c> / <c>g__Local|</c> shapes — an
/// inferior-form gap the LocalRewriter register cannot show. Expression-tree
/// lambdas are tracked here too, but deliberately declined: ExpressionLambdaRewriter
/// emits the same public factory-call shape that source can write by hand.</para>
///
/// <para>Synced against dotnet/roslyn
/// <c>src/Compilers/CSharp/Portable/Lowering/ClosureConversion/</c> @ main.</para>
/// </summary>
internal static class ClosureCoverage
{
    [Completeness(CompletenessLevel.Partial, "expression, simple block, non-capturing local-bound bodies, and parameter-capturing local-bound bodies; outer-local capturing local-bound bodies still owed")]
    public static LambdaRaisingPass Lambda => new();

    [Completeness(CompletenessLevel.Partial, "static local functions with expression/simple block/local-bodied forms, plus capturing (ref struct display-class env, substituted back) zero-local local functions, declared back into the host method and called by their source name; capturing local-bodied, recursive, nested, and shared-environment local functions still owed")]
    public static LocalFunctionRaisingPass LocalFunction => new();

    [Completeness(CompletenessLevel.Partial, "a lambda's captured variables, substituted back from its <>c__DisplayClass environment — folded onto the delegate, or a local set up and shared across statements (allocation/stores elided); a class captured by a local function, or nested environments, still owed")]
    public static LambdaRaisingPass CapturedClosure => new();

    [Completeness(CompletenessLevel.None, "Expression<Func<...>> syntax lowered by ExpressionLambdaRewriter to System.Linq.Expressions factory calls; no generated closure method or IL/PDB-independent discriminator separates it from equivalent source-authored Expression.Parameter/Add/Lambda calls, so the factory-call form is kept")]
    public static Declined ExpressionTreeLambda => default!;
}
