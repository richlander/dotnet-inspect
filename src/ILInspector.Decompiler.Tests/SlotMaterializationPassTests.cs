using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class SlotMaterializationPassTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Char = TypeRef.CoreLib("System", "Char");
    static readonly TypeRef Action = TypeRef.CoreLib("System", "Action");
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "Owner");

    static IrFunction Function(ImmutableArray<TypeRef> locals, BlockContainer body)
        => new("M", Owner, new MethodSignature(TypeRef.CoreLib("System", "Void"), [
            new Parameter("x", Int32),
        ], HasThis: false, GenericParameterCount: 0), locals, body);

    // Adversarial review (5b-2, CRITICAL): a materialized store whose value
    // nests a load of a slot processed later. Cloning that store before the
    // nested load was replaced injected a fresh LoadStackSlot into the live
    // tree while the pass replaced the original inside the discarded subtree,
    // leaving the live one orphaned forever. The two-phase rewrite (all loads,
    // then all stores) retires every slot node.
    [Fact]
    public void MaterializesCoupledSlotsWithoutOrphaningNestedLoads()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        // Slot 0's first load appears before slot 256's, so slot 0 is
        // processed first — the ordering that triggered the orphan.
        block.Add(new StoreStackSlot(0, new Constant(1, Int32)));
        block.Add(new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)));
        block.Add(new StoreStackSlot(256, new LoadArgument(0, "x", Int32)));
        block.Add(new StoreStackSlot(0, new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false,
            new LoadStackSlot(256, Int32), new Constant(1, Int32))));
        block.Add(new StoreLocal(1, Int32, new LoadStackSlot(0, Int32)));
        body.Add(block);
        var function = Function([Int32, Int32], body);

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Equal(4, function.Locals.Length);
    }

    // Adversarial review (5b-2, HIGH): slot numbering restarts at 0 inside a
    // lambda, and a raised lambda prints through a fresh inner printer with no
    // view of the outer locals table — materializing outer S_0 next to a
    // residual inner S_0 is CS0136. The slot stays on the unifier.
    [Fact]
    public void DefersSlotWhoseNumberAppearsInNestedLambdaScope()
    {
        var lambdaBody = new BlockContainer();
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new StoreStackSlot(0, new Constant(2, Int32)));
        lambdaBlock.Add(new Return(new LoadStackSlot(0, Int32)));
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(Action, [], [], [],
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, lambdaBody);

        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Constant(1, Int32)));
        block.Add(new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)));
        block.Add(new StoreLocal(1, Action, lambda));
        body.Add(block);
        var function = Function([Int32, Action], body);

        new SlotMaterializationPass().Run(function, PassContext.None);

        // Outer slot 0 deferred; the lambda's slot 0 untouched.
        Assert.Equal(2, function.Descendants.OfType<StoreStackSlot>().Count());
        Assert.Equal(2, function.Locals.Length);
    }

    // Adversarial review (5b-2 round 2, blocking): a typed-int slot feeding a
    // char[] element store is the printer's #1751 identity recovery — the
    // slot RE-TYPES to char ('+' / '-'), which a materialized int local
    // forecloses. Slots whose element-store target disagrees with the
    // testified type stay on the unifier.
    [Fact]
    public void DefersSlotWhoseElementStoreTargetDisagreesWithTestimony()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Constant(43, Int32)));
        block.Add(new StoreElement(Char,
            new LoadArgument(0, "chars", TypeRef.SzArray(Char)),
            new Constant(0, Int32),
            new LoadStackSlot(0, Int32)));
        body.Add(block);
        var function = Function([], body);

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Locals);
    }

    // Adversarial review (5b-2, latent): LoadSinkTargetType omitted
    // StoreElement even though the sink model handles it — an untyped slot
    // load consumed by an array-element store contributed no testimony,
    // vetoing (or mis-typing) the slot. It now testifies the element type.
    [Fact]
    public void StoreElementSinkTestifiesElementType()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(1, new Constant('a', Char)));
        block.Add(new StoreElement(Char,
            new LoadArgument(0, "chars", TypeRef.SzArray(Char)),
            new Constant(0, Int32),
            new LoadStackSlot(1, type: null)));
        body.Add(block);

        var testified = CoercionSinks.TestifiedSlotTypes(
            body, returnType: null, ImmutableDictionary<TypeRef, TypeShape>.Empty);

        Assert.Equal(Char, testified[1]);
    }
}
