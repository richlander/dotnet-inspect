using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A C# destructor lowers to a Finalize override whose body is
// try { BODY } finally { base.Finalize(); }. DestructorRecoveryPass strips that
// scaffold back to BODY and marks the function a destructor.
public class DestructorRecoveryPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "HasFinalizer");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void FinalizeOverride_RecoversAsDestructorBody()
    {
        var function = Raised("Finalize");

        Assert.True(function.IsDestructor);
        // The try/finally scaffold and the base.Finalize() call are gone — the body
        // is the destructor's own statements.
        Assert.Empty(function.Descendants.OfType<TryFinally>());
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "Finalize");

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("s_finalized = true;", output);
        Assert.DoesNotContain("base.Finalize", output);
        Assert.DoesNotContain("finally", output);
    }

    [Fact]
    public void FinalizeOverride_WithOnlyTrailingReturn_RecoversAsDestructorBody()
    {
        var function = BuildManualFinalize(includeExecutableTrailingStatement: false);

        new DestructorRecoveryPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.True(function.IsDestructor);
        Assert.Empty(function.Descendants.OfType<TryFinally>());
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "Finalize");
    }

    [Fact]
    public void FinalizeOverride_WithExecutableTrailingStatement_StandsDown()
    {
        var function = BuildManualFinalize(includeExecutableTrailingStatement: true);

        new DestructorRecoveryPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.False(function.IsDestructor);
        Assert.Single(function.Descendants.OfType<TryFinally>());
        Assert.Contains(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "AfterBase");
        Assert.Contains(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "Finalize");
    }

    static IrFunction BuildManualFinalize(bool includeExecutableTrailingStatement)
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new TryFinally(
            BlockWithCall("Body"),
            BlockWithBaseFinalize()));
        if (includeExecutableTrailingStatement)
            block.Add(new ExpressionStatement(Call("AfterBase")));
        block.Add(new Return(null));
        body.Add(block);

        return new IrFunction(
            "Finalize",
            Holder,
            new MethodSignature(Void, [], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static BlockContainer BlockWithCall(string methodName)
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new ExpressionStatement(Call(methodName)));
        body.Add(block);
        return body;
    }

    static BlockContainer BlockWithBaseFinalize()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new ExpressionStatement(new Call(
            new MethodRef(Object, "Finalize", Void, [], HasThis: true),
            isVirtual: false,
            [new LoadArgument(0, "this", Holder)])));
        body.Add(block);
        return body;
    }

    static Call Call(string methodName)
        => new(
            new MethodRef(Holder, methodName, Void, [], HasThis: false),
            isVirtual: false,
            []);
}
