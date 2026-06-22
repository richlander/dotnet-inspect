namespace ILInspector.Decompiler.Pipeline;

internal sealed class CollectionExpressionFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.CollectionExpression)),
            typeof(InlineArrayCollectionPass),
            [
                new FactPrimitive("generated.inline-array-buffer", "InlineArrayCollectionPass IsSynthesizedInlineArray guard"),
                new FactPrimitive("member.private-impl-inline-array-helpers", "InlineArrayCollectionPass InlineArrayAsSpan/AsReadOnlySpan and InlineArrayElementRef helper checks"),
                new FactPrimitive("dataflow.inline-array-local-contained", "InlineArrayCollectionPass local reference count, init, and slot coverage checks"),
                new FactPrimitive("pdb.hidden-list-fill-locals", "InlineArrayCollectionPass HasKnownHiddenLocal guard for List<T> count/span locals"),
                new FactPrimitive("member.collections-marshal-identity", "MemberIdentity SetCount/AsSpan and List<T> signature checks"),
            ],
            PositiveCoverage: "IrImporterTests inline-array span collection-expression fixture; CollectionExpressionFrontierTests PDB-discriminated List<T> literal; InlineArrayElementRefPassTests direct inline-array span conversion/element-ref fixtures",
            AdversarialCoverage: "CollectionExpressionFrontierTests manual CollectionsMarshal lookalikes and no-symbol List<T> literal decline; InlineArrayElementRefPassTests runtime inline-array buffers, mixed span conversion, and direct-place conversion negatives",
            MissingDiscriminator: "no-symbol general List<T> literals lowered through public CollectionsMarshal.SetCount/AsSpan have the same IL/IR as a manual count-local marshal sequence, so they remain lowered"),
    ];
}
