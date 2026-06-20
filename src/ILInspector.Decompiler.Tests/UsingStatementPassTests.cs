using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class UsingStatementPassTests
{
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
    public void ReferenceTypeUsingWithDisposeGuard_RaisesToUsingStatement()
    {
        var function = Raised(nameof(CfgSampleClass.NormalUsing));

        var usingStatement = Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Equal("StringReader", usingStatement.ResourceType.ToDisplayString());
        Assert.IsType<NewObject>(usingStatement.Resource);
        Assert.Empty(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void PrintRaised_RendersUsingHeaderAndBody()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NormalUsing))).Output;

        Assert.NotNull(output);
        Assert.Contains("using (StringReader reader = new StringReader(s))", output);
        Assert.Contains("return reader.Read();", output);
        Assert.DoesNotContain("finally", output);
    }

    [Fact]
    public void FinallyWithExtraWork_IsLeftAsTryFinally()
    {
        var function = Raised(nameof(CfgSampleClass.FinallyWithExtraWork));

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void ResourceReassignedInsideTry_IsLeftAsTryFinally()
    {
        var function = BuildUsingLookalike(TypeRef.CoreLib("System", "IDisposable"), reassignInsideTry: true);

        new UsingStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
        Assert.Equal(2, function.Descendants.OfType<StoreLocal>().Count(store => store.Index == 0));
        function.CheckInvariant();
    }

    [Fact]
    public void UserIDisposableLookalike_IsLeftAsTryFinally()
    {
        var function = BuildUsingLookalike(
            TypeRef.Definition("UserAssembly", "System", "IDisposable"),
            reassignInsideTry: false);

        new UsingStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
        function.CheckInvariant();
    }

    [Fact]
    public void ValueTypeUsingWithUnguardedDispose_RaisesToUsingStatement()
    {
        // List<T>.Enumerator is a struct IDisposable: csc emits no null guard,
        // disposing through the local's address (constrained callvirt). The
        // value-type slice of the pass must raise this just like the
        // reference-type null-guarded shape.
        var function = Raised(nameof(CfgSampleClass.StructUsing));

        var usingStatement = Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Equal("Enumerator", usingStatement.ResourceType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void ValueTypeUsing_RendersUsingHeaderWithoutFinally()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.StructUsing))).Output;

        Assert.NotNull(output);
        Assert.Contains("using (Enumerator e = items.GetEnumerator())", output);
        Assert.DoesNotContain("finally", output);
        Assert.DoesNotContain("Dispose", output);
    }

    [Fact]
    public void ValueTypeDisposeLookalike_IsLeftAsTryFinally()
    {
        var function = BuildValueTypeUsingLookalike(
            TypeRef.Definition("UserAssembly", "Samples", "DisposableStruct", ValueTypeHint.ValueType),
            "Dispose",
            TypeRef.CoreLib("System", "Void"),
            []);

        new UsingStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
        function.CheckInvariant();
    }

    [Fact]
    public void ValueTypeDisposeWithWrongSignature_IsLeftAsTryFinally()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var function = BuildValueTypeUsingLookalike(
            TypeRef.CoreLib("System", "IDisposable"),
            "Dispose",
            TypeRef.CoreLib("System", "Void"),
            [intType],
            [new Constant(0, intType)]);

        new UsingStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
        function.CheckInvariant();
    }

    static IrFunction BuildUsingLookalike(TypeRef disposableType, bool reassignInsideTry)
    {
        var voidType = TypeRef.CoreLib("System", "Void");
        var dispose = new MethodRef(disposableType, "Dispose", voidType, [], HasThis: true);

        var tryBlock = new Block(0);
        if (reassignInsideTry)
            tryBlock.Add(new StoreLocal(0, disposableType, new Constant(null, disposableType)));
        var tryBody = new BlockContainer();
        tryBody.Add(tryBlock);

        var thenBlock = new Block(0);
        thenBlock.Add(new ExpressionStatement(new Call(dispose, isVirtual: true, [new LoadLocal(0, disposableType)])));
        var finallyBlock = new Block(0);
        finallyBlock.Add(new IfStatement(new LoadLocal(0, disposableType), thenBlock, null));
        var finallyBody = new BlockContainer();
        finallyBody.Add(finallyBlock);

        var entry = new Block(0);
        entry.Add(new StoreLocal(0, disposableType, new Constant(null, disposableType)));
        entry.Add(new TryFinally(tryBody, finallyBody));
        var body = new BlockContainer();
        body.Add(entry);

        var signature = new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [disposableType], body);
    }

    static IrFunction BuildValueTypeUsingLookalike(
        TypeRef disposableType,
        string disposeName,
        TypeRef returnType,
        TypeRef[] parameterTypes,
        IrExpression[]? extraArguments = null)
    {
        var voidType = TypeRef.CoreLib("System", "Void");
        var dispose = new MethodRef(disposableType, disposeName, returnType, [.. parameterTypes], HasThis: true);

        var tryBody = new BlockContainer();
        tryBody.Add(new Block(0));

        var arguments = new List<IrExpression> { new LoadLocalAddress(0, disposableType) };
        if (extraArguments is not null)
            arguments.AddRange(extraArguments);
        var finallyBlock = new Block(0);
        finallyBlock.Add(new ExpressionStatement(new Call(dispose, isVirtual: true, arguments)));
        var finallyBody = new BlockContainer();
        finallyBody.Add(finallyBlock);

        var entry = new Block(0);
        entry.Add(new StoreLocal(0, disposableType, new Constant(null, disposableType)));
        entry.Add(new TryFinally(tryBody, finallyBody));
        var body = new BlockContainer();
        body.Add(entry);

        var signature = new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [disposableType], body);
    }
}
