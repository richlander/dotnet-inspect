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
}
