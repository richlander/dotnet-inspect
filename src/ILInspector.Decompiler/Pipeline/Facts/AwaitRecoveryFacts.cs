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
                new FactPrimitive("state-machine.classic-async-kickoff", "ClassicAsyncReconstructionPass kickoff/MoveNext correlation"),
                new FactPrimitive("state-machine.classic-async-await", "ClassicAsyncReconstructionPass awaiter GetAwaiter/GetResult/AwaitUnsafeOnCompleted shape"),
            ],
            PositiveCoverage: "CfgSampleClass runtime-async await fixtures; Fixtures.ClassicAsync overlay single, sequential, branch, loop, ValueTask, and try/finally awaits",
            AdversarialCoverage: "AwaitAdversarialTests namespace/type/assembly/instance lookalikes",
            MissingDiscriminator: "classic async reconstruction is fixture-backed; broader user state-machine lookalikes still need adversarial hardening"),
    ];
}
