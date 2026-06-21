namespace ILInspector.Decompiler.Pipeline;

internal sealed class IteratorRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.Yield)),
            typeof(IteratorReconstructionPass),
            [
                new FactPrimitive("generated.iterator-state-machine", "GeneratedCodeIdentity.IsIteratorStateMachineConstructor"),
            ],
            PositiveCoverage: "IteratorReconstructionPassTests linear, yield-nothing, counting-loop, conditional, multi-yield, nested-loop, and foreach-delegation iterator fixtures",
            AdversarialCoverage: "IteratorReconstructionPassTests parameter-referencing empty iterator and IteratorAcknowledgmentPassTests state-machine-name lookalike",
            MissingDiscriminator: "captured iterator shapes and deeper state-machine/PDB correlation remain owed"),
    ];
}
