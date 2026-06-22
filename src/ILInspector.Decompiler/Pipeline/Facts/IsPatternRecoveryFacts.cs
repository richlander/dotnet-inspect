namespace ILInspector.Decompiler.Pipeline;

internal sealed class IsPatternRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.IsPatternOperator)),
            typeof(IsPatternPass),
            [
                new FactPrimitive("place.local-scope", "IsPatternPass.ReferencedOnlyWithin"),
                new FactPrimitive("property-subpattern-comparison", "CSharpPrinter.TryPropertySubpattern"),
            ],
            PositiveCoverage: "IsPatternPassTests type, property, relational, and multi-property pattern fixtures",
            AdversarialCoverage: "IsPatternPassTests binding-use, side-effecting-value, variable-bound, local-use manual-as, duplicate-property, and unsigned relational-property negatives",
            MissingDiscriminator: "positional/list sub-patterns and non-integral relational sub-patterns remain unraised"),
    ];
}
