using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Adversarial guards for StackSlotLiveRangePass. Its established non-EH path
// requires block-local loads; structured EH uses a stronger proof because later
// rewrites can reshape its regions: every reference to the reused slot must
// belong to a top-level statement in one block. Synthetic-IR near misses pair
// with positive canaries for both paths.
public class StackSlotLiveRangeCrossBlockTests
{
    static readonly TypeRef Owner = TypeRef.CoreLib("Synthetic", "T");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Exception = TypeRef.CoreLib("System", "Exception");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");

    const int Slot = 5;

    // block0: S5 = int (range A, used); S5 = string (range B, used). When
    // crossBlock, a successor block also reads S5 (range B live-out).
    static IrFunction Build(bool crossBlock)
    {
        var container = new BlockContainer();

        var b0 = ReusedSlotBlock();
        container.Add(b0);

        if (crossBlock)
        {
            var b1 = new Block(100);
            b1.Add(new ExpressionStatement(new LoadStackSlot(Slot, String)));    // live-out range-B read
            b1.Add(new Return(null));
            container.Add(b1);
        }
        else
        {
            b0.Add(new Return(null));
        }

        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Owner, signature, [Int32, String], container);
        new StackSlotLiveRangePass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    static Block ReusedSlotBlock()
    {
        var block = new Block(0);
        block.Add(new StoreStackSlot(Slot, new Constant(1, Int32)));
        block.Add(new StoreLocal(0, Int32, new LoadStackSlot(Slot, Int32)));
        block.Add(new StoreStackSlot(Slot, new Constant("x", String)));
        block.Add(new StoreLocal(1, String, new LoadStackSlot(Slot, String)));
        return block;
    }

    static IrFunction BuildStructuredEh(bool nestedLoop, bool handlerLoad, bool filterLoad = false)
    {
        var tryBody = new BlockContainer();
        Block tryBlock;
        if (nestedLoop)
        {
            var loopBody = ReusedSlotBlock();
            loopBody.Add(new Return(null));
            tryBlock = new Block(0);
            tryBlock.Add(new WhileLoop(new Constant(true, Boolean), loopBody));
        }
        else
        {
            tryBlock = ReusedSlotBlock();
        }
        tryBlock.Add(new Return(null));
        tryBody.Add(tryBlock);

        var catchBody = new BlockContainer();
        var catchBlock = new Block(100);
        if (handlerLoad)
            catchBlock.Add(new ExpressionStatement(new LoadStackSlot(Slot, String)));
        catchBlock.Add(new Return(null));
        catchBody.Add(catchBlock);

        var body = new BlockContainer();
        var bodyBlock = new Block(0);
        IrExpression? filter = filterLoad ? new LoadStackSlot(Slot, Boolean) : null;
        bodyBlock.Add(new TryCatch(tryBody, [new CatchClause(Exception, catchBody, filter)]));
        bodyBlock.Add(new Return(null));
        body.Add(bodyBlock);

        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Owner, signature, [Int32, String], body);
        new StackSlotLiveRangePass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    static IrFunction BuildHandlerCandidate(bool inFinally)
    {
        var tryBody = new BlockContainer();
        var tryBlock = new Block(0);
        tryBlock.Add(new Return(null));
        tryBody.Add(tryBlock);

        var handlerBody = new BlockContainer();
        var handlerBlock = ReusedSlotBlock();
        handlerBlock.Add(new Return(null));
        handlerBody.Add(handlerBlock);

        IrNode eh = inFinally
            ? new TryFinally(tryBody, handlerBody)
            : new TryCatch(tryBody, [new CatchClause(Exception, handlerBody)]);

        var body = new BlockContainer();
        var bodyBlock = new Block(200);
        bodyBlock.Add(eh);
        bodyBlock.Add(new Return(null));
        body.Add(bodyBlock);

        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Owner, signature, [Int32, String], body);
        new StackSlotLiveRangePass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    static IrFunction BuildReadBeforeWrite()
    {
        var rebuilt = BuildStructuredEhWithReadBeforeWrite();
        new StackSlotLiveRangePass().Run(rebuilt, PassContext.None);
        rebuilt.CheckInvariant();
        return rebuilt;
    }

    static IrFunction BuildStructuredEhWithReadBeforeWrite()
    {
        var tryBody = new BlockContainer();
        var tryBlock = ReusedSlotBlock();
        tryBlock.Add(new StoreStackSlot(Slot, new LoadStackSlot(Slot, String)));
        tryBlock.Add(new StoreLocal(1, String, new LoadStackSlot(Slot, String)));
        tryBlock.Add(new Return(null));
        tryBody.Add(tryBlock);

        var catchBody = new BlockContainer();
        var catchBlock = new Block(100);
        catchBlock.Add(new Return(null));
        catchBody.Add(catchBlock);

        var body = new BlockContainer();
        var bodyBlock = new Block(200);
        bodyBlock.Add(new TryCatch(tryBody, [new CatchClause(Exception, catchBody)]));
        bodyBlock.Add(new Return(null));
        body.Add(bodyBlock);

        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Owner, signature, [Int32, String], body);
    }

    static bool Split(IrFunction function)
        => function.Descendants.OfType<StoreStackSlot>().Any(s => s.Slot >= StoreStackSlot.DupSlotBase)
            || function.Descendants.OfType<LoadStackSlot>().Any(l => l.Slot >= StoreStackSlot.DupSlotBase);

    [Fact]
    public void BlockLocalRange_Splits()
    {
        Assert.True(Split(Build(crossBlock: false)));
    }

    [Fact]
    public void CrossBlockRange_StaysUnsplit()
    {
        var function = Build(crossBlock: true);
        Assert.False(Split(function));
        // The successor read is left intact on the original slot.
        Assert.Equal(3, function.Descendants.OfType<LoadStackSlot>().Count(l => l.Slot == Slot));
    }

    [Fact]
    public void TryBodyStraightLineRange_Splits()
    {
        Assert.True(Split(BuildStructuredEh(nestedLoop: false, handlerLoad: false)));
    }

    [Fact]
    public void CoreLibTryBodyStraightLineRange_FullPipelineRemovesSlot()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Exception", "CreateTypeInitializationException");
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain("S_0", result.Output);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
    }

    [Fact]
    public void NestedLoopRange_StaysUnsplit()
    {
        Assert.False(Split(BuildStructuredEh(nestedLoop: true, handlerLoad: false)));
    }

    [Fact]
    public void HandlerRead_StaysUnsplit()
    {
        Assert.False(Split(BuildStructuredEh(nestedLoop: false, handlerLoad: true)));
    }

    [Fact]
    public void CatchBodyStraightLineRange_StaysUnsplit()
    {
        Assert.False(Split(BuildHandlerCandidate(inFinally: false)));
    }

    [Fact]
    public void FinallyBodyStraightLineRange_StaysUnsplit()
    {
        Assert.False(Split(BuildHandlerCandidate(inFinally: true)));
    }

    [Fact]
    public void FilterRead_StaysUnsplit()
    {
        Assert.False(Split(BuildStructuredEh(nestedLoop: false, handlerLoad: false, filterLoad: true)));
    }

    [Fact]
    public void ReadBeforeWriteRange_StaysUnsplit()
    {
        Assert.False(Split(BuildReadBeforeWrite()));
    }
}
