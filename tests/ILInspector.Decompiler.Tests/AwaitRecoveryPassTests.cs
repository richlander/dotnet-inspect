using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class AwaitRecoveryPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "AwaitHolder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef UIntPtr = TypeRef.CoreLib("System", "UIntPtr");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef AsyncHelpers = TypeRef.CoreLib("System.Runtime.CompilerServices", "AsyncHelpers");
    static readonly TypeRef TaskInt = TypeRef.GenericInstance(
        TypeRef.CoreLib("System.Threading.Tasks", "Task`1"),
        [Int32]);

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

        Assert.True(function.RequiresAsyncBodyModifier);
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
    public void NonRuntimeMethod_WithExactCorelibAwait_StandsDown()
    {
        var function = BuildExactCorelibAwait(MetadataFactState.No);

        new AwaitRecoveryPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.False(function.RequiresAsyncBodyModifier);
        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
        Assert.Single(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Await");
    }

    [Fact]
    public void UnsafeAwaitOperand_StandsDownBecauseAwaitCannotEnterUnsafeContext()
    {
        var function = BuildExactCorelibAwait(MetadataFactState.Yes);
        function.UsesUpdatedMemorySafetyRules = true;
        var awaitCall = Assert.Single(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "Await");
        awaitCall.Arguments[0].ReplaceWith(new LoadProperty(
            new MethodRef(Holder, "get_RiskyTask", TaskInt, [], HasThis: true)
            {
                RequiresUnsafe = true,
            },
            new LoadArgument(0, "holder", Holder),
            []));

        new AwaitRecoveryPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.False(function.RequiresAsyncBodyModifier);
        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
        Assert.Single(function.Descendants.OfType<Call>(), call => call.Callee.Name == "Await");
    }

    [Fact]
    public void UnboxPointerConversionBesideAwait_StandsDown()
    {
        var pointer = new ILInspector.Decompiler.Pipeline.Convert(
            UIntPtr,
            isChecked: false,
            isUnsigned: false,
            new Unbox(Int32, new LoadArgument(0, "value", Object)));
        var awaitCall = new Call(
            new MethodRef(
                AsyncHelpers,
                "Await",
                Int32,
                [TaskInt],
                HasThis: false)
            {
                TypeArguments = [Int32],
            },
            isVirtual: false,
            [new LoadArgument(1, "task", TaskInt)]);
        var combine = new MethodRef(
            Holder,
            "Combine",
            Int32,
            [UIntPtr, Int32],
            HasThis: false);
        var block = new Block();
        block.Add(new Return(new Call(
            combine,
            isVirtual: false,
            [pointer, awaitCall])));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(
                TaskInt,
                [
                    new Parameter("value", Object),
                    new Parameter("task", TaskInt),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            IsRuntimeAsync = MetadataFactState.Yes,
            UsesUpdatedMemorySafetyRules = true,
        };

        Assert.True(UnsafeAwaitOperand.RequiresUnsafeContext(
            pointer,
            usesUpdatedMemorySafetyRules: true));

        new AwaitRecoveryPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.False(function.RequiresAsyncBodyModifier);
        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
        Assert.Single(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "Await");
    }

    [Theory]
    [InlineData("local")]
    [InlineData("stack-slot")]
    [InlineData("return")]
    public void PointerToByRefBindingBesideAwait_StandsDown(string binding)
    {
        var pointer = TypeRef.Pointer(Int32);
        var byRef = TypeRef.ByRef(Int32);
        var awaitCall = new Call(
            new MethodRef(
                AsyncHelpers,
                "Await",
                Int32,
                [TaskInt],
                HasThis: false)
            {
                TypeArguments = [Int32],
            },
            isVirtual: false,
            [new LoadArgument(0, "task", TaskInt)]);
        var pointerValue = new Call(
            new MethodRef(
                Holder,
                "PointerAfterAwait",
                pointer,
                [Int32],
                HasThis: false),
            isVirtual: false,
            [awaitCall]);
        var block = new Block();
        TypeRef returnType;
        System.Collections.Immutable.ImmutableArray<TypeRef> locals;
        switch (binding)
        {
            case "local":
                block.Add(new StoreLocal(0, byRef, pointerValue));
                block.Add(new Return(null));
                returnType = Void;
                locals = [byRef];
                break;
            case "stack-slot":
                block.Add(new StoreStackSlot(0, pointerValue));
                block.Add(new ExpressionStatement(new Call(
                    new MethodRef(
                        Holder,
                        "Consume",
                        Void,
                        [byRef],
                        HasThis: false),
                    isVirtual: false,
                    [new LoadStackSlot(0, byRef)])));
                block.Add(new Return(null));
                returnType = Void;
                locals = [];
                break;
            case "return":
                block.Add(new Return(pointerValue));
                returnType = byRef;
                locals = [];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(binding));
        }
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(
                returnType,
                [new Parameter("task", TaskInt)],
                HasThis: false,
                GenericParameterCount: 0),
            locals,
            body)
        {
            IsRuntimeAsync = MetadataFactState.Yes,
            UsesUpdatedMemorySafetyRules = true,
        };

        Assert.True(UnsafeAwaitOperand.RequiresUnsafeContext(
            block.Children[0],
            usesUpdatedMemorySafetyRules: true));

        new AwaitRecoveryPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.False(function.RequiresAsyncBodyModifier);
        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
        Assert.Single(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "Await");
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

    static IrFunction BuildExactCorelibAwait(MetadataFactState runtimeAsync)
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(new Call(
            new MethodRef(AsyncHelpers, "Await", Int32, [TaskInt], HasThis: false)
            {
                TypeArguments = [Int32],
            },
            isVirtual: false,
            [new LoadArgument(0, "task", TaskInt)])));
        body.Add(block);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(
                TaskInt,
                [new Parameter("task", TaskInt)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            IsRuntimeAsync = runtimeAsync,
        };
    }
}
