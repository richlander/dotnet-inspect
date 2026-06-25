namespace ILInspector.Decompiler.Pipeline;

internal sealed class CollectionExpressionRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.CollectionExpression)),
            typeof(InlineArrayCollectionPass),
            [
                new FactPrimitive("member.privateimpl-inline-array", "InlineArrayCollectionPass exact generated <PrivateImplementationDetails>.InlineArray* helper checks plus inline-array type evidence"),
                new FactPrimitive("member.corelib-identity:ReadOnlySpan.ctor", "MemberIdentity.IsReadOnlySpanArrayConstructor"),
                new FactPrimitive("member.corelib-identity:Span.ctor", "MemberIdentity.IsSpanArrayConstructor"),
                new FactPrimitive("member.corelib-identity:ReadOnlySpan.CopyTo", "MemberIdentity.IsReadOnlySpanCopyTo"),
                new FactPrimitive("member.corelib-identity:Span.Slice", "MemberIdentity.IsSpanSlice"),
                new FactPrimitive("member.corelib-identity:Span.Length", "MemberIdentity.IsSpanLengthGetter"),
                new FactPrimitive("pdb.hidden-collection-temporaries", "InlineArrayCollectionPass IsHiddenLocal guard for array-spread temporaries"),
                new FactPrimitive("pdb.hidden-list-fill-locals", "InlineArrayCollectionPass HasKnownHiddenLocal guard for List<T> count/span locals"),
                new FactPrimitive("member.collections-marshal-identity", "MemberIdentity SetCount/AsSpan and List<T> signature checks"),
            ],
            PositiveCoverage: "IrImporterTests inline-array span collection expression fixture; CollectionExpressionFrontierTests array spread-with-tail and PDB-discriminated List<T> literal fixtures; IdiomShapeScorecardTests pins collection expressions as CollectionExpression syntax",
            AdversarialCoverage: "InlineArrayElementRefPassTests runtime inline-array helper negative; CollectionExpressionFrontierTests general collection capacity/comparer rows, symbol-less array spread, manual array-spread lowering with source-named temporaries, manual CollectionsMarshal lookalikes, and no-symbol List<T> literal declines",
            MissingDiscriminator: "no-symbol general List<T> literals lowered through public CollectionsMarshal.SetCount/AsSpan have the same IL/IR as a manual count-local marshal sequence, so they remain lowered; yielded spread collection expressions, symbol-less/manual array spread lowerings, and broader/multi-spread array forms remain unraised until a safe discriminator is proven"),
    ];
}
