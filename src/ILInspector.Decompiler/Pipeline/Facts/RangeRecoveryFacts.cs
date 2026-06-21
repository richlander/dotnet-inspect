namespace ILInspector.Decompiler.Pipeline;

internal sealed class RangeRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.Range)),
            typeof(RangeFromGetSubArrayPass),
            [
                new FactPrimitive("member.corelib-identity:RuntimeHelpers.GetSubArray", "MemberIdentity.IsRuntimeHelpersGetSubArray"),
                new FactPrimitive("member.corelib-identity:string.Substring", "MemberIdentity.IsStringSubstring"),
                new FactPrimitive("member.corelib-identity:Span.Slice", "MemberIdentity.IsSpanSlice"),
                new FactPrimitive("member.corelib-identity:System.Range", "MemberIdentity.IsCoreLibraryType"),
                new FactPrimitive("member.corelib-identity:System.Index", "MemberIdentity.IsCoreLibraryType"),
            ],
            PositiveCoverage: "RangeFromGetSubArrayPassTests array range endpoint matrix plus string/span two-bound and from-end open fixtures",
            AdversarialCoverage: "RangeFromGetSubArrayPassTests user RuntimeHelpers lookalike, manual/one-sided Substring/Slice negatives, and mismatched receiver-spill negative",
            MissingDiscriminator: "ordinary one-sided string/span forms plus broader manual Substring/Slice calls remain unraised"),
    ];
}
