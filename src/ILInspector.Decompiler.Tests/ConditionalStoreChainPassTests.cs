using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ConditionalStoreChainPassTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    // The dispatch-first switch-store shape csc emits for
    //   switch (n) { case 0: x = 10; break; case 1: x = 20; break; default: x = 30; break; }
    //   <continuation that reads x>
    // Guards first (each tests n and jumps to a separated arm), then the arms
    // (each `x = const; goto MERGE`), then the merge.
    static IrFunction BuildThreeWaySwitchStore(int armBlockStatements = 1)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var body = new BlockContainer();

        var d0 = new Block(0);
        d0.Add(new ConditionalBranch(Eq(0), 12));          // case 0 -> arm0
        var d1 = new Block(4);
        d1.Add(new ConditionalBranch(Eq(1), 16));          // case 1 -> arm1
        var d2 = new Block(8);
        d2.Add(new Branch(20));                             // default -> arm2

        var arm0 = new Block(12);
        arm0.Add(new StoreLocal(0, Int32, new Constant(10, Int32)));
        if (armBlockStatements > 1)
            arm0.Add(new StoreLocal(1, Int32, new Constant(99, Int32)));  // extra statement
        arm0.Add(new Branch(24));
        var arm1 = new Block(16);
        arm1.Add(new StoreLocal(0, Int32, new Constant(20, Int32)));
        arm1.Add(new Branch(24));
        var arm2 = new Block(20);
        arm2.Add(new StoreLocal(0, Int32, new Constant(30, Int32)));      // last arm falls into the merge

        var merge = new Block(24);
        merge.Add(new Return(new LoadLocal(0, Int32)));

        foreach (var block in (Block[])[d0, d1, d2, arm0, arm1, arm2, merge])
            body.Add(block);

        return Function(owner, body);
    }

    // A two-arm if/else store: csc interleaves guard+arm and the structurer already
    // recovers it as if/else, so this pass must leave it untouched (no ternary).
    static IrFunction BuildTwoWayStore()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var body = new BlockContainer();

        var d0 = new Block(0);
        d0.Add(new ConditionalBranch(Eq(0), 8));           // case 0 -> arm0
        var arm1 = new Block(4);
        arm1.Add(new StoreLocal(0, Int32, new Constant(20, Int32)));
        arm1.Add(new Branch(12));
        var arm0 = new Block(8);
        arm0.Add(new StoreLocal(0, Int32, new Constant(10, Int32)));
        var merge = new Block(12);
        merge.Add(new Return(new LoadLocal(0, Int32)));

        foreach (var block in (Block[])[d0, arm1, arm0, merge])
            body.Add(block);

        return Function(owner, body);
    }

    [Fact]
    public void FoldsThreeWaySwitchStoreIntoNestedConditional()
    {
        var function = BuildThreeWaySwitchStore();

        new ConditionalStoreChainPass().Run(function, PassContext.None);

        // The dispatch and arm blocks collapse into the head; only the head and the
        // merge survive, and the store value is a nested conditional.
        Assert.Equal(2, function.Body.Blocks.Count);
        Assert.Equal(2, function.Descendants.OfType<Conditional>().Count());
        var store = Assert.Single(
            function.Descendants.OfType<StoreLocal>(),
            s => s.Index == 0 && s.Value is Conditional);
        Assert.IsType<Conditional>(store.Value);
        // No residual goto ladder.
        Assert.Empty(function.Descendants.OfType<Branch>());
    }

    [Fact]
    public void DeclinesTwoArmStore()
    {
        var function = BuildTwoWayStore();

        new ConditionalStoreChainPass().Run(function, PassContext.None);

        // Fewer than three arms: leave it for the structurer's if/else recovery.
        Assert.Empty(function.Descendants.OfType<Conditional>());
        Assert.Equal(4, function.Body.Blocks.Count);
    }

    [Fact]
    public void DeclinesWhenArmDoesMoreThanStore()
    {
        var function = BuildThreeWaySwitchStore(armBlockStatements: 2);

        new ConditionalStoreChainPass().Run(function, PassContext.None);

        // An arm that carries a second store is not a clean value select; decline.
        Assert.Empty(function.Descendants.OfType<Conditional>());
        Assert.Equal(7, function.Body.Blocks.Count);
    }

    static IrExpression Eq(int constant)
        => new Comparison(ComparisonKind.Equal, isUnsigned: false, new LoadLocal(2, Int32), new Constant(constant, Int32));

    static IrFunction Function(TypeRef owner, BlockContainer body)
        => new(
            "M",
            owner,
            new MethodSignature(Int32, [new Parameter("n", Int32)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
}
