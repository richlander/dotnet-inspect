using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class LoopLocalFunctionTests
{
    [Theory]
    [InlineData(nameof(LoopLocalFunctionSamples.DoWhile), 1)]
    [InlineData(nameof(LoopLocalFunctionSamples.While), 1)]
    [InlineData(nameof(LoopLocalFunctionSamples.Dependency), 2)]
    public void CompilerStructuredLoop_RaisesCompleteLocalFunctions(string method, int count)
    {
        using var source = MetadataSource.Open(typeof(LoopLocalFunctionSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(LoopLocalFunctionSamples).FullName!, method);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function, reference => IrImporter.Import(source, reference));
        Assert.True(result.Succeeded);
        Assert.Equal(count, function.Descendants.OfType<LocalFunctionStatement>().Count());
        Assert.Contains("while (", result.Output);
        Assert.DoesNotContain("g__", result.Output);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void LoopDependencyWithUnavailableBody_RemainsPartial()
    {
        using var source = MetadataSource.Open(typeof(LoopLocalFunctionSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(LoopLocalFunctionSamples).FullName!, nameof(LoopLocalFunctionSamples.Dependency));
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function, reference => reference.Name.Contains("g__Loop|", StringComparison.Ordinal)
            ? null : IrImporter.Import(source, reference));
        Assert.Empty(function.Descendants.OfType<LocalFunctionStatement>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.Contains("g__", result.Output);
        function.CheckInvariant();
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    [InlineData(false)]
    [InlineData(true)]
    public void CompilerStructuredLoops_CompileBackExact(bool lowered)
    {
        var results = FidelityCheck.Evaluate(typeof(LoopLocalFunctionSamples).Assembly.Location,
            lowered: lowered, type => type == typeof(LoopLocalFunctionSamples).FullName).ToArray();
        Assert.Equal(3, results.Length);
        Assert.All(results, result => Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact && result.UsedProductWholeMember,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }
}

public static class LoopLocalFunctionSamples
{
    public static int DoWhile(int value)
    {
        return Loop(value);
        static int Loop(int remaining)
        {
            int sum = 0;
            do { sum += remaining; remaining--; } while (remaining > 0);
            return sum;
        }
    }

    public static int While(int value)
    {
        return Loop(value);
        static int Loop(int remaining)
        {
            int sum = 0;
            while (remaining > 0)
            {
                remaining--;
                if (remaining == 2) continue;
                sum += remaining;
                if (sum > 20) break;
            }
            return sum;
        }
    }

    public static bool Dependency(int value)
    {
        return Dispatch(value);
        static bool Dispatch(int input) => Loop(input);
        static bool Loop(int remaining)
        {
            do
            {
                if (remaining < 0) return false;
                remaining--;
            } while (remaining > 0);
            return true;
        }
    }
}
