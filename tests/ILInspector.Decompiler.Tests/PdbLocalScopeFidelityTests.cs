using CSharpText.Tests;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
[Trait("Area", "Fidelity")]
public sealed class PdbLocalScopeFidelityTests
{
    [Fact]
    public void DisjointSequentialAndValueTypeNames_CompileBackExactly()
    {
        string[] methods =
        [
            nameof(PdbScopeFixtures.DisjointScopeLocals),
            nameof(PdbScopeFixtures.SequentialScopeLocals),
            nameof(PdbScopeFixtures.SequentialValueTypeScopeLocals),
        ];
        var results = FidelityCheck.Evaluate(
            typeof(PdbScopeFixtures).Assembly.Location,
            type => type == typeof(PdbScopeFixtures).FullName,
            method => methods.Contains(method.Method, StringComparer.Ordinal));

        Assert.Equal(methods.Order(StringComparer.Ordinal),
            results.Select(result => result.Method).Order(StringComparer.Ordinal));
        Assert.All(results, result => Assert.True(
            result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }
}
