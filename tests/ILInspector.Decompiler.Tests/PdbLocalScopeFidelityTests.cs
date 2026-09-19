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
            nameof(PdbScopeFixtures.SequentialScopeLocalsWithGoto),
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

    [Fact]
    public void InternalLabelScopes_CompileBackSuccessfully()
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            typeof(PdbScopeFixtures).Assembly.Location,
            type => type == typeof(PdbScopeFixtures).FullName,
            method => method.Method
                == nameof(PdbScopeFixtures.SequentialScopeLocalsWithInternalLabels)));

        Assert.True(
            result.Status is FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }

    [Fact]
    public void SwitchSectionOutVariables_CompileBackSuccessfully()
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            typeof(PdbScopeFixtures).Assembly.Location,
            type => type == typeof(PdbScopeFixtures).FullName,
            method => method.Method
                == nameof(PdbScopeFixtures.SwitchSectionOutVariables)));

        Assert.True(
            result.Status is FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }

    [Fact]
    public void DisjointPatternNames_CompileBackExactly()
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            typeof(PdbScopeFixtures).Assembly.Location,
            type => type == typeof(PdbScopeFixtures).FullName,
            method => method.Method == nameof(PdbScopeFixtures.SequentialPatterns)));

        Assert.True(
            result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }

    [Fact]
    public void DisjointOutVariableNames_CompileBackExactly()
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            typeof(PdbScopeFixtures).Assembly.Location,
            type => type == typeof(PdbScopeFixtures).FullName,
            method => method.Method == nameof(PdbScopeFixtures.SequentialOutVariables)));

        Assert.True(
            result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }

    [Fact]
    public void SwitchExpressionOutVariableNames_CompileBackSuccessfully()
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            typeof(PdbScopeFixtures).Assembly.Location,
            type => type == typeof(PdbScopeFixtures).FullName,
            method => method.Method == nameof(PdbScopeFixtures.SwitchExpressionOutVariables)));

        Assert.True(
            result.Status is FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }
}
