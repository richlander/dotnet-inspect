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
                new FactPrimitive("member.pattern-identity:DisposeAsync", "UsingStatementPass runtime-async DisposeAsync() returning ValueTask"),
                new FactPrimitive("dataflow.local-write-region", "UsingStatementPass local reference/write checks"),
            ],
            PositiveCoverage: "UsingStatementPassTests reference, value-type constrained, ref-struct pattern dispose, and single/nested runtime-async await using DisposeAsync shapes",
            AdversarialCoverage: "UsingStatementPassTests resource reassignment lookalike, wrong-signature pattern Dispose negatives, await-using DisposeAsync/ValueTask-returning negatives, and manual DisposeAsync-in-finally negative",
            MissingDiscriminator: "classic async state-machine await using and broader DisposeAsync pattern/provider shapes are not raised"),
    ];
}
