using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Adversarial guard for StackSlotLiveRangePass cross-block live ranges (issue #1432).
// The split only renumbers loads inside the store's own block, so a slot read from a
// successor block (live-out) would be left on the stale slot — now holding a different
// range's value. The pass claims block-local ranges only, so it must decline when the
// slot escapes the block. Synthetic-IR near-miss (csc keeps these ranges block-local)
// paired with a block-local positive canary that still splits.
public class StackSlotLiveRangeCrossBlockTests
{
    static readonly TypeRef Owner = TypeRef.CoreLib("Synthetic", "T");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");

    const int Slot = 5;

    // block0: S5 = int (range A, used); S5 = string (range B, used). When
    // crossBlock, a successor block also reads S5 (range B live-out).
    static IrFunction Build(bool crossBlock)
    {
        var container = new BlockContainer();

        var b0 = new Block(0);
        b0.Add(new StoreStackSlot(Slot, new Constant(1, Int32)));                 // range A
        b0.Add(new ExpressionStatement(new LoadStackSlot(Slot, Int32)));
        b0.Add(new StoreStackSlot(Slot, new Constant("x", String)));             // range B (split candidate)
        b0.Add(new ExpressionStatement(new LoadStackSlot(Slot, String)));        // in-block range-B read
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
        var function = new IrFunction("M", Owner, signature, [], container);
        new StackSlotLiveRangePass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
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
}
