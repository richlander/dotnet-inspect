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
    public void TwoArrayForeachLoops_RaiseBoth()
    {
        var function = Raised(nameof(CfgSampleClass.TwoForeachArrays));

        Assert.Equal(2, function.Descendants.OfType<ForeachStatement>().Count());
        Assert.DoesNotContain(function.Descendants.OfType<ForLoop>(), _ => true);
    }

    [Fact]
    public void StringLoop_PrintRaised_RendersForeach()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.ForeachString))).Output;

        Assert.NotNull(output);
        Assert.Contains("foreach (char ch in text)", output);
        Assert.Contains("sum += ch;", output);
        Assert.DoesNotContain(".Length", output);
        Assert.DoesNotContain("for (", output);
    }

    [Fact]
    public void StringLoop_WithBreak_RaisesToForeach()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.ForeachStringWithBreak))).Output;

        Assert.NotNull(output);
        Assert.Contains("foreach (char ch in text)", output);
        Assert.Contains("break;", output);
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
    public void HandWrittenStringCopyIndexedFor_StaysForLoop()
    {
        var function = Raised(nameof(CfgSampleClass.CopyThenIndexedForString));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Single(function.Descendants.OfType<ForLoop>());
    }

    [Fact]
    public void RectangularArrayLoop_RaisesToForeach()
    {
        var function = Raised(nameof(CfgSampleClass.ForeachRectangularArray));

        var foreachStatement = Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.Equal("int", foreachStatement.LocalType.ToDisplayString());
        Assert.IsType<LoadArgument>(foreachStatement.Collection);
        Assert.DoesNotContain(function.Descendants.OfType<ForLoop>(), _ => true);
    }

    [Fact]
    public void RectangularArrayLoop_PrintRaised_RendersForeach()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.ForeachRectangularArray))).Output;

        Assert.NotNull(output);
        Assert.Contains("foreach (int value in matrix)", output);
        Assert.Contains("sum += value;", output);
        Assert.DoesNotContain("GetLowerBound", output);
        Assert.DoesNotContain("GetUpperBound", output);
        Assert.DoesNotContain("for (", output);
    }

    [Fact]
    public void HandWrittenRectangularGetLengthLoops_StayForLoops()
    {
        var function = Raised(nameof(CfgSampleClass.ManualRectangularGetLengthLoops));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Equal(2, function.Descendants.OfType<ForLoop>().Count());
    }

    [Fact]
    public void HandWrittenRectangularBoundsLoops_StayForLoops()
    {
        var function = Raised(nameof(CfgSampleClass.CopyThenManualRectangularBoundsLoops));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Equal(2, function.Descendants.OfType<ForLoop>().Count());
    }

    [Fact]
    public void PatternEnumeratorLoop_RaisesToForeach()
    {
        var function = Raised(nameof(CfgSampleClass.ForeachPatternEnumerable));

        var foreachStatement = Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.Equal("int", foreachStatement.LocalType.ToDisplayString());
        Assert.IsType<LoadArgument>(foreachStatement.Collection);
        Assert.DoesNotContain(function.Descendants.OfType<WhileLoop>(), _ => true);
    }

    [Fact]
    public void PatternEnumeratorLoop_PrintRaised_RendersForeach()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.ForeachPatternEnumerable))).Output;

        Assert.NotNull(output);
        Assert.Contains("foreach (int value in source)", output);
        Assert.Contains("sum += value;", output);
        Assert.DoesNotContain("GetEnumerator", output);
        Assert.DoesNotContain("MoveNext", output);
    }

    [Fact]
    public void PatternEnumeratorLoop_WithoutSymbols_StaysWhile()
    {
        var function = RaisedWithoutSymbols(nameof(CfgSampleClass.ForeachPatternEnumerable));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Single(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void HandWrittenPatternEnumeratorLoop_StaysWhile()
    {
        var function = Raised(nameof(CfgSampleClass.ManualPatternEnumeratorLoop));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Single(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void HandWrittenPatternEnumeratorLoop_WithoutSymbols_StaysWhile()
    {
        var function = RaisedWithoutSymbols(nameof(CfgSampleClass.ManualPatternEnumeratorLoop));
        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Single(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void EnumeratorThenArrayForeach_RaisesBothForms()
    {
        var function = Raised(nameof(CfgSampleClass.EnumeratorThenArrayForeach));

        Assert.Equal(2, function.Descendants.OfType<ForeachStatement>().Count());
        Assert.DoesNotContain(function.Descendants.OfType<ForLoop>(), _ => true);
        Assert.DoesNotContain(function.Descendants.OfType<UsingStatement>(), _ => true);
        Assert.DoesNotContain(function.Descendants.OfType<WhileLoop>(), _ => true);
    }

    [Fact]
    public void StringAndArrayForeach_RaisesBothForms()
    {
        var function = Raised(nameof(CfgSampleClass.StringAndArrayForeach));

        Assert.Equal(2, function.Descendants.OfType<ForeachStatement>().Count());
        Assert.DoesNotContain(function.Descendants.OfType<ForLoop>(), _ => true);
    }

    [Fact]
    public void TwoStringForeachLoops_RaiseBoth()
    {
        var function = Raised(nameof(CfgSampleClass.TwoForeachStrings));

        Assert.Equal(2, function.Descendants.OfType<ForeachStatement>().Count());
        Assert.DoesNotContain(function.Descendants.OfType<ForLoop>(), _ => true);
    }

    [Fact]
    public void NestedStringForeach_RaisesBoth()
    {
        var function = Raised(nameof(CfgSampleClass.NestedForeachString));

        Assert.Equal(2, function.Descendants.OfType<ForeachStatement>().Count());
        Assert.DoesNotContain(function.Descendants.OfType<ForLoop>(), _ => true);
    }

    [Theory]
    [InlineData(nameof(CfgSampleClass.ForeachStringField), "CfgSampleClass.s_text")]
    [InlineData(nameof(CfgSampleClass.ForeachStringMethodResult), "CfgSampleClass.GetText()")]
    [InlineData(nameof(CfgSampleClass.ForeachStringLiteral), "\"literal\"")]
    public void StringForeach_OverNonLocalReceiver_RaisesWithReceiverRestored(string method, string receiver)
    {
        var function = Raised(method);

        var foreachStatement = Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.Equal("char", foreachStatement.LocalType.ToDisplayString());
        Assert.DoesNotContain(function.Descendants.OfType<ForLoop>(), _ => true);
        Assert.Contains($"foreach (char ch in {receiver})", CSharpPrinter.Print(function).Output);
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

    [Fact]
    public void CustomPatternEnumeratorUsingWhile_StaysLowered()
    {
        var function = BuildCustomPatternEnumeratorUsingWhile();

        new ForeachStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ForeachStatement>());
        Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<WhileLoop>());
        function.CheckInvariant();
    }

    static IrFunction BuildCustomPatternEnumeratorUsingWhile()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var collectionType = TypeRef.Definition("UserAssembly", "Samples", "CustomCollection");
        var enumeratorType = TypeRef.Definition("UserAssembly", "Samples", "CustomEnumerator", ValueTypeHint.ReferenceType);
        var getEnumerator = new MethodRef(collectionType, "GetEnumerator", enumeratorType, [], HasThis: true);
        var moveNext = new MethodRef(enumeratorType, "MoveNext", boolType, [], HasThis: true);
        var current = new MethodRef(enumeratorType, "get_Current", intType, [], HasThis: true)
        {
            IsSpecialName = true,
        };

        var loopBody = new Block();
        loopBody.Add(new StoreLocal(1, intType, new LoadProperty(current, new LoadLocal(0, enumeratorType), [])));
        var usingBody = new BlockContainer();
        var usingBlock = new Block();
        usingBlock.Add(new WhileLoop(new Call(moveNext, isVirtual: true, [new LoadLocal(0, enumeratorType)]), loopBody));
        usingBody.Add(usingBlock);

        var entry = new Block();
        entry.Add(new UsingStatement(0, enumeratorType, new Call(getEnumerator, isVirtual: false, [new LoadArgument(0, "items", collectionType)]), usingBody));
        var body = new BlockContainer();
        body.Add(entry);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("items", collectionType)], HasThis: false, GenericParameterCount: 0),
            [enumeratorType, intType],
            body);
    }
}
