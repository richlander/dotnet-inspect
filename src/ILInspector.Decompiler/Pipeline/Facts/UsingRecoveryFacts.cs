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
                new FactPrimitive("dataflow.local-write-region", "UsingStatementPass local reference/write checks"),
            ],
            PositiveCoverage: "UsingStatementPassTests reference and value-type dispose shapes",
            AdversarialCoverage: "UsingStatementPassTests resource reassignment lookalike",
            MissingDiscriminator: "ref-struct pattern dispose and await using are not raised"),
    ];
}
