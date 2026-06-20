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
            PositiveCoverage: "IteratorReconstructionPassTests linear, empty, counting-loop, conditional, and multi-yield iterator fixtures",
            AdversarialCoverage: "IteratorReconstructionPassTests nested/foreach-delegation negatives and IteratorAcknowledgmentPassTests state-machine-name lookalike",
            MissingDiscriminator: "foreach-delegation, nested loops, captured iterators, and deeper state-machine/PDB correlation remain owed"),
    ];
}
