namespace ILInspector.Decompiler.Pipeline;

internal sealed class IndexRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.Index)),
            typeof(IndexFromEndPass),
            [
                new FactPrimitive("place.stack-slot", "PlaceIdentity.SameStackSlot"),
                new FactPrimitive("member.corelib-identity:span.Item", "MemberIdentity.IsSpanIndexerGetter"),
            ],
            PositiveCoverage: "IndexFromEndPassTests array/string/span from-end fixtures",
            AdversarialCoverage: "IndexFromEndPassTests hand-written array/string/span Length-minus-index negatives",
            MissingDiscriminator: "Range/slicing forms remain covered by RangeFromGetSubArrayPass"),
    ];
}
