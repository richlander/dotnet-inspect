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
            PositiveCoverage: "IsPatternPassTests type, property, relational, and multi-property pattern fixtures; ListPatternPassTests single-element string-array list pattern with constant OR alternatives",
            AdversarialCoverage: "IsPatternPassTests binding-use, side-effecting-value, variable-bound, local-use manual-as, duplicate-property, unsigned relational-property, and floating-point relational sub-pattern (NaN-unsafe fold) negatives; ListPatternPassTests manual single-element string-array guard negative and general-list-pattern no-discriminator pin",
            MissingDiscriminator: "positional sub-patterns and recursive property declaration sub-patterns (`{ P: T t }`) remain unraised; general list patterns beyond the single-element string-array constant-OR slice remain unraised because `values is [1, 2, ..]` shares pass-visible IR with the hand-written null/length/index guard; non-integral relational sub-patterns are deliberately declined (float ordered/unordered compares disagree on NaN; unsigned compares disagree with signed relational patterns) rather than folded"),
    ];
}
