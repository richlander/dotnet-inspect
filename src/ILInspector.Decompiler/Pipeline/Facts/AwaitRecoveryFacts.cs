using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal sealed class AwaitRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.Await)),
            typeof(AwaitRecoveryPass),
            [
                new FactPrimitive("member.corelib-identity:AsyncHelpers.Await", "MemberIdentity.IsAsyncHelpersAwait"),
            ],
            PositiveCoverage: "CfgSampleClass runtime-async await fixtures",
            AdversarialCoverage: "AwaitAdversarialTests namespace/type/assembly/instance lookalikes",
            MissingDiscriminator: "classic async state-machine awaits require PDB/state-machine facts"),
    ];
}
