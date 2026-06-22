namespace ILInspector.Decompiler.Pipeline;

internal sealed class ExpressionTreeRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.ClosureConversion, nameof(ClosureCoverage.ExpressionTreeLambda)),
            typeof(Declined),
            [
                new FactPrimitive("no-generated-closure-method", "ExpressionLambdaRewriter emits System.Linq.Expressions factory calls instead of a delegate target body"),
                new FactPrimitive("manual-factory-lookalike", "ExpressionTreeLambdaTests.ManualExpressionTreeFactory_StaysFactoryCalls"),
            ],
            PositiveCoverage: "ExpressionTreeLambdaTests simple arithmetic expression-tree lambda fixture remains visible as Expression.Parameter/Add/Lambda factory calls",
            AdversarialCoverage: "ExpressionTreeLambdaTests manual Expression.Parameter/Add/Lambda factory-call lookalike remains factory calls",
            MissingDiscriminator: "Expression-tree lambda syntax has no IL/PDB-independent discriminator from equivalent source-authored factory calls; keep the factory-call form unless a future source-syntax oracle is explicitly introduced"),
    ];
}
