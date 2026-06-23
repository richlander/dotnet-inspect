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
    public void ByteRangeSearchTree_FullyRaisedAfterBoolArmFold()
    {
        // ByteRangeSearchTree isolates the HttpClientFactory::IsNonPublic row from
        // #1081/#1084: the sparse byte dispatch has guarded range arms that share
        // one false return tail. ComparisonTreeBoolArmPass folds those arms to
        // straight-line bool returns, so the comparison tree no longer survives as
        // a residual gap.
        Assert.Null(Completeness.Residual(Raised(nameof(CfgSampleClass.ByteRangeSearchTree))));
    }

    [Fact]
    public void ExceptionFilter_RaisedCatchWhen_HasNoResidual()
    {
        // #1052 first slice: the narrow typed filter shape is raised to
        // catch-when, so the former filter-entangled conditional branch residual
        // disappears instead of staying as an EH gap.
        var function = Raised(nameof(CfgSampleClass.FilteredLength));

        Assert.Null(Completeness.Residual(function));
        Assert.Empty(function.Descendants.OfType<EndFilter>());
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

    [Fact]
    public void CultureInfoCreateSpecificCulture_RemainsEhUnstructuredPivot()
    {
        // #1135 pivot: CreateSpecificCulture has two adjacent filterless catch
        // islands and EH structuring stays fully flat. This is not a narrow
        // StructuringPass lane; it needs broader EH-region support before the
        // outer conditionals can be productively raised.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Globalization.CultureInfo", "CreateSpecificCulture");
        Assert.NotNull(function);

        IrPasses.Run(function!);

        Assert.Equal("structuring: conditional-branch", Completeness.Residual(function!));
        Assert.Equal("eh-unstructured", EhShapeClassifier.Classify(function!));
        Assert.Empty(function!.Descendants.OfType<TryCatch>());
        Assert.Contains(function.Descendants, node => node is Leave);
    }
}
