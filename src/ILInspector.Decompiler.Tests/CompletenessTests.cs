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
    public void EhShape_BackwardLeaveBehindOuterSharedMerge_ClassifiesOuterGuard()
    {
        var function = FunctionWithOuterSharedMergeAndBackwardLeave();

        Assert.Equal("prologue-epilogue-guard", EhShapeClassifier.Classify(function));
    }

    [Fact]
    public void EhShape_BackwardLeaveWithoutOuterMerge_RemainsLeaveRetryLoop()
    {
        var function = FunctionWithBackwardLeaveOnly();

        Assert.Equal("leave-retry-loop", EhShapeClassifier.Classify(function));
    }

    static IrFunction FunctionWithOuterSharedMergeAndBackwardLeave()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var voidType = TypeRef.CoreLib("System", "Void");
        var owner = TypeRef.CoreLib("Synthetic", "Owner");
        var exception = TypeRef.CoreLib("System", "Exception");

        var retryBody = new BlockContainer();
        var retry = new Block(0x0020);
        retry.Add(new Leave(0x0000));
        retryBody.Add(retry);
        var catchBody = new BlockContainer();
        catchBody.Add(new Block(0x0030));

        var root = new BlockContainer();
        var head = new Block(0x0000);
        head.Add(new ConditionalBranch(new LoadArgument(0, "flag", boolType), 0x0020));
        var falseArm = new Block(0x0010);
        falseArm.Add(new Branch(0x0040));
        var tryHolder = new Block(0x0020);
        tryHolder.Add(new TryCatch(retryBody, [new CatchClause(exception, catchBody)]));
        var trueExit = new Block(0x0030);
        trueExit.Add(new Branch(0x0040));
        var merge = new Block(0x0040);
        merge.Add(new Return(null));
        foreach (var block in (Block[])[head, falseArm, tryHolder, trueExit, merge])
            root.Add(block);

        return new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [new Parameter("flag", boolType)], HasThis: false, GenericParameterCount: 0),
            [],
            root);
    }

    static IrFunction FunctionWithBackwardLeaveOnly()
    {
        var voidType = TypeRef.CoreLib("System", "Void");
        var owner = TypeRef.CoreLib("Synthetic", "Owner");
        var exception = TypeRef.CoreLib("System", "Exception");

        var retryBody = new BlockContainer();
        var retry = new Block(0x0010);
        retry.Add(new Leave(0x0010));
        retryBody.Add(retry);
        var catchBody = new BlockContainer();
        catchBody.Add(new Block(0x0020));

        var root = new BlockContainer();
        var holder = new Block(0x0010);
        holder.Add(new TryCatch(retryBody, [new CatchClause(exception, catchBody)]));
        root.Add(holder);

        return new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [],
            root);
    }
}
