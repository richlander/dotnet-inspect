using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ClassicAsyncReconstructionHonestyTests
{
    const string FixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures";

    [Theory]
    [InlineData("SequentialWithFieldStore")]
    [InlineData("LoopWithFieldStore")]
    public void UnconsumedUserStoreDeclinesAtPartialFidelity(
        string methodName)
    {
        using var source = OpenClassicFixture(readSymbols: true);
        IrFunction function = ImportAndRaise(source, methodName);

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.False(result.RequiresAsyncBodyModifier);
        var marker = Assert.Single(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode == "classic async");
        Assert.Contains(
            "unconsumed user effects",
            marker.Reason,
            StringComparison.Ordinal);
        Assert.Contains(
            "original kickoff preserved",
            marker.Reason,
            StringComparison.Ordinal);
        Assert.Contains(
            function.Diagnostics,
            diagnostic =>
                diagnostic.Id == DiagnosticIds.UnsupportedConstruct
                && diagnostic.Message.Contains(
                    "unconsumed user effects",
                    StringComparison.Ordinal));
        Assert.Contains(
            "unconsumed user effects",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Start<",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Observed =",
            result.Output,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TwoSequentialAwaits", "GC.KeepAlive((x, y));")]
    [InlineData("AwaitInLoop", "foreach (Task<int> task in tasks)")]
    public void FaithfulLegacyRecipeRemainsFullyReconstructed(
        string methodName,
        string expectedOutput)
    {
        using var source = OpenClassicFixture(readSymbols: true);
        IrFunction function = ImportAndRaise(source, methodName);

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.DoesNotContain(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode == "classic async");
        Assert.Contains(
            expectedOutput,
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SequentialAwaitLocalNameComesFromSymbols()
    {
        using var source = OpenClassicFixture(readSymbols: true);
        IrFunction function = ImportAndRaise(
            source,
            "TwoSequentialNamedAwaits");

        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains(
            "int alpha = await a;",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "int beta = await b;",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "GC.KeepAlive((alpha, beta));",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SequentialAwaitLocalNameIsNotInventedWithoutSymbols()
    {
        using var source = OpenClassicFixture(readSymbols: false);
        IrFunction function = ImportAndRaise(
            source,
            "TwoSequentialNamedAwaits");

        string output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain(
            "int y =",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "(alpha, y)",
            output,
            StringComparison.Ordinal);
    }

    static MetadataSource OpenClassicFixture(bool readSymbols)
    {
        string configuration =
            new DirectoryInfo(AppContext.BaseDirectory).Name;
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "ILInspector.Decompiler.Fixtures.ClassicAsync",
            configuration,
            "ILInspector.Decompiler.Fixtures.ClassicAsync.dll"));
        return readSymbols
            ? MetadataSource.Open(path)
            : MetadataSource.OpenWithoutSymbols(path);
    }

    static IrFunction ImportAndRaise(
        MetadataSource source,
        string methodName)
    {
        IrFunction? function = IrImporter.Import(
            source,
            FixtureType,
            methodName);
        Assert.NotNull(function);

        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));
        function.CheckInvariant();
        return function;
    }
}
