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
            PositiveCoverage: "IteratorReconstructionPassTests linear, yield-nothing, counting-loop, conditional, multi-yield, switch-with-shared-continuation, while(true)+yield-break, nested-loop, foreach-delegation, exact two-enumerator nested-using, and two-level nested-foreach-delegation iterator fixtures; the two-resource fixtures include successful compile-back. LambdaRaisingPassTests pins both the real System.CommandLine lockstep local-function iterator and its indexed-loop/nested-foreach response-file iterator. LadderIteratorGateTests product-ladder matrix covers iterator accessors, generic/non-generic IEnumerable/IEnumerator returns, closure and cleanup shapes, and explicit residual frontiers",
            AdversarialCoverage: "IteratorReconstructionPassTests parameter-referencing empty iterator, state-machine-name lookalike (IteratorAcknowledgmentPassTests), missing disposal helper/state-machine Dispose evidence, wrong disposal-helper receiver, helper call outside its finally handler, yielded states routed through the wrong resource depth, mismatched yield/resume running state, missing resource reset, and an unrecognized yield state for the two-resource shapes, collection-spread yield body that reconstructs the iterator while leaving the spread lowered, and switch/while Debug+Release compiler shapes; IteratorUserFinallyTests and the foreach user-finally tests ensure authored cleanup is preserved or honestly declined; LadderIteratorGateTests keeps other using/user-finally/generic/struct/captured-expression rows honest",
            MissingDiscriminator: "Using/disposal iterators beyond the exact two-resource, single-assignment, reverse-helper-order lockstep shape and the exact indexed-loop/two-level nested-foreach-delegation shape remain owed, as do generic iterator methods, struct instance/custom GetEnumerator iterators, and captured yield expressions; switch reconstruction is deliberately limited to the proven two-case/default compiler shape with a shared post-switch continuation, and yield-break loop reconstruction is limited to the proven while(true) conditional-break state graph. A reconstructed iterator can still contain lowered scaffolding from an unraised inner frontier — e.g. a yielded spread collection expression (`yield return [.. arch, true];`) keeps its inline-array/CopyTo/Slice lowering because the spread collection-target frontier is unraised (see CollectionExpression). Async iterators and C# 13 ref/unsafe iterator relaxations are separate ladder rows, not this C# 2 synchronous iterator row."),
    ];
}
