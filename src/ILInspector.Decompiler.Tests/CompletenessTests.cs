using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The self-contained gap classifier behind <c>--gaps</c>: a raised method is a
/// gap iff its tree still holds unstructured control flow, read from the tree alone.
/// </summary>
public class CompletenessTests
{
    static IrFunction Raised(string method)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, method)!;
        IrPasses.Run(function);
        return function;
    }

    [Fact]
    public void FullyStructuredMethod_HasNoResidual()
    {
        // Add is straight-line; nothing structures to a residual branch.
        Assert.Null(Completeness.Residual(Raised(nameof(CfgSampleClass.Add))));
    }

    [Fact]
    public void ComparisonTreeSwitch_FullyRaised_HasNoResidual()
    {
        // ClassifyMode is a sparse switch SwitchRaisingPass collects back into a
        // `switch` statement — no surviving goto, so no gap.
        Assert.Null(Completeness.Residual(Raised(nameof(CfgSampleClass.ClassifyMode))));
    }

    [Fact]
    public void ByteRangeSearchTree_RemainsComparisonTreeResidual()
    {
        // ByteRangeSearchTree isolates the HttpClientFactory::IsNonPublic row from
        // #1081: the sparse byte dispatch itself is recognizable, but guarded
        // range arms still share one false return tail, so the strict structurer
        // keeps the comparison tree flat until a dedicated follow-up handles it.
        var function = Raised(nameof(CfgSampleClass.ByteRangeSearchTree));

        Assert.Equal("structuring: conditional-branch", Completeness.Residual(function));
        Assert.Equal("comparison-tree", ConditionalBranchShapeClassifier.Classify(function));
    }

    [Fact]
    public void ExceptionFilter_RemainsFilterEhEntangledResidual()
    {
        // #1089 row 7: catch/filter-entangled branches are the highest-risk EH
        // slice. Until filter structuring lands, the filter's endfilter/leaves
        // must stay as honest Partial residuals, classified as the `filter`
        // subshape rather than consumed by a branch rewrite that crosses handler
        // boundaries.
        var function = Raised(nameof(CfgSampleClass.FilteredLength));

        Assert.Equal("structuring: conditional-branch", Completeness.Residual(function));
        Assert.Equal("eh-entangled", ConditionalBranchShapeClassifier.Classify(function));
        Assert.Equal("filter", EhShapeClassifier.Classify(function));
        Assert.NotEmpty(function.Descendants.OfType<EndFilter>());
        Assert.NotEmpty(function.Descendants.OfType<Leave>());
    }

    [Fact]
    public void CommonExitGotos_RecoveredByRegionExitDiamond()
    {
        // GotoCommonExitGuardedMerge's inner arms both branch to the enclosing
        // diamond's tracked join. The region-exit diamond recovery names that as
        // an if/else local merge without consuming the outer sibling block.
        Assert.Null(Completeness.Residual(Raised(nameof(CfgSampleClass.GotoCommonExitGuardedMerge))));
    }

    [Fact]
    public void CommonExitReturnTail_FoldedToFullyStructured()
    {
        // GotoCommonExit's shared `return result;` tail is inlined into each arm by
        // the return-merge pass (the step-2 common-exit fold), so the guard tree
        // nests cleanly and no residual control flow survives.
        Assert.Null(Completeness.Residual(Raised(nameof(CfgSampleClass.GotoCommonExit))));
    }

    [Fact]
    public void DiamondArmEarlyExitToJoin_RecoveredByMergeExit()
    {
        // DiamondArmEarlyExitGuardedMerge's false arm branches straight to the
        // region join (`if (y > 0) goto done;`). The merge ends in a guard, so the
        // return-merge pass leaves the join as a real block past the arm's lexical
        // stop. The index-range model bailed (cond-target-past-region); the step-3
        // merge-exit recovery raises it because the target is the tracked join, so
        // no residual control flow survives.
        Assert.Null(Completeness.Residual(Raised(nameof(CfgSampleClass.DiamondArmEarlyExitGuardedMerge))));
    }
}
