using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class AwaitRecoveryPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    [Fact]
    public void Await_RecoversValueProducingAwait()
    {
        var function = Raised(nameof(CfgSampleClass.AwaitOnce));

        Assert.Single(function.Descendants.OfType<AwaitExpression>());
        // The synthetic AsyncHelpers.Await call is gone.
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Await");
    }

    [Fact]
    public void Await_RecoversVoidAwait()
    {
        var function = Raised(nameof(CfgSampleClass.AwaitVoid));

        var await = Assert.Single(function.Descendants.OfType<AwaitExpression>());
        // Non-generic AsyncHelpers.Await returns void.
        Assert.Equal("void", await.ResultType?.ToDisplayString());
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Await");
    }

    [Fact]
    public void PrintValueAwait_RendersAwaitKeyword()
    {
        var output = Print(nameof(CfgSampleClass.AwaitOnce));

        Assert.Contains("await", output);
        Assert.DoesNotContain("AsyncHelpers", output);
    }

    [Fact]
    public void PrintVoidAwait_RendersAwaitStatement()
    {
        var output = Print(nameof(CfgSampleClass.AwaitVoid));

        Assert.Contains("await t", output);
        Assert.DoesNotContain("AsyncHelpers", output);
    }

    [Fact]
    public void NonAsyncMethod_HasNoAwaitExpression()
    {
        var function = Raised(nameof(CfgSampleClass.ToByte));

        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
    }

    [Fact]
    public void TwoAwaits_PreserveSourceOrder()
    {
        // Runtime-async keeps the first await's value on the evaluation stack
        // across the second await, so `x = await a; y = await b;` imports with
        // `await a` stranded below the `y = await b` store. The importer must
        // spill the earlier await to pin its position; otherwise it reorders to
        // `int y = await b; return (await a) + y;` — awaiting b before a.
        var output = Print(nameof(CfgSampleClass.AwaitTwo));

        int awaitA = output.IndexOf("await a", StringComparison.Ordinal);
        int awaitB = output.IndexOf("await b", StringComparison.Ordinal);
        Assert.True(awaitA >= 0, $"missing `await a` in:\n{output}");
        Assert.True(awaitB >= 0, $"missing `await b` in:\n{output}");
        Assert.True(awaitA < awaitB, $"`await a` must precede `await b`:\n{output}");
    }

    [Fact]
    public void TwoAwaits_DoNotInlineFirstPastSecond()
    {
        // The first await must remain a standalone statement (spilled to a temp),
        // never folded into the return where it would sit after the second await.
        var function = Raised(nameof(CfgSampleClass.AwaitTwo));

        var awaits = function.Descendants.OfType<AwaitExpression>().ToList();
        Assert.Equal(2, awaits.Count);
        // Neither await is nested inside the other (no reordering collapse).
        Assert.DoesNotContain(awaits, outer => outer.Descendants.OfType<AwaitExpression>().Any());
    }
}
