namespace ILInspector.Decompiler.Pipeline;

internal sealed class ExpressionTreeRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.ClosureConversion, nameof(ClosureCoverage.ExpressionTreeLambda)),
            typeof(ExpressionTreeLambdaRaisingPass),
            [
                new FactPrimitive("ir:whole-block-construction", "the block is exactly the parameter-setup + array-fill + Expression.Lambda<Func<int,…,int>> return with no unaccounted statements"),
                new FactPrimitive("dataflow:single-source-parameter-identity", "each ParameterExpression has one owning StoreLocal of Expression.Parameter(typeof(int),name); every use is a LoadLocal of that same local (rejects the two-value-source manual alias)"),
                new FactPrimitive("type-shape:homogeneous-int-arithmetic-body", "the body is Expression.Add/Subtract/Multiply/Divide/Modulo over parameter refs and Expression.Constant(box int, typeof(int)) only"),
                new FactPrimitive("assembly-identity:trusted-platform-expression-family", "the Expression factory calls resolve through a framework public-key token checked on the exact declaring-type metadata handle (MethodRef.DeclaringTypeIsTrustedPlatform; TypeSpecification declaring types are stripped to their base handle, never re-resolved by simple assembly name) and the delegate is the corelib System.Func family — not a name-forged lookalike Expression/Func in a user assembly"),
                new FactPrimitive("checkedness:unchecked-arithmetic-boundary", "the matched graph uses only the unchecked Expression.Add/Subtract/Multiply factories (checked variants decline), and the recovered body is spelled inside unchecked(...) so a checked consuming project rebuilds the same unchecked Add node"),
            ],
            PositiveCoverage: "ExpressionTreeLambdaTests / ExpressionTreeFidelityTests / LadderRung9GateTests: compiler-produced simple and multi-parameter Int32 arithmetic expression lambdas raise to `p => unchecked(e)` at Full fidelity; a Roslyn checked-context oracle confirms the recovered unchecked wrapper rebuilds Expression.Add (not AddChecked)",
            AdversarialCoverage: "manual Expression.Parameter/Add/Lambda factory lookalike (two-value-source parameter alias, reused/duplicate/unspellable parameter identity), non-Int32/mixed-type arithmetic, checked (Expression.AddChecked) bodies, non-generic Expression/LambdaExpression/object return sinks, an unsigned assembly literally named System.Linq.Expressions with a lookalike Expression factory family, duplicate same-simple-name assembly references and corelib-like names reached through a modopt/array TypeSpecification declaring type (ExpressionTreeProvenanceTests), captured (member-token), comparison, and method-call graphs stay in their honest factory-call form",
            MissingDiscriminator: "captured, member-token, method-call, comparison, non-Int32, and multi-type numeric expression graphs still lack a proven sound rewrite and remain owed (#2864)"),
    ];
}
