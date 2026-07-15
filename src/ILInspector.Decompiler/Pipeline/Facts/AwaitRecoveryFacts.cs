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
                new FactPrimitive("member.corelib-identity:AsyncHelpers.AwaitAwaiter", "MemberIdentity.IsAsyncHelpersAwaiter"),
                new FactPrimitive("metadata.runtime-async-definition", "ImportedMethod.IsRuntimeAsync -> IrFunction.IsRuntimeAsync"),
                new FactPrimitive("ownership.runtime-async-awaiter-control-flow", "RuntimeAsyncAwaiterPass same-local and exclusive three-block ownership"),
                new FactPrimitive("state-machine.classic-async-kickoff", "ClassicAsyncReconstructionPass kickoff/MoveNext correlation"),
                new FactPrimitive("state-machine.classic-async-await", "ClassicAsyncReconstructionPass awaiter GetAwaiter/GetResult/AwaitUnsafeOnCompleted shape"),
            ],
            PositiveCoverage: "CfgSampleClass direct runtime-async await fixtures; RuntimeAsyncAwaiterFixtures compiled Task.Yield call/parameter, class call/parameter, extension, sequential, and branch scaffolds exercise both safe and unsafe helpers; synthetic safe/unsafe helper identities; Fixtures.ClassicAsync overlay single, sequential, branch, loop, ValueTask, and try/finally awaits",
            AdversarialCoverage: "AwaitAdversarialTests namespace/type/assembly/instance lookalikes; RuntimeAsyncAwaiterPassTests independently break defining-method metadata, helper assembly/signature, extension evidence, each same-local correlation, reference ownership, and CFG ownership; ClassicAsyncReconstructionPassTests pin the classic recognition matrix",
            MissingDiscriminator: "classic async reconstruction still trusts compiler-reserved names plus DeclaringTypeCompilerGenerated; Start and .Task-named return remain name-matched rather than builder-correlated"),
    ];
}
