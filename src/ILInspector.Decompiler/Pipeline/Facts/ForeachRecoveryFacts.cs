namespace ILInspector.Decompiler.Pipeline;

internal sealed class ForeachRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.ForEachStatement)),
            typeof(ForeachStatementPass),
            [
                new FactPrimitive("pdb.hidden-enumerator-local", "ForeachStatementPass HasSourceLocalName guard"),
                new FactPrimitive("enumerator-call-shape", "ForeachStatementPass GetEnumerator/MoveNext/Current shape checks"),
                new FactPrimitive("dataflow.enumerator-local-consumed", "ForeachStatementPass ReferencesEnumerator residual-use guard"),
            ],
            PositiveCoverage: "ForeachStatementPassTests foreach over IEnumerable with and without symbols",
            AdversarialCoverage: "ForeachStatementPassTests source-named and no-symbols hand-written enumerator using/while loop",
            MissingDiscriminator: "array foreach, custom pattern enumerators, and broader collection/extension GetEnumerator shapes not raised"),
    ];
}
