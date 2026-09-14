using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Validity")]
public class ValidityPipelineParityTests
{
    [Theory]
    [InlineData(nameof(HeterogeneousArmSample.GuardedArea), true)]
    [InlineData(nameof(HeterogeneousArmSample.Area), false)]
    public void RaisedValidityMatchesProductDisjointnessCapabilities(
        string methodName,
        bool requiresDisjointness)
    {
        var type = typeof(HeterogeneousArmSample);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var expectedFunction = Import(source, type, methodName);
        var actualFunction = Import(source, type, methodName);
        var conservativeFunction = Import(source, type, methodName);

        string? expected = CSharpPrinter.PrintRaised(
            expectedFunction,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint).Output;
        string? actual =
            ValidityCheck.RenderProjection(source, actualFunction).Output;
        string? conservative = CSharpPrinter.PrintRaised(
            conservativeFunction,
            method => IrImporter.Import(source, method)).Output;

        Assert.Contains("switch", expected);
        Assert.Equal(expected, actual);
        if (requiresDisjointness)
            Assert.DoesNotContain("switch", conservative);
        else
            Assert.Equal(expected, conservative);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidityUsesSiblingImportForGenericTypeLocalFunctions(
        bool lowered)
    {
        var type = typeof(GenericTypeLocalFunctionSamples<>);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = Import(
            source,
            type,
            nameof(GenericTypeLocalFunctionSamples<int>.NoTypeParameter));

        string? output =
            ValidityCheck.RenderProjection(source, function, lowered).Output;

        Assert.Contains("return Own(value);", output);
        Assert.Contains("static int Own(int input) => input + 1;", output);
        Assert.DoesNotContain("_g__Own_", output);
    }

    static IrFunction Import(MetadataSource source, Type type, string methodName)
    {
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);
        return function!;
    }
}
