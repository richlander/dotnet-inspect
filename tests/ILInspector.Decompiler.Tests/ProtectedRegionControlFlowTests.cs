using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class ProtectedRegionControlFlowTests
{
    static string FixturePath => typeof(CfgSampleClass).Assembly.Location;

    [Fact]
    public void LeaveInTryBelowBoundary_IsRaisable()
    {
        var (leave, boundary) = LeaveInProtectedRegion(ProtectedLocation.Try);

        Assert.True(ProtectedRegionControlFlow.CanRaiseLeave(leave));
        Assert.True(ProtectedRegionControlFlow.CanRaiseLeave(leave, boundary));
    }

    [Fact]
    public void LeaveInCatchBelowBoundary_IsRaisable()
    {
        var (leave, boundary) = LeaveInProtectedRegion(ProtectedLocation.Catch);

        Assert.True(ProtectedRegionControlFlow.CanRaiseLeave(leave));
        Assert.True(ProtectedRegionControlFlow.CanRaiseLeave(leave, boundary));
    }

    [Fact]
    public void LeaveInFinallyBody_IsNotRaisable()
    {
        var (leave, boundary) = LeaveInProtectedRegion(ProtectedLocation.Finally);

        Assert.False(ProtectedRegionControlFlow.CanRaiseLeave(leave));
        Assert.False(ProtectedRegionControlFlow.CanRaiseLeave(leave, boundary));
    }

    [Fact]
    public void ProtectedRegionAboveBoundary_DoesNotLicenseBoundedRaise()
    {
        var leave = new Leave(0x42);
        var loopBody = new Block();
        loopBody.Add(leave);
        var loop = new WhileLoop(new Constant(true, TypeRef.CoreLib("System", "Boolean")), loopBody);
        var tryBlock = new Block();
        tryBlock.Add(loop);
        var tryBody = Container(tryBlock);
        var catchBody = Container(new Block());
        _ = Root(new TryCatch(
            tryBody,
            [new CatchClause(TypeRef.CoreLib("System", "Exception"), catchBody)]));

        Assert.True(ProtectedRegionControlFlow.CanRaiseLeave(leave));
        Assert.False(ProtectedRegionControlFlow.CanRaiseLeave(leave, loop));
    }

    [Fact]
    public void MetadataBackedTryAndCatchLeaves_UseSharedNormalTransfers()
    {
        var (function, tryCatch, tryLeave, catchLeave) =
            StructuredProtectedContinues();
        InstructionExceptionFlowFacts facts = AvailableFacts(function);

        AssertRaisableTransfer(facts, tryLeave);
        AssertRaisableTransfer(facts, catchLeave);
        Assert.True(ProtectedRegionControlFlow.CanRaiseLeave(tryLeave));
        Assert.True(ProtectedRegionControlFlow.CanRaiseLeave(catchLeave));
        Assert.True(ProtectedRegionControlFlow.CanRaiseLeave(
            tryLeave,
            tryCatch.Parent!));
        Assert.True(ProtectedRegionControlFlow.CanRaiseLeave(
            catchLeave,
            tryCatch.Parent!));
        Assert.False(ProtectedRegionControlFlow.CanRaiseLeave(
            tryLeave,
            tryCatch));
        Assert.False(ProtectedRegionControlFlow.CanRaiseLeave(
            catchLeave,
            tryCatch));
        Assert.Null(function.ExceptionFactFailure);
    }

    [Fact]
    public void MetadataBackedLeaveWithoutCorrelation_DeclinesVisibly()
    {
        var (function, tryCatch, tryLeave, _) =
            StructuredProtectedContinues();
        function.ClearImportedExceptionFacts();

        Assert.False(ProtectedRegionControlFlow.CanRaiseLeave(tryLeave));
        Assert.False(ProtectedRegionControlFlow.CanRaiseLeave(
            tryLeave,
            tryCatch.Parent!));
        Assert.Contains(
            "no correlated Instructions evidence",
            function.ExceptionFactFailure);
    }

    [Fact]
    public void Raise_RejectsSameRangeFromAnotherBodyObservation()
    {
        var (function, tryCatch, tryLeave, _) =
            StructuredProtectedContinues();
        var (foreignFunction, foreignTryCatch, _, _) =
            StructuredProtectedContinues();
        InstructionExceptionFlowFacts facts = AvailableFacts(function);
        InstructionExceptionRegionId localId =
            Assert.IsType<InstructionExceptionRegionId>(
                tryCatch.ExceptionProtectedRegion);
        InstructionExceptionRegionId foreignId =
            Assert.IsType<InstructionExceptionRegionId>(
                foreignTryCatch.ExceptionProtectedRegion);
        InstructionExceptionRegion local = Assert.Single(
            facts.Regions,
            region => region.Id == localId);
        InstructionExceptionRegion foreign = Assert.Single(
            AvailableFacts(foreignFunction).Regions,
            region => region.Id == foreignId);
        Assert.Equal(local.Extent, foreign.Extent);
        Assert.NotEqual(local.Id, foreign.Id);

        IReadOnlyList<IrNode> children = tryCatch.DetachChildren();
        var stale = new TryCatch(
            (BlockContainer)children[0],
            children.Skip(1).Cast<CatchClause>())
        {
            ExceptionProtectedRegion = foreignId,
        };
        tryCatch.ReplaceWith(stale);

        Assert.False(ProtectedRegionControlFlow.CanRaiseLeave(tryLeave));
        Assert.False(ProtectedRegionControlFlow.CanRaiseLeave(
            tryLeave,
            stale.Parent!));
        Assert.Contains(
            "does not match its Instructions source context",
            function.ExceptionFactFailure);
    }

    static (
        IrFunction Function,
        TryCatch TryCatch,
        Leave TryLeave,
        Leave CatchLeave) StructuredProtectedContinues()
    {
        using var source = MetadataSource.Open(FixturePath);
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                typeof(CfgSampleClass).FullName!,
                nameof(CfgSampleClass.Issue2861_ForLoopTryAndCatchContinues)));
        _ = AvailableFacts(function);

        new EhStructuringPass().Run(function, PassContext.None);

        TryCatch tryCatch = Assert.Single(
            function.Descendants.OfType<TryCatch>());
        Leave tryLeave = Assert.Single(
            tryCatch.TryBody.Descendants.OfType<Leave>());
        Leave catchLeave = Assert.Single(
            Assert.Single(tryCatch.Clauses)
                .Body.Descendants.OfType<Leave>());
        return (function, tryCatch, tryLeave, catchLeave);
    }

    static void AssertRaisableTransfer(
        InstructionExceptionFlowFacts facts,
        Leave leave)
    {
        InstructionNormalTransfer transfer =
            Assert.IsType<InstructionExceptionFlowResult<
                InstructionNormalTransfer>.Available>(
                    facts.NormalTransferAt(
                        leave.SourceOffset,
                        leave.TargetOffset)).Value;
        Assert.Equal(
            InstructionNormalTransferKind.Leave,
            transfer.Kind);
        Assert.NotEmpty(transfer.RegionsLeft);
    }

    static InstructionExceptionFlowFacts AvailableFacts(
        IrFunction function)
        => Assert.IsType<InstructionExceptionFlowResult<
            InstructionExceptionFlowFacts>.Available>(
                function.ExceptionFlow).Value;

    static (Leave Leave, WhileLoop Boundary) LeaveInProtectedRegion(ProtectedLocation location)
    {
        var leave = new Leave(0x42);
        var leaveBody = Container(Block(leave));
        var emptyBody = Container(new Block());
        IrNode region = location switch
        {
            ProtectedLocation.Try => new TryCatch(
                leaveBody,
                [new CatchClause(TypeRef.CoreLib("System", "Exception"), emptyBody)]),
            ProtectedLocation.Catch => new TryCatch(
                emptyBody,
                [new CatchClause(TypeRef.CoreLib("System", "Exception"), leaveBody)]),
            ProtectedLocation.Finally => new TryFinally(emptyBody, leaveBody),
            _ => throw new ArgumentOutOfRangeException(nameof(location)),
        };

        var loopBody = new Block();
        loopBody.Add(region);
        var loop = new WhileLoop(new Constant(true, TypeRef.CoreLib("System", "Boolean")), loopBody);
        _ = Root(loop);
        return (leave, loop);
    }

    static Block Block(IrNode node)
    {
        var block = new Block();
        block.Add(node);
        return block;
    }

    static BlockContainer Container(Block block)
    {
        var container = new BlockContainer();
        container.Add(block);
        return container;
    }

    static BlockContainer Root(IrNode node)
        => Container(Block(node));

    enum ProtectedLocation
    {
        Try,
        Catch,
        Finally,
    }
}
