using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ForeachStatementPassTests
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

    static IrFunction RaisedWithoutSymbols(string methodName)
    {
        using var source = MetadataSource.OpenWithoutSymbols(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void EnumeratorUsingLoop_RaisesToForeach()
    {
        var function = Raised(nameof(CfgSampleClass.ForeachLoop));

        var foreachStatement = Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.Equal("int", foreachStatement.LocalType.ToDisplayString());
        Assert.IsType<LoadArgument>(foreachStatement.Collection);
        Assert.DoesNotContain(function.Descendants.OfType<UsingStatement>(), _ => true);
        Assert.DoesNotContain(function.Descendants.OfType<WhileLoop>(), _ => true);
    }

    [Fact]
    public void PrintRaised_RendersForeach()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.ForeachLoop))).Output;

        Assert.NotNull(output);
        Assert.Contains("foreach (int item in items)", output);
        Assert.Contains("result.Add(item.ToString());", output);
        Assert.DoesNotContain("GetEnumerator", output);
        Assert.DoesNotContain("MoveNext", output);
    }

    [Fact]
    public void ForeachLoop_WithoutSymbols_StillRaisesToForeach()
    {
        var function = RaisedWithoutSymbols(nameof(CfgSampleClass.ForeachLoop));

        var foreachStatement = Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.Equal("int", foreachStatement.LocalType.ToDisplayString());
        Assert.IsType<LoadArgument>(foreachStatement.Collection);
        Assert.DoesNotContain(function.Descendants.OfType<UsingStatement>(), _ => true);
        Assert.DoesNotContain(function.Descendants.OfType<WhileLoop>(), _ => true);
    }

    [Fact]
    public void ArrayLoop_RaisesToForeach()
    {
        var function = Raised(nameof(CfgSampleClass.ForeachArray));

        var foreachStatement = Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.Equal("int", foreachStatement.LocalType.ToDisplayString());
        Assert.IsType<LoadArgument>(foreachStatement.Collection);
        Assert.DoesNotContain(function.Descendants.OfType<ForLoop>(), _ => true);
    }

    [Fact]
    public void ArrayLoop_PrintRaised_RendersForeach()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.ForeachArray))).Output;

        Assert.NotNull(output);
        Assert.Contains("foreach (int n in numbers)", output);
        Assert.Contains("sum += n;", output);
        Assert.DoesNotContain(".Length", output);
        Assert.DoesNotContain("for (", output);
    }

    [Fact]
    public void HandWrittenIndexedForOverArray_StaysForLoop()
    {
        var function = Raised(nameof(CfgSampleClass.IndexedForOverArray));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Single(function.Descendants.OfType<ForLoop>());
    }

    [Fact]
    public void HandWrittenArrayCopyIndexedFor_StaysForLoop()
    {
        var function = Raised(nameof(CfgSampleClass.CopyThenIndexedFor));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Single(function.Descendants.OfType<ForLoop>());
    }

    [Fact]
    public void StringLoop_RaisesToForeach()
    {
        var function = Raised(nameof(CfgSampleClass.ForeachString));

        var foreachStatement = Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.Equal("char", foreachStatement.LocalType.ToDisplayString());
        Assert.IsType<LoadArgument>(foreachStatement.Collection);
        Assert.DoesNotContain(function.Descendants.OfType<ForLoop>(), _ => true);
    }

    [Fact]
    public void StringLoop_PrintRaised_RendersForeach()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.ForeachString))).Output;

        Assert.NotNull(output);
        Assert.Contains("foreach (char c in text)", output);
        Assert.Contains("sum += c;", output);
        Assert.DoesNotContain(".Length", output);
        Assert.DoesNotContain("for (", output);
    }

    [Fact]
    public void HandWrittenIndexedForOverString_StaysForLoop()
    {
        var function = Raised(nameof(CfgSampleClass.IndexedForOverString));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Single(function.Descendants.OfType<ForLoop>());
    }

    [Fact]
    public void SourceNamedEnumeratorUsingLoop_StaysUsingWhile()
    {
        var function = Raised(nameof(CfgSampleClass.StructUsing));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void HandWrittenEnumeratorUsingLoop_WithoutSymbols_StaysUsingWhile()
    {
        var function = RaisedWithoutSymbols(nameof(CfgSampleClass.StructUsing));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<WhileLoop>());
    }
}
