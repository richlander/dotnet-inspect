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
                new FactPrimitive("member.corelib-identity:string.Length/string.Chars", "MemberIdentity.IsStringLengthGetter/IsStringCharsGetter"),
                new FactPrimitive("mdarray-bounds-and-get-shape", "ForeachStatementPass rank-2 GetLowerBound/GetUpperBound/Get shape checks"),
            ],
            PositiveCoverage: "ForeachStatementPassTests foreach over IEnumerable with and without symbols, single-dimensional arrays, strings, and rank-2 rectangular arrays",
            AdversarialCoverage: "ForeachStatementPassTests source-named/no-symbols hand-written enumerator using/while loop and hand-written indexed array/string/rectangular-array loops",
            MissingDiscriminator: "higher-rank arrays, custom pattern enumerators, and broader collection/extension GetEnumerator shapes not raised"),
    ];
}
