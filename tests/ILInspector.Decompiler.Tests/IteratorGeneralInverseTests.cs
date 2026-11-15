using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The general iterator inverse (docs/design/iterator-reconstruction.md, stage
/// 1): kickoffs every shape matcher declines are reconstructed from the raw
/// MoveNext, and the decline boundary states its reason on the honest marker.
/// </summary>
[Trait("Area", "Pass")]
public class IteratorGeneralInverseTests
{
    static string FixturePath => typeof(LadderIterator.IteratorSamples).Assembly.Location;
    static readonly string SamplesType = typeof(LadderIterator.IteratorSamples).FullName!;
    static readonly string PointType = typeof(LadderIterator.IteratorPoint).FullName!;

    static (IrFunction Function, string Output) Raised(string typeName, string methodName)
    {
        using var source = MetadataSource.Open(FixturePath);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded);
        return (function!, (result.Output ?? "").Trim());
    }

    [Fact]
    public void HoistedParameterInYieldedValue_Reconstructs()
    {
        var (function, output) = Raised(SamplesType, nameof(LadderIterator.IteratorSamples.CapturedYieldExpression));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal("yield return value * 2;\nyield return value * 3;", output);
    }

    [Fact]
    public void HoistedParameterSideEffectBeforeBreak_KeepsTheSideEffect()
    {
        var (function, output) = Raised(SamplesType, nameof(LadderIterator.IteratorSamples.CapturedParameterSideEffectThenBreak));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal("Console.WriteLine(value);\nyield break;", output);
    }

    [Fact]
    public void ValueTypeReceiver_ReadsMembersThroughThis()
    {
        var (function, output) = Raised(PointType, nameof(LadderIterator.IteratorPoint.InstanceSequence));

        // The machine reads the copied receiver through <>4__this's address;
        // the source reads the same members through `this`.
        Assert.Equal("yield return x + y;\nyield return z;", output);
        Assert.Empty(function.Descendants.OfType<UnsupportedNode>());
        Assert.DoesNotContain("<>4__this", output);
    }

    [Theory]
    [InlineData(nameof(LadderIterator.IteratorSamples.UserFinally))]
    [InlineData(nameof(LadderIterator.IteratorSamples.UsingStatement))]
    public void YieldInsideTry_DeclinesWithItsReason(string methodName)
    {
        var (function, output) = Raised(SamplesType, methodName);

        Assert.NotEqual(DecompilationFidelity.Full, function.Fidelity);
        var marker = Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal("iterator", marker.Opcode);
        Assert.Contains("not reconstructed: a yield inside a try block lowers to finally frames", marker.Reason);
        Assert.Empty(function.Descendants.OfType<YieldReturn>());
        Assert.Contains("return default;", output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void ReconstructedRows_CompileBackOpcodeExact()
    {
        var results = FidelityCheck.Evaluate(FixturePath).Where(r => r.Type == SamplesType).ToList();

        foreach (string name in new[]
        {
            nameof(LadderIterator.IteratorSamples.CapturedYieldExpression),
            nameof(LadderIterator.IteratorSamples.CapturedParameterSideEffectThenBreak),
        })
        {
            var result = Assert.Single(results, r => r.Method == name);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
    }
}
