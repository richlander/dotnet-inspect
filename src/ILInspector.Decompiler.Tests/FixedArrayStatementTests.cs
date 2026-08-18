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

    [Fact]
    public void ArrayPin_ExternallyTargetedBodyLabel_StaysLowered()
    {
        var function = ExternallyTargetedBodyLabelFunction();

        new FixedStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Fixed>());
        Assert.Single(function.Descendants.OfType<LabelAnchor>());
        function.CheckInvariant();
    }

    [Fact]
    public void ManagedReferencePin_ExternallyTargetedBodyLabel_StaysLowered()
    {
        var function = ExternallyTargetedManagedReferenceBodyLabelFunction();

        new FixedStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Fixed>());
        Assert.Single(function.Descendants.OfType<LabelAnchor>());
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

    static IrFunction ExternallyTargetedBodyLabelFunction()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var intType = TypeRef.CoreLib("System", "Int32");
        var arrayType = TypeRef.SzArray(intType);
        var pinnedArrayType = TypeRef.Pinned(arrayType);
        var pointerType = TypeRef.Pointer(intType);

        var thenBlock = new Block(20);
        thenBlock.Add(new StoreLocal(1, pointerType, new Constant(0, pointerType)));
        var elseBlock = new Block(30);
        elseBlock.Add(new StoreLocal(
            1,
            pointerType,
            new LoadElementAddress(
                intType,
                new LoadLocal(0, pinnedArrayType),
                new Constant(0, intType),
                isReadOnly: false)));
        var guard = new IfStatement(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadArgument(0, "array", arrayType),
                new Constant(null, arrayType)),
            thenBlock,
            elseBlock);
        var anchor = new LabelAnchor();
        anchor.SetSourceOffset(100);

        var block = new Block(0);
        block.Add(new Branch(100));
        block.Add(new StoreLocal(0, pinnedArrayType, new LoadArgument(0, "array", arrayType)));
        block.Add(guard);
        block.Add(anchor);
        block.Add(new StoreLocal(
            2,
            intType,
            new LoadIndirect(intType, new LoadLocal(1, pointerType))));
        block.Add(new StoreLocal(0, pinnedArrayType, new Constant(null, pinnedArrayType)));
        block.Add(new Return(new LoadLocal(2, intType)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            owner,
            new MethodSignature(
                intType,
                [new Parameter("array", arrayType)],
                HasThis: false,
                GenericParameterCount: 0),
            [pinnedArrayType, pointerType, intType],
            body);
    }

    static IrFunction ExternallyTargetedManagedReferenceBodyLabelFunction()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var intType = TypeRef.CoreLib("System", "Int32");
        var pinnedReferenceType = TypeRef.Pinned(TypeRef.ByRef(intType));
        var pointerType = TypeRef.Pointer(intType);
        var anchor = new LabelAnchor();
        anchor.SetSourceOffset(100);

        var block = new Block(0);
        block.Add(new Branch(100));
        block.Add(new StoreLocal(
            0,
            pinnedReferenceType,
            new LoadArgumentAddress(0, "value", intType)));
        block.Add(new StoreLocal(
            1,
            pointerType,
            new ILInspector.Decompiler.Pipeline.Convert(
                pointerType,
                isChecked: false,
                isUnsigned: false,
                new LoadLocal(0, pinnedReferenceType))));
        block.Add(anchor);
        block.Add(new Return(new LoadIndirect(intType, new LoadLocal(1, pointerType))));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            owner,
            new MethodSignature(
                intType,
                [new Parameter("value", intType)],
                HasThis: false,
                GenericParameterCount: 0),
            [pinnedReferenceType, pointerType],
            body);
    }
}
