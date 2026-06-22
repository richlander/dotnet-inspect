using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class InlineArrayElementRefPassTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Buffer = TypeRef.Definition("UserAssembly", "Samples", "Inline4", ValueTypeHint.ValueType);
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
        };
}
