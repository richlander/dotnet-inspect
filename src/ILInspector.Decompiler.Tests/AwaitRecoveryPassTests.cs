using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class AwaitRecoveryPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "AwaitHolder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef AsyncHelpers = TypeRef.CoreLib("System.Runtime.CompilerServices", "AsyncHelpers");

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
    public void Await_RecoversValueTaskAwait()
    {
        var function = Raised(nameof(CfgSampleClass.AwaitValueTask));

        var await = Assert.Single(function.Descendants.OfType<AwaitExpression>());
        Assert.Equal("int", await.ResultType?.ToDisplayString());
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Await");
    }

    [Fact]
    public void Await_RecoversConfiguredAwaitable()
    {
        var function = Raised(nameof(CfgSampleClass.AwaitConfiguredTask));

        var await = Assert.Single(function.Descendants.OfType<AwaitExpression>());
        Assert.Equal("int", await.ResultType?.ToDisplayString());
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Await");
    }

    [Fact]
    public void Await_RecoversConfiguredValueTaskAwaitable()
    {
        var function = Raised(nameof(CfgSampleClass.AwaitConfiguredValueTask));

        var await = Assert.Single(function.Descendants.OfType<AwaitExpression>());
        Assert.Equal("int", await.ResultType?.ToDisplayString());
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Await");
    }

    [Fact]
    public void CorelibAwait_WithNonAwaitableSignature_StandsDown()
    {
        var function = BuildNonAwaitableCorelibAwait();

        new AwaitRecoveryPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
        Assert.Single(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Await");
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

    [Fact]
    public void ThreeAwaits_PreserveSourceOrder()
    {
        var output = Print(nameof(CfgSampleClass.AwaitThree));

        int a = output.IndexOf("await a", StringComparison.Ordinal);
        int b = output.IndexOf("await b", StringComparison.Ordinal);
        int c = output.IndexOf("await c", StringComparison.Ordinal);
        Assert.True(a >= 0 && b >= 0 && c >= 0, $"missing an await in:\n{output}");
        Assert.True(a < b && b < c, $"awaits must read a, b, c in order:\n{output}");
        Assert.Equal(3, Raised(nameof(CfgSampleClass.AwaitThree)).Descendants.OfType<AwaitExpression>().Count());
    }

    [Fact]
    public void AwaitsAsCallArguments_PreserveArgumentOrder()
    {
        // Combine(x, y) => x - y, so a reordering would silently flip the result.
        var output = Print(nameof(CfgSampleClass.AwaitInArguments));

        int a = output.IndexOf("await a", StringComparison.Ordinal);
        int b = output.IndexOf("await b", StringComparison.Ordinal);
        Assert.True(a >= 0 && b >= 0, $"missing an await in:\n{output}");
        Assert.True(a < b, $"`await a` must precede `await b`:\n{output}");
    }

    [Fact]
    public void AwaitAcrossVoidCall_KeepsAwaitBeforeCall()
    {
        // The void call sequences between the await and its use; the await must
        // materialize before the call, not slide past it (void-call spill path).
        var output = Print(nameof(CfgSampleClass.AwaitAcrossVoidCall));

        int awaitPos = output.IndexOf("await a", StringComparison.Ordinal);
        int sink = output.IndexOf("Sink(", StringComparison.Ordinal);
        Assert.True(awaitPos >= 0 && sink >= 0, $"missing await or call in:\n{output}");
        Assert.True(awaitPos < sink, $"`await a` must precede the void call:\n{output}");
    }

    static IrFunction BuildNonAwaitableCorelibAwait()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(new Call(
            new MethodRef(AsyncHelpers, "Await", Int32, [Int32], HasThis: false),
            isVirtual: false,
            [new Constant(42, Int32)])));
        body.Add(block);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
