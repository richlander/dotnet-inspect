using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The self-contained gap classifier behind <c>--gaps</c>: a raised method is a
/// gap iff its tree still holds unstructured control flow, detected without a
/// second decompiler.
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
        // GotoCommonExit's gotos to a shared exit are the forward-common-merge
        // shape the structuring pass still leaves flat — a surviving branch.
        var residual = Gaps.Residual(Raised(nameof(CfgSampleClass.GotoCommonExit)));
        Assert.NotNull(residual);
        Assert.StartsWith("structuring:", residual);
    }
}
