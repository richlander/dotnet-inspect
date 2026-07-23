using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// #3051: StructReceiverInliningPass folds a single-use struct rvalue temporary
// back into its member-access receiver (V = a.Prop; ... V.Member -> a.Prop.Member)
// so the guard block the spill sat in becomes a pure condition that structuring
// can nest and boolean folding can recompose into one && return.
//
// The pass is gated for soundness: the definition must be a fresh rvalue (a
// call/property load, never an address/place), and the slot must be fold-safe
// method-wide — every read of it is a member-receiver address load whose
// statement immediately follows a store to the same slot in the same block, so
// each read's reaching definition is the adjacent store and the slot is never
// live across a block boundary. The folded store's use must additionally be the
// slot's only read in the adjacent statement with no order-sensitive node
// evaluated before it. Opcode-preservation of the fold is proven by the
// compiled-fixture round-trip described at the end of this file.
[Trait("Area", "Pass")]
public class StructReceiverInliningPassTests
{
    static readonly TypeRef Holder = TypeRef.Definition("synthetic", "", "Holder");
    static readonly TypeRef Outer = TypeRef.Definition("synthetic", "", "Outer");
    static readonly TypeRef S = TypeRef.Definition("synthetic", "", "S");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");

    // Outer.Inner is a struct-valued property; S.Value is a member read on that struct.
    static readonly MethodRef GetInner = new(Outer, "get_Inner", S, [], HasThis: true);
    static readonly MethodRef GetValue = new(S, "get_Value", Int32, [], HasThis: true);

    static readonly FieldRef InnerField = new(Outer, "InnerField", S);

    static LoadProperty Inner(IrExpression receiver) => new(GetInner, receiver, []);
    static LoadProperty Value(IrExpression receiver) => new(GetValue, receiver, []);

