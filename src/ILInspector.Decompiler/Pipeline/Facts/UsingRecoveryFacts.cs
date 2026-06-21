namespace ILInspector.Decompiler.Pipeline;

internal sealed class UsingRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.UsingStatement)),
            typeof(UsingStatementPass),
            [
                new FactPrimitive("member.corelib-identity:IDisposable.Dispose", "MemberIdentity.IsIDisposableDispose"),
                new FactPrimitive("member.pattern-identity:Dispose", "UsingStatementPass pattern Dispose() checks with same-assembly value-type evidence"),
                new FactPrimitive("dataflow.local-write-region", "UsingStatementPass local reference/write checks"),
            ],
            PositiveCoverage: "UsingStatementPassTests reference, value-type constrained, and ref-struct pattern dispose shapes",
            AdversarialCoverage: "UsingStatementPassTests resource reassignment lookalike and wrong-signature pattern Dispose negatives",
            MissingDiscriminator: "await using is not raised"),
    ];
}
