using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
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
        Assert.Contains(foreachStatement.ConsumedMemberRefs, method => method.Name == "GetEnumerator");
        Assert.Contains(foreachStatement.ConsumedMemberRefs, method => method.Name == "MoveNext");
        Assert.Contains(foreachStatement.ConsumedMemberRefs, method => method.Name == "get_Current");
        Assert.Contains(foreachStatement.ConsumedMemberRefs, method => method.Name == "Dispose");
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
    public void RuntimeAsyncEnumeratorLoop_RaisesToAwaitForeach()
    {
        var function = Raised(nameof(CfgSampleClass.AwaitForeach));

        var foreachStatement =
            Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.True(foreachStatement.IsAwait);
        Assert.Equal("int", foreachStatement.LocalType.ToDisplayString());
        Assert.IsType<LoadArgument>(foreachStatement.Collection);
        Assert.Contains(
            foreachStatement.ConsumedMemberRefs,
            method => method.Name == "GetAsyncEnumerator");
        Assert.Contains(
            foreachStatement.ConsumedMemberRefs,
            method => method.Name == "MoveNextAsync");
        Assert.Contains(
            foreachStatement.ConsumedMemberRefs,
            method => method.Name == "get_Current");
        Assert.Contains(
            foreachStatement.ConsumedMemberRefs,
            method => method.Name == "DisposeAsync");
        Assert.DoesNotContain(
            function.Descendants.OfType<UsingStatement>(),
            _ => true);
        Assert.DoesNotContain(function.Descendants.OfType<WhileLoop>(), _ => true);
    }

    [Fact]
    public void RuntimeAsyncEnumeratorLoop_RendersAwaitForeach()
    {
        var output =
            CSharpPrinter.Print(Raised(nameof(CfgSampleClass.AwaitForeach))).Output;

        Assert.NotNull(output);
        Assert.Contains("await foreach (int value in source)", output);
        Assert.Contains("sum += value;", output);
        Assert.DoesNotContain("GetAsyncEnumerator", output);
        Assert.DoesNotContain("MoveNextAsync", output);
        Assert.DoesNotContain("DisposeAsync", output);
        Assert.DoesNotContain("ExceptionDispatchInfo", output);
    }

    [Fact]
    public void ManualAwaitEnumeratorLoop_StaysAwaitUsingWhile()
    {
        var function = Raised(nameof(CfgSampleClass.ManualAwaitEnumeratorLoop));

        Assert.DoesNotContain(
            function.Descendants.OfType<ForeachStatement>(),
            _ => true);
        Assert.Contains(
            function.Descendants.OfType<UsingStatement>(),
            statement => statement.IsAwait);
        Assert.Single(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void RuntimeAsyncEnumeratorLoop_WithoutSymbols_StillRaises()
    {
        var function = RaisedWithoutSymbols(nameof(CfgSampleClass.AwaitForeach));

        var foreachStatement =
            Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.True(foreachStatement.IsAwait);
        Assert.DoesNotContain(
            function.Descendants.OfType<UsingStatement>(),
            _ => true);
        Assert.DoesNotContain(function.Descendants.OfType<WhileLoop>(), _ => true);
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
    public void RectangularArray3DLoop_RaisesToForeach()
    {
        var function = Raised(nameof(CfgSampleClass.ForeachRectangularArray3D));

        var foreachStatement = Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.Equal("int", foreachStatement.LocalType.ToDisplayString());
        Assert.IsType<LoadArgument>(foreachStatement.Collection);
        Assert.DoesNotContain(function.Descendants.OfType<ForLoop>(), _ => true);
    }

    [Fact]
    public void RectangularArray3DLoop_PrintRaised_RendersForeach()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.ForeachRectangularArray3D))).Output;

        Assert.NotNull(output);
        Assert.Contains("foreach (int value in cube)", output);
        Assert.Contains("sum += value;", output);
        Assert.DoesNotContain("GetLowerBound", output);
        Assert.DoesNotContain("GetUpperBound", output);
        Assert.DoesNotContain("for (", output);
    }

    [Fact]
    public void HandWrittenRectangular3DBoundsLoops_StayForLoops()
    {
        var function = Raised(nameof(CfgSampleClass.CopyThenManualRectangular3DBoundsLoops));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Equal(3, function.Descendants.OfType<ForLoop>().Count());
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
    public void PatternEnumeratorLoop_WithNonCorelibBooleanMoveNext_StaysWhile()
    {
        var function = BuildPatternEnumeratorLoop(TypeRef.Definition("UserAssembly", "System", "Boolean"));

        new ForeachStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ForeachStatement>());
        Assert.Single(function.Descendants.OfType<WhileLoop>());
        function.CheckInvariant();
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
    [InlineData(nameof(CfgSampleClass.ForeachStringMethodResult), "GetText()")]
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
    public void GenericListForeach_RaisesToForeach()
    {
        var function = Raised(nameof(CfgSampleClass.ForeachGenericList));

        var foreachStatement = Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.Equal("int", foreachStatement.LocalType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void GenericListForeach_PrintRaised_RendersForeach()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.ForeachGenericList))).Output;

        Assert.NotNull(output);
        Assert.Contains("foreach (int value in items)", output);
        Assert.DoesNotContain("GetEnumerator", output);
        Assert.DoesNotContain("MoveNext", output);
    }

    [Fact]
    public void GenericListForeach_WithoutSymbols_StaysUsingWhile()
    {
        // Without the PDB-hidden enumerator discriminator, the compiler foreach
        // is indistinguishable from a hand-written using/while, so it declines.
        var function = RaisedWithoutSymbols(nameof(CfgSampleClass.ForeachGenericList));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void InlineCurrentForeach_RaisesToForeach()
    {
        // The single-use iteration variable is folded into its one use by
        // ExpressionInliningPass before this pass runs, so no `item = e.Current`
        // store survives — the hidden enumerator is referenced only by MoveNext
        // and one inline `e.Current`. The pass rebinds that inline read to a
        // fresh foreach variable. (JsonElement.DeepEquals Array arm, #3164.)
        var function = Raised(nameof(CfgSampleClass.ForeachSingleUseWithParallelEnumerator));

        var foreachStatement = Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.Equal("int", foreachStatement.LocalType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        // Only the manually advanced parallel enumerator's while loop is gone;
        // the foreach replaced the compiler loop, leaving no WhileLoop behind.
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
        Assert.Contains(foreachStatement.ConsumedMemberRefs, method => method.Name == "get_Current");
        Assert.Contains(foreachStatement.ConsumedMemberRefs, method => method.Name == "MoveNext");
    }

    [Fact]
    public void InlineCurrentForeach_PrintRaised_RendersForeachWithInlineCurrentRebound()
    {
        var output = CSharpPrinter.Print(
            Raised(nameof(CfgSampleClass.ForeachSingleUseWithParallelEnumerator))).Output;

        Assert.NotNull(output);
        // The foreach header binds a fresh variable and the inline use is rebound
        // to it; the parallel manual enumerator keeps its own MoveNext/Current.
        Assert.Contains("foreach (int ", output);
        Assert.Contains("in a)", output);
        Assert.Contains("other.MoveNext();", output);
        // No compiler enumerator loop survives: the foreach's own
        // GetEnumerator/MoveNext are consumed (the parallel `other` enumerator
        // keeps its own, which is expected).
        Assert.DoesNotContain("using (", output);
        Assert.DoesNotContain(".MoveNext())", output);
    }

    [Fact]
    public void InlineCurrentForeach_WithoutSymbols_StaysUsingWhile()
    {
        // Same discriminator as the hoisted enumerator form: without the hidden
        // enumerator slot the compiler foreach cannot be told from a hand-written
        // using/while, so it declines and the loop stays lowered.
        var function = RaisedWithoutSymbols(
            nameof(CfgSampleClass.ForeachSingleUseWithParallelEnumerator));

        Assert.DoesNotContain(function.Descendants.OfType<ForeachStatement>(), _ => true);
        Assert.Single(function.Descendants.OfType<UsingStatement>());
    }

    [Fact]
    public void InlineCurrentForeach_ConditionalCurrentRead_StaysUsingWhile()
    {
        // The inline matcher must decline a loop whose single `e.Current` read is
        // reached conditionally (here, inside an `if`-then), the shape a
        // hand-written `while` produces when it reads Current only some
        // iterations — e.g. Enumerable.ElementAt's `if (index == 0) return
        // e.Current;`. csc emits that read after the branch test, so its source
        // offset is not the loop body's minimum; ReadOriginatesAtLoopBodyTop
        // therefore declines it. Re-hoisting it to a foreach header would run
        // get_Current every iteration, changing how often it executes (and, for a
        // throwing/side-effecting enumerator, observable behavior). The
        // enumerator is a hidden slot over a supported IEnumerable<int>, so the
        // symbol/collection gate passes and the decision rests entirely on the
        // provenance guard.
        var function = BuildInlineConditionalCurrentEnumeratorLoop();

        new ForeachStatementPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<ForeachStatement>());
        Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void InlineCurrentForeach_UnconditionalReadAfterSideEffect_StaysUsingWhile()
    {
        // Reorder guard: the single `e.Current` read is unconditional (it sits in
        // the loop-body `if`-*condition*, evaluated every iteration), so a
        // frequency-only check would wrongly accept it — but a side-effecting
        // `SideEffect()` call was emitted before it. Hoisting get_Current to the
        // foreach header would move it ahead of that call, changing their order
        // (observable if either throws or mutates shared state). The read's source
        // offset is not the loop body's minimum (the call's is), so
        // ReadOriginatesAtLoopBodyTop declines and the loop stays lowered.
        var function = BuildInlineCurrentReadAfterSideEffectLoop();

        new ForeachStatementPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<ForeachStatement>());
        Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void InlineCurrentForeach_ReadInsideTryAtBodyTop_StaysUsingWhile()
    {
        // Exception regions emit no IL of their own, so a hand-written
        // `try { if (e.Current == 0) ... } catch { ... }` at the loop-body top has
        // get_Current as the first *executable* instruction (offset == body top) —
        // the offset check alone would accept it. But the read is protected by the
        // handler; hoisting it to the foreach header moves get_Current out of the
        // try, so a throwing enumerator would escape the loop instead of being
        // caught. ReadOriginatesAtLoopBodyTop rejects a read wrapped in any
        // try/catch/finally within the body. (A real foreach whose iteration
        // variable is used inside a try must store it — the stack-cached inline
        // form cannot cross the region boundary — so this declines no real
        // foreach.)
        var function = BuildInlineCurrentReadInTryLoop();

        new ForeachStatementPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<ForeachStatement>());
        Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void InlineCurrentForeach_UnconditionalCurrentReadInsideIfCondition_RaisesToForeach()
    {
        // Positive control for the guard: the same hidden-enumerator loop, but the
        // single `e.Current` read sits in the loop-body `if`-*condition* as the
        // first operation, so its source offset is the loop body's minimum — the
        // provenance of a real foreach header. The inline matcher recovers the
        // foreach.
        var function = BuildInlineUnconditionalCurrentEnumeratorLoop();

        new ForeachStatementPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
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

    [Fact]
    public void EnumeratorUsingLoop_WithStructCollectionAddressReceiver_RendersValueCollection()
    {
        var function = BuildStructCollectionEnumeratorUsingWhile();

        new ForeachStatementPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var foreachStatement = Assert.Single(function.Descendants.OfType<ForeachStatement>());
        var collection = Assert.IsType<LoadLocal>(foreachStatement.Collection);
        Assert.Equal(2, collection.Index);
        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Empty(function.Descendants.OfType<WhileLoop>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("foreach (int value in items)", output);
        Assert.DoesNotContain("foreach (int value in ref items)", output);
        Assert.DoesNotContain(" in ref ", output);
    }

    [Fact]
    public void EnumeratorUsingLoop_WithStructFieldAddressReceiver_RendersValueCollection()
    {
        var function = BuildStructFieldCollectionEnumeratorUsingWhile();

        new ForeachStatementPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var foreachStatement = Assert.Single(function.Descendants.OfType<ForeachStatement>());
        var collection = Assert.IsType<LoadField>(foreachStatement.Collection);
        Assert.Equal("items", collection.Field.Name);
        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Empty(function.Descendants.OfType<WhileLoop>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("foreach (int value in items)", output);
        Assert.DoesNotContain("foreach (int value in ref items)", output);
        Assert.DoesNotContain(" in ref ", output);
    }

    [Fact]
    public void EnumeratorUsingLoop_WithCopiedCurrentReceiver_StaysUsingWhile()
    {
        var function = BuildEnumeratorUsingWhileWithCopiedCurrentReceiver();

        new ForeachStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ForeachStatement>());
        Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<WhileLoop>());
        function.CheckInvariant();
    }

    static IrFunction BuildEnumeratorUsingWhileWithCopiedCurrentReceiver()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var enumerableType = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"),
            [intType]);
        var enumeratorType = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "IEnumerator`1"),
            [intType]);
        var getEnumerator = new MethodRef(enumerableType, "GetEnumerator", enumeratorType, [], HasThis: true);
        var moveNext = new MethodRef(enumeratorType, "MoveNext", boolType, [], HasThis: true);
        var current = new MethodRef(enumeratorType, "get_Current", intType, [], HasThis: true)
        {
            IsSpecialName = true,
        };

        var loopBody = new Block();
        loopBody.Add(new StoreLocal(2, enumeratorType, new LoadLocal(0, enumeratorType)));
        loopBody.Add(new StoreLocal(1, intType, new LoadProperty(current, new LoadLocal(2, enumeratorType), [])));

        var usingBody = new BlockContainer();
        var usingBlock = new Block();
        usingBlock.Add(new WhileLoop(new Call(moveNext, isVirtual: true, [new LoadLocal(0, enumeratorType)]), loopBody));
        usingBody.Add(usingBlock);

        var entry = new Block();
        entry.Add(new UsingStatement(0, enumeratorType, new Call(getEnumerator, isVirtual: true, [new LoadArgument(0, "items", enumerableType)]), usingBody));
        var body = new BlockContainer();
        body.Add(entry);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("items", enumerableType)], HasThis: false, GenericParameterCount: 0),
            [enumeratorType, intType, enumeratorType],
            body);
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

    // A compiler foreach over IEnumerable<int> whose single-use iteration
    // variable's `e.Current` read was inlined by ExpressionInliningPass — but
    // into an `if` branch, so the read is reached only some iterations. Models
    // the Enumerable.ElementAt shape the inline matcher must decline. When
    // <paramref name="conditional"/> is false the read instead sits in the
    // loop-body `if`-condition (evaluated every iteration), the safe shape the
    // matcher raises.
    enum InlineReadPlacement
    {
        // e.Current read is the first operation of the loop body (foreach header).
        BodyTop,
        // e.Current read sits inside an `if`-then, reached only some iterations.
        ConditionalThen,
        // e.Current read is unconditional but a side-effecting statement precedes
        // it in the body, so hoisting it to the loop top would reorder get_Current
        // ahead of that statement.
        AfterSideEffect,
        // e.Current read is the first executable instruction of the body but sits
        // inside a try, so hoisting it to the foreach header would move it out of
        // the protected region — the offset is body-top but the region is metadata.
        TryProtectedBodyTop,
    }

    static IrFunction BuildInlineCurrentEnumeratorLoop(InlineReadPlacement placement)
    {
        // The loop body's first IL instruction sits at this offset; the guard
        // compares the read's origin to the body block's import-stamped StartOffset.
        const int BodyStart = 0x10;

        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var voidType = TypeRef.CoreLib("System", "Void");
        var exceptionType = TypeRef.CoreLib("System", "Exception");
        var collectionType = TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1");
        var enumeratorType = TypeRef.CoreLib("System.Collections.Generic", "IEnumerator`1");
        var ownerType = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var getEnumerator = new MethodRef(collectionType, "GetEnumerator", enumeratorType, [], HasThis: true);
        var moveNext = new MethodRef(enumeratorType, "MoveNext", boolType, [], HasThis: true);
        var sideEffect = new MethodRef(ownerType, "SideEffect", voidType, [], HasThis: false);
        var current = new MethodRef(enumeratorType, "get_Current", intType, [], HasThis: true)
        {
            IsSpecialName = true,
        };

        // Stamp source offsets in emission order: the guard recovers foreach-header
        // provenance from them (the read is a real header only when its subtree's
        // minimum offset equals the loop body's entry offset).
        static T Stamp<T>(T node, int offset) where T : IrNode
        {
            node.SetSourceOffset(offset);
            return node;
        }

        LoadProperty CurrentRead(int receiverOffset)
            => Stamp(new LoadProperty(current, Stamp(new LoadLocal(0, enumeratorType), receiverOffset), []), receiverOffset + 1);

        var loopBody = new Block(BodyStart);
        switch (placement)
        {
            case InlineReadPlacement.BodyTop:
                // if (e.Current == 0) return true;  — the read is the first thing
                // in the body (in the `if`-condition), evaluated every iteration.
                {
                    var thenArm = new Block();
                    thenArm.Add(Stamp(new Return(Stamp(new Constant(1, boolType), BodyStart + 4)), BodyStart + 5));
                    loopBody.Add(Stamp(new IfStatement(
                        Stamp(new Comparison(ComparisonKind.Equal, isUnsigned: false, CurrentRead(BodyStart), Stamp(new Constant(0, intType), BodyStart + 2)), BodyStart + 3),
                        thenArm,
                        null), BodyStart + 3));
                }
                break;

            case InlineReadPlacement.ConditionalThen:
                // if (probe == 0) return e.Current;  — the read runs only when the
                // branch is taken, and is emitted after the probe comparison.
                {
                    var thenArm = new Block();
                    thenArm.Add(Stamp(new Return(CurrentRead(BodyStart + 5)), BodyStart + 7));
                    loopBody.Add(Stamp(new IfStatement(
                        Stamp(new Comparison(ComparisonKind.Equal, isUnsigned: false, Stamp(new LoadArgument(1, "probe", intType), BodyStart), Stamp(new Constant(0, intType), BodyStart + 1)), BodyStart + 2),
                        thenArm,
                        null), BodyStart + 2));
                }
                break;

            case InlineReadPlacement.AfterSideEffect:
                // SideEffect(); if (e.Current == 0) return true;  — the read is
                // unconditional (in the `if`-condition) but a side-effecting call
                // was emitted first, so get_Current is not the body's first op.
                {
                    loopBody.Add(Stamp(new ExpressionStatement(Stamp(new Call(sideEffect, isVirtual: false, []), BodyStart)), BodyStart));
                    var thenArm = new Block();
                    thenArm.Add(Stamp(new Return(Stamp(new Constant(1, boolType), BodyStart + 8)), BodyStart + 9));
                    loopBody.Add(Stamp(new IfStatement(
                        Stamp(new Comparison(ComparisonKind.Equal, isUnsigned: false, CurrentRead(BodyStart + 5), Stamp(new Constant(0, intType), BodyStart + 7)), BodyStart + 8),
                        thenArm,
                        null), BodyStart + 8));
                }
                break;

            case InlineReadPlacement.TryProtectedBodyTop:
                // try { if (e.Current == 0) return true; } catch { return false; }
                // — get_Current is the first executable instruction (offset ==
                // body top), but it is inside the try's protected region, which
                // emits no IL of its own. Hoisting it to the foreach header would
                // move it out of the handler's scope.
                {
                    var thenArm = new Block();
                    thenArm.Add(Stamp(new Return(Stamp(new Constant(1, boolType), BodyStart + 4)), BodyStart + 5));
                    var tryInner = new Block(BodyStart);
                    tryInner.Add(Stamp(new IfStatement(
                        Stamp(new Comparison(ComparisonKind.Equal, isUnsigned: false, CurrentRead(BodyStart), Stamp(new Constant(0, intType), BodyStart + 2)), BodyStart + 3),
                        thenArm,
                        null), BodyStart + 3));
                    var tryBody = new BlockContainer();
                    tryBody.Add(tryInner);

                    var catchInner = new Block(BodyStart + 0x10);
                    catchInner.Add(Stamp(new Return(Stamp(new Constant(0, boolType), BodyStart + 0x11)), BodyStart + 0x12));
                    var catchBody = new BlockContainer();
                    catchBody.Add(catchInner);
                    var catchClause = new CatchClause(exceptionType, catchBody);

                    loopBody.Add(Stamp(new TryCatch(tryBody, [catchClause]), BodyStart));
                }
                break;
        }

        var usingBody = new BlockContainer();
        var usingBlock = new Block();
        usingBlock.Add(new WhileLoop(new Call(moveNext, isVirtual: true, [new LoadLocal(0, enumeratorType)]), loopBody));
        usingBody.Add(usingBlock);

        var entry = new Block();
        entry.Add(new UsingStatement(0, enumeratorType, new Call(getEnumerator, isVirtual: true, [new LoadArgument(0, "items", collectionType)]), usingBody));
        var body = new BlockContainer();
        body.Add(entry);
        return new IrFunction(
            "M",
            ownerType,
            new MethodSignature(boolType, [new Parameter("items", collectionType), new Parameter("probe", intType)], HasThis: false, GenericParameterCount: 0),
            [enumeratorType],
            body);
    }

    static IrFunction BuildInlineConditionalCurrentEnumeratorLoop()
        => BuildInlineCurrentEnumeratorLoop(InlineReadPlacement.ConditionalThen);

    static IrFunction BuildInlineUnconditionalCurrentEnumeratorLoop()
        => BuildInlineCurrentEnumeratorLoop(InlineReadPlacement.BodyTop);

    static IrFunction BuildInlineCurrentReadAfterSideEffectLoop()
        => BuildInlineCurrentEnumeratorLoop(InlineReadPlacement.AfterSideEffect);

    static IrFunction BuildInlineCurrentReadInTryLoop()
        => BuildInlineCurrentEnumeratorLoop(InlineReadPlacement.TryProtectedBodyTop);

    static IrFunction BuildStructCollectionEnumeratorUsingWhile()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var collectionType = TypeRef.Definition("UserAssembly", "Samples", "StructCollection", ValueTypeHint.ValueType);
        var enumeratorType = TypeRef.Definition("UserAssembly", "Samples", "StructEnumerator", ValueTypeHint.ValueType);
        var getEnumerator = new MethodRef(collectionType, "GetEnumerator", enumeratorType, [], HasThis: true);
        var moveNext = new MethodRef(enumeratorType, "MoveNext", boolType, [], HasThis: true);
        var current = new MethodRef(enumeratorType, "get_Current", intType, [], HasThis: true)
        {
            IsSpecialName = true,
        };

        var loopBody = new Block();
        loopBody.Add(new StoreLocal(1, intType, new LoadProperty(current, new LoadLocalAddress(0, enumeratorType), [])));

        var usingBody = new BlockContainer();
        var usingBlock = new Block();
        usingBlock.Add(new WhileLoop(new Call(moveNext, isVirtual: false, [new LoadLocalAddress(0, enumeratorType)]), loopBody));
        usingBody.Add(usingBlock);

        var entry = new Block();
        entry.Add(new StoreLocal(2, collectionType, new LoadArgument(0, "source", collectionType)));
        entry.Add(new UsingStatement(0, enumeratorType, new Call(getEnumerator, isVirtual: false, [new LoadLocalAddress(2, collectionType)]), usingBody));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("source", collectionType)], HasThis: false, GenericParameterCount: 0),
            [enumeratorType, intType, collectionType],
            body);
        function.LocalNames = [null, "value", "items"];
        return function;
    }

    static IrFunction BuildStructFieldCollectionEnumeratorUsingWhile()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var ownerType = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var collectionType = TypeRef.Definition("UserAssembly", "Samples", "StructCollection", ValueTypeHint.ValueType);
        var enumeratorType = TypeRef.Definition("UserAssembly", "Samples", "StructEnumerator", ValueTypeHint.ValueType);
        var collectionField = new FieldRef(ownerType, "items", collectionType);
        var getEnumerator = new MethodRef(collectionType, "GetEnumerator", enumeratorType, [], HasThis: true);
        var moveNext = new MethodRef(enumeratorType, "MoveNext", boolType, [], HasThis: true);
        var current = new MethodRef(enumeratorType, "get_Current", intType, [], HasThis: true)
        {
            IsSpecialName = true,
        };

        var loopBody = new Block();
        loopBody.Add(new StoreLocal(1, intType, new LoadProperty(current, new LoadLocalAddress(0, enumeratorType), [])));

        var usingBody = new BlockContainer();
        var usingBlock = new Block();
        usingBlock.Add(new WhileLoop(new Call(moveNext, isVirtual: false, [new LoadLocalAddress(0, enumeratorType)]), loopBody));
        usingBody.Add(usingBlock);

        var entry = new Block();
        entry.Add(new UsingStatement(
            0,
            enumeratorType,
            new Call(getEnumerator, isVirtual: false, [new LoadFieldAddress(collectionField, new LoadArgument(0, "this", ownerType))]),
            usingBody));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "M",
            ownerType,
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: true, GenericParameterCount: 0),
            [enumeratorType, intType],
            body);
        function.LocalNames = [null, "value"];
        return function;
    }

    static IrFunction BuildPatternEnumeratorLoop(TypeRef moveNextReturnType)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var collectionType = TypeRef.Definition("UserAssembly", "Samples", "CustomCollection");
        var enumeratorType = TypeRef.Definition("UserAssembly", "Samples", "CustomEnumerator", ValueTypeHint.ValueType);
        var getEnumerator = new MethodRef(collectionType, "GetEnumerator", enumeratorType, [], HasThis: true);
        var moveNext = new MethodRef(enumeratorType, "MoveNext", moveNextReturnType, [], HasThis: true);
        var current = new MethodRef(enumeratorType, "get_Current", intType, [], HasThis: true)
        {
            IsSpecialName = true,
        };

        var loopBody = new Block();
        loopBody.Add(new StoreLocal(1, intType, new LoadProperty(current, new LoadLocalAddress(0, enumeratorType), [])));

        var entry = new Block();
        entry.Add(new StoreLocal(0, enumeratorType, new Call(getEnumerator, isVirtual: false, [new LoadArgument(0, "items", collectionType)])));
        entry.Add(new WhileLoop(new Call(moveNext, isVirtual: false, [new LoadLocalAddress(0, enumeratorType)]), loopBody));

        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("items", collectionType)], HasThis: false, GenericParameterCount: 0),
            [enumeratorType, intType],
            body);
        function.LocalNames = [null, "value"];
        return function;
    }
}
