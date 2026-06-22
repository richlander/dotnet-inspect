using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class InlineArrayElementRefPassTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Buffer = TypeRef.Definition("UserAssembly", "Samples", "Inline4", ValueTypeHint.ValueType);

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

    static MethodRef Helper(string name, IReadOnlyList<TypeRef> parameterTypes)
        => new(
            TypeRef.Definition(TypeRef.CoreLibrary, "", "<PrivateImplementationDetails>"),
            name,
            TypeRef.ByRef(Int32),
            [.. parameterTypes],
            HasThis: false)
        {
            TypeArguments = [Buffer, Int32],
        };
}
