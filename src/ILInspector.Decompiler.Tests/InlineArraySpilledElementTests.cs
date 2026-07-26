using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Synthetic coverage for <see cref="InlineArrayCollectionPass"/>'s address-spilled
/// element-store recovery (issue #3129, S4). When a collection-expression element
/// value carries a branch, csc evaluates the element-ref address before the branch
/// and spills the <c>ref</c> — and often the branch value — to single-use
/// evaluation-stack slots, then stores through the spilled address. The pass
/// recovers that shape only when every spill slot is written once and read once; a
/// multi-use spill slot or a volatile store must leave the whole collection flat.
/// The compiled-fixture canary lives in <c>CfgSampleClass.InlineArrayObjectConditionalElementSpan</c>;
/// these tests pin the guard boundaries a compiled fixture cannot express.
/// </summary>
[Trait("Area", "Pass")]
public class InlineArraySpilledElementTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef ByRefObject = TypeRef.ByRef(Object);

    // A compiler-synthesized inline-array buffer (the older `<>y__InlineArrayN`
    // spelling) instantiated over object — the span source csc emits for a params
    // ReadOnlySpan<object> collection expression.
    static readonly TypeRef BufferDefinition =
        TypeRef.Definition(TypeRef.CoreLibrary, "", "<>y__InlineArray2`1", ValueTypeHint.ValueType, MetadataFactState.Yes);
    static readonly TypeRef Buffer = TypeRef.GenericInstance(BufferDefinition, [Object]);
    static readonly TypeRef SpanObject = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [Object]);

    enum SpillDefect
    {
        None,
        AddressSlotReadTwice,
        ValueSlotReadTwice,
        VolatileStore,
    }

    [Fact]
    public void SpilledAddressAndValue_RaisesToCollectionExpression()
    {
        var function = BuildSpilledCollection(SpillDefect.None);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        // Elements are re-sequenced into slot (= source) order: the direct element 0
        // then the spilled element 1, recovered to its real branch value.
        Assert.Collection(
            collection.Children,
            first => Assert.IsType<Box>(first),
            second => Assert.IsType<Conditional>(second));
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            c => c.Callee.Name.Contains("InlineArray", StringComparison.Ordinal));
        function.CheckInvariant();
    }

    [Fact]
    public void AddressSpillReadTwice_StaysFlat()
    {
        // The spilled element-ref address must be read exactly once — the address
        // computation has no observable effect only when the collection expression
        // is the sole consumer. A second read means the ref escapes; leave it flat.
        var function = BuildSpilledCollection(SpillDefect.AddressSlotReadTwice);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void ValueSpillReadTwice_StaysFlat()
    {
        // A spilled element value read more than once cannot be lifted into the
        // element position without duplicating (and re-evaluating) it; leave flat.
        var function = BuildSpilledCollection(SpillDefect.ValueSlotReadTwice);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void VolatileSpilledStore_StaysFlat()
    {
        // A volatile store through the spilled address is an observable memory
        // operation the collection expression would silently drop; leave flat.
        var function = BuildSpilledCollection(SpillDefect.VolatileStore);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    /// <summary>
    /// Builds the two-element params ReadOnlySpan&lt;object&gt; collection shape:
    /// element 0 is a direct indirect store of <c>box a</c>; element 1's value
    /// carries a branch, so its element-ref address is spilled to slot 0 and its
    /// conditional value to slot 1 before the store through the spilled address.
    /// <paramref name="defect"/> perturbs exactly one guard input to prove the
    /// negative leaves the collection flat.
    /// </summary>
    static IrFunction BuildSpilledCollection(SpillDefect defect)
    {
        const int AddressSlot = 0;
        const int ValueSlot = 1;

        var block = new Block();

        // <>y__InlineArray2<object> buffer = default; (local 0)
        block.Add(new InitObject(Buffer, new LoadLocalAddress(0, Buffer)));

        // Element 0: *(InlineArrayElementRef(ref buffer, 0)) = box a;
        block.Add(new StoreIndirect(
            Object,
            ElementRef(0),
            new Box(Int32, new LoadArgument(0, "a", Int32))));

        // Element 1: ref object address = InlineArrayElementRef(ref buffer, 1);
        block.Add(new StoreStackSlot(AddressSlot, ElementRef(1)));
        // object value = flag ? box 1 : box 2;
        block.Add(new StoreStackSlot(
            ValueSlot,
            new Conditional(
                new LoadArgument(1, "flag", Bool),
                new Box(Int32, new Constant(1, Int32)),
                new Box(Int32, new Constant(2, Int32)))));
        // *address = value;
        block.Add(new StoreIndirect(Object, new LoadStackSlot(AddressSlot, ByRefObject), new LoadStackSlot(ValueSlot, Object))
        {
            IsVolatile = defect == SpillDefect.VolatileStore,
        });

        // A second read of a spill slot perturbs the single-use guard.
        if (defect == SpillDefect.AddressSlotReadTwice)
            block.Add(new StoreLocal(2, ByRefObject, new LoadStackSlot(AddressSlot, ByRefObject)));
        else if (defect == SpillDefect.ValueSlotReadTwice)
            block.Add(new StoreLocal(2, Object, new LoadStackSlot(ValueSlot, Object)));

        // ReadOnlySpan<object> span = InlineArrayAsReadOnlySpan<...>(ref buffer, 2); (local 1)
        block.Add(new StoreLocal(1, SpanObject, new Call(
            AsReadOnlySpan(),
            isVirtual: false,
            [new LoadLocalAddress(0, Buffer), new Constant(2, Int32)])));
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(
            Void,
            [new Parameter("a", Int32), new Parameter("flag", Bool)],
            HasThis: false,
            GenericParameterCount: 0);
        var declaringType = TypeRef.Definition("Synthetic", "", "T");
        return defect switch
        {
            SpillDefect.AddressSlotReadTwice =>
                new IrFunction("M", declaringType, signature, [Buffer, SpanObject, ByRefObject], body),
            SpillDefect.ValueSlotReadTwice =>
                new IrFunction("M", declaringType, signature, [Buffer, SpanObject, Object], body),
            _ => new IrFunction("M", declaringType, signature, [Buffer, SpanObject], body),
        };
    }

    static Call ElementRef(int slot)
        => new(
            new MethodRef(
                TypeRef.Definition(TypeRef.CoreLibrary, "", "<PrivateImplementationDetails>"),
                "InlineArrayElementRef",
                ByRefObject,
                [TypeRef.ByRef(Buffer), Int32],
                HasThis: false)
            {
                TypeArguments = [Buffer, Object],
                DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
            },
            isVirtual: false,
            [new LoadLocalAddress(0, Buffer), new Constant(slot, Int32)]);

    static MethodRef AsReadOnlySpan()
        => new(
            TypeRef.Definition(TypeRef.CoreLibrary, "", "<PrivateImplementationDetails>"),
            "InlineArrayAsReadOnlySpan",
            SpanObject,
            [TypeRef.ByRef(Buffer), Int32],
            HasThis: false)
        {
            TypeArguments = [Buffer, Object],
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
}
