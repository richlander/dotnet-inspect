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
        // List<int>.Enumerator is a foreign nested type here (StructUsing is a
        // method of CfgSampleClass, not List<T>), so it qualifies through its
        // declaring type — a bare `Enumerator` would be CS0246 in this scope.
        Assert.Contains("using (List<int>.Enumerator e = items.GetEnumerator())", output);
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
    public void NestedRuntimeAsyncAwaitUsing_RaisesBothResources()
    {
        var function = Raised(nameof(CfgSampleClass.NestedAwaitUsingResources));

        var usingStatements = function.Descendants.OfType<UsingStatement>().ToArray();
        Assert.Equal(2, usingStatements.Length);
        Assert.All(usingStatements, statement => Assert.True(statement.IsAwait));
        Assert.Empty(function.Descendants.OfType<TryCatch>());
    }

    [Fact]
    public void NestedRuntimeAsyncAwaitUsing_RendersNestedAwaitUsingHeaders()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NestedAwaitUsingResources))).Output;

        Assert.NotNull(output);
        Assert.Contains("await using (AsyncDisposableResource outer = new AsyncDisposableResource(outerValue))", output);
        Assert.Contains("await using (AsyncDisposableResource inner = new AsyncDisposableResource(innerValue))", output);
        Assert.Contains("return outer.Value + inner.Value;", output);
        Assert.DoesNotContain("ExceptionDispatchInfo", output);
    }

    [Fact]
    public void AwaitUsingRethrowHelperLookalike_IsLeftLowered()
    {
        var helperType = TypeRef.Definition("UserAssembly", "Synthetic.Samples", "FakeDispatchInfo");
        var function = BuildAwaitUsingWithRethrowHelper(helperType);

        new UsingStatementPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<UsingStatement>(), statement => statement.IsAwait);
        Assert.Single(function.Descendants.OfType<TryCatch>());
        Assert.Contains(function.Descendants.OfType<Call>(), call => call.Callee.DeclaringType.Equals(helperType) && call.Callee.Name == "Capture");
        Assert.Contains(function.Descendants.OfType<Call>(), call => call.Callee.DeclaringType.Equals(helperType) && call.Callee.Name == "Throw");
        function.CheckInvariant();
    }

    [Fact]
    public void ManualDisposeAsyncInFinally_IsLeftAsTryFinally()
    {
        var function = Raised(nameof(CfgSampleClass.ManualDisposeAsyncInFinally));

        Assert.Empty(function.Descendants.OfType<UsingStatement>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
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
        // resource (the manual await-using lookalike) must never collapse into a
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

    static IrFunction BuildAwaitUsingWithRethrowHelper(TypeRef helperType)
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var exceptionType = TypeRef.CoreLib("System", "Exception");
        var intType = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var valueTaskType = TypeRef.CoreLib("System.Threading.Tasks", "ValueTask");
        var resourceType = TypeRef.Definition("UserAssembly", "Synthetic.Samples", "AsyncResource");

        const int resourceLocal = 0;
        const int exceptionLocal = 1;
        const int stateLocal = 2;
        const int isInstanceSlot = 256;
        const int exceptionSlot = 257;

        var disposeAsync = new MethodRef(resourceType, "DisposeAsync", valueTaskType, [], HasThis: true);
        var capture = new MethodRef(helperType, "Capture", helperType, [objectType], HasThis: false);
        var throwMethod = new MethodRef(helperType, "Throw", voidType, [], HasThis: true);

        var tryBody = new BlockContainer();
        tryBody.Add(new Block(0));

        var catchBody = new BlockContainer();
        catchBody.Add(new Block(0));
        var catchClause = new CatchClause(objectType, catchBody) { VariableIndex = exceptionLocal };

        var disposeThen = new Block(0);
        disposeThen.Add(new ExpressionStatement(new AwaitExpression(
            new Call(disposeAsync, isVirtual: true, [new LoadLocal(resourceLocal, resourceType)]),
            voidType)));

        var fallbackThen = new Block(0);
        fallbackThen.Add(new ExpressionStatement(new LoadStackSlot(exceptionSlot, exceptionType)));
        fallbackThen.Add(new Throw(new LoadLocal(exceptionLocal, objectType)));

        var rethrowThen = new Block(0);
        rethrowThen.Add(new StoreStackSlot(
            isInstanceSlot,
            new IsInstance(exceptionType, new LoadLocal(exceptionLocal, objectType))));
        rethrowThen.Add(new StoreStackSlot(exceptionSlot, new LoadStackSlot(isInstanceSlot, exceptionType)));
        rethrowThen.Add(new IfStatement(
            new LogicalNot(new LoadStackSlot(isInstanceSlot, exceptionType)),
            fallbackThen,
            elseArm: null));
        rethrowThen.Add(new ExpressionStatement(new Call(
            throwMethod,
            isVirtual: true,
            [new Call(capture, isVirtual: false, [new LoadStackSlot(exceptionSlot, exceptionType)])])));

        var returnThen = new Block(0);
        returnThen.Add(new Return(null));

        var entry = new Block(0);
        entry.Add(new StoreLocal(resourceLocal, resourceType, new Constant(null, resourceType)));
        entry.Add(new StoreLocal(exceptionLocal, objectType, new Constant(null, objectType)));
        entry.Add(new StoreLocal(stateLocal, intType, new Constant(0, intType)));
        entry.Add(new TryCatch(tryBody, [catchClause]));
        entry.Add(new IfStatement(new LoadLocal(resourceLocal, resourceType), disposeThen, elseArm: null));
        entry.Add(new IfStatement(new LoadLocal(exceptionLocal, objectType), rethrowThen, elseArm: null));
        entry.Add(new IfStatement(
            new Comparison(ComparisonKind.Equal, isUnsigned: false, new LoadLocal(stateLocal, intType), new Constant(1, intType)),
            returnThen,
            elseArm: null));
        entry.Add(new Throw(new Constant(null, objectType)));

        var body = new BlockContainer();
        body.Add(entry);

        var signature = new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [resourceType, objectType, intType], body);
    }
}
