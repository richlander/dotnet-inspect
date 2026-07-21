using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// #2935: FluentChainRecompositionPass folds a spilled fluent call chain — each
// single-use scratch temp that holds a chain link (a call/property load) and
// feeds the receiver of the next chained call — back into one chain expression.
// It reuses ConstructorChainArgumentPass's effect-order proof, so it must make
// the fold only when it reorders no effect, and must leave a re-used receiver
// (e.g. a lock copy) or a non-chain-link head untouched.
public class FluentChainRecompositionPassTests
{
    static readonly TypeRef Chain = TypeRef.Definition("synthetic", "", "AssertionChain");
    static readonly TypeRef Holder = TypeRef.Definition("synthetic", "", "Holder");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Str = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    static readonly MethodRef Current = new(Chain, "Current", Chain, [], HasThis: false);
    static readonly MethodRef ForCondition = new(Chain, "ForCondition", Chain, [Bool], HasThis: true);
    static readonly MethodRef FailWith = new(Chain, "FailWith", Chain, [Str], HasThis: true);

    static Call Static(MethodRef method, params IrExpression[] args) => new(method, isVirtual: false, [.. args]);
    static Call Instance(MethodRef method, IrExpression receiver, params IrExpression[] args)
        => new(method, isVirtual: false, [receiver, .. args]);

    static IrFunction BuildMethod(params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Holder, signature, [], container);
    }

    static void RunPass(IrFunction function) => new FluentChainRecompositionPass().Run(function, PassContext.None);

    static int StackStores(IrFunction function) => function.Descendants.OfType<StoreStackSlot>().Count();

    // S0 = Current(); S1 = S0.ForCondition(cond); S1.FailWith(msg)
    // collapses to a single Current().ForCondition(cond).FailWith(msg) statement.
    [Fact]
    public void SpilledReceiverChain_IsRecomposed()
    {
        var function = BuildMethod(
            new StoreStackSlot(0, Static(Current)),
            new StoreStackSlot(1, Instance(ForCondition, new LoadStackSlot(0, Chain), new Constant(true, Bool))),
            new ExpressionStatement(Instance(FailWith, new LoadStackSlot(1, Chain), new Constant("m", Str))));

        RunPass(function);

        Assert.Equal(0, StackStores(function));
        var block = Assert.IsType<Block>(Assert.IsType<BlockContainer>(function.Body).Children[0]);
        var statement = Assert.IsType<ExpressionStatement>(block.Children[0]);
        var failWith = Assert.IsType<Call>(statement.Expression);
        Assert.Equal("FailWith", failWith.Callee.Name);
        var forCondition = Assert.IsType<Call>(failWith.Arguments[0]);
        Assert.Equal("ForCondition", forCondition.Callee.Name);
        var head = Assert.IsType<Call>(forCondition.Arguments[0]);
        Assert.Equal("Current", head.Callee.Name);
        function.CheckInvariant();
    }

    // A receiver temp read twice (the shape a lock/monitor copy or any shared
    // receiver takes) is not a single-use chain link: folding it would duplicate
    // its effect, so the spill must be preserved.
    [Fact]
    public void ReusedReceiverTemp_IsNotFolded()
    {
        var function = BuildMethod(
            new StoreStackSlot(0, Static(Current)),
            new ExpressionStatement(Instance(ForCondition, new LoadStackSlot(0, Chain), new Constant(true, Bool))),
            new ExpressionStatement(Instance(FailWith, new LoadStackSlot(0, Chain), new Constant("m", Str))));

        RunPass(function);

        Assert.Equal(1, StackStores(function));
        function.CheckInvariant();
    }

    // The spilled head must itself be a chain link (a call/property load). A temp
    // holding an ordinary value — here a constant — is a live local the general
    // inliner owns, not a fluent-chain head, so the pass leaves it alone.
    [Fact]
    public void NonChainLinkHead_IsNotFolded()
    {
        var toString = new MethodRef(Int32, "ToString", Str, [], HasThis: true);
        var function = BuildMethod(
            new StoreStackSlot(0, new Constant(5, Int32)),
            new ExpressionStatement(Instance(toString, new LoadStackSlot(0, Int32))));

        RunPass(function);

        Assert.Equal(1, StackStores(function));
        function.CheckInvariant();
    }

    // Effect-order safety: when the sink consumes an argument spill out of store
    // order, folding would reorder the argument effects. The shared effect-order
    // proof must decline the fold and keep every spill.
    [Fact]
    public void ReorderingArgumentSpill_IsNotFolded()
    {
        var effect = new MethodRef(Holder, "Effect", Str, [], HasThis: false);
        var twoArg = new MethodRef(Chain, "TwoArg", Chain, [Str, Str], HasThis: true);
        var function = BuildMethod(
            new StoreStackSlot(0, Static(Current)),
            new StoreStackSlot(1, Static(effect)),
            new StoreStackSlot(2, Static(effect)),
            // Consumes the argument spills reversed: slot 2 before slot 1.
            new ExpressionStatement(Instance(
                twoArg,
                new LoadStackSlot(0, Chain),
                new LoadStackSlot(2, Str),
                new LoadStackSlot(1, Str))));

        RunPass(function);

        Assert.Equal(3, StackStores(function));
        function.CheckInvariant();
    }
}
