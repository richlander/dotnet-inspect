using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class ThrowingLocalFunctionTests
{
    [Theory]
    [InlineData(nameof(ThrowingLocalFunctionSamples.ThrowValue), 1)]
    [InlineData(nameof(ThrowingLocalFunctionSamples.ThrowVoidAfterEffect), 1)]
    [InlineData(nameof(ThrowingLocalFunctionSamples.ThrowDependency), 2)]
    public void CompilerTerminalThrow_RaisesWithCompleteDeclarations(string method, int declarationCount)
    {
        using var source = MetadataSource.Open(typeof(ThrowingLocalFunctionSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(ThrowingLocalFunctionSamples).FullName!, method);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function, reference => IrImporter.Import(source, reference));

        Assert.True(result.Succeeded);
        Assert.Equal(declarationCount, function.Descendants.OfType<LocalFunctionStatement>().Count());
        Assert.Contains("throw ", result.Output);
        Assert.DoesNotContain("g__", result.Output);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThrowingLocalWithUnavailableBody_RemainsPartial(bool dependency)
    {
        using var source = MetadataSource.Open(typeof(ThrowingLocalFunctionSamples).Assembly.Location);
        var method = dependency ? nameof(ThrowingLocalFunctionSamples.ThrowDependency) : nameof(ThrowingLocalFunctionSamples.ThrowValue);
        var function = IrImporter.Import(source, typeof(ThrowingLocalFunctionSamples).FullName!, method);
        Assert.NotNull(function);
        var result = dependency
            ? CSharpPrinter.PrintRaised(function, reference => reference.Name.Contains("g__Fail|", StringComparison.Ordinal)
                ? null : IrImporter.Import(source, reference))
            : CSharpPrinter.PrintRaised(function);

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
    public void CompilerTerminalThrows_CompileBackExact(bool lowered)
    {
        var results = FidelityCheck.Evaluate(typeof(ThrowingLocalFunctionSamples).Assembly.Location,
            lowered: lowered, type => type == typeof(ThrowingLocalFunctionSamples).FullName).ToArray();
        Assert.Equal(3, results.Length);
        Assert.All(results, result => Assert.True(
            result.Status == FidelityCheck.CompileBackStatus.Exact && result.UsedProductWholeMember,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }
}

public static class ThrowingLocalFunctionSamples
{
    public static int ThrowValue(int value)
    {
        return Fail(value);
        static int Fail(int input) { throw new ArgumentOutOfRangeException(nameof(input), input, null); }
    }

    public static void ThrowVoidAfterEffect(int value)
    {
        Fail(value);
        static void Fail(int input)
        {
            Console.WriteLine(input);
            throw new InvalidOperationException(input.ToString());
        }
    }

    public static int ThrowDependency(int value)
    {
        return Dispatch(value);
        static int Dispatch(int input) => Fail(input + 1);
        static int Fail(int input) { throw new InvalidOperationException(input.ToString()); }
    }
}
