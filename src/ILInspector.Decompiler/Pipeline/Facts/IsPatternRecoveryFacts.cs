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
                new FactPrimitive("property-declaration-pattern-guard", "IsPatternPass.MatchRecursivePropertyDeclaration/MatchNestedRecursivePropertyDeclaration"),
            ],
            PositiveCoverage: "IsPatternPassTests type, property, relational, multi-property, and recursive property declaration pattern fixtures; ListPatternPassTests single-element string-array list pattern with constant OR alternatives",
            AdversarialCoverage: "IsPatternPassTests binding-use, side-effecting-value, variable-bound, local-use manual-as, duplicate-property, unsigned relational-property, floating-point relational sub-pattern (NaN-unsafe fold), and escaping recursive-property binding negatives; ListPatternPassTests manual single-element string-array guard negative",
            MissingDiscriminator: "positional sub-patterns, general list patterns beyond the single-element string-array constant-OR slice, recursive property declaration patterns beyond the single-property captured-binding slice, and non-integral relational sub-patterns are not raised; float relational sub-patterns are deliberately declined (ordered/unordered compares disagree on NaN; unsigned compares disagree with signed relational patterns) rather than folded"),
    ];
}
