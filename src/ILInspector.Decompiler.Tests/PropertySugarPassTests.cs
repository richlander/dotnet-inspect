using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class PropertySugarPassTests
{
    static readonly TypeRef Holder = TypeRef.Definition("SyntheticAssembly", "Synthetic", "Holder");
    static readonly TypeRef Action = TypeRef.CoreLib("System", "Action");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    [Fact]
    public void StaticIndexedGetter_StaysCall()
    {
        var function = GetterFunction(Accessor("get_Item", Int32, [Int32], hasThis: false), [new LoadArgument(0, "i", Int32)]);

        new PropertySugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<LoadProperty>());
        Assert.Single(function.Descendants.OfType<Call>());
    }

    [Fact]
    public void StaticIndexedSetter_StaysCall()
    {
        var function = SetterFunction(Accessor("set_Item", Void, [Int32, Int32], hasThis: false), [new LoadArgument(0, "i", Int32), new Constant(42, Int32)]);

        new PropertySugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<StoreProperty>());
        Assert.Single(function.Descendants.OfType<Call>());
    }

    [Fact]
    public void GenericGetter_StaysCall()
    {
        var function = GetterFunction(Accessor("get_Value", Int32, [], hasThis: false) with { TypeArguments = [Int32] }, []);

        new PropertySugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<LoadProperty>());
        Assert.Single(function.Descendants.OfType<Call>());
    }

    [Fact]
    public void GenericSetter_StaysCall()
    {
        var function = SetterFunction(Accessor("set_Value", Void, [Int32], hasThis: false) with { TypeArguments = [Int32] }, [new Constant(42, Int32)]);

        new PropertySugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<StoreProperty>());
        Assert.Single(function.Descendants.OfType<Call>());
    }

    [Fact]
    public void StaticNonIndexedGetter_StillRaises()
    {
        var function = GetterFunction(Accessor("get_Count", Int32, [], hasThis: false), []);

        new PropertySugarPass().Run(function, PassContext.None);

        var property = Assert.Single(function.Descendants.OfType<LoadProperty>());
        Assert.False(property.HasInstance);
        Assert.Empty(property.IndexArguments);
    }

    [Fact]
    public void NameInferredGetter_StaysCall()
    {
        var function = GetterFunction(NameInferredSpecialName("get_Count", Int32, [], hasThis: true), [new LoadArgument(0, "self", Holder)]);

        new PropertySugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<LoadProperty>());
        Assert.Single(function.Descendants.OfType<Call>());
    }

    [Fact]
    public void StaticNonIndexedSetter_StillRaises()
    {
        var function = SetterFunction(Accessor("set_Count", Void, [Int32], hasThis: false), [new Constant(42, Int32)]);

        new PropertySugarPass().Run(function, PassContext.None);

        var property = Assert.Single(function.Descendants.OfType<StoreProperty>());
        Assert.False(property.HasInstance);
        Assert.Empty(property.IndexArguments);
    }

    [Fact]
    public void NameInferredSetter_StaysCall()
    {
        var function = SetterFunction(
            NameInferredSpecialName("set_Count", Void, [Int32], hasThis: true),
            [new LoadArgument(0, "self", Holder), new Constant(42, Int32)]);

        new PropertySugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<StoreProperty>());
        Assert.Single(function.Descendants.OfType<Call>());
    }

    [Fact]
    public void InstanceIndexedSetter_StillRaises()
    {
        var function = SetterFunction(
            Accessor("set_Item", Void, [Int32, Int32], hasThis: true),
            [new LoadArgument(0, "self", Holder), new LoadArgument(1, "i", Int32), new Constant(42, Int32)]);

        new PropertySugarPass().Run(function, PassContext.None);

        var property = Assert.Single(function.Descendants.OfType<StoreProperty>());
        Assert.True(property.HasInstance);
        Assert.Single(property.IndexArguments);
    }

    [Fact]
    public void GenericEventAccessor_StaysCall()
    {
        var accessor = Accessor("add_Changed", Void, [Action], hasThis: true) with { TypeArguments = [Int32] };
        var function = EventFunction(accessor);

        new PropertySugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<EventSubscription>());
        Assert.Single(function.Descendants.OfType<Call>());
    }

    [Fact]
    public void EventAccessor_StillRaises()
    {
        var function = EventFunction(Accessor("add_Changed", Void, [Action], hasThis: true));

        new PropertySugarPass().Run(function, PassContext.None);

        var subscription = Assert.Single(function.Descendants.OfType<EventSubscription>());
        Assert.True(subscription.IsAdd);
        Assert.Equal("Changed", subscription.EventName);
    }

    [Fact]
    public void NameInferredEventAccessor_StaysCall()
    {
        var function = EventFunction(NameInferredSpecialName("add_Changed", Void, [Action], hasThis: true));

        new PropertySugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<EventSubscription>());
        Assert.Single(function.Descendants.OfType<Call>());
    }

    static MethodRef Accessor(string name, TypeRef returnType, System.Collections.Immutable.ImmutableArray<TypeRef> parameters, bool hasThis)
        => new(Holder, name, returnType, parameters, hasThis)
        {
            IsSpecialName = true,
            AccessorKind = name switch
            {
                _ when name.StartsWith("get_", StringComparison.Ordinal) => AccessorKind.PropertyGet,
                _ when name.StartsWith("set_", StringComparison.Ordinal) => AccessorKind.PropertySet,
                _ when name.StartsWith("add_", StringComparison.Ordinal) => AccessorKind.EventAdd,
                _ when name.StartsWith("remove_", StringComparison.Ordinal) => AccessorKind.EventRemove,
                _ => AccessorKind.None,
            },
        };

    static MethodRef NameInferredSpecialName(string name, TypeRef returnType, System.Collections.Immutable.ImmutableArray<TypeRef> parameters, bool hasThis)
        => new(Holder, name, returnType, parameters, hasThis) { IsSpecialName = true };

    static IrFunction GetterFunction(MethodRef accessor, IReadOnlyList<IrExpression> arguments)
    {
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new Return(new Call(accessor, isVirtual: false, arguments)));
        body.Add(block);
        return Function(accessor.ReturnType, body);
    }

    static IrFunction SetterFunction(MethodRef accessor, IReadOnlyList<IrExpression> arguments)
    {
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new ExpressionStatement(new Call(accessor, isVirtual: false, arguments)));
        block.Add(new Return(null));
        body.Add(block);
        return Function(Void, body);
    }

    static IrFunction EventFunction(MethodRef accessor)
    {
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new ExpressionStatement(new Call(
            accessor,
            isVirtual: false,
            [new LoadArgument(0, "self", Holder), new LoadArgument(1, "handler", Action)])));
        block.Add(new Return(null));
        body.Add(block);
        return Function(Void, body);
    }

    static IrFunction Function(TypeRef returnType, BlockContainer body)
        => new(
            "M",
            Holder,
            new MethodSignature(returnType, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
}
