using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class RuntimeAsyncAwaiterPassTests
{
    const string AsyncFixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures";
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef TaskType = TypeRef.CoreLib("System.Threading.Tasks", "Task");
    static readonly TypeRef AsyncHelpers = TypeRef.CoreLib("System.Runtime.CompilerServices", "AsyncHelpers");
    static readonly TypeRef Awaitable = TypeRef.Definition("Synthetic", "Samples", "SafeAwaitable");
    static readonly TypeRef Awaiter = TypeRef.Definition("Synthetic", "Samples", "SafeAwaiter");

    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(RuntimeAsyncAwaiterFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(RuntimeAsyncAwaiterFixtures).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    [Fact]
    public void CompiledYield_RecoversAwait()
    {
        var function = Raised(nameof(RuntimeAsyncAwaiterFixtures.YieldOnce));

        Assert.Equal(MetadataFactState.Yes, function.IsRuntimeAsync);
        Assert.True(function.RequiresAsyncBodyModifier);
        Assert.Single(function.Descendants.OfType<AwaitExpression>());
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name is "AwaitAwaiter" or "UnsafeAwaitAwaiter");
        Assert.Contains("await Task.Yield();", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CompiledYield_CompileBackShellCarriesAsyncModifier()
    {
        var assembly = typeof(RuntimeAsyncAwaiterFixtures).Assembly.Location;
        var result = Assert.Single(FidelityCheck.EvaluateTargets(
            [assembly],
            [new FidelityCheck.CompileBackTarget(
                assembly,
                typeof(RuntimeAsyncAwaiterFixtures).FullName!,
                nameof(RuntimeAsyncAwaiterFixtures.YieldOnce),
                Overload: 0,
                Signature: "(corelib:System.Int32) -> corelib:System.Threading.Tasks.Task`1<corelib:System.Int32>")]));

        Assert.True(
            result.Status is FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff,
            $"Expected compile-checkable runtime-async source, got {result.Status}: {result.Detail}");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CompiledYield_ReturnToSenderShellCarriesAsyncModifier()
    {
        var assembly = typeof(RuntimeAsyncAwaiterFixtures).Assembly.Location;
        var result = Assert.Single(ReturnToSender.CompileBackTargets(
            assembly,
            [new ReturnToSender.RequestedTarget(
                typeof(RuntimeAsyncAwaiterFixtures).FullName!,
                nameof(RuntimeAsyncAwaiterFixtures.YieldOnce),
                Overload: 0)]));

        Assert.Contains(" async ", result.Source);
        Assert.Contains(nameof(RuntimeAsyncAwaiterFixtures.YieldOnce), result.Source);
        Assert.True(
            result.Status is FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff,
            $"Expected compile-checkable runtime-async source, got {result.Status}: {result.Detail}");
    }

    [Fact]
    public void CompiledSequentialYields_RecoversBothAwaits()
    {
        var function = Raised(nameof(RuntimeAsyncAwaiterFixtures.YieldTwice));

        Assert.Equal(2, function.Descendants.OfType<AwaitExpression>().Count());
        Assert.DoesNotContain("AsyncHelpers", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void CompiledConditionalYield_RecoversNestedAwait()
    {
        var function = Raised(nameof(RuntimeAsyncAwaiterFixtures.YieldInBranch));
        var output = CSharpPrinter.Print(function).Output;

        Assert.Single(function.Descendants.OfType<AwaitExpression>());
        Assert.Contains("if (condition)", output);
        Assert.Contains("await Task.Yield();", output);
    }

    [Fact]
    public void CompiledAwaitThenUnsafeConsumer_PreservesPreUnsafeSpill()
    {
        var function = RaisedAsyncFixture("AwaitThenConsumePointer");
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
        Assert.Contains(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode == "runtime await"
                && node.Reason.Contains(
                    "unsafe context would contain await",
                    StringComparison.Ordinal));
        Assert.DoesNotContain("unsafe\n{\n    return ConsumePointer(await", result.Output);
    }

    [Fact]
    public void CompiledLegacyAsyncLocalFunction_UsesUnsafeSignature()
    {
        var function = RaisedAsyncFixture("AwaitWithUnsafeLocalFunction");
        var output = CSharpPrinter.Print(function).Output;

        Assert.False(function.UsesUpdatedMemorySafetyRules);
        Assert.Contains("static unsafe int Read(int* pointer)", output);
        Assert.DoesNotContain("int Read(int* pointer) => *pointer;", output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CompiledLegacyAsyncLocalFunction_CompilesBack()
    {
        string assembly = AsyncFixtureAssemblyPath();
        var compileBack = Assert.Single(ReturnToSender.CompileBackTargets(
            assembly,
            [new ReturnToSender.RequestedTarget(
                AsyncFixtureType,
                "AwaitWithUnsafeLocalFunction",
                Overload: 0)]));
        Assert.Contains("static unsafe int Read(int* pointer)", compileBack.Source);
        Assert.True(
            compileBack.Status is FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff,
            $"Expected compile-checkable runtime-async source, got "
                + $"{compileBack.Status}: {compileBack.Detail}");
    }

    [Theory]
    [InlineData(nameof(RuntimeAsyncAwaiterFixtures.YieldParameter))]
    [InlineData(nameof(RuntimeAsyncAwaiterFixtures.ClassAwaitableCall))]
    [InlineData(nameof(RuntimeAsyncAwaiterFixtures.ClassAwaitableParameter))]
    [InlineData(nameof(RuntimeAsyncAwaiterFixtures.ExtensionAwaitableParameter))]
    public void CompiledAwaitableShapes_RecoverAwait(string methodName)
    {
        var function = Raised(methodName);
        var output = CSharpPrinter.Print(function).Output;

        Assert.Equal(MetadataFactState.Yes, function.IsRuntimeAsync);
        Assert.True(function.RequiresAsyncBodyModifier);
        Assert.Single(function.Descendants.OfType<AwaitExpression>());
        Assert.Contains("await ", output);
        Assert.DoesNotContain("AsyncHelpers", output);
    }

    [Theory]
    [InlineData("AwaitAwaiter")]
    [InlineData("UnsafeAwaitAwaiter")]
    public void ExactSafeAndUnsafeHelpers_RecoverSyntheticAwait(string helperName)
    {
        var function = Synthetic(helperName: helperName);

        new RuntimeAsyncAwaiterPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var awaitExpression = Assert.Single(function.Descendants.OfType<AwaitExpression>());
        Assert.Equal(
            ["GetAwaiter", "get_IsCompleted", "GetResult"],
            awaitExpression.ConsumedMemberRefs.Select(method => method.Name));
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == helperName);
    }

    [Fact]
    public void UnsafeAwaiterPatternMember_StandsDownBecauseAwaitCannotEnterUnsafeContext()
    {
        var function = Synthetic(requiresUnsafeAwaiterMember: true);
        function.UsesUpdatedMemorySafetyRules = true;

        new RuntimeAsyncAwaiterPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
        Assert.Contains(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "GetAwaiter");
    }

    [Fact]
    public void UnsafeAwaitOperand_StandsDownBecauseAwaitCannotEnterUnsafeContext()
    {
        var function = Synthetic(requiresUnsafeOperand: true);
        function.UsesUpdatedMemorySafetyRules = true;

        new RuntimeAsyncAwaiterPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
        Assert.Contains(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "GetAwaiter");
    }

    [Fact]
    public void UnsafeGetResultConsumer_StandsDownBecauseAwaitCannotEnterUnsafeContext()
    {
        var function = Synthetic(unsafeGetResultConsumer: true);

        new RuntimeAsyncAwaiterPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
        Assert.Contains(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "GetResult");
    }

    [Theory]
    [InlineData(SyntheticBreak.NonRuntimeMethod)]
    [InlineData(SyntheticBreak.WrongHelperAssembly)]
    [InlineData(SyntheticBreak.WrongHelperSignature)]
    [InlineData(SyntheticBreak.DifferentHelperLocal)]
    [InlineData(SyntheticBreak.DifferentIsCompletedLocal)]
    [InlineData(SyntheticBreak.DifferentGetResultLocal)]
    [InlineData(SyntheticBreak.EscapedAwaiterAfterGetResult)]
    [InlineData(SyntheticBreak.EscapedAwaitableAfterGetResult)]
    [InlineData(SyntheticBreak.ExternalHelperEntry)]
    [InlineData(SyntheticBreak.ExternalMergeEntry)]
    [InlineData(SyntheticBreak.StaticGetAwaiterWithoutExtensionEvidence)]
    public void BrokenDiscriminator_StandsDown(SyntheticBreak broken)
    {
        var function = Synthetic(broken: broken);

        new RuntimeAsyncAwaiterPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
        Assert.Contains(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "UnsafeAwaitAwaiter");
    }

    static IrFunction Synthetic(
        string helperName = "UnsafeAwaitAwaiter",
        SyntheticBreak broken = SyntheticBreak.None,
        bool requiresUnsafeAwaiterMember = false,
        bool requiresUnsafeOperand = false,
        bool unsafeGetResultConsumer = false)
    {
        int helperLocal = broken == SyntheticBreak.DifferentHelperLocal ? 2 : 1;
        int completedLocal = broken == SyntheticBreak.DifferentIsCompletedLocal ? 2 : 1;
        int resultLocal = broken == SyntheticBreak.DifferentGetResultLocal ? 2 : 1;
        var helperType = broken == SyntheticBreak.WrongHelperAssembly
            ? TypeRef.Definition("Synthetic", "System.Runtime.CompilerServices", "AsyncHelpers")
            : AsyncHelpers;
        var helperParameter = broken == SyntheticBreak.WrongHelperSignature ? Awaitable : Awaiter;

        var head = new Block(0);
        IrExpression awaitable = requiresUnsafeOperand
            ? new Call(
                new MethodRef(
                    TypeRef.Definition("Synthetic", "Samples", "Holder"),
                    "GetAwaitable",
                    Awaitable,
                    [],
                    HasThis: false)
                {
                    RequiresUnsafe = true,
                },
                isVirtual: false,
                [])
            : new LoadArgument(0, "awaitable", Awaitable);
        var awaitableStore = new StoreLocal(0, Awaitable, awaitable);
        head.Add(awaitableStore);
        var getAwaiter = broken == SyntheticBreak.StaticGetAwaiterWithoutExtensionEvidence
            ? new Call(
                new MethodRef(
                    TypeRef.Definition("Synthetic", "Samples", "AwaitableExtensions"),
                    "GetAwaiter",
                    Awaiter,
                    [Awaitable],
                    HasThis: false)
                {
                    IsExtension = MetadataFactState.No,
                },
                isVirtual: false,
                [new LoadLocal(0, Awaitable)])
            : new Call(
                new MethodRef(Awaitable, "GetAwaiter", Awaiter, [], HasThis: true)
                {
                    RequiresUnsafe = requiresUnsafeAwaiterMember,
                },
                isVirtual: false,
                [new LoadLocalAddress(0, Awaitable)]);
        head.Add(new StoreLocal(
            1,
            Awaiter,
            getAwaiter));
        head.Add(new ConditionalBranch(
            new LoadProperty(
                new MethodRef(Awaiter, "get_IsCompleted", Bool, [], HasThis: true),
                new LoadLocalAddress(completedLocal, Awaiter),
                []),
            targetOffset: 20));

        var helper = new Block(10);
        helper.Add(new ExpressionStatement(new Call(
            new MethodRef(helperType, helperName, Void, [helperParameter], HasThis: false)
            {
                TypeArguments = [Awaiter],
            },
            isVirtual: false,
            [new LoadLocal(helperLocal, Awaiter)])));

        var merge = new Block(20);
        var getResultCall = new Call(
            new MethodRef(
                Awaiter,
                "GetResult",
                unsafeGetResultConsumer ? Int32 : Void,
                [],
                HasThis: true),
            isVirtual: false,
            [new LoadLocalAddress(resultLocal, Awaiter)]);
        if (unsafeGetResultConsumer)
        {
            merge.Add(new StoreIndirect(
                Int32,
                new LoadArgument(1, "pointer", TypeRef.Pointer(Int32)),
                getResultCall));
        }
        else
        {
            merge.Add(new ExpressionStatement(getResultCall));
        }
        if (broken == SyntheticBreak.EscapedAwaiterAfterGetResult)
            merge.Add(new ExpressionStatement(new LoadLocal(1, Awaiter)));
        if (broken == SyntheticBreak.EscapedAwaitableAfterGetResult)
            merge.Add(new ExpressionStatement(new LoadLocal(0, Awaitable)));
        merge.Add(new Return(null));

        var body = new BlockContainer();
        if (broken is SyntheticBreak.ExternalHelperEntry or SyntheticBreak.ExternalMergeEntry)
        {
            var external = new Block(-10);
            external.Add(new Branch(
                broken == SyntheticBreak.ExternalHelperEntry ? 10 : 20));
            body.Add(external);
        }

        body.Add(head);
        body.Add(helper);
        body.Add(merge);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Holder"),
            new MethodSignature(
                TaskType,
                unsafeGetResultConsumer
                    ? [
                        new Parameter("awaitable", Awaitable),
                        new Parameter("pointer", TypeRef.Pointer(Int32)),
                    ]
                    : [new Parameter("awaitable", Awaitable)],
                HasThis: false,
                GenericParameterCount: 0),
            [Awaitable, Awaiter, Awaiter],
            body)
        {
            IsRuntimeAsync = broken == SyntheticBreak.NonRuntimeMethod
                ? MetadataFactState.No
                : MetadataFactState.Yes,
        };
    }

    static IrFunction RaisedAsyncFixture(string methodName)
    {
        string path = AsyncFixtureAssemblyPath();
        using var source = MetadataSource.Open(path);
        var function = IrImporter.Import(source, AsyncFixtureType, methodName);
        Assert.NotNull(function);
        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));
        function.CheckInvariant();
        return function;
    }

    static string AsyncFixtureAssemblyPath()
    {
        string configuration =
            new DirectoryInfo(AppContext.BaseDirectory).Name;
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "ILInspector.Decompiler.Fixtures.RuntimeAsync",
            configuration,
            "ILInspector.Decompiler.Fixtures.RuntimeAsync.dll"));
    }

    public enum SyntheticBreak
    {
        None,
        NonRuntimeMethod,
        WrongHelperAssembly,
        WrongHelperSignature,
        DifferentHelperLocal,
        DifferentIsCompletedLocal,
        DifferentGetResultLocal,
        EscapedAwaiterAfterGetResult,
        EscapedAwaitableAfterGetResult,
        ExternalHelperEntry,
        ExternalMergeEntry,
        StaticGetAwaiterWithoutExtensionEvidence,
    }
}
