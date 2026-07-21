using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class RuntimeAsyncAwaiterPassTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
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

        Assert.Single(function.Descendants.OfType<AwaitExpression>());
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == helperName);
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
        SyntheticBreak broken = SyntheticBreak.None)
    {
        int helperLocal = broken == SyntheticBreak.DifferentHelperLocal ? 2 : 1;
        int completedLocal = broken == SyntheticBreak.DifferentIsCompletedLocal ? 2 : 1;
        int resultLocal = broken == SyntheticBreak.DifferentGetResultLocal ? 2 : 1;
        var helperType = broken == SyntheticBreak.WrongHelperAssembly
            ? TypeRef.Definition("Synthetic", "System.Runtime.CompilerServices", "AsyncHelpers")
            : AsyncHelpers;
        var helperParameter = broken == SyntheticBreak.WrongHelperSignature ? Awaitable : Awaiter;

        var head = new Block(0);
        var awaitableStore = new StoreLocal(0, Awaitable, new LoadArgument(0, "awaitable", Awaitable));
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
                new MethodRef(Awaitable, "GetAwaiter", Awaiter, [], HasThis: true),
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
        merge.Add(new ExpressionStatement(new Call(
            new MethodRef(Awaiter, "GetResult", Void, [], HasThis: true),
            isVirtual: false,
            [new LoadLocalAddress(resultLocal, Awaiter)])));
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
                [new Parameter("awaitable", Awaitable)],
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
