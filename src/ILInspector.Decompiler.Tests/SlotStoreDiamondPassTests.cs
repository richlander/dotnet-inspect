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

    static IrFunction TwoEffectfulPrefixDiamond(BinaryKind finalKind, bool loadTrueThenFalse)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var a = new MethodRef(owner, "A", Int32, [], HasThis: false);
        var b = new MethodRef(owner, "B", Int32, [], HasThis: false);

        var head = new Block(0);
        head.Add(new ConditionalBranch(new LoadArgument(0, "cond", Bool), 8));

        var falseArm = new Block(4);
        falseArm.Add(new StoreStackSlot(0, new Constant(0, Int32)));
        falseArm.Add(new Branch(12));

        // True arm: t1 = A(); t2 = B(); S = (t1 op t2) or (t2 op t1).
        var trueArm = new Block(8);
        trueArm.Add(new StoreStackSlot(1, new Call(a, isVirtual: false, [])));
        trueArm.Add(new StoreStackSlot(2, new Call(b, isVirtual: false, [])));
        var (left, right) = loadTrueThenFalse
            ? (1, 2)   // load t1 then t2 => load order == store order (safe)
            : (2, 1);  // load t2 then t1 => reversed (effect reorder)
        trueArm.Add(new StoreStackSlot(0, new Binary(
            finalKind, isChecked: false, isUnsigned: false,
            new LoadStackSlot(left, Int32), new LoadStackSlot(right, Int32))));
        trueArm.Add(new Branch(12));

        var merge = new Block(12);
        merge.Add(new Return(new LoadStackSlot(0, Int32)));

        var body = new BlockContainer();
        foreach (var block in (Block[])[head, falseArm, trueArm, merge])
            body.Add(block);

        return new IrFunction(
            "M",
            owner,
            new MethodSignature(Int32, [new Parameter("cond", Bool)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    [Fact]
    public void DeclinesWhenTwoEffectfulPrefixesAreConsumedInReverseOrder()
    {
        var function = TwoEffectfulPrefixDiamond(BinaryKind.Subtract, loadTrueThenFalse: false);

        new SlotStoreDiamondPass().Run(function, PassContext.None);

        // Folding would emit S = cond ? (B() - A()) : 0, reordering A() before B().
        Assert.Empty(function.Descendants.OfType<Conditional>());
        Assert.Equal(4, function.Body.Blocks.Count);
        Assert.Equal(2, function.Descendants.OfType<Call>().Count());
    }

    [Fact]
    public void FoldsWhenEffectfulPrefixesAreConsumedInStoreOrder()
    {
        var function = TwoEffectfulPrefixDiamond(BinaryKind.Subtract, loadTrueThenFalse: true);

        new SlotStoreDiamondPass().Run(function, PassContext.None);

        // S = cond ? (A() - B()) : 0 keeps A() before B(); ordered spill still folds.
        Assert.Single(function.Descendants.OfType<Conditional>());
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot is 1 or 2);
    }
}
