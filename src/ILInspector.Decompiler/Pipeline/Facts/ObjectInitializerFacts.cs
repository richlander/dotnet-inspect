namespace ILInspector.Decompiler.Pipeline;

internal sealed class ObjectInitializerFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.ObjectOrCollectionInitializerExpression)),
            typeof(ObjectInitializerPass),
            [
                new FactPrimitive("dataflow.stack-slot-dup-chain", "ObjectInitializerPass alias-slot tracking"),
                new FactPrimitive("dataflow.single-use-local", "ObjectInitializerPass named-local tracking"),
                new FactPrimitive("metadata.collection-initializer-receiver", "IEnumerable receiver proof for Add-based collection entries"),
                new FactPrimitive("ir.initializer-entry-shape", "InitializerEntry member/indexer/Add argument model"),
            ],
            PositiveCoverage: "ObjectInitializerPassTests property, field, indexer, list, dictionary, nested object/collection, nested-reassign (Inner = new U { ... }, folded inner-first), named-local object/collection fixtures, ObjectInitializerContiguityTests gap-before-single-escape seed materialization, and LadderRung2GateTests compiler-temp local/return initializer chains",
            AdversarialCoverage: "ObjectInitializerPassTests extra outside use remains lowered; self-reference and mixed member/collection shapes stay flat; non-IEnumerable Add lookalikes stay lowered; named-local nested mutation stays lowered; nested-mutate (Inner = { ... }) stays distinct from nested-reassign (Inner = new U { ... }); a receiver slot re-stored (clobbered) between the run and its escape stays lowered",
            MissingDiscriminator: "named locals with extra outside uses remain lowered"),
    ];
}
