using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// #1421: ConstructorChainArgumentPass inlines the contiguous run of single-use
// argument spills preceding a base/this chain call. It must not do so when the
// call consumes those spills out of store order, or effectful argument
// computations get reordered while the decompiler still reports the raised form.
public class ConstructorChainArgumentOrderTests
{
    static readonly TypeRef Derived = TypeRef.CoreLib("System", "MyDerived");
    static readonly TypeRef Base = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly FieldRef StaticField = new(Derived, "F", Int32);

    static MethodRef Effect(string name) => new(Derived, name, Int32, [], HasThis: false);

    static IrFunction BuildCtor(int argCount, params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        var parameterTypes = Enumerable.Repeat(Int32, argCount).ToArray();
        var signature = new MethodSignature(Void, [], HasThis: true, GenericParameterCount: 0);
        return new IrFunction(".ctor", Derived, signature, [], container) { BaseType = Base };
    }

    static Call ChainCall(int argCount, params IrExpression[] args)
    {
        var ctor = new MethodRef(Base, ".ctor", Void, [.. Enumerable.Repeat(Int32, argCount)], HasThis: true);
        return new Call(ctor, isVirtual: false, [new LoadArgument(0, "this", Derived), .. args]);
    }

    static void RunPass(IrFunction function) => new ConstructorChainArgumentPass().Run(function, PassContext.None);

    static int StackStores(IrFunction function) => function.Descendants.OfType<StoreStackSlot>().Count();

    // Reversed load order: stores A() then B(), call consumes B() then A().
    // Inlining would evaluate B() before A() — decline and keep the spills.
    [Fact]
    public void ReversedSpillOrder_DoesNotInline()
    {
        var call = ChainCall(2, new LoadStackSlot(1, Int32), new LoadStackSlot(0, Int32));
        var function = BuildCtor(
            2,
            new StoreStackSlot(0, new Call(Effect("A"), isVirtual: false, [])),
            new StoreStackSlot(1, new Call(Effect("B"), isVirtual: false, [])),
            new ExpressionStatement(call));

        RunPass(function);

        Assert.Equal(2, StackStores(function));
        function.CheckInvariant();
    }

    // Store order matches argument order: both effectful spills inline cleanly.
    [Fact]
    public void StoreOrderMatchesArgumentOrder_Inlines()
    {
        var call = ChainCall(2, new LoadStackSlot(0, Int32), new LoadStackSlot(1, Int32));
        var function = BuildCtor(
            2,
            new StoreStackSlot(0, new Call(Effect("A"), isVirtual: false, [])),
            new StoreStackSlot(1, new Call(Effect("B"), isVirtual: false, [])),
            new ExpressionStatement(call));

        RunPass(function);

        Assert.Equal(0, StackStores(function));
        // Left-to-right: A() then B(), preserving the original store order.
        Assert.Equal("A", ((Call)call.Arguments[1]).Callee.Name);
        Assert.Equal("B", ((Call)call.Arguments[2]).Callee.Name);
        function.CheckInvariant();
    }

    // A single effectful spill is always order-safe and still inlines.
    [Fact]
    public void SingleEffectfulSpill_Inlines()
    {
        var call = ChainCall(1, new LoadStackSlot(0, Int32));
        var function = BuildCtor(
            1,
            new StoreStackSlot(0, new Call(Effect("A"), isVirtual: false, [])),
            new ExpressionStatement(call));

        RunPass(function);

        Assert.Equal(0, StackStores(function));
        function.CheckInvariant();
    }

    // Hazard: an inline effectful argument sits left of the spill's slot. The
    // spill was stored before the call, so inlining it to the right of that
    // argument would evaluate the inline effect first — decline.
    [Fact]
    public void EffectfulInlineArgumentLeftOfSpill_DoesNotInline()
    {
        var call = ChainCall(2, new Call(Effect("B"), isVirtual: false, []), new LoadStackSlot(0, Int32));
        var function = BuildCtor(
            2,
            new StoreStackSlot(0, new Call(Effect("C"), isVirtual: false, [])),
            new ExpressionStatement(call));

        RunPass(function);

        Assert.Equal(1, StackStores(function));
        function.CheckInvariant();
    }

