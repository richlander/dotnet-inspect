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
                new FactPrimitive("deconstruction.generated-locals", "IsPatternPass.HasSourceLocalName"),
            ],
            PositiveCoverage: "IsPatternPassTests type, property, relational, multi-property, and two-element positional pattern fixtures; ListPatternPassTests single-element string-array list pattern with constant OR alternatives",
            AdversarialCoverage: "IsPatternPassTests binding-use, side-effecting-value, variable-bound, local-use manual-as, duplicate-property, source-authored manual positional deconstruct, unsigned relational-property, and floating-point relational sub-pattern (NaN-unsafe fold) negatives; ListPatternPassTests manual single-element string-array guard negative",
            MissingDiscriminator: "broader positional sub-pattern shapes beyond the two-element string-equality/integral-relational bool-return slice, general list patterns beyond the single-element string-array constant-OR slice, and recursive property declaration sub-patterns (`{ P: T t }`) remain unraised; non-integral relational sub-patterns are deliberately declined (float ordered/unordered compares disagree on NaN; unsigned compares disagree with signed relational patterns) rather than folded"),
    ];
}
