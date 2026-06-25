using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class InlineArrayElementRefPassTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Buffer = TypeRef.Definition("UserAssembly", "Samples", "Inline4", ValueTypeHint.ValueType);
    static readonly TypeRef RuntimeBuffer = TypeRef.Definition("UserAssembly", "Samples", "ArgumentData", ValueTypeHint.ValueType);
    static readonly TypeRef SpanInt = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Span`1"), [Int32]);

    [Fact]
    public void FirstElementRef_RaisesToInlineArrayIndexAddress()
    {
        var function = StoreThroughHelper(
            Helper("InlineArrayFirstElementRef", [TypeRef.ByRef(Buffer)]),
            [new LoadArgumentAddress(0, "buffer", Buffer)]);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        var store = Assert.Single(function.Descendants.OfType<StoreIndirect>());
        var address = Assert.IsType<LoadElementAddress>(store.Address);
        Assert.Equal(0, Assert.IsType<Constant>(address.Index).Value);
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name.Contains("InlineArray", StringComparison.Ordinal));
        Assert.Contains("buffer[0] = value;", CSharpPrinter.Print(function).Output);
        function.CheckInvariant();
    }

    [Fact]
    public void ElementRef_RaisesToInlineArrayIndexAddress()
    {
        var function = StoreThroughHelper(
            Helper("InlineArrayElementRef", [TypeRef.ByRef(Buffer), Int32]),
            [
                new LoadArgumentAddress(0, "buffer", Buffer),
                new LoadArgument(2, "index", Int32),
            ]);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        var store = Assert.Single(function.Descendants.OfType<StoreIndirect>());
        var address = Assert.IsType<LoadElementAddress>(store.Address);
        Assert.IsType<LoadArgument>(address.Index);
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name.Contains("InlineArray", StringComparison.Ordinal));
        Assert.Contains("buffer[index] = value;", CSharpPrinter.Print(function).Output);
        function.CheckInvariant();
    }

    [Fact]
    public void ElementRef_WithoutGeneratedEvidence_StaysLowered()
    {
        // A <PrivateImplementationDetails>.InlineArrayElementRef lookalike that lacks the
        // [CompilerGenerated] attribute is not the runtime intrinsic. Raising it to buffer[i]
        // would drop the real call and emit invalid/inequivalent C# at Full fidelity, so it
        // must stay lowered (#1365).
        var lookalike = new MethodRef(
            TypeRef.Definition(TypeRef.CoreLibrary, "", "<PrivateImplementationDetails>"),
            "InlineArrayElementRef",
            TypeRef.ByRef(Int32),
            [TypeRef.ByRef(Buffer), Int32],
            HasThis: false)
        {
            TypeArguments = [Buffer, Int32],
            DeclaringTypeCompilerGenerated = MetadataFactState.No,
        };
        var function = StoreThroughHelper(
            lookalike,
            [
                new LoadArgumentAddress(0, "buffer", Buffer),
                new LoadArgument(2, "index", Int32),
            ]);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<LoadElementAddress>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayElementRef");
        function.CheckInvariant();
    }

    [Fact]
    public void InitOnlyLocalBufferAsSpan_RaisesToCast()
    {
        var function = LocalBufferAsSpan(includeElementStore: false);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<InlineArraySpanConversion>());
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsSpan");
        Assert.Contains("(Span<int>)V_0", CSharpPrinter.Print(function).Output);
        function.CheckInvariant();
    }

    [Fact]
    public void LocalBufferWithElementRefStore_StaysOutOfPlaceConversion()
    {
        var function = LocalBufferAsSpan(includeElementStore: true);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<InlineArraySpanConversion>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsSpan");
        function.CheckInvariant();
    }

    [Fact]
    public void FirstElementRef_WithSpanConversionInInstanceMethod_Raises()
    {
        var function = FieldBufferWithSpanAndElementRef(
            Helper("InlineArrayFirstElementRef", [TypeRef.ByRef(Buffer)]),
            [],
            hasThis: true);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<InlineArraySpanConversion>());
        Assert.Single(function.Descendants.OfType<LoadElementAddress>());
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name.Contains("InlineArray", StringComparison.Ordinal));
        function.CheckInvariant();
    }

    [Fact]
    public void FirstElementRef_WithSpanConversionInStaticMethodStaysPartial()
    {
        var function = FieldBufferWithSpanAndElementRef(
            Helper("InlineArrayFirstElementRef", [TypeRef.ByRef(Buffer)]),
            [],
            hasThis: false);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<InlineArraySpanConversion>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayFirstElementRef");
        function.CheckInvariant();
    }

    [Fact]
    public void IndexedElementRef_WithSpanConversionStaysPartial()
    {
        var function = FieldBufferWithSpanAndElementRef(
            Helper("InlineArrayElementRef", [TypeRef.ByRef(Buffer), Int32]),
            [new LoadArgument(1, "index", Int32)],
            hasThis: true);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<InlineArraySpanConversion>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayElementRef");
        function.CheckInvariant();
    }

    [Fact]
    public void RuntimeInlineArrayIndexerShape_DoesNotRaiseToCollectionExpression()
    {
        var function = RuntimeInlineArrayBufferAsSpan();

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        Assert.Equal(2, function.Descendants.OfType<LoadElementAddress>().Count());
        function.CheckInvariant();
    }

    static IrFunction StoreThroughHelper(MethodRef helper, IReadOnlyList<IrExpression> arguments)
    {
        var block = new Block();
        block.Add(new StoreIndirect(Int32, new Call(helper, isVirtual: false, arguments), new LoadArgument(1, "value", Int32)));
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(
            Void,
            [
                new Parameter("buffer", Buffer),
                new Parameter("value", Int32),
                new Parameter("index", Int32),
            ],
            HasThis: false,
            GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [], body);
    }

    static IrFunction LocalBufferAsSpan(bool includeElementStore)
    {
        var block = new Block();
        block.Add(new InitObject(Buffer, new LoadLocalAddress(0, Buffer)));
        if (includeElementStore)
        {
            block.Add(new StoreIndirect(
                Int32,
                new Call(Helper("InlineArrayElementRef", [TypeRef.ByRef(Buffer), Int32]), isVirtual: false,
                    [new LoadLocalAddress(0, Buffer), new Constant(0, Int32)]),
                new LoadArgument(0, "value", Int32)));
        }
        block.Add(new StoreLocal(1, SpanInt, new Call(
            Helper("InlineArrayAsSpan", [TypeRef.ByRef(Buffer), Int32], SpanInt),
            isVirtual: false,
            [new LoadLocalAddress(0, Buffer), new Constant(4, Int32)])));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(Void, [new Parameter("value", Int32)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [Buffer, SpanInt], body);
    }

    static IrFunction FieldBufferWithSpanAndElementRef(
        MethodRef elementRef,
        IReadOnlyList<IrExpression> extraElementRefArguments,
        bool hasThis)
    {
        var field = new FieldRef(TypeRef.Definition("Synthetic", "", "T"), "_buffer", Buffer);
        var elementArguments = new List<IrExpression> { new LoadFieldAddress(field, instance: null) };
        elementArguments.AddRange(extraElementRefArguments);

        var block = new Block();
        block.Add(new StoreLocal(0, SpanInt, new Call(
            Helper("InlineArrayAsSpan", [TypeRef.ByRef(Buffer), Int32], SpanInt),
            isVirtual: false,
            [new LoadFieldAddress(field, instance: null), new Constant(4, Int32)])));
        block.Add(new StoreIndirect(
            Int32,
            new Call(elementRef, isVirtual: false, elementArguments),
            new LoadArgument(0, "value", Int32)));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(
            Void,
            [new Parameter("value", Int32), new Parameter("index", Int32)],
            HasThis: hasThis,
            GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [SpanInt], body);
    }

    static IrFunction RuntimeInlineArrayBufferAsSpan()
    {
        var block = new Block();
        block.Add(new InitObject(RuntimeBuffer, new LoadLocalAddress(0, RuntimeBuffer)));
        block.Add(new StoreIndirect(
            Int32,
            new Call(RuntimeHelper("InlineArrayElementRef", [TypeRef.ByRef(RuntimeBuffer), Int32]), isVirtual: false,
                [new LoadLocalAddress(0, RuntimeBuffer), new Constant(0, Int32)]),
            new LoadArgument(0, "first", Int32)));
        block.Add(new StoreIndirect(
            Int32,
            new Call(RuntimeHelper("InlineArrayElementRef", [TypeRef.ByRef(RuntimeBuffer), Int32]), isVirtual: false,
                [new LoadLocalAddress(0, RuntimeBuffer), new Constant(1, Int32)]),
            new LoadArgument(1, "second", Int32)));
        block.Add(new StoreLocal(1, SpanInt, new Call(
            RuntimeHelper("InlineArrayAsReadOnlySpan", [TypeRef.ByRef(RuntimeBuffer), Int32], SpanInt),
            isVirtual: false,
            [new LoadLocalAddress(0, RuntimeBuffer), new Constant(2, Int32)])));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(
            Void,
            [new Parameter("first", Int32), new Parameter("second", Int32)],
            HasThis: false,
            GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [RuntimeBuffer, SpanInt], body);
    }

    static MethodRef Helper(string name, IReadOnlyList<TypeRef> parameterTypes)
        => Helper(name, parameterTypes, TypeRef.ByRef(Int32));

    static MethodRef Helper(string name, IReadOnlyList<TypeRef> parameterTypes, TypeRef returnType)
        => new(
            TypeRef.Definition(TypeRef.CoreLibrary, "", "<PrivateImplementationDetails>"),
            name,
            returnType,
            [.. parameterTypes],
            HasThis: false)
        {
            TypeArguments = [Buffer, Int32],
            // The real runtime intrinsic holder is [CompilerGenerated]; the raise now
            // requires that evidence (#1365), so the positive fixtures carry it.
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };

    static MethodRef RuntimeHelper(string name, IReadOnlyList<TypeRef> parameterTypes)
        => RuntimeHelper(name, parameterTypes, TypeRef.ByRef(Int32));

    static MethodRef RuntimeHelper(string name, IReadOnlyList<TypeRef> parameterTypes, TypeRef returnType)
        => new(
            TypeRef.Definition(TypeRef.CoreLibrary, "", "<PrivateImplementationDetails>"),
            name,
            returnType,
            [.. parameterTypes],
            HasThis: false)
        {
            TypeArguments = [RuntimeBuffer, Int32],
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
}
