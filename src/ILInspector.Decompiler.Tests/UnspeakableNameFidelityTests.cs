using System.Collections.Immutable;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Residual compiler-generated metadata names such as <c>&lt;&gt;c</c> and
/// <c>&lt;M&gt;b__0_0</c> are not valid C# identifiers. When raising leaves them
/// in the final IR, the output must degrade honestly instead of claiming Full.
/// </summary>
public class UnspeakableNameFidelityTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Action = TypeRef.CoreLib("System", "Action");

    static IrFunction Function(ImmutableArray<TypeRef> locals, BlockContainer body)
    {
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Object, signature, locals, body);
    }

    static BlockContainer Container(params IrNode[] statements)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        foreach (var statement in statements)
            block.Add(statement);
        return container;
    }

    [Fact]
    public void ResidualCompilerGeneratedTypeName_DegradesToPartial()
    {
        var displayClass = TypeRef.Definition("Synthetic", "Samples", "<>c__DisplayClass0_0");
        var ctor = new MethodRef(displayClass, ".ctor", Void, [], HasThis: false);
        var body = Container(
            new StoreLocal(0, displayClass, new NewObject(ctor, [])),
            new Return(null));

        var function = Function([displayClass], body);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void ResidualLambdaMethodName_DegradesToPartial()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "ClosureHolder");
        var lambda = new MethodRef(holder, "<M>b__0_0", Void, [], HasThis: false);
        var body = Container(
            new ExpressionStatement(new DelegateCreation(Action, lambda, isVirtual: false, new Constant(null, Object))),
            new Return(null));

        var function = Function([], body);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void AutoPropertyBackingField_StaysFull()
    {
        var declaringType = TypeRef.Definition("Synthetic", "Samples", "C");
        var backing = new FieldRef(declaringType, "<Count>k__BackingField", Int32);
        var body = Container(new Return(new LoadField(backing, new LoadArgument(0, "this", declaringType))));
        var signature = new MethodSignature(Int32, [], HasThis: true, GenericParameterCount: 0);
        var function = new IrFunction("get_Count", declaringType, signature, [], body);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void LocalFunctionMetadataName_StaysFull()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "C");
        var localFunction = new MethodRef(holder, "<M>g__Local|0_0", Void, [], HasThis: false);
        var body = Container(
            new ExpressionStatement(new DelegateCreation(Action, localFunction, isVirtual: false, new Constant(null, Object))),
            new Return(null));

        var function = Function([], body);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    static readonly TypeRef SampleType = TypeRef.Definition("Synthetic", "Samples", "C");

    static ObjectInitializerExpression Initializer(string? member)
    {
        var ctor = new MethodRef(SampleType, ".ctor", Void, [], HasThis: true);
        return new ObjectInitializerExpression(
            new NewObject(ctor, []),
            isCollection: false,
            [new InitializerEntry(member, [new Constant(1, Int32)])]);
    }

    [Fact]
    public void ObjectInitializerUnspellableMember_DegradesToPartial()
    {
        // new C { bad-name = 1 } — the raised container holds the member name string
        // after the StoreProperty that would have flagged it was detached.
        var function = Function([], Container(new Return(Initializer("bad-name"))));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void ObjectInitializerNormalMember_StaysFull()
    {
        var function = Function([], Container(new Return(Initializer("Value"))));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void DeconstructionPropertyTargetUnspellable_DegradesToPartial()
    {
        var setter = new MethodRef(SampleType, "set_bad-name", Void, [Int32], HasThis: true);
        var target = DeconstructionTarget.Property(setter, new LoadArgument(0, "this", SampleType), [], isVirtual: false);
        var function = Function([], Container(
            new DeconstructionAssignment([target], new Constant(null, Object)),
            new Return(null)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void DeconstructionFieldTargetUnspellable_DegradesToPartial()
    {
        var field = new FieldRef(SampleType, "bad-name", Int32);
        var target = DeconstructionTarget.FieldTarget(field, isThisInstance: true);
        var function = Function([], Container(
            new DeconstructionAssignment([target], new Constant(null, Object)),
            new Return(null)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void RecursivePropertyPatternUnspellableSubpattern_DegradesToPartial()
    {
        // value is { bad-name: int t }
        var getter = new MethodRef(SampleType, "get_bad-name", Int32, [], HasThis: true);
        var pattern = new RecursivePropertyDeclarationPattern(new LoadArgument(0, "value", Object), getter, Int32, 1);
        var function = Function([Object, Int32], Container(new ExpressionStatement(pattern), new Return(null)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void RecursivePropertyPatternNormalSubpattern_StaysFull()
    {
        var getter = new MethodRef(SampleType, "get_Length", Int32, [], HasThis: true);
        var pattern = new RecursivePropertyDeclarationPattern(new LoadArgument(0, "value", Object), getter, Int32, 1);
        var function = Function([Object, Int32], Container(new ExpressionStatement(pattern), new Return(null)));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void EventSubscriptionUnspellableName_DegradesToPartial()
    {
        // C.bad-name += null; — the raised EventSubscription carries the event name
        // after the add_ Call that would have flagged it was detached.
        var accessor = new MethodRef(SampleType, "add_bad-name", Void, [Action], HasThis: false);
        var subscription = new EventSubscription(accessor, isAdd: true, instance: null, value: new Constant(null, Action));
        var function = Function([], Container(subscription, new Return(null)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void EventSubscriptionNormalName_StaysFull()
    {
        var accessor = new MethodRef(SampleType, "add_Changed", Void, [Action], HasThis: false);
        var subscription = new EventSubscription(accessor, isAdd: true, instance: null, value: new Constant(null, Action));
        var function = Function([], Container(subscription, new Return(null)));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }
}
