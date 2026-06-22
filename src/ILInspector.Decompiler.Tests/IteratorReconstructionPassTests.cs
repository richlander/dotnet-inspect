using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class IteratorReconstructionPassTests
{
    // Reconstruction needs the cross-method seam (to import the state machine's
    // MoveNext); IrPasses.Run(function) uses PassContext.None and would leave the
    // kickoff for the acknowledgment fallback instead.
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        var context = new PassContext(new Stepper(enabled: false),
            importMethodBody: method => IrImporter.Import(source, method));
        IrPasses.Run(function!, IrPasses.Default, context);
        function!.CheckInvariant();
        return function!;
    }

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    [Fact]
    public void LinearConstantIterator_ReconstructsYieldSequence()
    {
        var function = Raised(nameof(CfgSampleClass.YieldTwo));

        var yields = function.Descendants.OfType<YieldReturn>().ToList();
        Assert.Equal(2, yields.Count);
        Assert.Equal(1, Assert.IsType<Constant>(yields[0].Value).Value);
        Assert.Equal(2, Assert.IsType<Constant>(yields[1].Value).Value);

        // The misleading state-machine handoff and acknowledgment marker are gone.
        Assert.Empty(function.Descendants.OfType<NewObject>());
        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
    }

    [Fact]
    public void ReconstructedIterator_RendersYieldReturns()
    {
        var output = Print(nameof(CfgSampleClass.YieldTwo));

        Assert.Contains("yield return 1;", output);
        Assert.Contains("yield return 2;", output);
        Assert.DoesNotContain("not reconstructed", output);
    }

    [Fact]
    public void ReconstructedIterator_IsFullFidelity()
    {
        Assert.Equal(DecompilationFidelity.Full, Raised(nameof(CfgSampleClass.YieldTwo)).Fidelity);
    }

    [Fact]
    public void CountingLoopIterator_ReconstructsWhileLoop()
    {
        var function = Raised(nameof(CfgSampleClass.YieldRange));

        // The `for (int i = 0; i < n; i++) yield return i;` shape comes back as a
        // structured loop with a single yield — not the acknowledgment fallback.
        var yield = Assert.Single(function.Descendants.OfType<YieldReturn>());
        Assert.IsType<LoadLocal>(yield.Value);
        Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void CountingLoopIterator_RendersLoopAndYield()
    {
        var output = Print(nameof(CfgSampleClass.YieldRange));

        Assert.Contains("int i = 0;", output);
        Assert.Contains("while (i < n)", output);
        Assert.Contains("yield return i;", output);
        Assert.DoesNotContain("not reconstructed", output);
    }

    [Fact]
    public void CountingLoopIterator_ConstantBoundAndArithmeticElement()
    {
        // Constant bound, no parameter, and an arithmetic yielded value exercise the
        // self-contained remap (hoisted loop field -> local) without a parameter.
        var output = Print(nameof(CfgSampleClass.YieldSquares));

        Assert.Contains("while (i < 4)", output);
        Assert.Contains("yield return i * i;", output);
    }

    [Fact]
    public void NestedLoopIterator_ReconstructsNestedLoops()
    {
        var function = Raised(nameof(CfgSampleClass.YieldGrid));

        // `for i … for j … yield return i + j;` lowers to an irreducible state
        // dispatch (the resume edge jumps into the inner loop). The transform-then-
        // restructure path strips the scaffolding to make it reducible, then the
        // structurer raises both loops — no acknowledgment marker.
        Assert.Single(function.Descendants.OfType<YieldReturn>());
        Assert.Equal(2, function.Descendants.OfType<ForLoop>().Count());
        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void NestedLoopIterator_RendersBothLoopsAndYield()
    {
        var output = Print(nameof(CfgSampleClass.YieldGrid));

        Assert.Contains("for (int i = 0;", output);
        Assert.Contains("for (int j = 0;", output);
        Assert.Contains("yield return i + j;", output);
        Assert.DoesNotContain("not reconstructed", output);
    }

    [Fact]
    public void NestedLoopIterator_FoldsBothForUpdatesToOperator()
    {
        // Both loops' increments are rebuilt through a single shared spill slot
        // (`V_1 = j; … V_1 = i;` captures with `j = V_1 + 1` / `i = V_1 + 1`
        // updates); the post-reconstruction IncrementDecrementPass inlines the
        // shared temp so both updates spell `i++` / `j++` and the temp disappears.
        var output = Print(nameof(CfgSampleClass.YieldGrid));

        Assert.Contains("for (int i = 0; i < 2; i++)", output);
        Assert.Contains("for (int j = 0; j < 2; j++)", output);
        Assert.DoesNotContain("V_1", output);
        Assert.DoesNotContain("+ 1", output);
    }

    [Fact]
    public void NonIterator_IsUnaffected()
    {
        var function = Raised(nameof(CfgSampleClass.NotAnIterator));

        Assert.Empty(function.Descendants.OfType<YieldReturn>());
        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Contains("source", Print(nameof(CfgSampleClass.NotAnIterator)));
    }

    [Fact]
    public void ThreeYieldChain_ReconstructsAllElements()
    {
        var function = Raised(nameof(CfgSampleClass.YieldThree));

        var values = function.Descendants.OfType<YieldReturn>()
            .Select(y => Assert.IsType<Constant>(y.Value).Value).ToList();
        Assert.Equal(new object?[] { 10, 20, 30 }, values);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void StringElementIterator_ReconstructsReferenceLiterals()
    {
        var output = Print(nameof(CfgSampleClass.YieldStrings));

        Assert.Contains("yield return \"a\";", output);
        Assert.Contains("yield return \"b\";", output);
    }

    [Fact]
    public void EnumeratorReturningIterator_IsAlsoReconstructed()
    {
        var function = Raised(nameof(CfgSampleClass.YieldEnumerator));

        var values = function.Descendants.OfType<YieldReturn>()
            .Select(y => Assert.IsType<Constant>(y.Value).Value).ToList();
        Assert.Equal(new object?[] { 7, 8 }, values);
        Assert.Empty(function.Descendants.OfType<UnsupportedNode>());
    }

    [Fact]
    public void EmptyIterator_ReconstructsYieldBreak()
    {
        var function = Raised(nameof(CfgSampleClass.JustBreak));

        // No yields, exactly one `yield break;`, and no acknowledgment marker.
        Assert.Empty(function.Descendants.OfType<YieldReturn>());
        Assert.Single(function.Descendants.OfType<YieldBreak>());
        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void EmptyIterator_RendersYieldBreak()
    {
        var output = Print(nameof(CfgSampleClass.JustBreak));

        Assert.Contains("yield break;", output);
        Assert.DoesNotContain("not reconstructed", output);
    }

    [Fact]
    public void NonEmptyIterator_HasNoSpuriousYieldBreak()
    {
        // A normal linear iterator falls off the end implicitly; reconstruction
        // must not append a trailing `yield break;`.
        var function = Raised(nameof(CfgSampleClass.YieldTwo));

        Assert.Empty(function.Descendants.OfType<YieldBreak>());
    }

    [Fact]
    public void ConditionalYieldIterator_ReconstructsGuardedYield()
    {
        var function = Raised(nameof(CfgSampleClass.YieldIf));

        // `if (flag) yield return 1; yield return 2;` — two yields, one under a
        // parameter-tested guard — comes back structured, not the fallback marker.
        var yields = function.Descendants.OfType<YieldReturn>().ToList();
        Assert.Equal(2, yields.Count);
        Assert.Single(function.Descendants.OfType<IfStatement>());
        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void ConditionalYieldIterator_RendersGuardAndBothYields()
    {
        var output = Print(nameof(CfgSampleClass.YieldIf));

        Assert.Contains("if (flag)", output);
        Assert.Contains("yield return 1;", output);
        Assert.Contains("yield return 2;", output);
        Assert.DoesNotContain("not reconstructed", output);
    }

    [Fact]
    public void MultiYieldLoopIterator_ReconstructsBothYieldsInLoop()
    {
        var function = Raised(nameof(CfgSampleClass.YieldPairs));

        // `for (int i = 0; i < n; i++) { yield return i; yield return -i; }` —
        // two yields per iteration — keeps the loop and both yields.
        Assert.Equal(2, function.Descendants.OfType<YieldReturn>().Count());
        Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void MultiYieldLoopIterator_RendersLoopAndBothYields()
    {
        var output = Print(nameof(CfgSampleClass.YieldPairs));

        Assert.Contains("int i = 0;", output);
        Assert.Contains("while (i < n)", output);
        Assert.Contains("yield return i;", output);
        Assert.Contains("yield return -i;", output);
        Assert.DoesNotContain("not reconstructed", output);
    }

    [Fact]
    public void MultiYieldLoopIterator_FoldsRebuiltIncrementToOperator()
    {
        // Reconstruction rebuilds the hoisted loop variable's increment through a
        // spill slot (`V_1 = i; i = V_1 + 1;`); the post-reconstruction
        // IncrementDecrementPass run folds that dead-temp shape back into `i++`.
        var output = Print(nameof(CfgSampleClass.YieldPairs));

        Assert.Contains("i++;", output);
        Assert.DoesNotContain("= V_1 + 1", output);
        Assert.DoesNotContain("V_1 = i;", output);
    }

    [Fact]
    public void SideEffectingEmptyIterator_PreservesSideEffectBeforeBreak()
    {
        var function = Raised(nameof(CfgSampleClass.BreakWithSideEffect));

        // A yield-nothing iterator that runs a side effect first is NOT a bare
        // `yield break;` — the call must survive, ahead of the break, at Full
        // fidelity and with no acknowledgment marker.
        Assert.Single(function.Descendants.OfType<YieldBreak>());
        Assert.Empty(function.Descendants.OfType<YieldReturn>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "WriteLine");
        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void SideEffectingEmptyIterator_RendersCallThenBreak()
    {
        var output = Print(nameof(CfgSampleClass.BreakWithSideEffect));

        Assert.Contains("Console.WriteLine(\"side effect\");", output);
        Assert.Contains("yield break;", output);
        Assert.DoesNotContain("not reconstructed", output);
    }

    [Fact]
    public void ParameterReferencingEmptyIterator_DeclinesToAcknowledgment()
    {
        var function = Raised(nameof(CfgSampleClass.BreakWithParameterSideEffect));

        // The side effect reads a hoisted parameter field, unspeakable in the
        // kickoff's scope. Reconstruction must NOT silently collapse to `yield break;`
        // — it declines so the honest acknowledgment marker stands.
        Assert.Empty(function.Descendants.OfType<YieldBreak>());
        var marker = Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal("iterator", marker.Opcode);
    }

    [Fact]
    public void CollectionExpressionSpreadIterator_ReconstructsYieldButNotSpread()
    {
        var function = Raised(nameof(CfgSampleClass.YieldCollectionExpressionSpread));

        // The iterator layer reconstructs the single yield, but the yielded
        // spread collection expression (`[.. arch, true]`) stays at the lowered
        // inline-array/CopyTo/Slice altitude: the spread collection-target frontier
        // is unraised (see CollectionExpression ledger), so this composes two
        // independent frontiers rather than recovering the source idiom. The
        // reconstruction is still Full fidelity — honest, just lower altitude.
        Assert.Single(function.Descendants.OfType<YieldReturn>());
        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);

        // Pin the exact lowered spread shape so an array-spread raise cannot reach
        // into the iterator interaction without an explicit fixture proving it safe.
        var output = Print(nameof(CfgSampleClass.YieldCollectionExpressionSpread));
        Assert.Contains("new bool[1 + ", output);
        Assert.Contains(".CopyTo(", output);
        Assert.Contains(".Slice(", output);
        Assert.Contains("yield return", output);
        Assert.DoesNotContain("[..", output);
    }

    [Fact]
    public void ForeachDelegationIterator_ReconstructsForeach()
    {
        var function = Raised(nameof(CfgSampleClass.YieldEach));

        // foreach-over-source delegation lowers to an irreducible single-yield dispatch
        // with the iterator's split disposal idiom (a fault handler plus a `<>m__Finally1`
        // call). The transform-then-restructure path strips both the state scaffolding and
        // the disposal, then recovers the foreach — no acknowledgment marker.
        Assert.Single(function.Descendants.OfType<YieldReturn>());
        Assert.Single(function.Descendants.OfType<ForeachStatement>());
        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void ForeachDelegationIterator_RendersForeachAndYield()
    {
        var output = Print(nameof(CfgSampleClass.YieldEach));

        Assert.Contains("foreach (", output);
        Assert.Contains("in source)", output);
        Assert.Contains("yield return", output);
        Assert.DoesNotContain("not reconstructed", output);
        // The split disposal scaffolding is fully gone.
        Assert.DoesNotContain("Finally", output);
        Assert.DoesNotContain("GetEnumerator", output);
    }
}
