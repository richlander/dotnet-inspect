using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The self-contained gap classifier behind <c>--gaps</c>: a raised method is a
/// gap iff its tree still holds unstructured control flow, read from the tree alone.
/// </summary>
public class GapsTests
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
        Assert.Null(Gaps.Residual(Raised(nameof(CfgSampleClass.Add))));
    }

    [Fact]
    public void ComparisonTreeSwitch_FullyRaised_HasNoResidual()
    {
        // ClassifyMode is a sparse switch the structuring pass raises to nested
        // if/else (#640) — no surviving goto, so no gap.
        Assert.Null(Gaps.Residual(Raised(nameof(CfgSampleClass.ClassifyMode))));
    }

    [Fact]
    public void CommonExitGotos_FlaggedAsStructuringGap()
    {
        // GotoCommonExitGuardedMerge's gotos reach a merge that is not a short
        // return tail (it ends in a guard), so the return-merge pass leaves it and
        // the index-range structurer still cannot express the past-region join —
        // a surviving branch the gap docket records.
        var residual = Gaps.Residual(Raised(nameof(CfgSampleClass.GotoCommonExitGuardedMerge)));
        Assert.NotNull(residual);
        Assert.StartsWith("structuring:", residual);
    }

    [Fact]
    public void CommonExitReturnTail_FoldedToFullyStructured()
    {
        // GotoCommonExit's shared `return result;` tail is inlined into each arm by
        // the return-merge pass (the step-2 common-exit fold), so the guard tree
        // nests cleanly and no residual control flow survives.
        Assert.Null(Gaps.Residual(Raised(nameof(CfgSampleClass.GotoCommonExit))));
    }
}
