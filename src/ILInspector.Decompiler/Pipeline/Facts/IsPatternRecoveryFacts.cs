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
            AdversarialCoverage: "IsPatternPassTests binding-use, side-effecting-value, variable-bound, local-use manual-as, duplicate-property, unsigned relational-property, and floating-point relational sub-pattern (NaN-unsafe fold) negatives",
            MissingDiscriminator: "positional/list sub-patterns remain unraised; non-integral relational sub-patterns are deliberately declined (float ordered/unordered compares disagree on NaN; unsigned compares disagree with signed relational patterns) rather than folded"),
    ];
}
