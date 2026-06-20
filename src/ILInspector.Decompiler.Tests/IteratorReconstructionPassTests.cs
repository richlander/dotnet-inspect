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
    public void ParameterizedLoopIterator_FallsBackToAcknowledgment()
    {
        var function = Raised(nameof(CfgSampleClass.YieldRange));

        // The captured-param + loop shape is outside the linear-constant slice, so
        // reconstruction declines and the honest marker stands.
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
}
