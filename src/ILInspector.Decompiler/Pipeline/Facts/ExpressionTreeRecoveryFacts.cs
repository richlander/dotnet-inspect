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
            ],
            PositiveCoverage: "ExpressionTreeLambdaTests / ExpressionTreeFidelityTests / LadderRung9GateTests: compiler-produced simple and multi-parameter Int32 arithmetic expression lambdas raise to `p => e` at Full fidelity",
            AdversarialCoverage: "manual Expression.Parameter/Add/Lambda factory lookalike (two-value-source parameter alias), non-Int32/mixed-type arithmetic, captured (member-token), comparison, and method-call graphs stay in their honest factory-call form",
            MissingDiscriminator: "captured, member-token, method-call, comparison, non-Int32, and multi-type numeric expression graphs still lack a proven sound rewrite and remain owed (#2864)"),
    ];
}
