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
            PositiveCoverage: "StringInterpolationPassTests straight-line handler append fixtures, including alignment, format specifiers, and backslash-escaped formats that round-trip through the escaped clause (CfgSampleClass.InterpolationWithBackslashFormat)",
            AdversarialCoverage: "StringInterpolationPassTests user handler lookalike, brace-in-format (stays lowered), and backslash/quote/newline format clauses (escaped, not raw)",
            MissingDiscriminator: "non-straight-line handler flows need dataflow facts"),
    ];
}
