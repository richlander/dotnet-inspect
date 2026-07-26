using DotnetInspector.Output;
using ILInspector.Decompiler;

namespace DotnetInspector.Tests;

public class AppliedTasteSectionTests
{
    [Fact]
    public void BuildRows_StyleLensDecision_IsByteDivergent()
    {
        var decision = new DecompilerDecision(
            "style-lens.prefer-conditional-return",
            DecompilerDecisionCategories.StyleLens,
            "NeitherOr",
            "Rewrote a guarded boolean return as a conditional expression.");

        var row = Assert.Single(ApiOutputFormatter.BuildAppliedTasteRows([decision]));

        Assert.Equal("style-lens.prefer-conditional-return", row.Rule);
        Assert.Equal("byte-divergent", row.Fidelity);
        Assert.Equal("NeitherOr", row.Subject);
        Assert.Equal(decision.Detail, row.Detail);
    }

    [Fact]
    public void BuildRows_TasteDecision_IsBytePreserving()
    {
        var decision = new DecompilerDecision(
            "expression.wrap-splittable-chain",
            DecompilerDecisionCategories.Taste,
            "M",
            "Wrapped a splittable call chain across lines.");

        var row = Assert.Single(ApiOutputFormatter.BuildAppliedTasteRows([decision]));

        Assert.Equal("byte-preserving", row.Fidelity);
    }

    [Fact]
    public void BuildRows_ThisQualificationDecision_IsBytePreserving()
    {
        // The opt-in this.-qualification knobs (#3156) record byte-preserving
        // taste decisions keyed by their StyleOptionCatalog id; they surface as
        // ordinary byte-preserving rows, not filtered like the framework import.
        var decision = new DecompilerDecision(
            "qualify-field-access",
            DecompilerDecisionCategories.Taste,
            "ReadField",
            "Qualified instance member 'ReadField' with 'this.'.")
        {
            OldValue = "_value",
            NewValue = "this._value",
        };

        var row = Assert.Single(ApiOutputFormatter.BuildAppliedTasteRows([decision]));

        Assert.Equal("qualify-field-access", row.Rule);
        Assert.Equal("byte-preserving", row.Fidelity);
        Assert.Equal("ReadField", row.Subject);
    }

    [Fact]
    public void BuildRows_ExcludesAlwaysOnFrameworkImport()
    {
        // The framework-import rewrite is always-on and universally expected, not
        // a configurable taste choice: it must never surface on the taste surface.
        var frameworkImport = new DecompilerDecision(
            "type-name.framework-imported",
            DecompilerDecisionCategories.Taste,
            "MakeList",
            "Imported List<T>.");
        var lens = new DecompilerDecision(
            "style-lens.prefer-branchless-boolean",
            DecompilerDecisionCategories.StyleLens,
            "MakeList",
            "Folded a guarded boolean return.");

        var rows = ApiOutputFormatter.BuildAppliedTasteRows([frameworkImport, lens]);

        var row = Assert.Single(rows);
        Assert.Equal("style-lens.prefer-branchless-boolean", row.Rule);
    }

    [Fact]
    public void BuildRows_NoDecisions_YieldsEmpty()
        => Assert.Empty(ApiOutputFormatter.BuildAppliedTasteRows([]));

    // The Annotated view's inline taste annotation: one trailing side comment on
    // the signature, in the same shape the fact overlay uses for analysis, so a
    // reader scans style and analysis the same way (#3191).

    [Fact]
    public void TasteAnnotation_BytePreservingDecision_NamesTheRuleAndSubject()
    {
        var decision = new DecompilerDecision(
            "qualify-field-access",
            DecompilerDecisionCategories.Taste,
            "_count",
            "Qualified instance member '_count' with 'this.'.");

        Assert.Equal(
            "taste.qualify-field-access(_count)",
            ApiOutputFormatter.BuildTasteAnnotation([decision]));
    }

    [Fact]
    public void TasteAnnotation_StyleLens_ReportsFidelityInsteadOfSubject()
    {
        // A lens's subject is the enclosing method, already spelled on the line
        // this comment rides. Its fidelity is the fact the reader does not
        // otherwise have -- and the only signal explaining the absent IL.
        var decision = new DecompilerDecision(
            "style-lens.prefer-conditional-return",
            DecompilerDecisionCategories.StyleLens,
            "Allow",
            "Rewrote a guarded boolean return as a conditional expression.");

        Assert.Equal(
            "taste.prefer-conditional-return(fidelity=byte-divergent)",
            ApiOutputFormatter.BuildTasteAnnotation([decision]));
    }

    [Fact]
    public void TasteAnnotation_ExcludesAlwaysOnFrameworkImport()
    {
        var frameworkImport = new DecompilerDecision(
            "type-name.framework-imported",
            DecompilerDecisionCategories.Taste,
            "MakeList",
            "Imported List<T>.");

        Assert.Null(ApiOutputFormatter.BuildTasteAnnotation([frameworkImport]));
    }

    [Fact]
    public void TasteAnnotation_NoDecisions_YieldsNothing()
        => Assert.Null(ApiOutputFormatter.BuildTasteAnnotation([]));

    [Fact]
    public void TasteAnnotation_RepeatedDecisionsOnOneKnob_CollapseToOnePart()
    {
        // A knob that fires on several members of the same name records a decision
        // each time; the signature comment is a summary, not a tally.
        var decision = new DecompilerDecision(
            "qualify-field-access",
            DecompilerDecisionCategories.Taste,
            "_count",
            "Qualified instance member '_count' with 'this.'.");

        Assert.Equal(
            "taste.qualify-field-access(_count)",
            ApiOutputFormatter.BuildTasteAnnotation([decision, decision]));
    }
}
