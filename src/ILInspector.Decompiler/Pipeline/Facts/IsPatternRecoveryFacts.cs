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
                new FactPrimitive("property-declaration-pattern-guard", "IsPatternPass.MatchRecursivePropertyDeclaration/MatchNestedRecursivePropertyDeclaration"),
            ],
            PositiveCoverage: "IsPatternPassTests type, property, relational, multi-property, two-element positional, and recursive property declaration pattern fixtures; ListPatternPassTests single-element string-array list pattern with constant OR alternatives",
            AdversarialCoverage: "IsPatternPassTests binding-use, side-effecting-value, variable-bound, local-use manual-as, duplicate-property, source-authored manual positional deconstruct, unsigned relational-property, floating-point relational sub-pattern (NaN-unsafe fold), and escaping recursive-property binding negatives; ListPatternPassTests manual single-element string-array guard negative and general-list-pattern no-discriminator pin",
            MissingDiscriminator: "broader positional sub-pattern shapes beyond the two-element string-equality/integral-relational bool-return slice, recursive property declaration patterns beyond the single-property captured-binding slice, and general list patterns beyond the single-element string-array constant-OR slice are not raised; general list patterns such as `values is [1, 2, ..]` share pass-visible IR with the hand-written null/length/index guard; non-integral relational sub-patterns are deliberately declined (float ordered/unordered compares disagree on NaN; unsigned compares disagree with signed relational patterns) rather than folded"),
    ];
}