    // Reorder-trivial spills (constants) read nothing and have no effect, so a
    // reversed pure shape still inlines — only order-sensitive values are gated.
    [Fact]
    public void ReversedTrivialSpills_StillInline()
    {
        var call = ChainCall(2, new LoadStackSlot(1, Int32), new LoadStackSlot(0, Int32));
        var function = BuildCtor(
            2,
            new StoreStackSlot(0, new Constant(1, Int32)),
            new StoreStackSlot(1, new Constant(2, Int32)),
            new ExpressionStatement(call));

        RunPass(function);

        Assert.Equal(0, StackStores(function));
        function.CheckInvariant();
    }

    // A pure-looking read (LoadField) stored after an effectful spill must not be
    // inlined to the left of that effect: it would observe state from before the
    // effect ran. Gated by reorder-triviality, not just observable effect.
    [Fact]
    public void ReadStoredAfterEffect_ConsumedLeftOfIt_DoesNotInline()
    {
        // V_0 = Mutate();  V_1 = this.F;  base(this, V_1, V_0)
        // store order: Mutate (slot 0), read F (slot 1); call loads them reversed.
        var call = ChainCall(2, new LoadStackSlot(1, Int32), new LoadStackSlot(0, Int32));
        var function = BuildCtor(
            2,
            new StoreStackSlot(0, new Call(Effect("Mutate"), isVirtual: false, [])),
            new StoreStackSlot(1, new LoadField(StaticField, instance: null)),
            new ExpressionStatement(call));

        RunPass(function);

        Assert.Equal(2, StackStores(function));
        function.CheckInvariant();
    }

    // Two effectful spills loaded reversed *inside one argument expression*
    // (F(B, A)) must also decline — order is modeled by evaluation rank, not just
    // the top-level argument index.
    [Fact]
    public void ReversedSpillsNestedInOneArgument_DoesNotInline()
    {
        var combine = new MethodRef(Derived, "F", Int32, [Int32, Int32], HasThis: false);
        var call = ChainCall(1, new Call(combine, isVirtual: false, [new LoadStackSlot(1, Int32), new LoadStackSlot(0, Int32)]));
        var function = BuildCtor(
            1,
            new StoreStackSlot(0, new Call(Effect("A"), isVirtual: false, [])),
            new StoreStackSlot(1, new Call(Effect("B"), isVirtual: false, [])),
            new ExpressionStatement(call));

        RunPass(function);

        Assert.Equal(2, StackStores(function));
        function.CheckInvariant();
    }

    // A raised property getter (LoadProperty) is an observable effect: an inline
    // getter left of a spill blocks inlining, even though it is not a Call node.
    [Fact]
    public void InlinePropertyGetterLeftOfSpill_DoesNotInline()
    {
        var getter = new MethodRef(Derived, "get_B", Int32, [], HasThis: false);
        var call = ChainCall(2, new LoadProperty(getter, instance: null, []), new LoadStackSlot(0, Int32));
        var function = BuildCtor(
            2,
            new StoreStackSlot(0, new LoadField(StaticField, instance: null)),
            new ExpressionStatement(call));

        RunPass(function);

        Assert.Equal(1, StackStores(function));
        function.CheckInvariant();
    }

    // A spill load inside a conditional (ternary) arm must not be inlined: it would
    // turn an unconditional pre-call store into conditionally-executed code.
    [Fact]
    public void SpillInConditionalArm_DoesNotInline()
    {
        var cond = new Comparison(ComparisonKind.Equal, isUnsigned: false, new LoadArgument(0, "this", Derived), new Constant(null, Derived));
        var ternary = new Conditional(cond, new LoadStackSlot(0, Int32), new Constant(0, Int32));
        var call = ChainCall(1, ternary);
        var function = BuildCtor(
            1,
            new StoreStackSlot(0, new Call(Effect("A"), isVirtual: false, [])),
            new ExpressionStatement(call));

        RunPass(function);

        Assert.Equal(1, StackStores(function));
        function.CheckInvariant();
    }

    // An inline place read (LoadField) left of an effectful spill that could mutate
    // it is an ordering barrier: inlining would read the field before the mutation.
    [Fact]
    public void InlineReadLeftOfEffectfulSpill_DoesNotInline()
    {
        var call = ChainCall(2, new LoadField(StaticField, instance: null), new LoadStackSlot(0, Int32));
        var function = BuildCtor(
            2,
            new StoreStackSlot(0, new Call(Effect("MutateF"), isVirtual: false, [])),
            new ExpressionStatement(call));

        RunPass(function);

        Assert.Equal(1, StackStores(function));
        function.CheckInvariant();
    }

}
