using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
[Trait("Area", "Fidelity")]
public class GenericTypeLocalFunctionFidelityTests
{
    [Fact]
    public void RaisedGenericTypeLocalFunctions_CompileBackExactly()
    {
        string typeName = typeof(GenericTypeLocalFunctionSamples<>).FullName!;
        string[] methods =
        [
            nameof(GenericTypeLocalFunctionSamples<int>.NoTypeParameter),
            nameof(GenericTypeLocalFunctionSamples<int>.TypeParameterOnly),
            nameof(GenericTypeLocalFunctionSamples<int>.TypeAndMethodParameters),
        ];

        var results = FidelityCheck.Evaluate(
            typeof(GenericTypeLocalFunctionSamples<>).Assembly.Location,
            type => type == typeName,
            method => methods.Contains(method.Method, StringComparer.Ordinal));

        Assert.Equal(methods.Order(StringComparer.Ordinal), results.Select(result => result.Method).Order(StringComparer.Ordinal));
        Assert.All(results, result => Assert.True(
            result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }
}
