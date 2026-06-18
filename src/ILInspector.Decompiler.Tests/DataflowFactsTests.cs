using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class DataflowFactsTests
{
    // A diamond: the entry conditionally branches; only the fall-through arm
    // assigns V_0; both arms join at a block that reads V_0. V_0 is therefore
    // possibly-read-before-assigned and must keep `= default`. Built flat so the
    // printer's CFG dataflow (not the structured walk) analyzes it.
    static IrFunction Diamond()
    {
        var i32 = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();

        var entry = new Block(0x00);
        entry.Add(new ConditionalBranch(
            new Comparison(ComparisonKind.Equal, isUnsigned: false, new Constant(1, i32), new Constant(0, i32)),
            targetOffset: 0x10));

        var assign = new Block(0x08);
        assign.Add(new StoreLocal(0, i32, new Constant(7, i32)));
        assign.Add(new Branch(0x14));

        var skip = new Block(0x10);
        skip.Add(new Branch(0x14));

        var join = new Block(0x14);
        join.Add(new Return(new LoadLocal(0, i32)));

        container.Add(entry);
        container.Add(assign);
        container.Add(skip);
        container.Add(join);

        var signature = new MethodSignature(i32, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("Diamond", TypeRef.CoreLib("System", "Object"), signature, [i32], container);
    }

    [Fact]
    public void CollectDataflowFacts_RecordsTheCfgInOutSets()
    {
        var facts = CSharpPrinter.CollectDataflowFacts(Diamond());

        Assert.False(facts.Bailed);
        Assert.Equal(["V_0"], facts.LocalNames);

        // V_0 is genned on only one arm, so the join drops it: read-before-assign.
        Assert.Equal([0], facts.ReadBeforeAssign);

        var container = Assert.Single(facts.Containers);
        Assert.Equal(4, container.Blocks.Count);

        var assign = Assert.Single(container.Blocks, b => b.Offset == 0x08);
        Assert.Equal([0], assign.Gen);
        Assert.Empty(assign.In);
        Assert.Equal([0], assign.Out);

        var join = Assert.Single(container.Blocks, b => b.Offset == 0x14);
        Assert.Equal([0x08, 0x10], join.Predecessors);
        // The intersection over predecessors keeps only locals assigned on every
        // path — V_0 is not — so it is absent from the join's in-set.
        Assert.Empty(join.In);
    }

    [Fact]
    public void CollectDataflowFacts_BareSafeLocalIsNotReadBeforeAssign()
    {
        // Same shape, but the read happens after a block that assigns on both
        // arms: drop the conditional skip's bypass by assigning in it too.
        var i32 = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();

        var entry = new Block(0x00);
        entry.Add(new ConditionalBranch(
            new Comparison(ComparisonKind.Equal, isUnsigned: false, new Constant(1, i32), new Constant(0, i32)),
            targetOffset: 0x10));

        var assign = new Block(0x08);
        assign.Add(new StoreLocal(0, i32, new Constant(7, i32)));
        assign.Add(new Branch(0x14));

        var alsoAssign = new Block(0x10);
        alsoAssign.Add(new StoreLocal(0, i32, new Constant(9, i32)));
        alsoAssign.Add(new Branch(0x14));

        var join = new Block(0x14);
        join.Add(new Return(new LoadLocal(0, i32)));

        container.Add(entry);
        container.Add(assign);
        container.Add(alsoAssign);
        container.Add(join);

        var signature = new MethodSignature(i32, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("BothArms", TypeRef.CoreLib("System", "Object"), signature, [i32], container);

        var facts = CSharpPrinter.CollectDataflowFacts(function);

        Assert.False(facts.Bailed);
        Assert.Empty(facts.ReadBeforeAssign);

        var join2 = Assert.Single(facts.Containers).Blocks.Single(b => b.Offset == 0x14);
        Assert.Equal([0], join2.In);  // assigned on every path, so definitely-assigned at the join
    }
}
