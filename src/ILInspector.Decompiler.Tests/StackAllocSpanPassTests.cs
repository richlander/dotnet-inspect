using ILInspector.Decompiler.Pipeline;
using IrConvert = ILInspector.Decompiler.Pipeline.Convert;

namespace ILInspector.Decompiler.Tests;

public class StackAllocSpanPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef VoidPointer = TypeRef.Pointer(Void);

    [Fact]
    public void CorelibSpanDirectStackalloc_Raises()
    {
        var function = Build(StackAllocSpanConstructor(
            TypeRef.CoreLib("System", "Span`1"),
            new StackAllocate(new Constant(4, Int32))));

        new StackAllocSpanPass().Run(function, PassContext.None);

        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal("int", raised.ElementType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void CorelibReadOnlySpanDirectStackalloc_Raises()
    {
        var function = Build(StackAllocSpanConstructor(
            TypeRef.CoreLib("System", "ReadOnlySpan`1"),
            new StackAllocate(new Constant(4, Int32))));

        new StackAllocSpanPass().Run(function, PassContext.None);

        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal("int", raised.ElementType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void SystemMemorySpanDirectStackalloc_Raises()
    {
        var function = Build(StackAllocSpanConstructor(
            TypeRef.Definition("System.Memory", "System", "Span`1"),
            new StackAllocate(new Constant(4, Int32))));

        new StackAllocSpanPass().Run(function, PassContext.None);

        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal("int", raised.ElementType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void SystemMemoryReadOnlySpanDirectStackalloc_Raises()
    {
        var function = Build(StackAllocSpanConstructor(
            TypeRef.Definition("System.Memory", "System", "ReadOnlySpan`1"),
            new StackAllocate(new Constant(4, Int32))));

        new StackAllocSpanPass().Run(function, PassContext.None);

        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal("int", raised.ElementType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void ConvertWrappedStackallocPointer_Raises()
    {
        var function = Build(StackAllocSpanConstructor(
            TypeRef.CoreLib("System", "Span`1"),
            new IrConvert(VoidPointer, isChecked: false, isUnsigned: false, new StackAllocate(new Constant(4, Int32)))));

        new StackAllocSpanPass().Run(function, PassContext.None);

        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal("int", raised.ElementType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<IrConvert>());
        function.CheckInvariant();
    }

    [Fact]
    public void CheckedConvertWrappedStackallocPointer_DoesNotRaise()
    {
        var function = Build(StackAllocSpanConstructor(
            TypeRef.CoreLib("System", "Span`1"),
            new IrConvert(VoidPointer, isChecked: true, isUnsigned: false, new StackAllocate(new Constant(4, Int32)))));

        new StackAllocSpanPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Single(function.Descendants.OfType<IrConvert>());
        Assert.Single(function.Descendants.OfType<StackAllocate>());
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
        function.CheckInvariant();
    }

    [Fact]
    public void UserSystemSpanLookalike_DoesNotRaise()
    {
        var function = Build(StackAllocSpanConstructor(
            TypeRef.Definition("UserAssembly", "System", "Span`1"),
            new StackAllocate(new Constant(4, Int32))));

        new StackAllocSpanPass().Run(function, PassContext.None);

        var construction = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Equal("UserAssembly", construction.Constructor.DeclaringType.ElementType?.Assembly);
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
        function.CheckInvariant();
    }

    [Fact]
    public void WrappedStackallocPointer_DoesNotRaise()
    {
        var wrapper = new Call(
            new MethodRef(Holder, "Wrap", TypeRef.Pointer(Void), [TypeRef.Pointer(Byte)], HasThis: false),
            isVirtual: false,
            [new StackAllocate(new Constant(4, Int32))]);
        var function = Build(StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), wrapper));

        new StackAllocSpanPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Single(function.Descendants.OfType<Call>());
        Assert.Single(function.Descendants.OfType<StackAllocate>());
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
        function.CheckInvariant();
    }

    // #2907: StackAllocInitializerPass (#2869) recovers an initializer into a
    // stackalloc-through-slot shape (`slot = stackalloc T[n] {...}`) but only
    // replaces the slot's stored value -- never the later Span constructor call
    // that reads the slot. These tests exercise this pass's slot-indirection
    // resolution, which raises through that slot when (and only when) it is
    // exclusively owned by the one store/load pair reaching the constructor.

    [Fact]
    public void OwnedSlotWithInitializer_Raises()
    {
        var elements = new IrExpression[] { new Constant(1, Int32), new Constant(2, Int32), new Constant(3, Int32) };
        var stackAllocArray = new StackAllocArray(Int32, new Constant(3, Int32), TypeRef.Pointer(Int32), elements);
        var store = new StoreStackSlot(0, stackAllocArray);
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 3);

        var function = BuildSlot(store, newObject);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.True(raised.HasInitializer);
        Assert.Equal(3, raised.Elements.Length);
        Assert.Equal("int", raised.ElementType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        function.CheckInvariant();
    }

    [Fact]
    public void OwnedSlotWithoutInitializer_Raises()
    {
        var store = new StoreStackSlot(0, new StackAllocate(new Constant(4, Int32)));
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1);

        var function = BuildSlot(store, newObject);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.False(raised.HasInitializer);
        Assert.Empty(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        function.CheckInvariant();
    }

    [Fact]
    public void OwnedSlotThroughConvertWrappedLoad_Raises()
    {
        var elements = new IrExpression[] { new Constant(9, Int32) };
        var stackAllocArray = new StackAllocArray(Int32, new Constant(1, Int32), TypeRef.Pointer(Int32), elements);
        var store = new StoreStackSlot(0, stackAllocArray);
        var wrappedLoad = new IrConvert(VoidPointer, isChecked: false, isUnsigned: false, new LoadStackSlot(0, TypeRef.Pointer(Int32)));
        var newObject = StackAllocSpanConstructorWithPointer(TypeRef.CoreLib("System", "Span`1"), wrappedLoad, count: 1);

        var function = BuildSlot(store, newObject);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.True(raised.HasInitializer);
        Assert.Empty(function.Descendants.OfType<IrConvert>());
        function.CheckInvariant();
    }

    [Fact]
    public void SlotWithSecondLoad_DoesNotRaise()
    {
        var stackAllocArray = new StackAllocArray(Int32, new Constant(1, Int32), TypeRef.Pointer(Int32), [new Constant(1, Int32)]);
        var store = new StoreStackSlot(0, stackAllocArray);
        var extraLoad = new StoreLocal(1, VoidPointer, new LoadStackSlot(0, VoidPointer));
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1);

        var function = BuildSlot(store, extraLoad, newObject);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var construction = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Same(newObject, construction);
        Assert.Single(function.Descendants.OfType<StackAllocArray>()); // still the store's un-raised value
        function.CheckInvariant();
    }

    [Fact]
    public void SlotWithSecondStore_DoesNotRaise()
    {
        var stackAllocArray = new StackAllocArray(Int32, new Constant(1, Int32), TypeRef.Pointer(Int32), [new Constant(1, Int32)]);
        var store = new StoreStackSlot(0, stackAllocArray);
        var secondStore = new StoreStackSlot(0, new StackAllocate(new Constant(4, Int32)));
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1);

        var function = BuildSlot(store, secondStore, newObject);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var construction = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Same(newObject, construction);
        Assert.Single(function.Descendants.OfType<StackAllocArray>()); // still the store's un-raised value
        function.CheckInvariant();
    }

    [Fact]
    public void SlotLoadBeforeStore_DoesNotRaise()
    {
        // Same block, but the load's statement sits ahead of the store -- the
        // slot's definition can't have reached this use.
        var stackAllocArray = new StackAllocArray(Int32, new Constant(1, Int32), TypeRef.Pointer(Int32), [new Constant(1, Int32)]);
        var store = new StoreStackSlot(0, stackAllocArray);
        var span = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Span`1"), [Int32]);
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1);

        var block = new Block(0);
        block.Add(new StoreLocal(1, span, newObject));
        block.Add(store);
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var construction = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Same(newObject, construction);
        Assert.Single(function.Descendants.OfType<StackAllocArray>()); // still the store's un-raised value
        function.CheckInvariant();
    }

    [Fact]
    public void SlotStoreInDifferentBlock_DoesNotRaise()
    {
        var stackAllocArray = new StackAllocArray(Int32, new Constant(1, Int32), TypeRef.Pointer(Int32), [new Constant(1, Int32)]);
        var store = new StoreStackSlot(0, stackAllocArray);
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1);

        var block0 = new Block(0);
        block0.Add(store);
        block0.Add(new Branch(1));
        var block1 = new Block(1);
        block1.Add(new Return(newObject));

        var body = new BlockContainer();
        body.Add(block0);
        body.Add(block1);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(newObject.ResultType ?? Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var construction = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Same(newObject, construction);
        Assert.Single(function.Descendants.OfType<StackAllocArray>()); // still the store's un-raised value
        function.CheckInvariant();
    }

    [Fact]
    public void SlotThroughNonStackallocStore_DoesNotRaise()
    {
        var store = new StoreStackSlot(0, new Call(new MethodRef(Holder, "GetPointer", VoidPointer, [], HasThis: false), isVirtual: false, []));
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1);

        var function = BuildSlot(store, newObject);

        new StackAllocSpanPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
        function.CheckInvariant();
    }

    static NewObject StackAllocSpanConstructor(TypeRef spanDefinition, IrExpression pointer, int count)
        => StackAllocSpanConstructorWithPointer(spanDefinition, pointer, count);

    static NewObject StackAllocSpanConstructorWithPointer(TypeRef spanDefinition, IrExpression pointer, int count)
    {
        var span = TypeRef.GenericInstance(spanDefinition, [Int32]);
        var ctor = new MethodRef(span, ".ctor", Void, [VoidPointer, Int32], HasThis: true);
        return new NewObject(ctor, [pointer, new Constant(count, Int32)]);
    }

    static IrFunction BuildSlot(params IrNode[] leadingStatements)
    {
        var block = new Block(0);
        foreach (var statement in leadingStatements[..^1])
            block.Add(statement);
        var finalUsage = leadingStatements[^1];
        var returnValue = finalUsage as IrExpression ?? throw new System.InvalidOperationException("Final statement must be the Span constructor.");
        block.Add(new Return(returnValue));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(returnValue.ResultType ?? Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static NewObject StackAllocSpanConstructor(TypeRef spanDefinition, IrExpression pointer)
    {
        var span = TypeRef.GenericInstance(spanDefinition, [Int32]);
        var ctor = new MethodRef(span, ".ctor", Void, [VoidPointer, Int32], HasThis: true);
        return new NewObject(ctor, [pointer, new Constant(1, Int32)]);
    }

    static IrFunction Build(IrExpression value)
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(value));
        body.Add(block);
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(value.ResultType ?? Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
