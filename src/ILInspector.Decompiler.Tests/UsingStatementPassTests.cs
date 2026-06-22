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
    public void RefStructPatternDispose_RaisesToUsingStatement()
    {
        var function = Raised(nameof(CfgSampleClass.RefStructPatternUsing));

        var usingStatement = Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.Equal("RefStructResource", usingStatement.ResourceType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void RefStructPatternDispose_RendersUsingHeaderWithoutFinally()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.RefStructPatternUsing))).Output;

        Assert.NotNull(output);
        Assert.Contains("using (RefStructResource resource = new RefStructResource(value))", output);
        Assert.Contains("return resource.Value;", output);
        Assert.DoesNotContain("finally", output);
    }

    [Fact]
    public void RuntimeAsyncAwaitUsing_RaisesToAwaitUsingStatement()
    {
        var function = Raised(nameof(CfgSampleClass.AwaitUsingResource));

        var usingStatement = Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.True(usingStatement.IsAwait);
        Assert.Equal("AsyncDisposableResource", usingStatement.ResourceType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void RuntimeAsyncAwaitUsing_RendersAwaitUsingHeader()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.AwaitUsingResource))).Output;

        Assert.NotNull(output);
        Assert.Contains("await using (AsyncDisposableResource resource = new AsyncDisposableResource(value))", output);
        Assert.Contains("return resource.Value;", output);
        Assert.DoesNotContain("ExceptionDispatchInfo", output);
    }

    [Fact]
    public void ManualDisposeAsyncInFinally_IsLeftAsTryFinally()
    {
        var function = Raised(nameof(CfgSampleClass.ManualDisposeAsyncInFinally));

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void ManualAwaitDisposeAsyncInFinally_IsNotRaisedToAwaitUsing()
    {
        var function = Raised(nameof(CfgSampleClass.ManualAwaitDisposeAsyncInFinally));

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.NotEmpty(function.Descendants.OfType<TryCatch>());
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
            TypeRef.Definition("UserAssembly", "Samples", "DisposableStruct", ValueTypeHint.ValueType),
            "Dispose",
            TypeRef.CoreLib("System", "Void"),
            [intType],
            [new Constant(0, intType)],
            knownValueTypeShape: true);

        new UsingStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
        function.CheckInvariant();
    }

    [Fact]
    public void AwaitUsingDisposeAsyncShape_IsLeftAsTryFinally()
    {
        // `await using` lowers its cleanup to DisposeAsync() (returning ValueTask),
        // not Dispose(). The sync using matcher keys on the exact member name
        // "Dispose", so this await-using-shaped finally must stay a try/finally —
        // raising it to `using` would silently drop the awaited async disposal.
        var function = BuildValueTypeUsingLookalike(
            TypeRef.Definition("UserAssembly", "Samples", "AsyncDisposableStruct", ValueTypeHint.ValueType),
            "DisposeAsync",
            TypeRef.CoreLib("System.Threading.Tasks", "ValueTask"),
            [],
            knownValueTypeShape: true);

        new UsingStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
        function.CheckInvariant();
    }

    [Fact]
    public void PatternDisposeReturningValueTask_IsLeftAsTryFinally()
    {
        // A pattern member named "Dispose" but returning ValueTask (the
        // pattern-based `await using` shape) must not be mistaken for the
        // synchronous void Dispose() the matcher accepts: the void-return
        // discriminator keeps the awaited disposal out of a sync `using`.
        var function = BuildValueTypeUsingLookalike(
            TypeRef.Definition("UserAssembly", "Samples", "AsyncDisposableStruct", ValueTypeHint.ValueType),
            "Dispose",
            TypeRef.CoreLib("System.Threading.Tasks", "ValueTask"),
            [],
            knownValueTypeShape: true);

        new UsingStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
        function.CheckInvariant();
    }

    [Fact]
    public void RealDisposeAsyncInFinally_IsNotRaisedToUsing()
    {
        // A real compiled finally that calls DisposeAsync() on a reference-type
        // resource (the manual async-dispose lookalike) must never collapse into a
        // synchronous `using`: DisposeAsync is not IDisposable.Dispose.
        var function = Raised(nameof(CfgSampleClass.ManualDisposeAsyncInFinally));

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
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
        IrExpression[]? extraArguments = null,
        bool knownValueTypeShape = false)
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
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [disposableType], body);
        if (knownValueTypeShape)
        {
            function.TypeShapes = new Dictionary<TypeRef, TypeShape>
            {
                [disposableType] = TypeShape.ValueType,
            };
        }
        return function;
    }
}
