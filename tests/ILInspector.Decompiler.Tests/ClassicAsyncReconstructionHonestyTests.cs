using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.DecompilerHarness;
using DotnetInspector.Fixtures;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ClassicAsyncReconstructionHonestyTests
{
    const string FixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures";
    const string NamedResultFixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.NamedAwaitResultSamples";

    [Theory]
    [InlineData("NamedReceiver", "return result.Response;")]
    [InlineData("NamedReceiverTransform", "return result.Response.Trim();")]
    public void SingleAwaitPreservesNamedResultReceiver(
        string methodName,
        string expectedReturn)
    {
        using var source = OpenClassicFixture(readSymbols: true);
        var function = ImportAndRaise(source, methodName, NamedResultFixtureType);
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Equal("result", Assert.Single(function.LocalNames, static name => name is not null));
        Assert.Contains("AwaitReply result = await task.ConfigureAwait(false);", result.Output);
        Assert.Contains(expectedReturn, result.Output);
        Assert.Single(function.Descendants.OfType<AwaitExpression>());
        Assert.All(function.Descendants.OfType<LoadLocalAddress>(), address => Assert.Equal("result", function.LocalNames[address.Index]));
    }

    [Fact]
    public void SingleAwaitUnnamedReceiverRemainsReconstructed()
    {
        using var source = OpenClassicFixture(readSymbols: true);
        var function = ImportAndRaise(source, "UnnamedReceiver", NamedResultFixtureType);
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Contains("return (await task.ConfigureAwait(false)).Response;", result.Output);
        Assert.DoesNotContain("AwaitReply result =", result.Output);
    }

    [Theory]
    [InlineData("ExtraResultUse", "return string.Concat(result.Response, result.Response);")]
    [InlineData("ExtraResultEffect", "GC.KeepAlive(result);")]
    [InlineData("GuardedResultUse", "return result.Response;")]
    [InlineData("EscapingResultAddress", "ReadByRef(ref result)")]
    public void SingleAwaitUnownedNamedResultReconstructsThroughTheGeneralInverse(
        string methodName,
        string expected)
    {
        // The named-result recipe declines these (its result ownership is
        // unproven); the general inverse carries the result's extra uses as
        // ordinary user code.
        using var source = OpenClassicFixture(readSymbols: true);
        var function = ImportAndRaise(source, methodName, NamedResultFixtureType);
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Contains("AwaitReply result = await task.ConfigureAwait(false);", result.Output);
        Assert.Contains(expected, result.Output);
        Assert.DoesNotContain(".Start<", result.Output);
        Assert.DoesNotContain(function.Diagnostics, diagnostic => diagnostic.Id == DiagnosticIds.InternalError);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void SingleAwaitNamedResultAndNeighborCompileBack()
    {
        var results = FidelityCheck.Evaluate(
            FixtureCatalog.DecompilerClassicAsync.AssemblyPath(),
            type => type == NamedResultFixtureType,
            method => method.Method is "NamedReceiver" or "NamedReceiverTransform" or "UnnamedReceiver");

        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.True(
            result.Status is FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }

    [Theory]
    [InlineData("SequentialWithFieldStore")]
    [InlineData("SequentialWithChainedFieldStores")]
    [InlineData("SequentialWithNullCoalescingFieldStore")]
    [InlineData("SequentialWithPropertyStore")]
    [InlineData("SequentialWithInitObjectStore")]
    [InlineData("SequentialWithEventSubscription")]
    [InlineData("SequentialWithParameterWrite")]
    [InlineData("SequentialWithHoistedLocalWrite")]
    [InlineData("SequentialWithHoistedLocalIncrement")]
    [InlineData("SequentialWithDeconstructionWrite")]
    [InlineData("SequentialWithCapturedNullCoalescingWrite")]
    [InlineData("AwaitConditionalWithWrappedResult")]
    [InlineData("AwaitCompoundConditional")]
    [InlineData("AwaitInLoopWithWrappedOperand")]
    [InlineData("LoopWithFieldStore")]
    [InlineData("LoopWithAccumulatorWrite")]
    [InlineData("LoopWithClamp")]
    [InlineData("AwaitInTryFinallyWithGuardedCall")]
    [InlineData("SequentialWithOrdinarySetResultCall")]
    [InlineData("SequentialWithSeparateBuilderReceiver")]
    public void RecipeDeclinedShapeReconstructsThroughTheGeneralInverse(
        string methodName)
    {
        // The recipes decline these on unconsumed user effects or builder
        // accounting; the general inverse runs first and carries the user code.
        using var source = OpenClassicFixture(readSymbols: true);
        IrFunction function = ImportAndRaise(source, methodName);

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.DoesNotContain(".Start<", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode == "classic async");
    }

    [Fact]
    public void AwaitReturnTailJoinedByThreeArmsStaysOneSuspension()
    {
        // The selection's three arms branch to the await's return tail; the
        // return merge must not copy that suspension into each arm.
        using var source = OpenClassicFixture(readSymbols: true);
        IrFunction function = ImportAndRaise(source, "AwaitOperandSelectedThreeWays");

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Single(function.Descendants.OfType<AwaitExpression>());
        Assert.Contains("first ? a : second ? string.Concat(b, \"@\") : c", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void RecipeDeclinedShapesCompileBack()
    {
        string[] methods =
        [
            "SequentialWithFieldStore", "SequentialWithChainedFieldStores",
            "SequentialWithNullCoalescingFieldStore", "SequentialWithPropertyStore",
            "SequentialWithInitObjectStore", "SequentialWithEventSubscription",
            "SequentialWithParameterWrite", "SequentialWithHoistedLocalWrite",
            "SequentialWithHoistedLocalIncrement", "SequentialWithDeconstructionWrite",
            "SequentialWithCapturedNullCoalescingWrite", "AwaitConditionalWithWrappedResult",
            "AwaitCompoundConditional", "AwaitInLoopWithWrappedOperand", "LoopWithFieldStore",
            "LoopWithAccumulatorWrite", "LoopWithClamp", "AwaitInTryFinallyWithGuardedCall",
            "SequentialWithOrdinarySetResultCall", "SequentialWithSeparateBuilderReceiver",
            "AwaitInLoop", "AwaitOperandSelectedThreeWays",
        ];
        var results = FidelityCheck.Evaluate(
            FixtureCatalog.DecompilerClassicAsync.AssemblyPath(),
            type => type == FixtureType,
            method => methods.Contains(method.Method));

        Assert.Equal(methods.Length, results.Count);
        Assert.All(results, result => Assert.True(
            result.Status is FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }

    [Theory]
    [InlineData("SequentialWithStructParameterReset", "protocol field 'captured' survived as an init")]
    [InlineData("SequentialWithEmbeddedIncrement", "user-defined ++ in this position is not representable")]
    public void UnconsumedUserStoreDeclinesAtPartialFidelity(
        string methodName,
        string reason)
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
            "general inverse declined",
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
                && diagnostic.Message.Contains(reason, StringComparison.Ordinal));
        Assert.Contains(
            ".Start<",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SameNamedFieldFromAnotherModuleIsNotMachineStorage()
    {
        Guid machineMvid = Guid.NewGuid();
        TypeRef machine = Definition(
            machineMvid,
            MetadataTokens.TypeDefinitionHandle(2));
        TypeRef sameNameFromAnotherModule = Definition(
            Guid.NewGuid(),
            MetadataTokens.TypeDefinitionHandle(2));
        Assert.Equal(machine, sameNameFromAnotherModule);

        var localStore = Store(machine);
        var foreignStore = Store(sameNameFromAnotherModule);

        Assert.True(
            ClassicAsyncReconstructionPass.IsMachineFieldStore(
                localStore,
                machine));
        Assert.False(
            ClassicAsyncReconstructionPass.IsMachineFieldStore(
                foreignStore,
                machine));
    }

    [Theory]
    [InlineData("TwoSequentialAwaits", "GC.KeepAlive((x, y));")]
    [InlineData("AwaitInLoop", "foreach (Task<int> item in tasks)")]
    [InlineData(
        "AwaitOrdinarySetMethod",
        "return await set_GetTask(task);")]
    [InlineData("AwaitInTryFinally", "finally")]
    [InlineData(
        "SequentialWithRealizedInitializer",
        "Value = alpha + beta")]
    [InlineData(
        "SequentialWithRealizedWithExpression",
        "with")]
    [InlineData(
        "SequentialWithImplicitConversion",
        "long beta = (long)(await b);")]
    public void RecipeAcceptedShapeRemainsFullyReconstructed(
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
        var awaits = function.Descendants.OfType<AwaitExpression>().ToList();
        Assert.NotEmpty(awaits);
        Assert.All(
            awaits,
            awaitExpression => Assert.Contains(
                awaitExpression.ConsumedMemberRefs,
                method => method.Name == "GetResult"));
        Assert.All(
            awaits,
            awaitExpression => Assert.Contains(
                awaitExpression.ConsumedMemberRefs,
                method => method.Name == "GetAwaiter"));
        Assert.Contains(
            expectedOutput,
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnsafeEvaluationFoldedIntoAwait_DeclinesHonestly()
    {
        using var source = OpenClassicFixture(readSymbols: true);
        IrFunction function = ImportAndRaise(source, "AwaitUnsafeProperty");

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.False(result.RequiresAsyncBodyModifier);
        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
        Assert.Contains(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode == "classic async"
                && node.Reason.Contains(
                    "general inverse declined",
                    StringComparison.Ordinal));
        Assert.Contains(
            function.Diagnostics,
            diagnostic => diagnostic.Id == DiagnosticIds.UnsupportedConstruct
                && diagnostic.Message.Contains(
                    "requires unsafe context and contains await",
                    StringComparison.Ordinal));
        Assert.DoesNotContain("unsafe\n{\n    return await", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void PointerReceiverFoldedIntoAwait_DeclinesHonestly()
    {
        using var source = OpenClassicFixture(readSymbols: true);
        IrFunction function = ImportAndRaise(source, "AwaitPointerReceiver");

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.False(result.RequiresAsyncBodyModifier);
        Assert.Empty(function.Descendants.OfType<AwaitExpression>());
        Assert.Contains(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode == "classic async"
                && node.Reason.Contains(
                    "general inverse declined",
                    StringComparison.Ordinal));
        Assert.Contains(
            function.Diagnostics,
            diagnostic => diagnostic.Id == DiagnosticIds.UnsupportedConstruct
                && diagnostic.Message.Contains(
                    "requires unsafe context and contains await",
                    StringComparison.Ordinal));
        Assert.DoesNotContain("unsafe\n{\n    return await", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void NaturalUnmatchedShapeReconstructsThroughTheGeneralInverse()
    {
        using var source = OpenClassicFixture(readSymbols: true);
        IrFunction function = ImportAndRaise(source, "TwoAwaitsOverTasksArray");

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Contains("int first = await tasks[0];", result.Output, StringComparison.Ordinal);
        Assert.Contains("int second = await tasks[1];", result.Output, StringComparison.Ordinal);
        Assert.Contains("return first + second;", result.Output, StringComparison.Ordinal);
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

    [Fact]
    public void LoopRoleNamesAreSynthesized()
    {
        using var source = OpenClassicFixture(readSymbols: true);
        IrFunction function = ImportAndRaise(
            source,
            "AwaitInLoop");

        DecompilerResult result = CSharpPrinter.Print(function);

        // `sum` is the hoisted field's source name; the iteration variable was
        // folded into its one use, so the foreach raise synthesizes it.
        Assert.Equal(["sum"], function.LocalNames.Where(static name => name is not null));
        Assert.Contains("item", function.SynthesizedLocalNames);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(
            "int sum = 0;",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "foreach (Task<int> item in tasks)",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "sum += await item;",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "return sum;",
            result.Output,
            StringComparison.Ordinal);
    }

    static MetadataSource OpenClassicFixture(bool readSymbols)
    {
        string path = FixtureCatalog.DecompilerClassicAsync.AssemblyPath();
        return readSymbols
            ? MetadataSource.Open(path)
            : MetadataSource.OpenWithoutSymbols(path);
    }

    static IrFunction ImportAndRaise(
        MetadataSource source,
        string methodName,
        string typeName = FixtureType)
    {
        IrFunction? function = IrImporter.Import(
            source,
            typeName,
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

    static TypeRef Definition(
        Guid moduleVersionId,
        System.Reflection.Metadata.TypeDefinitionHandle handle)
    {
        MetadataTypeDefinitionName name =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Samples",
                    ["Outer", "<Method>d__0"]))
                .Name;
        return TypeRef.DefinitionWithResolution(
            "Samples",
            "Samples",
            "Outer+<Method>d__0",
            ValueTypeHint.ValueType,
            MetadataFactState.Unknown,
            enclosingType: null,
            definitionName: name,
            resolutionAssembly: null,
            definitionHandle: handle,
            definitionModuleVersionId: moduleVersionId);
    }

    static StoreField Store(TypeRef declaringType)
        => new(
            new FieldRef(
                declaringType,
                "<>1__state",
                TypeRef.CoreLib("System", "Int32")),
            new LoadArgument(0, "this", declaringType),
            new Constant(
                0,
                TypeRef.CoreLib("System", "Int32")));
}
