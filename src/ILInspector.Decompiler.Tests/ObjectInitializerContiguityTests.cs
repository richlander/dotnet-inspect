using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Adversarial guard for ObjectInitializerPass stack-slot contiguity (issue #1408).
// The stack-slot form folds member-store values into `new T { ... }` at the single
// downstream escape. If a non-consumed (side-effecting) statement sits between the
// member stores and the escape, folding moves the member-value computations after
// it — reordering observable side effects. csc emits the dup-chain use contiguously,
// so this is a synthetic-IR near-miss; paired with a contiguous positive canary.
public class ObjectInitializerContiguityTests
{
    static readonly TypeRef Type = TypeRef.Definition("Synthetic", "Samples", "InitTarget");
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    const int Slot = 3;

    static IrFunction Build(bool interveningStatement)
    {
        var ctor = new MethodRef(Type, ".ctor", Void, [], HasThis: true);
        var setX = new MethodRef(Type, "set_X", Void, [Int32], HasThis: true) { IsSpecialName = true };
        var log = new MethodRef(Owner, "Log", Int32, [], HasThis: false);
        var other = new MethodRef(Owner, "Other", Void, [], HasThis: false);

        var block = new Block();
        block.Add(new StoreStackSlot(Slot, new NewObject(ctor, [])));                                  // seed
        block.Add(new StoreProperty(setX, new LoadStackSlot(Slot, Type), [],
            new Call(log, isVirtual: false, [])));                                                     // member store (value has a side effect)
        if (interveningStatement)
            block.Add(new ExpressionStatement(new Call(other, isVirtual: false, [])));                 // non-consumed side-effecting statement
        block.Add(new Return(new LoadStackSlot(Slot, Type)));                                          // escape

        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction("M", Owner, new MethodSignature(Type, [], HasThis: false, GenericParameterCount: 0), [], body);
        new ObjectInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    [Fact]
    public void ContiguousRun_RaisesToInitializer()
    {
        var function = Build(interveningStatement: false);
        Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
    }

    [Fact]
    public void StatementBetweenStoresAndEscape_IsNotFolded()
    {
        var function = Build(interveningStatement: true);
        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        // The member store and the intervening call keep their original order.
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        Assert.Equal(2, function.Descendants.OfType<Call>().Count());
    }
}