    static IrFunction BuildFunction(params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Holder, signature, [], container);
    }

    static IrFunction BuildFunction(Block[] blocks)
    {
        var container = new BlockContainer();
        foreach (var block in blocks)
            container.Add(block);
        var signature = new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Holder, signature, [], container);
    }

    static Block MakeBlock(int offset, params IrNode[] statements)
    {
        var block = new Block(offset);
        foreach (var statement in statements)
            block.Add(statement);
        return block;
    }

    static void RunPass(IrFunction function) => new StructReceiverInliningPass().Run(function, PassContext.None);

    static int StoreCount(IrFunction function, int index)
        => function.Descendants.OfType<StoreLocal>().Count(s => s.Index == index);

    // V_0 = outer.Inner; return V_0.Value  ->  return outer.Inner.Value
    // The spilled struct rvalue folds into the member receiver and the store is gone.
    [Fact]
    public void StructRvalueReceiver_IsFoldedIntoMemberReceiver()
    {
        var function = BuildFunction(
            new StoreLocal(0, S, Inner(new LoadArgument(0, "outer", Outer))),
            new Return(Value(new LoadLocalAddress(0, S))));

        RunPass(function);

        Assert.Equal(0, StoreCount(function, 0));
        var block = Assert.IsType<Block>(Assert.IsType<BlockContainer>(function.Body).Children[0]);
        var ret = Assert.IsType<Return>(Assert.Single(block.Children));
        var value = Assert.IsType<LoadProperty>(ret.Value);
        Assert.Equal("Value", value.PropertyName);
        // The receiver is now the struct rvalue directly, not a spilled local address.
        var inner = Assert.IsType<LoadProperty>(value.Instance);
        Assert.Equal("Inner", inner.PropertyName);
        function.CheckInvariant();
    }

    // A slot csc reused for two independent struct temporaries folds per live
    // range: each store is adjacent to its own single receiver use, so both fold
    // and the reused local is eliminated entirely.
    [Fact]
    public void ReusedSlot_FoldsEachLiveRange()
    {
        var block0 = MakeBlock(0,
            new StoreLocal(0, S, Inner(new LoadArgument(0, "a", Outer))),
            new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, Value(new LoadLocalAddress(0, S)), new Constant(1, Int32)), 99));
        var block1 = MakeBlock(50,
            new StoreLocal(0, S, Inner(new LoadArgument(1, "b", Outer))),
            new Return(Value(new LoadLocalAddress(0, S))));
        var function = BuildFunction([block0, block1]);

        RunPass(function);

        Assert.Equal(0, StoreCount(function, 0));
        // Both receivers are now the folded Inner rvalue.
        var loads = function.Descendants.OfType<LoadProperty>().Where(p => p.PropertyName == "Value").ToList();
        Assert.Equal(2, loads.Count);
        Assert.All(loads, v => Assert.IsType<LoadProperty>(v.Instance));
        Assert.Empty(function.Descendants.OfType<LoadLocalAddress>());
        function.CheckInvariant();
    }

    // Two reads of the temp in one live range is not a single-use receiver: the
    // fold would remove a store a second reader still needs. Both reads survive.
    [Fact]
    public void MultiUseTemp_IsNotFolded()
    {
        var twoArg = new MethodRef(Holder, "Combine", Int32, [Int32, Int32], HasThis: false);
        var function = BuildFunction(
            new StoreLocal(0, S, Inner(new LoadArgument(0, "outer", Outer))),
            new Return(new Call(twoArg, isVirtual: false,
            [
                Value(new LoadLocalAddress(0, S)),
                Value(new LoadLocalAddress(0, S)),
            ])));

        RunPass(function);

        Assert.Equal(1, StoreCount(function, 0));
        function.CheckInvariant();
    }

    // The address feeds a by-ref argument of a static call, not a member
    // receiver. Folding an rvalue there would be an illegal `ref (rvalue)`.
    [Fact]
    public void AddressAsByRefArgument_IsNotFolded()
    {
        var consume = new MethodRef(Holder, "Consume", Int32, [TypeRef.ByRef(S)], HasThis: false);
        var function = BuildFunction(
            new StoreLocal(0, S, Inner(new LoadArgument(0, "outer", Outer))),
            new Return(new Call(consume, isVirtual: false, [new LoadLocalAddress(0, S)])));

        RunPass(function);

        Assert.Equal(1, StoreCount(function, 0));
        function.CheckInvariant();
    }

    // The definition is a field access (a place/lvalue), not a fresh rvalue: a
    // mutating member folded onto it would act on real storage. Declined.
    [Fact]
    public void PlaceDefinition_IsNotFolded()
    {
        var function = BuildFunction(
            new StoreLocal(0, S, new LoadField(InnerField, new LoadArgument(0, "outer", Outer))),
            new Return(Value(new LoadLocalAddress(0, S))));

        RunPass(function);

        Assert.Equal(1, StoreCount(function, 0));
        function.CheckInvariant();
    }

    // An effectful sibling evaluates before the receiver in the use statement, so
    // moving the store into the receiver position would reorder it past that
    // effect. The barrier walk declines the fold.
    [Fact]
    public void BarrierBeforeReceiver_IsNotFolded()
    {
        var effect = new Call(new MethodRef(Holder, "Effect", Int32, [], HasThis: false), isVirtual: false, []);
        var function = BuildFunction(
            new StoreLocal(0, S, Inner(new LoadArgument(0, "outer", Outer))),
            // Left operand (an effectful call) evaluates before the receiver read.
            new Return(new Comparison(ComparisonKind.Equal, false, effect, Value(new LoadLocalAddress(0, S)))));

        RunPass(function);

        Assert.Equal(1, StoreCount(function, 0));
        function.CheckInvariant();
    }

    // An unrelated statement sits between the store and the use, so the store is
    // no longer the immediately-preceding definition. Adjacency fails; declined.
    [Fact]
    public void NonAdjacentStore_IsNotFolded()
    {
        var effect = new Call(new MethodRef(Holder, "Effect", Int32, [], HasThis: false), isVirtual: false, []);
        var function = BuildFunction(
            new StoreLocal(0, S, Inner(new LoadArgument(0, "outer", Outer))),
            new ExpressionStatement(effect),
            new Return(Value(new LoadLocalAddress(0, S))));

        RunPass(function);

        Assert.Equal(1, StoreCount(function, 0));
        function.CheckInvariant();
    }

    // A back-edge routes execution, on a later iteration, from the loop-body
    // store back to a read at the loop head that precedes the store in document
    // order. That head read is not immediately preceded by a store to the slot,
    // so the whole-slot fold-safety check fails and the fold declines — no
    // reasoning about document order or back-edge spans is required.
    [Fact]
    public void LoopCarriedReader_IsNotFolded()
    {
        // Head (offset 0): r = v.Value  — the loop-carried read of slot 0.
        var head = MakeBlock(0,
            new StoreLocal(1, Bool, Value(new LoadLocalAddress(0, S))));
        // Body (offset 16): v = outer.Inner; if (v.Value) goto head — store,
        // adjacent member-receiver use, and the back-edge to the head.
        var body = MakeBlock(16,
            new StoreLocal(0, S, Inner(new LoadArgument(0, "outer", Outer))),
            new ConditionalBranch(Value(new LoadLocalAddress(0, S)), 0));
        var function = BuildFunction([head, body]);

        RunPass(function);

        Assert.Equal(1, StoreCount(function, 0));
        function.CheckInvariant();
    }

    // Forward bypass: the store's adjacent use looks single, but a later read
    // sits after a conditionally-taken second store to the same slot. On the
    // path that skips the second store, that later read observes the first
    // store's value — so folding the first store away would leave it reading a
    // removed value. The later read is not immediately preceded by a store, so
    // the whole-slot check fails and both stores survive.
    [Fact]
    public void ForwardBypassReader_IsNotFolded()
    {
        // block0: v = a.Inner; v.Value; if (cond) goto block2 (skip the second store)
        var block0 = MakeBlock(0,
            new StoreLocal(0, S, Inner(new LoadArgument(0, "a", Outer))),
            new ExpressionStatement(Value(new LoadLocalAddress(0, S))),
            new ConditionalBranch(new LoadArgument(1, "cond", Bool), 32));
        // block1: v = b.Inner (the conditionally-executed redefinition)
        var block1 = MakeBlock(16,
            new StoreLocal(0, S, Inner(new LoadArgument(2, "b", Outer))));
        // block2: return v.Value — the join read, reached with block0's value on
        // the bypass path.
        var block2 = MakeBlock(32,
            new Return(Value(new LoadLocalAddress(0, S))));
        var function = BuildFunction([block0, block1, block2]);

        RunPass(function);

        Assert.Equal(2, StoreCount(function, 0));
        function.CheckInvariant();
    }
}
