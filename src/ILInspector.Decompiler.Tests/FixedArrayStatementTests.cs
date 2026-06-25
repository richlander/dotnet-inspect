using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class FixedArrayStatementTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    [Fact]
    public void ArrayPin_RaisesToFixedStatement()
    {
        var function = Raised(nameof(CfgSampleClass.FixedWholeArray));

        // The null/empty guard diamond and the pin/unpin scaffolding collapse into a
        // single array-form fixed statement (source rendered as-is, not `&place`).
        var fixedStatement = Assert.Single(function.Descendants.OfType<Fixed>());
        Assert.False(fixedStatement.SourceIsAddress);
        Assert.Equal("Byte", fixedStatement.ElementType.Name);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void ArrayPin_RendersValidFixed()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.FixedWholeArray))).Output;

        Assert.NotNull(output);
        Assert.Contains("fixed (byte* p = ", output);
        Assert.DoesNotContain("pinned", output);
    }

    [Fact]
    public void ArrayPin_PointerUseAfterUnpin_StaysLowered()
    {
        var function = PostUnpinPointerUseFunction();

        new FixedStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Fixed>());
        Assert.Single(function.Descendants.OfType<IfStatement>());
        Assert.Single(function.Descendants.OfType<Return>());
        function.CheckInvariant();
    }

    static IrFunction PostUnpinPointerUseFunction()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var intType = TypeRef.CoreLib("System", "Int32");
        var arrayType = TypeRef.SzArray(byteType);
        var pinnedArrayType = TypeRef.Pinned(arrayType);
        var pointerType = TypeRef.Pointer(byteType);

        var thenBlock = new Block();
        thenBlock.Add(new StoreLocal(1, pointerType, new Constant(0, intType)));
        var elseBlock = new Block();
        elseBlock.Add(new StoreLocal(
            1,
            pointerType,
            new LoadElementAddress(
                byteType,
                new LoadLocal(0, pinnedArrayType),
                new Constant(0, intType),
                isReadOnly: false)));
        var guard = new IfStatement(
            new LogicalBinary(
                LogicalKind.Or,
                new LogicalNot(new LoadArgument(0, "array", arrayType)),
                new Comparison(
                    ComparisonKind.Equal,
                    isUnsigned: false,
                    new ArrayLength(new LoadArgument(0, "array", arrayType)),
                    new Constant(0, intType))),
            thenBlock,
            elseBlock);

        var block = new Block();
        block.Add(new StoreLocal(0, pinnedArrayType, new LoadArgument(0, "array", arrayType)));
        block.Add(guard);
        block.Add(new StoreLocal(0, pinnedArrayType, new Constant(null, pinnedArrayType)));
        block.Add(new Return(new LoadLocal(1, pointerType)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            owner,
            new MethodSignature(pointerType, [new Parameter("array", arrayType)], HasThis: false, GenericParameterCount: 0),
            [pinnedArrayType, pointerType],
            body);
    }
}
