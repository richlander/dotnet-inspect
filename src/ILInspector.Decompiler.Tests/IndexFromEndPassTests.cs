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
}
