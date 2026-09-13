using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
[Trait("Area", "Fidelity")]
public class LocalFunctionArgumentFidelityTests
{
    [Fact]
    public void RaisedLocalFunctionArguments_CompileBackExactly()
    {
        var type = typeof(LocalFunctionArgumentSamples);
        string[] methods =
        [
            nameof(LocalFunctionArgumentSamples.RefArgument),
            nameof(LocalFunctionArgumentSamples.OutArgument),
            nameof(LocalFunctionArgumentSamples.InArgument),
            nameof(LocalFunctionArgumentSamples.ValueArgument),
        ];

        var results = FidelityCheck.Evaluate(
            type.Assembly.Location,
            candidate => candidate == type.FullName,
            method => methods.Contains(method.Method, StringComparer.Ordinal));

        Assert.Equal(
            methods.Order(StringComparer.Ordinal),
            results.Select(result => result.Method).Order(StringComparer.Ordinal));
        Assert.All(results, result => Assert.True(
            result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }
}
