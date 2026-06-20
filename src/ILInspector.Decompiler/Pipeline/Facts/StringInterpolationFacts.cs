namespace ILInspector.Decompiler.Pipeline;

internal sealed class StringInterpolationFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.StringInterpolation)),
            typeof(StringInterpolationPass),
            [
                new FactPrimitive("member.corelib-identity:DefaultInterpolatedStringHandler", "MemberIdentity interpolated-string handler predicates"),
            ],
            PositiveCoverage: "StringInterpolationPassTests straight-line handler append fixtures",
            AdversarialCoverage: "StringInterpolationPassTests user handler lookalike",
            MissingDiscriminator: "non-straight-line handler flows need dataflow facts"),
    ];
}
