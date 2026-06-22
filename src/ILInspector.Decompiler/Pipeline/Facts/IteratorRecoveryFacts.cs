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
            AdversarialCoverage: "IteratorReconstructionPassTests parameter-referencing empty iterator, state-machine-name lookalike (IteratorAcknowledgmentPassTests), and collection-spread yield body that reconstructs the iterator while leaving the spread lowered (CollectionExpressionSpreadIterator_ReconstructsYieldButNotSpread)",
            MissingDiscriminator: "captured iterator shapes and deeper state-machine/PDB correlation remain owed; a reconstructed iterator can still contain lowered scaffolding from an unraised inner frontier — e.g. a yielded spread collection expression (`yield return [.. arch, true];`) keeps its inline-array/CopyTo/Slice lowering because the spread collection-target frontier is unraised (see CollectionExpression)"),
    ];
}
