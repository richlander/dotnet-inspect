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
            PositiveCoverage: "IteratorReconstructionPassTests linear, yield-nothing, counting-loop, conditional, multi-yield, nested-loop, and foreach-delegation iterator fixtures; LadderIteratorGateTests product-ladder matrix covers iterator accessors, generic/non-generic IEnumerable/IEnumerator returns, closure and cleanup shapes, and explicit residual frontiers",
            AdversarialCoverage: "IteratorReconstructionPassTests parameter-referencing empty iterator, state-machine-name lookalike (IteratorAcknowledgmentPassTests), collection-spread yield body that reconstructs the iterator while leaving the spread lowered, and LadderIteratorGateTests using/user-finally/generic/struct/captured-expression rows that must stay honest residuals until reconstructed",
            MissingDiscriminator: "using/disposal iterators, generic iterator methods, struct instance/custom GetEnumerator iterators, and captured yield expressions remain owed as owned residuals; a reconstructed iterator can still contain lowered scaffolding from an unraised inner frontier — e.g. a yielded spread collection expression (`yield return [.. arch, true];`) keeps its inline-array/CopyTo/Slice lowering because the spread collection-target frontier is unraised (see CollectionExpression). Async iterators and C# 13 ref/unsafe iterator relaxations are separate ladder rows, not this C# 2 synchronous iterator row."),
    ];
}
