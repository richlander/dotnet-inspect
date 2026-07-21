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

    [Fact]
    public void DirectPointerWithEffectfulSize_DoesNotRaise()
    {
        // Same defect as the slot-indirection case's SlotWithEffectfulSize --
        // the direct pointer's own size expression is discarded and replaced
        // by the constructor's count argument, so a non-constant/effectful
        // size must not be silently dropped here either.
        var sizeEffect = new Call(new MethodRef(Holder, "SizeEffect", Int32, [], HasThis: false), isVirtual: false, []);
        var function = Build(StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new StackAllocate(sizeEffect)));

        new StackAllocSpanPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
        Assert.Single(function.Descendants.OfType<Call>(), c => c.Callee.Name == "SizeEffect"); // side effect preserved
        function.CheckInvariant();
    }

    [Fact]
    public void DirectPointerWithDynamicPureSize_Raises()
    {
        // A dynamic-but-pure size (a local read, or arithmetic over one -- the
        // common `stackalloc byte[n]` shape, e.g. n * sizeof(T) after
        // StackAllocInitializerPass/import) must still raise: requiring a
        // literal Constant here would reject this very common real-world
        // pattern and reintroduce the original invalid-Full CS8346 shape this
        // pass exists to fix.
        var size = new Binary(BinaryKind.Multiply, isChecked: false, isUnsigned: false, new LoadLocal(0, Int32), new SizeOf(Int32));
        var function = Build(StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new StackAllocate(size)));

        new StackAllocSpanPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Empty(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void DirectPointerWithInitializerCountMismatch_DoesNotRaise()
    {
        // Same defect as the slot-indirection case's
        // SlotWithInitializerCountMismatch -- the pointer's own initializer
        // element count and the constructor's independent length argument
        // must agree before raising.
        var elements = new IrExpression[] { new Constant(1, Int32), new Constant(2, Int32), new Constant(3, Int32) };
        var stackAllocArray = new StackAllocArray(Int32, new Constant(3, Int32), TypeRef.Pointer(Int32), elements);
        var function = Build(StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), stackAllocArray, count: 1));

        new StackAllocSpanPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        var unraised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal(3, unraised.Elements.Length); // still the pointer's un-raised value
        function.CheckInvariant();
    }

    [Fact]
    public void DirectPointerWithElementTypeMismatch_DoesNotRaise()
    {
        // Same defect as the slot-indirection case's
        // SlotWithElementTypeMismatch -- the pointer's own initializer element
        // type and the constructor's Span<T> type argument must agree before
        // raising.
        var elements = new IrExpression[] { new Constant(300, Int32) };
        var stackAllocArray = new StackAllocArray(Int32, new Constant(1, Int32), TypeRef.Pointer(Int32), elements);
        var function = Build(StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), stackAllocArray, count: 1, elementType: Byte));

        new StackAllocSpanPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        var unraised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal("int", unraised.ElementType.ToDisplayString()); // still the pointer's un-raised value
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
    public void SlotWithEffectfulSize_DoesNotRaise()
    {
        // The store's own byte-size expression is a call, not a constant --
        // discarding it (as the raise would, since it uses the constructor's
        // length argument instead) would silently erase the call's side effect
        // and reorder it past whatever follows the store.
        var sizeEffect = new Call(new MethodRef(Holder, "SizeEffect", Int32, [], HasThis: false), isVirtual: false, []);
        var store = new StoreStackSlot(0, new StackAllocate(sizeEffect));
        var marker = new Call(new MethodRef(Holder, "Marker", Void, [], HasThis: false), isVirtual: false, []);
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1);

        var function = BuildSlot(store, marker, newObject);

        new StackAllocSpanPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<StackAllocArray>());
        Assert.Single(function.Descendants.OfType<Call>(), c => c.Callee.Name == "SizeEffect"); // side effect preserved
        function.CheckInvariant();
    }

    [Fact]
    public void SlotWithInitializerCountMismatch_DoesNotRaise()
    {
        // The store's recovered initializer has 3 elements but the
        // constructor's own length argument says 1 -- these are two
        // independent expressions in the tree (unlike the direct-pointer case,
        // where a single stackalloc node feeds both), so nothing else proves
        // they describe the same span. Raising would silently change the
        // observable Span.Length from 1 to 3.
        var elements = new IrExpression[] { new Constant(1, Int32), new Constant(2, Int32), new Constant(3, Int32) };
        var stackAllocArray = new StackAllocArray(Int32, new Constant(3, Int32), TypeRef.Pointer(Int32), elements);
        var store = new StoreStackSlot(0, stackAllocArray);
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1);

        var function = BuildSlot(store, newObject);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var construction = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Same(newObject, construction);
        var unraised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal(3, unraised.Elements.Length); // still the store's un-raised value
        function.CheckInvariant();
    }

    [Fact]
    public void SlotWithNonAdjacentStatement_DoesNotRaise()
    {
        // Even with a constant size, a statement between the store and the
        // load's statement could itself throw, run out of stack, or otherwise
        // observe or alter state before the allocation would occur -- moving
        // the allocation to the load's site changes when (and whether) it
        // executes relative to that statement. Requiring the load's statement
        // to be the store's immediate successor rules this out entirely.
        var store = new StoreStackSlot(0, new StackAllocate(new Constant(4, Int32)));
        var marker = new Call(new MethodRef(Holder, "Marker", Void, [], HasThis: false), isVirtual: false, []);
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1);

        var function = BuildSlot(store, marker, newObject);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var construction = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Same(newObject, construction);
        Assert.Single(function.Descendants.OfType<StackAllocate>()); // still the store's un-raised value
        function.CheckInvariant();
    }

    [Fact]
    public void SlotWithConvertWrappedInitializerCountMismatch_DoesNotRaise()
    {
        // Same defect as SlotWithInitializerCountMismatch_DoesNotRaise, but the
        // store's value is Convert-wrapped: the count/element-type checks must
        // see through the wrapper to the underlying StackAllocArray, not
        // silently skip validation because the wrapper doesn't itself match
        // the `is StackAllocArray` pattern.
        var elements = new IrExpression[] { new Constant(1, Int32), new Constant(2, Int32), new Constant(3, Int32) };
        var stackAllocArray = new StackAllocArray(Int32, new Constant(3, Int32), TypeRef.Pointer(Int32), elements);
        var wrapped = new IrConvert(VoidPointer, isChecked: false, isUnsigned: false, stackAllocArray);
        var store = new StoreStackSlot(0, wrapped);
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1);

        var function = BuildSlot(store, newObject);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var construction = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Same(newObject, construction);
        var unraised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal(3, unraised.Elements.Length); // still the store's un-raised value
        function.CheckInvariant();
    }

    [Fact]
    public void SlotWithElementTypeMismatch_DoesNotRaise()
    {
        // The store's recovered initializer is int[], but the constructor's
        // Span<T> type argument is byte -- these are independent expressions
        // that could disagree in adversarial IR (unlike the direct-pointer
        // case, where the element type comes from the same stackalloc node).
        // Raising would silently reinterpret the stored int elements as a
        // byte-typed initializer.
        var elements = new IrExpression[] { new Constant(300, Int32) };
        var stackAllocArray = new StackAllocArray(Int32, new Constant(1, Int32), TypeRef.Pointer(Int32), elements);
        var store = new StoreStackSlot(0, stackAllocArray);
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1, elementType: Byte);

        var function = BuildSlot(store, newObject);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var construction = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Same(newObject, construction);
        var unraised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal("int", unraised.ElementType.ToDisplayString()); // still the store's un-raised value
        function.CheckInvariant();
    }

    [Fact]
    public void SlotWithLoadInsideLoopCondition_DoesNotRaise()
    {
        // GetStatement's walk-up-to-the-enclosing-Block-child logic returns
        // the WhileLoop itself as "the statement" when the load sits inside
        // its condition -- but a loop condition is evaluated on every
        // iteration (and possibly zero times), not exactly once like an
        // ordinary adjacent statement. Raising here would move a one-time
        // allocation into code that runs a different number of times.
        var store = new StoreStackSlot(0, new StackAllocate(new Constant(4, Int32)));
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1);
        var condition = new IsInstance(Holder, newObject); // load embedded in a loop condition expression
        var loop = new WhileLoop(condition, new Block(0));

        var block = new Block(0);
        block.Add(store);
        block.Add(loop);
        block.Add(new Return(new Constant(0, Int32)));

        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var construction = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Same(newObject, construction);
        Assert.Single(function.Descendants.OfType<StackAllocate>()); // still the store's un-raised value
        function.CheckInvariant();
    }

    [Fact]
    public void SlotWithUninitializedArrayElementTypeMismatch_DoesNotRaise()
    {
        // An uninitialized StackAllocArray (no recovered elements, but still
        // carrying its own ElementType and Count) must have its element type
        // checked too -- not just the HasInitializer case -- or adversarial IR
        // could silently reinterpret e.g. a 100-element int allocation as a
        // 1-element byte allocation.
        var stackAllocArray = new StackAllocArray(Int32, new Constant(100, Int32), TypeRef.Pointer(Int32));
        var store = new StoreStackSlot(0, stackAllocArray);
        var newObject = StackAllocSpanConstructor(TypeRef.CoreLib("System", "Span`1"), new LoadStackSlot(0, VoidPointer), count: 1, elementType: Byte);

        var function = BuildSlot(store, newObject);

        new StackAllocSpanPass().Run(function, PassContext.None);

        var construction = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Same(newObject, construction);
        var unraised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.Equal("int", unraised.ElementType.ToDisplayString()); // still the store's un-raised value
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

    static NewObject StackAllocSpanConstructor(TypeRef spanDefinition, IrExpression pointer, int count, TypeRef? elementType = null)
        => StackAllocSpanConstructorWithPointer(spanDefinition, pointer, count, elementType);

    static NewObject StackAllocSpanConstructorWithPointer(TypeRef spanDefinition, IrExpression pointer, int count, TypeRef? elementType = null)
    {
        var span = TypeRef.GenericInstance(spanDefinition, [elementType ?? Int32]);
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
