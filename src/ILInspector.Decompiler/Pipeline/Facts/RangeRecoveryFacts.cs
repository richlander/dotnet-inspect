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
                new FactPrimitive("member.corelib-identity:System.Range", "MemberIdentity.IsCoreLibraryType"),
                new FactPrimitive("member.corelib-identity:System.Index", "MemberIdentity.IsCoreLibraryType"),
            ],
            PositiveCoverage: "RangeFromGetSubArrayPassTests range endpoint matrix",
            AdversarialCoverage: "RangeFromGetSubArrayPassTests user RuntimeHelpers lookalike",
            MissingDiscriminator: "string/span Slice and Substring forms are separate lowerings"),
    ];
}
