using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ProtectedRegionControlFlowTests
{
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
