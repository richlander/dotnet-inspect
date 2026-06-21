using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class IndexFromEndPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void ArrayLengthMinusConstant_RaisesToIndexFromEnd()
    {
        var function = Raised(nameof(CfgSampleClass.LastElement));

        var index = Assert.Single(function.Descendants.OfType<IndexFromEnd>());
        Assert.Equal(1, Assert.IsType<Constant>(index.Offset).Value);
        Assert.Single(function.Descendants.OfType<LoadElement>());
    }

    [Fact]
    public void PrintRaised_RendersArrayIndexFromEnd()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.LastElement))).Output;

        Assert.NotNull(output);
        Assert.Equal("return a[^1];", output.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void PrintRaised_RendersStringIndexFromEnd()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.LastChar))).Output;

        Assert.NotNull(output);
        Assert.Equal("return s[^1];", output.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void ArrayLengthMinusVariable_RaisesToIndexFromEnd()
    {
        var function = Raised(nameof(CfgSampleClass.NthFromEnd));

        var index = Assert.Single(function.Descendants.OfType<IndexFromEnd>());
        Assert.IsType<LoadArgument>(index.Offset);
        var output = CSharpPrinter.Print(function).Output;
        Assert.Equal("return a[^n];", output!.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void ArrayLengthMinusComputed_RaisesToIndexFromEnd()
    {
        var function = Raised(nameof(CfgSampleClass.NthFromEndComputed));

        var index = Assert.Single(function.Descendants.OfType<IndexFromEnd>());
        Assert.IsType<Binary>(index.Offset);
        var output = CSharpPrinter.Print(function).Output;
        Assert.Equal("return a[^(n + 1)];", output!.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void StringLengthMinusVariable_RaisesToIndexFromEnd()
    {
        var function = Raised(nameof(CfgSampleClass.NthCharFromEnd));

        Assert.Single(function.Descendants.OfType<IndexFromEnd>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Equal("return s[^n];", output!.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void ReadOnlySpanLengthMinusConstant_RaisesToIndexFromEnd()
    {
        var function = Raised(nameof(CfgSampleClass.ReadOnlySpanLast));

        Assert.Single(function.Descendants.OfType<IndexFromEnd>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Equal("return span[^1];", output!.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void SpanLengthMinusConstant_RaisesToIndexFromEnd()
    {
        var function = Raised(nameof(CfgSampleClass.SpanLast));

        Assert.Single(function.Descendants.OfType<IndexFromEnd>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Equal("return span[^1];", output!.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void ReadOnlySpanLengthMinusVariable_RaisesToIndexFromEnd()
    {
        var function = Raised(nameof(CfgSampleClass.ReadOnlySpanNthFromEnd));

        Assert.Single(function.Descendants.OfType<IndexFromEnd>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Equal("return span[^n];", output!.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void HandWrittenLengthMinusVariable_IsNotRaised()
    {
        // `a[a.Length - n]` re-loads the array directly (no receiver spill), so
        // even with a variable offset it is not the compiler's `^n` lowering.
        var function = Raised(nameof(CfgSampleClass.NthFromEndHandWritten));

        Assert.Empty(function.Descendants.OfType<IndexFromEnd>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Equal("return a[a.Length - n];", output!.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void HandWrittenStringLengthMinusConstant_IsNotRaised()
    {
        var function = Raised(nameof(CfgSampleClass.LastCharHandWritten));

        Assert.Empty(function.Descendants.OfType<IndexFromEnd>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Equal("return s[s.Length - 1];", output!.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void HandWrittenReadOnlySpanLengthMinusConstant_IsNotRaised()
    {
        var function = Raised(nameof(CfgSampleClass.ReadOnlySpanLastHandWritten));

        Assert.Empty(function.Descendants.OfType<IndexFromEnd>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Equal("return span[span.Length - 1];", output!.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void HandWrittenSpanLengthMinusConstant_IsNotRaised()
    {
        var function = Raised(nameof(CfgSampleClass.SpanLastHandWritten));

        Assert.Empty(function.Descendants.OfType<IndexFromEnd>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Equal("return span[span.Length - 1];", output!.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void UserCharsAndLengthProperties_AreNotStringIndexFromEnd()
    {
        var function = BuildUserCharsLengthIndexFromEndShape();

        new IndexFromEndPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<IndexFromEnd>());
        Assert.Single(function.Descendants.OfType<Binary>());
        function.CheckInvariant();
    }

    [Fact]
    public void HandWrittenLengthMinusConstant_IsNotRaised()
    {
        // `a[a.Length - 1]` re-loads the array directly (no receiver spill), so
        // it is not the compiler's `^n` lowering. Raising it would change the
        // opcode stream on recompile, so the pass must leave it alone.
        var function = Raised(nameof(CfgSampleClass.LastElementHandWritten));

        Assert.Empty(function.Descendants.OfType<IndexFromEnd>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Equal("return a[a.Length - 1];", output!.ReplaceLineEndings("\n").Trim());
    }

    static IrFunction BuildUserCharsLengthIndexFromEndShape()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var charType = TypeRef.CoreLib("System", "Char");
        var userStringType = TypeRef.Definition("UserAssembly", "System", "String");
        var lengthGetter = new MethodRef(userStringType, "get_Length", intType, [], HasThis: true)
        {
            IsSpecialName = true,
        };
        var charsGetter = new MethodRef(userStringType, "get_Chars", charType, [intType], HasThis: true)
        {
            IsSpecialName = true,
        };

        var receiverSlot = StoreStackSlot.DupSlotBase;
        var index = new Binary(
            BinaryKind.Subtract,
            isChecked: false,
            isUnsigned: false,
            new LoadProperty(lengthGetter, new LoadStackSlot(receiverSlot, userStringType), []),
            new Constant(1, intType));
        var chars = new LoadProperty(charsGetter, new LoadStackSlot(receiverSlot, userStringType), [index]);
        var block = new Block();
        block.Add(new Return(chars));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(charType, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
