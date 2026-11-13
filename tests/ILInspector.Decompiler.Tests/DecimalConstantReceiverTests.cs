using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class DecimalConstantReceiverTests
{
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(nameof(DecimalConstantReceiverSamples.Zero))]
    [InlineData(nameof(DecimalConstantReceiverSamples.One))]
    [InlineData(nameof(DecimalConstantReceiverSamples.MinusOne))]
    public async Task CompilerConstantReceiver_PreservesFieldReadAndNativeFidelity(string method)
    {
        string path = typeof(DecimalConstantReceiverSamples).Assembly.Location;
        string type = typeof(DecimalConstantReceiverSamples).FullName!;
        using var source = MetadataSource.Open(path);
        var function = IrImporter.Import(source, type, method)!;
        Assert.Contains(function.Descendants.OfType<StoreLocal>(), store => store.Value is LoadField);
        var projection = ValidityCheck.RenderProjection(source, function);
        Assert.Equal(DecompilationFidelity.Full, projection.Fidelity);
        Assert.Contains($"decimal.{method}.CompareTo(value)", projection.Output);
        Assert.DoesNotContain(function.Descendants.OfType<StoreLocal>(), store => store.Value is LoadField);
        function.CheckInvariant();

        var results = await FidelityCheck.EvaluateChangedMethodTargetsForTesting([path],
            [new(path, type, method, 0, CorpusMethodIdentity.SignatureText(function.Signature))]);
        var result = Assert.Single(results);
        Assert.Contains("ldsfld stloc ldloca", result.OriginalOpcodes);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
    }

    [Theory]
    [InlineData(nameof(DecimalConstantReceiverSamples.MinValue))]
    [InlineData(nameof(DecimalConstantReceiverSamples.MaxValue))]
    public void OtherDecimalConstants_AreOutsideFieldReadFold(string method)
    {
        string path = typeof(DecimalConstantReceiverSamples).Assembly.Location;
        string type = typeof(DecimalConstantReceiverSamples).FullName!;
        using var source = MetadataSource.Open(path);
        var function = IrImporter.Import(source, type, method)!;
        Assert.DoesNotContain(function.Descendants.OfType<LoadField>(), field => field.Field.Name == method);
        var stores = function.Descendants.OfType<StoreLocal>().ToArray();
        Assert.NotEmpty(stores);
        new StructReceiverInliningPass().Run(function, PassContext.None);
        Assert.Equal(stores, function.Descendants.OfType<StoreLocal>().ToArray());
        function.CheckInvariant();
    }
}
