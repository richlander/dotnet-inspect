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
                new FactPrimitive("pdb.hidden-pattern-enumerator-local", "ForeachStatementPass HasLocalNameSlot + HasSourceLocalName guard"),
            ],
            PositiveCoverage: "ForeachStatementPassTests foreach over IEnumerable with and without symbols, single-dimensional arrays, strings, rank-2 rectangular arrays, and PDB-discriminated custom pattern enumerators",
            AdversarialCoverage: "ForeachStatementPassTests source-named/no-symbols hand-written enumerator using/while loop, hand-written indexed array/string/rectangular-array loops, and no-symbols/manual pattern enumerator loops",
            MissingDiscriminator: "higher-rank arrays, no-symbol custom pattern enumerators, and broader collection/extension GetEnumerator shapes not raised"),
    ];
}
