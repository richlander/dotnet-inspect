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
                new FactPrimitive("ir.initializer-entry-shape", "InitializerEntry member/indexer/Add argument model"),
            ],
            PositiveCoverage: "ObjectInitializerPassTests property, field, indexer, list, dictionary, nested object/collection, and nested-reassign (Inner = new U { ... }, folded inner-first) fixtures",
            AdversarialCoverage: "ObjectInitializerPassTests extra outside use remains lowered; self-reference and mixed member/collection shapes stay flat; named-local nested mutation stays lowered; nested-mutate (Inner = { ... }) stays distinct from nested-reassign (Inner = new U { ... })",
            MissingDiscriminator: "named-local initializer shapes are still owed"),
    ];
}
