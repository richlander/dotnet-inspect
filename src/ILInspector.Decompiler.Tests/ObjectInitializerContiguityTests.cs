using ILInspector.Decompiler.Pipeline;
using System.Collections.Immutable;

namespace ILInspector.Decompiler.Tests;

// Adversarial guard for ObjectInitializerPass stack-slot contiguity (issue #1408).
// If a non-consumed side-effecting statement sits between the member stores and
// the single downstream escape, the pass must materialize the initializer at the
// seed rather than move it to the escape; otherwise member-value computations
// would be reordered after the gap. ObjectInitializerPassTests covers the
// csc-emitted short-circuit argument shape.
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
    public void StatementBetweenStoresAndEscape_FoldsAtSeedWithoutReordering()
    {
        var function = Build(interveningStatement: true);
        Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("new InitTarget { X = Owner.Log() }", output);
        Assert.True(
            output.IndexOf("new InitTarget { X = Owner.Log() }", StringComparison.Ordinal)
                < output.IndexOf("Other();", StringComparison.Ordinal),
            output);
    }

    [Fact]
    public void CollectionStatementBetweenAddsAndEscape_FoldsAtSeedWithoutReordering()
    {
        var function = CollectionWithInterveningStatement();
        Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), call => call.Callee.Name == "Add");

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("new InitTarget { 1, 2 }", output);
        Assert.True(
            output.IndexOf("new InitTarget { 1, 2 }", StringComparison.Ordinal)
                < output.IndexOf("Other();", StringComparison.Ordinal),
            output);
    }

    static IrFunction CollectionWithInterveningStatement()
    {
        var ctor = new MethodRef(Type, ".ctor", Void, [], HasThis: true);
        var add = new MethodRef(Type, "Add", Void, [Int32], HasThis: true);
        var other = new MethodRef(Owner, "Other", Void, [], HasThis: false);

        var block = new Block();
        block.Add(new StoreStackSlot(Slot, new NewObject(ctor, [])));
        block.Add(new ExpressionStatement(new Call(add, isVirtual: false, [new LoadStackSlot(Slot, Type), new Constant(1, Int32)])));
        block.Add(new StoreStackSlot(Slot + 1, new LoadStackSlot(Slot, Type)));
        block.Add(new ExpressionStatement(new Call(add, isVirtual: false, [new LoadStackSlot(Slot + 1, Type), new Constant(2, Int32)])));
        block.Add(new ExpressionStatement(new Call(other, isVirtual: false, [])));
        block.Add(new Return(new LoadStackSlot(Slot + 1, Type)));

        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction("M", Owner, new MethodSignature(Type, [], HasThis: false, GenericParameterCount: 0), [], body)
        {
            CollectionInitializerTypes = ImmutableHashSet.Create(Type),
        };
        new ObjectInitializerPass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }
}
