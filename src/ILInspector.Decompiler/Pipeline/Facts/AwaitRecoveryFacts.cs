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
            AdversarialCoverage: "AwaitAdversarialTests namespace/type/assembly/instance lookalikes; ClassicAsyncReconstructionPassTests near-miss matrix pins each recognition discriminator (method name, `<...>d__N` type-name shape incl. leading `<`, DeclaringTypeCompilerGenerated fact, `<>t__builder` field name, builder-store-on-local-address, Start call, .Task-named return, state-machine local, single-block shape)",
            MissingDiscriminator: "classic async reconstruction trusts compiler-reserved names (`<...>d__N` type, `<>t__builder` field) plus the DeclaringTypeCompilerGenerated metadata fact, all unspeakable in C# source; each recognition discriminator is pinned by ClassicAsyncReconstructionPassTests (the builder-store-on-local invariant via redundant gates). The Start call and .Task-named return are matched by name only (not builder-correlated); the deeper kickoff<->MoveNext correlation stays fixture-backed, since the imported sibling MoveNext must reconstruct or the kickoff stays lowered"),
    ];
}
