using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ReturnSinkingPassTests
{
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");

    [Fact]
    public void ExplicitElseBoolAccumulator_StaysAccumulatorForFidelity()
    {
        var condition = new LoadArgument(0, "condition", Bool);
        var thenBlock = new Block(1);
        thenBlock.Add(new StoreLocal(0, Bool, new Constant(true, Bool)));
        var elseBlock = new Block(2);
        elseBlock.Add(new StoreLocal(0, Bool, new Constant(false, Bool)));
        var entry = new Block(0);
        entry.Add(new IfStatement(condition, thenBlock, elseBlock));
        entry.Add(new Return(new LoadLocal(0, Bool)));
        var container = new BlockContainer();
        container.Add(entry);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Object"),
            new MethodSignature(Bool, [new Parameter("condition", Bool)], HasThis: false, GenericParameterCount: 0),
            [Bool],
            container);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Contains(function.Descendants.OfType<IfStatement>(), _ => true);
        Assert.Equal(2, function.Descendants.OfType<StoreLocal>().Count());
        var ret = Assert.Single(function.Body.Blocks[0].Children.OfType<Return>());
        Assert.IsType<LoadLocal>(ret.Value);
        function.CheckInvariant();
    }

    [Fact]
    public void ExplicitElseInvertedBoolAccumulator_StaysAccumulatorForFidelity()
    {
        var condition = new LoadArgument(0, "condition", Bool);
        var thenBlock = new Block(1);
        thenBlock.Add(new StoreLocal(0, Bool, new Constant(false, Bool)));
        var elseBlock = new Block(2);
        elseBlock.Add(new StoreLocal(0, Bool, new Constant(true, Bool)));
        var entry = new Block(0);
        entry.Add(new IfStatement(condition, thenBlock, elseBlock));
        entry.Add(new Return(new LoadLocal(0, Bool)));
        var container = new BlockContainer();
        container.Add(entry);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Object"),
            new MethodSignature(Bool, [new Parameter("condition", Bool)], HasThis: false, GenericParameterCount: 0),
            [Bool],
            container);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Contains(function.Descendants.OfType<IfStatement>(), _ => true);
        Assert.Equal(2, function.Descendants.OfType<StoreLocal>().Count());
        var ret = Assert.Single(function.Body.Blocks[0].Children.OfType<Return>());
        Assert.IsType<LoadLocal>(ret.Value);
        function.CheckInvariant();
    }

    [Fact]
    public void ExplicitElseWithPriorArmStatement_StaysStructured()
    {
        var thenBlock = new Block(1);
        thenBlock.Add(new ExpressionStatement(new Call(
            new MethodRef(TypeRef.CoreLib("System", "GC"), "KeepAlive", TypeRef.CoreLib("System", "Void"), [TypeRef.CoreLib("System", "Object")], HasThis: false),
            isVirtual: false,
            [new Constant(null, TypeRef.CoreLib("System", "Object"))])));
        thenBlock.Add(new StoreLocal(0, Bool, new Constant(true, Bool)));
        var elseBlock = new Block(2);
        elseBlock.Add(new StoreLocal(0, Bool, new Constant(false, Bool)));
        var entry = new Block(0);
        entry.Add(new IfStatement(new LoadArgument(0, "condition", Bool), thenBlock, elseBlock));
        entry.Add(new Return(new LoadLocal(0, Bool)));
        var container = new BlockContainer();
        container.Add(entry);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Object"),
            new MethodSignature(Bool, [new Parameter("condition", Bool)], HasThis: false, GenericParameterCount: 0),
            [Bool],
            container);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Contains(function.Descendants.OfType<IfStatement>(), _ => true);
        Assert.Contains(function.Descendants.OfType<Return>(), r => r.Value is Constant { Value: true });
        function.CheckInvariant();
    }

    // --- Adversarial near-miss coverage (issue #1289) -----------------------
    //
    // The pass is sound by construction only when it consumes every store and
    // every load of an accumulator local (all-or-nothing). The fixtures below
    // pin the documented decline families — escaping (non-return) load,
    // partial-store consumption, address-taken, branch-target store, and the two
    // switch forms — and a positive control proves the negatives are not passing
    // vacuously. If any "stays" fixture begins to sink, the all-or-nothing
    // guarantee has regressed.

    static IrFunction Function(BlockContainer body, params TypeRef[] locals) =>
        new(
            "M",
            Object,
            new MethodSignature(Int32, [new Parameter("x", Int32)], HasThis: false, GenericParameterCount: 0),
            [.. locals],
            body);

    static BlockContainer Container(params Block[] blocks)
    {
        var container = new BlockContainer();
        foreach (var block in blocks)
            container.Add(block);
        return container;
    }

    static ExpressionStatement Use(IrExpression value) =>
        new(new Call(
            new MethodRef(TypeRef.CoreLib("System", "GC"), "KeepAlive", Void, [Object], HasThis: false),
            isVirtual: false,
            [value]));

    static int StoreCount(IrFunction function, int index) =>
        function.Descendants.OfType<StoreLocal>().Count(s => s.Index == index);

    // Positive control: a pure return accumulator spilled across a try/finally is
    // sunk — the store becomes the try body's return and the trailing temp load
    // disappears. This is the shape every negative below perturbs by one feature.
    [Fact]
    public void TryFinallyAccumulator_Sinks()
    {
        var tryBody = Container(Block(new StoreLocal(0, Int32, new LoadArgument(0, "x", Int32))));
        var finallyBody = Container(Block(Use(new Constant(null, Object))));
        var entry = Block(new TryFinally(tryBody, finallyBody), new Return(new LoadLocal(0, Int32)));
        var function = Function(Container(entry), Int32);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Equal(0, StoreCount(function, 0));
        var ret = Assert.Single(tryBody.Blocks[0].Children.OfType<Return>());
        Assert.IsType<LoadArgument>(ret.Value);
        function.CheckInvariant();
    }

    // Escaping load: the accumulator's value is also read inside the finally, so
    // sinking the store would leave that read bound to an undefined local. The
    // non-return load must keep the whole candidate standing.
    [Fact]
    public void NonReturnLoadInFinally_StaysAccumulator()
    {
        var tryBody = Container(Block(new StoreLocal(0, Int32, new LoadArgument(0, "x", Int32))));
        var finallyBody = Container(Block(Use(new LoadLocal(0, Int32))));
        var entry = Block(new TryFinally(tryBody, finallyBody), new Return(new LoadLocal(0, Int32)));
        var function = Function(Container(entry), Int32);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Equal(1, StoreCount(function, 0));
        var ret = Assert.Single(entry.Children.OfType<Return>());
        Assert.IsType<LoadLocal>(ret.Value);
        function.CheckInvariant();
    }

    // Partial consumption: a second, non-tail store of the same local cannot be
    // folded, so the plan does not consume every store and must decline rather
    // than sink only the adjacent one (which would strand the other store).
    [Fact]
    public void UnconsumedSecondStore_StaysAccumulator()
    {
        var entry = Block(
            new StoreLocal(0, Int32, new Constant(1, Int32)),
            Use(new Constant(null, Object)),
            new StoreLocal(0, Int32, new LoadArgument(0, "x", Int32)),
            new Return(new LoadLocal(0, Int32)));
        var function = Function(Container(entry), Int32);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Equal(2, StoreCount(function, 0));
        var ret = Assert.Single(entry.Children.OfType<Return>());
        Assert.IsType<LoadLocal>(ret.Value);
        function.CheckInvariant();
    }

    // Address-taken: the accumulator's address escapes into the finally, so the
    // store is not safely removable even though every load is a return.
    [Fact]
    public void AddressTakenLocal_StaysAccumulator()
    {
        var tryBody = Container(Block(new StoreLocal(0, Int32, new LoadArgument(0, "x", Int32))));
        var finallyBody = Container(Block(Use(new LoadLocalAddress(0, Int32))));
        var entry = Block(new TryFinally(tryBody, finallyBody), new Return(new LoadLocal(0, Int32)));
        var function = Function(Container(entry), Int32);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Equal(1, StoreCount(function, 0));
        function.CheckInvariant();
    }

    // Branch-target store: a residual branch jumps to the block that holds the
    // fold store, so rewriting that store into a return would change which code
    // the branch reaches. The branch-target guard must keep it standing.
    [Fact]
    public void BranchTargetStoreBlock_StaysAccumulator()
    {
        const int targetOffset = 0x20;
        var tryBody = Container(Block(targetOffset, new StoreLocal(0, Int32, new LoadArgument(0, "x", Int32))));
        var finallyBody = Container(Block(new Branch(targetOffset)));
        var entry = Block(new TryFinally(tryBody, finallyBody), new Return(new LoadLocal(0, Int32)));
        var function = Function(Container(entry), Int32);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Equal(1, StoreCount(function, 0));
    }

    // SwitchBranch present: a value-producing jump table elsewhere in the method
    // means a switch expression's own accumulator may be in play, so the pass
    // bails entirely rather than risk sinking it.
    [Fact]
    public void SwitchBranchInMethod_DisablesSinking()
    {
        var entry = Block(
            new StoreLocal(0, Int32, new LoadArgument(0, "x", Int32)),
            new Return(new LoadLocal(0, Int32)));
        var dispatch = Block(new SwitchBranch(new LoadArgument(0, "x", Int32), ImmutableArray.Create(0x10)));
        var function = Function(Container(entry, dispatch), Int32);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Equal(1, StoreCount(function, 0));
    }

    // Switch statement tail: a trailing return after a switch statement whose
    // arms store the accumulator must not be sunk into per-arm returns, because
    // csc lowers switch expressions through their own result accumulator.
    [Fact]
    public void SwitchStatementArms_StayAccumulator()
    {
        var case0 = new SwitchSection(
            ImmutableArray.Create(new Constant(0, Int32)),
            isDefault: false,
            Container(Block(new StoreLocal(0, Int32, new Constant(10, Int32)), new Break())));
        var defaultSection = new SwitchSection(
            ImmutableArray<Constant>.Empty,
            isDefault: true,
            Container(Block(new StoreLocal(0, Int32, new Constant(20, Int32)), new Break())));
        var entry = Block(
            new Switch(new LoadArgument(0, "x", Int32), [case0, defaultSection]),
            new Return(new LoadLocal(0, Int32)));
        var function = Function(Container(entry), Int32);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Equal(2, StoreCount(function, 0));
        var ret = Assert.Single(entry.Children.OfType<Return>());
        Assert.IsType<LoadLocal>(ret.Value);
    }

    static Block Block(int startOffset, params IrNode[] statements)
    {
        var block = new Block(startOffset);
        foreach (var statement in statements)
            block.Add(statement);
        return block;
    }

    static Block Block(params IrNode[] statements) => Block(0, statements);
}
