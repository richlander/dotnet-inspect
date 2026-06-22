namespace ILInspector.Decompiler.Pipeline;

internal sealed class CollectionExpressionRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.CollectionExpression)),
            typeof(InlineArrayCollectionPass),
            [
                new FactPrimitive("member.privateimpl-inline-array", "InlineArrayCollectionPass exact <PrivateImplementationDetails>.InlineArray* helper checks"),
                new FactPrimitive("member.corelib-identity:ReadOnlySpan.ctor", "MemberIdentity.IsReadOnlySpanArrayConstructor"),
                new FactPrimitive("member.corelib-identity:Span.ctor", "MemberIdentity.IsSpanArrayConstructor"),
                new FactPrimitive("member.corelib-identity:ReadOnlySpan.CopyTo", "MemberIdentity.IsReadOnlySpanCopyTo"),
                new FactPrimitive("member.corelib-identity:Span.Slice", "MemberIdentity.IsSpanSlice"),
                new FactPrimitive("member.corelib-identity:Span.Length", "MemberIdentity.IsSpanLengthGetter"),
                new FactPrimitive("pdb.hidden-collection-temporaries", "InlineArrayCollectionPass IsHiddenLocal guard for array-spread temporaries"),
            ],
            PositiveCoverage: "IrImporterTests inline-array span collection expression fixture; CollectionExpressionFrontierTests array spread-with-tail fixture; IdiomShapeScorecardTests pins both as CollectionExpression syntax",
            AdversarialCoverage: "InlineArrayElementRefPassTests runtime inline-array helper negative; CollectionExpressionFrontierTests general collection capacity/comparer rows, symbol-less array spread, and manual array-spread lowering with source-named temporaries stay lowered",
            MissingDiscriminator: "general collection targets, yielded spread collection expressions, symbol-less/manual array spread lowerings, and broader/multi-spread array forms remain unraised until a safe discriminator is proven"),
    ];
}
