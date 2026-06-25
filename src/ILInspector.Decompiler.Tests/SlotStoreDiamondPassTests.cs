using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class SlotStoreDiamondPassTests
{
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");

    [Fact]
    public void FoldsNestedNullableBoolDiamondAheadOfSharedTrueArm()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var any = new MethodRef(owner, "Any", Bool, [Object, Object], HasThis: false);
        var body = new BlockContainer();
        var head = new Block(0);
        head.Add(new ConditionalBranch(new LoadArgument(0, "show", Bool), 16));
        var nullableHead = new Block(4);
        nullableHead.Add(new StoreStackSlot(0, new LoadArgument(1, "columns", Object)));
        nullableHead.Add(new ConditionalBranch(new LoadStackSlot(0, Object), 12));
        var nullArm = new Block(8);
        nullArm.Add(new StoreStackSlot(1, new Constant(false, Bool)));
        nullArm.Add(new Branch(20));
        var trueSetup = new Block(12);
        trueSetup.Add(new StoreStackSlot(2, new LoadArgument(2, "predicate", Object)));
        var trueValue = new Block(14);
        trueValue.Add(new StoreStackSlot(1, new Call(any, isVirtual: false, [new LoadStackSlot(0, Object), new LoadStackSlot(2, Object)])));
        trueValue.Add(new Branch(20));
        var sharedTrue = new Block(16);
        sharedTrue.Add(new StoreStackSlot(1, new Constant(true, Bool)));
        var merge = new Block(20);
        merge.Add(new ConditionalBranch(new LoadStackSlot(1, Bool), 28));
        var falseText = new Block(24);
        falseText.Add(new StoreStackSlot(3, new Constant(null, Object)));
        falseText.Add(new Branch(32));
        var trueText = new Block(28);
        trueText.Add(new StoreStackSlot(3, new LoadArgument(3, "summary", Object)));
        var done = new Block(32);
        done.Add(new Return(new LoadStackSlot(3, Object)));
        foreach (var block in (Block[])[head, nullableHead, nullArm, trueSetup, trueValue, sharedTrue, merge, falseText, trueText, done])
            body.Add(block);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(Object, [
                new Parameter("show", Bool),
                new Parameter("columns", Object),
                new Parameter("predicate", Object),
                new Parameter("summary", Object),
            ], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        var pass = new SlotStoreDiamondPass();
        pass.Run(function, PassContext.None);

        Assert.Equal(5, function.Body.Blocks.Count);
        Assert.Equal(2, function.Descendants.OfType<Conditional>().Count());
        Assert.Single(function.Descendants.OfType<Branch>());
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 2);
    }

    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    // Two effectful prefix stores whose loads in the final value are in store order:
    // the fold preserves evaluation order, so it must still raise.
    [Fact]
    public void FoldsTwoEffectfulPrefixesConsumedInStoreOrder()
    {
        var function = BuildEffectfulPrefixDiamond(reverseLoadOrder: false);

        var pass = new SlotStoreDiamondPass();
        pass.Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<Conditional>());
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot is 0 or 1);
    }

    // Same diamond, but the final value loads the two effectful prefixes in reverse
    // store order. Inlining would swap the side effects (A() then B() -> B() then A()),
    // so the pass must decline and leave the flat diamond intact.
    [Fact]
    public void DeclinesWhenEffectfulPrefixesReorder()
    {
        var function = BuildEffectfulPrefixDiamond(reverseLoadOrder: true);

        var pass = new SlotStoreDiamondPass();
        pass.Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Conditional>());
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 0);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    // A prefix value itself loads an earlier effectful prefix in reversed order:
    // t0 = A(); t1 = B() - t0; S = t1. Inlining yields S = B() - A(), swapping the
    // effects. The recursive order check must expand the nested prefix load and decline.
    [Fact]
    public void DeclinesWhenNestedPrefixReordersEffects()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var a = new MethodRef(owner, "A", Int32, [], HasThis: false);
        var b = new MethodRef(owner, "B", Int32, [], HasThis: false);
        var nestedValue = new Binary(
            BinaryKind.Subtract, isChecked: false, isUnsigned: false,
            new Call(b, isVirtual: false, []),
            new LoadStackSlot(0, Int32));
        var function = BuildPrefixDiamond(owner,
            new StoreStackSlot(0, new Call(a, isVirtual: false, [])),
            new StoreStackSlot(1, nestedValue),
            new LoadStackSlot(1, Int32));

        var pass = new SlotStoreDiamondPass();
        pass.Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Conditional>());
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    // The final value places effectful prefixes inside a conditionally-evaluated
    // construct (`t0 ?? t1`). Inlining would make B() conditional (only when A() is
    // null), changing whether it runs at all, so the pass must decline.
    [Fact]
    public void DeclinesWhenEffectfulPrefixFeedsConditionalEvaluation()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var a = new MethodRef(owner, "A", Int32, [], HasThis: false);
        var b = new MethodRef(owner, "B", Int32, [], HasThis: false);
        var coalesce = new Coalesce(new LoadStackSlot(0, Int32), new LoadStackSlot(1, Int32));
        var function = BuildPrefixDiamond(owner,
            new StoreStackSlot(0, new Call(a, isVirtual: false, [])),
            new StoreStackSlot(1, new Call(b, isVirtual: false, [])),
            coalesce);

        var pass = new SlotStoreDiamondPass();
        pass.Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Conditional>());
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 0);
    }

    static IrFunction BuildEffectfulPrefixDiamond(bool reverseLoadOrder)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var a = new MethodRef(owner, "A", Int32, [], HasThis: false);
        var b = new MethodRef(owner, "B", Int32, [], HasThis: false);

        IrExpression left = new LoadStackSlot(reverseLoadOrder ? 1 : 0, Int32);
        IrExpression right = new LoadStackSlot(reverseLoadOrder ? 0 : 1, Int32);
        var finalValue = new Binary(BinaryKind.Subtract, isChecked: false, isUnsigned: false, left, right);

        return BuildPrefixDiamond(owner,
            new StoreStackSlot(0, new Call(a, isVirtual: false, [])),
            new StoreStackSlot(1, new Call(b, isVirtual: false, [])),
            finalValue);
    }

    // Builds a non-returning slot-store diamond whose true arm is the given prefix
    // stores followed by `S = finalValue`, and whose false arm is `S = 0`.
    static IrFunction BuildPrefixDiamond(TypeRef owner, StoreStackSlot prefix0, StoreStackSlot prefix1, IrExpression finalValue)
    {
        var body = new BlockContainer();
        var head = new Block(0);
        head.Add(new ConditionalBranch(new LoadArgument(0, "cond", Bool), 8));
        var falseArm = new Block(4);
        falseArm.Add(new StoreStackSlot(2, new Constant(0, Int32)));
        falseArm.Add(new Branch(20));
        var trueArm = new Block(8);
        trueArm.Add(prefix0);
        trueArm.Add(prefix1);
        trueArm.Add(new StoreStackSlot(2, finalValue));
        trueArm.Add(new Branch(20));
        var merge = new Block(20);
        merge.Add(new Return(new LoadStackSlot(2, Int32)));
        foreach (var block in (Block[])[head, falseArm, trueArm, merge])
            body.Add(block);

        return new IrFunction(
            "M",
            owner,
            new MethodSignature(Int32, [new Parameter("cond", Bool)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
