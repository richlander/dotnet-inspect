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
            AdversarialCoverage: "RangeFromGetSubArrayPassTests user RuntimeHelpers lookalike, manual/one-sided Substring/Slice negatives, source-named start-temp negative, and mismatched receiver-spill / start-temp negatives. Adversarial review (#959) confirmed the GetSubArray identity and the string/span hidden start-temp + spilled-receiver (PlaceIdentity.SameStackSlot) discriminators reject hand-written reloads, source-named temps, and user-assembly lookalikes",
            // from-end-open string/span ranges (s[^n..]) ARE recovered; the still-unraised string/span
            // forms are specifically from-start (s[i..] lowers to the 1-arg Substring(i)) and to-end
            // (s[..j] lowers to Substring(0, j)), whose overloads carry no spill the pass keys on, plus
            // broader manual Substring/Slice calls.
            MissingDiscriminator: "from-start (s[i..] -> Substring(i)) and to-end (s[..j] -> Substring(0, j)) one-sided string/span forms, plus broader manual Substring/Slice calls, remain unraised"),
    ];
}
