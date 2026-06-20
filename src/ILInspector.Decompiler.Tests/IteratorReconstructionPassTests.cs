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
    public void NestedLoopIterator_FallsBackToAcknowledgment()
    {
        var function = Raised(nameof(CfgSampleClass.YieldGrid));

        // Two hoisted loop fields and more than two states are outside the
        // single-loop slice, so reconstruction declines and the honest marker stands.
        Assert.Empty(function.Descendants.OfType<YieldReturn>());
        var marker = Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal("iterator", marker.Opcode);
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
}
