using System.Collections.Immutable;
using System.Reflection.Metadata;

using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Convert = ILInspector.Decompiler.Pipeline.Convert;
using Parameter = ILInspector.Decompiler.Pipeline.Parameter;
using InstructionExceptionFlowFacts =
    ILInspector.Instructions.InstructionExceptionFlowFacts;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The classic async fixture population through the product pipeline, and the
/// request authentication the general inverse runs behind
/// (<c>docs/design/classic-async-reconstruction.md</c>).
/// </summary>
[Trait("Area", "Pass")]
public sealed class ClassicAsyncFixtureTests
{
    const string FixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures";
    const string ContinuationFixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.ContinuationFixtures";
    const string ExpressionFixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncExpressionFixtures";
    const string GenericExpressionFixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.GenericExpressionFixtures`1";
    const string InventoryFixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncInventoryFixtures";
    const string NamedResultFixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.NamedAwaitResultSamples";
    const string ReceiverFixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.ReceiverAliasFixtures";

    static readonly ImmutableArray<string> s_acceptedPopulation =
    [
        "AwaitCompoundConditional",
        "AwaitConditional",
        "AwaitConditionalWithWrappedResult",
        "AwaitInLoop",
        "AwaitInLoopWithWrappedOperand",
        "AwaitInTryFinally",
        "AwaitInTryFinallyWithGuardedCall",
        "AwaitOperandSelectedThreeWays",
        "AwaitOrdinarySetMethod",
        "AwaitValue",
        "AwaitValueTask",
        "AwaitVoid",
        "DynamicArrayReferenceIdentity",
        "DynamicReferenceIdentity",
        "InterfaceReceiver",
        "LoopWithAccumulatorWrite",
        "LoopWithClamp",
        "LoopWithFieldStore",
        "ObjectArrayReferenceIdentity",
        "SequentialWithCapturedNullCoalescingWrite",
        "SequentialWithChainedFieldStores",
        "SequentialWithDeconstructionWrite",
        "SequentialWithEventSubscription",
        "SequentialWithFieldStore",
        "SequentialWithHoistedLocalIncrement",
        "SequentialWithHoistedLocalWrite",
        "SequentialWithImplicitConversion",
        "SequentialWithInitObjectStore",
        "SequentialWithNullCoalescingFieldStore",
        "SequentialWithOrdinarySetResultCall",
        "SequentialWithParameterWrite",
        "SequentialWithPropertyStore",
        "SequentialWithRealizedInitializer",
        "SequentialWithRealizedWithExpression",
        "SequentialWithSeparateBuilderReceiver",
        "TwoAwaitsOverTasksArray",
        "TwoSequentialAwaits",
        "TwoSequentialNamedAwaits",
    ];

    public static IEnumerable<object[]> InventoryCases()
    {
        foreach (string method in new[]
        {
            "Plain", "ArrayNeighbor", "InterpolationNeighbor", "StructNeighbor", "InterfaceNeighbor",
            "ConstantArrayOperand", "ConstantByteArrayOperand", "RangeResult", "SliceResult", "SliceOperand",
            "FromEndResult", "FromEndOperand", "AnonymousResult", "AnonymousOperand", "DelegateOperand",
            "InterpolationAnonymous", "InterpolationNullable", "NestedInterpolation", "PriorInterpolationArgument",
            "FollowingInterpolationArgument", "ReadOnlySpanLiteral", "ByRefArgument", "OutArgument",
            "RefReceiverResult", "ConditionalAwaitable", "CoalesceAwaitable", "SwitchResult",
            "GenericAny", "GenericStruct", "GenericClass",
            "SliceStart", "SliceEnd", "SliceAll", "VirtualDelegateOperand", "SwitchEffects", "SwitchLabels",
            "ConditionalOperandEffects", "CoalesceOperandEffects", "CollectionEffects",
        })
            yield return [method];
    }

    [Fact]
    public void AcceptedPopulationIsMeasured()
    {
        using MetadataSource source = OpenClassicFixture();
        var accepted = ImmutableArray.CreateBuilder<string>();
        var rejectedExpected = ImmutableArray.CreateBuilder<string>();

        foreach (string methodName in ClassicKickoffMethods(source))
        {
            IrFunction function = Assert.IsType<IrFunction>(
                IrImporter.Import(source, FixtureType, methodName));
            IrPasses.Run(
                function,
                IrPasses.Default,
                PassContext.ForImport(
                    method => IrImporter.Import(source, method)));
            DecompilerResult result = CSharpPrinter.Print(function);
            if (result.Fidelity == DecompilationFidelity.Full)
            {
                accepted.Add(methodName);
            }
            else if (s_acceptedPopulation.Contains(methodName))
            {
                rejectedExpected.Add(
                    $"{methodName}: "
                        + string.Join(
                            " | ",
                            function.Diagnostics.Select(
                                diagnostic => diagnostic.Message)));
            }
        }

        ImmutableArray<string> actual =
            accepted.ToImmutable().Sort(StringComparer.Ordinal);
        Assert.True(
            s_acceptedPopulation.SequenceEqual(actual),
            $"Expected: {string.Join(", ", s_acceptedPopulation)}"
                + $"{Environment.NewLine}Actual: {string.Join(", ", actual)}"
                + $"{Environment.NewLine}Unexpected declines: "
                + string.Join(
                    Environment.NewLine,
                    rejectedExpected));
    }

    [Fact]
    public void RequestCorrelationBindsOwnerIssuedRolesExactly()
    {
        using MetadataSource source = OpenClassicFixture();
        ClassicInverseRequest request = OpenRequest(source, "TwoSequentialAwaits");
        Assert.Null(request.CorrelationFailure());
        MetadataMethodAddress declared = request.DeclaredMethod!.Value;
        MetadataMethodAddress execution = request.ExecutionMethod!.Value;

        Assert.NotNull(CopyRequest(request, declaredMethod: execution).CorrelationFailure());
        Assert.NotNull(CopyRequest(request, executionMethod: declared).CorrelationFailure());
        Assert.NotNull(CopyRequest(request, stateMachineLocal: -1).CorrelationFailure());

        using MetadataSource stateMachines = OpenClassicStateMachinesFixture();
        var relationships = Assert.IsType<
            StateMachineRelationshipsResult.Available>(
                StateMachineRelationshipIndex.Create(stateMachines.Reader)
                    .Relationships);
        StateMachineRelationship wrongKind = relationships.Relationships.First(
            candidate => candidate.Kind == StateMachineClaimKind.Iterator);
        Assert.NotNull(CopyRequest(request, relationship: wrongKind).CorrelationFailure());
    }

    [Fact]
    public void RequestCorrelationRejectsForeignExceptionFlowMethod()
    {
        using MetadataSource source = OpenClassicFixture();
        ClassicInverseRequest request = OpenRequest(source, "TwoSequentialAwaits");
        ClassicInverseRequest foreign = OpenRequest(source, "AwaitValue");
        IrFunction execution = (IrFunction)request.ExecutionBody.Clone();
        execution.ExceptionInstructions = foreign.ExecutionBody.ExceptionInstructions;
        execution.ExceptionClauseImports = foreign.ExecutionBody.ExceptionClauseImports;
        Assert.NotEqual(
            AvailableFacts(request.ExecutionBody).Body.Method,
            AvailableFacts(execution).Body.Method);

        Assert.Contains(
            "exception-flow body is not the relationship's MoveNext MethodDef",
            CopyRequest(request, executionBody: execution).CorrelationFailure(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AwaitValueTaskReconstructs()
    {
        using MetadataSource source = OpenClassicFixture();
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, FixtureType, "AwaitValueTask"));
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            method => IrImporter.Import(source, method)));
        function.CheckInvariant();
        DecompilerResult result = CSharpPrinter.Print(function);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("return await a;", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AppliedArgumentsKeepKickoffBinders()
    {
        using MetadataSource source = OpenClassicFixture();
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, NamedResultFixtureType, "NamedReceiver"));
        Parameter parameter = Assert.Single(function.Signature.Parameters);
        IrPasses.Run(function, IrPasses.Default,
            PassContext.ForImport(method => IrImporter.Import(source, method)));
        function.CheckInvariant();
        Assert.Equal(DecompilationFidelity.Full, CSharpPrinter.Print(function).Fidelity);
        LoadArgument argument = Assert.Single(function.Body.Descendants.OfType<LoadArgument>());
        Assert.Same(parameter, argument.Parameter);
    }

    [Theory]
    [InlineData("GuardedReceiverCleanup")]
    [InlineData("GuardedAwait")]
    [InlineData("InvertedGuardedAwait")]
    public void SingleAwaitGuardKeepsSourceEarlyReturn(string method)
    {
        using MetadataSource source = OpenClassicFixture();
        var function = IrImporter.Import(source, ContinuationFixtureType, method)!;
        var printed = CSharpPrinter.PrintRaised(function, reference => IrImporter.Import(source, reference));
        Assert.True(printed.Succeeded, string.Join("; ", printed.Diagnostics));
        Assert.Equal(DecompilationFidelity.Full, printed.Fidelity);
        Assert.Contains(method == "InvertedGuardedAwait" ? "if (disabled)" : "if (!enabled)", printed.Output);
        Assert.Contains("await Work()", printed.Output);
        if (method == "GuardedReceiverCleanup") Assert.Contains("Cleanup();", printed.Output);
        function.CheckInvariant();
    }

    [Theory]
    [InlineData("ReferenceGuardedAwait")]
    [InlineData("CompoundReceiverCleanup")]
    public void CompoundReferenceGuardKeepsSourceCondition(string name)
    {
        using MetadataSource source = OpenClassicFixture();
        var fresh = IrImporter.Import(source, ContinuationFixtureType, name)!;
        var printed = CSharpPrinter.PrintRaised(fresh, method => IrImporter.Import(source, method));
        Assert.True(printed.Succeeded, string.Join("; ", printed.Diagnostics));
        Assert.Equal(DecompilationFidelity.Full, printed.Fidelity);
        Assert.Contains("_first is null && _second is null", printed.Output);
        Assert.Contains("await", printed.Output);
        fresh.CheckInvariant();
    }

    [Fact]
    public void AwaitContinuationCleanupKeepsItsOrder()
    {
        using MetadataSource source = OpenClassicFixture();
        var function = IrImporter.Import(source, ContinuationFixtureType, "AwaitReceiverCleanup")!;
        var printed = CSharpPrinter.PrintRaised(function, reference => IrImporter.Import(source, reference));
        Assert.True(printed.Succeeded, string.Join("; ", printed.Diagnostics));
        Assert.Equal(DecompilationFidelity.Full, printed.Fidelity);
        var text = Assert.IsType<string>(printed.Output);
        Assert.Contains("await Work()", text);
        Assert.Contains("Cleanup();", text);
        Assert.Contains("_first = null", text);
        Assert.Contains("_second = null", text);
        Assert.True(text.IndexOf("Cleanup();", StringComparison.Ordinal)
            < text.IndexOf("_first = null", StringComparison.Ordinal));
        function.CheckInvariant();
    }

    [Theory]
    [InlineData("AwaitFieldReset")]
    [InlineData("GuardedAwaitFieldReset")]
    public void AwaitFieldResetIsKept(string name)
    {
        using MetadataSource source = OpenClassicFixture();
        var fresh = IrImporter.Import(source, ContinuationFixtureType, name)!;
        var printed = CSharpPrinter.PrintRaised(fresh, method => IrImporter.Import(source, method));
        Assert.True(printed.Succeeded, string.Join("; ", printed.Diagnostics));
        Assert.Equal(DecompilationFidelity.Full, printed.Fidelity);
        Assert.Contains("_memory = default", printed.Output);
        Assert.Contains("Cleanup();", printed.Output);
        fresh.CheckInvariant();
    }

    [Theory]
    [InlineData("AwaitReceiver")]
    [InlineData("AwaitReceiverResult")]
    public void ProvenReferenceReceiverAliasIsRecovered(string method)
    {
        using MetadataSource source = OpenClassicFixture();
        var function = IrImporter.Import(source, ReceiverFixtureType, method)!;
        var printed = CSharpPrinter.PrintRaised(function, reference => IrImporter.Import(source, reference));
        Assert.True(printed.Succeeded, string.Join("; ", printed.Diagnostics));
        Assert.Equal(DecompilationFidelity.Full, printed.Fidelity);
        Assert.Contains("await ", printed.Output);
        Assert.DoesNotContain("g__", printed.Output);
        function.CheckInvariant();
    }

    [Theory]
    [InlineData("AwaitInitializedStackalloc")]
    [InlineData("AwaitInitializedByteStackalloc")]
    public void InitializedStackallocRaises(string method)
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture(method);
        Assert.Single(function.Body.Descendants.OfType<StackAllocArray>());
        Assert.Single(function.Body.Descendants.OfType<AwaitExpression>());
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Theory]
    [InlineData("Coalesce", "return await a ?? \"fallback\";")]
    [InlineData("CoalesceCall", "return await a ?? Fallback();")]
    [InlineData("CoalesceParameter", "return await a ?? fallback;")]
    [InlineData("CoalesceThenCall", "(await a ?? Fallback()).Trim()")]
    [InlineData("CoalesceOperandCall", "(await a).Trim() ?? Fallback()")]
    [InlineData("CoalesceBooleanArgument", "await a ?? ChooseFallback(!useFirst)")]
    public void CoalescingExpressionsKeepTheAwaitOnTheLeft(string method, string expected)
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture(method);
        Coalesce expression = Assert.Single(function.Body.Descendants.OfType<Coalesce>());
        Assert.Single(expression.Left.Descendants.Prepend(expression.Left).OfType<AwaitExpression>());
        Assert.DoesNotContain(expression.Right.Descendants, node => node is AwaitExpression);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ReferenceCast", "(string)")]
    [InlineData("ValueCast", "(int)")]
    [InlineData("AsReference", " as string")]
    [InlineData("TypeTest", " is string")]
    [InlineData("NegatedTypeTest", " is not string")]
    [InlineData("NewIntArray", "new int[await a]")]
    [InlineData("NewReferenceArray", "new string[await a + 1]")]
    [InlineData("ArrayElementAfterAwait", "[index]")]
    public void TypedExpressionFormsRaise(string method, string expected)
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture(method);
        Assert.Single(function.Body.Descendants.OfType<AwaitExpression>());
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Not", "return !(await a);")]
    [InlineData("EqualFalse", "return !(await a);")]
    [InlineData("EqualTrue", "return await a;")]
    [InlineData("NotEqualFalse", "return await a;")]
    [InlineData("NotEqualTrue", "return !(await a);")]
    [InlineData("CompareBooleans", "return (await a) == b;")]
    [InlineData("NotComparison", "return (await a) <= b;")]
    [InlineData("NotUnsignedComparison", "return (await a) <= b;")]
    [InlineData("NotFloatComparison", "return !((await a) > b);")]
    [InlineData("DoubleNot", "return await a;")]
    public void BooleanExpressionsRaise(string method, string expected)
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture(method);
        Assert.Single(function.Body.Descendants.OfType<AwaitExpression>());
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TypeEquality", "typeof(string)")]
    [InlineData("CoalesceTypeOf", "?? typeof(string)")]
    [InlineData("TypeArrayEquality", "typeof(string[])")]
    [InlineData("TypeGenericEquality", "typeof(Dictionary<string, int>)")]
    [InlineData("TypeArguments", "TypeChoice(await value, typeof(string), typeof(int))")]
    public void TypeOfExpressionsRaise(string method, string expected)
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture(method);
        Assert.NotEmpty(function.Body.Descendants.OfType<TypeOf>());
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BooleanChoice")]
    [InlineData("BooleanChoiceCalls")]
    [InlineData("NegatedBooleanChoice")]
    [InlineData("BooleanChoiceObjects")]
    [InlineData("BooleanChoiceTypeOf")]
    [InlineData("ComparisonChoice")]
    [InlineData("BooleanChoiceThenCall")]
    public void AwaitedPredicatesRaise(string method)
    {
        (_, DecompilerResult result) = RaiseExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("?", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NestedInitializer", "Child = { Value = 7 }")]
    [InlineData("NestedInitializerEntries", "Other = PositiveChoice()")]
    [InlineData("NestedInitializerTypeOf", "Kind = typeof(string)")]
    public void PartialInitializerSourceStaysUseful(string method, string expected)
    {
        (_, DecompilerResult result) = RaiseExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Contains("CombineInitialized(await value,", result.Output, StringComparison.Ordinal);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(".Start<", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedCollectionInitializerIsCarriedAsStatements()
    {
        // The lowered initializer is carried as ordinary statements: correct
        // and valid, though not the collection-initializer idiom.
        (_, DecompilerResult result) = RaiseExpressionFixture("NestedCollectionInitializer");
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Contains("Values.Add(7);", result.Output, StringComparison.Ordinal);
        Assert.Contains("Values.Add(8);", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(".Start<", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultFieldStoreIsCarried()
    {
        (_, DecompilerResult result) = RaiseExpressionFixture("DefaultGuidFieldInitializer");
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Contains("IdField = default;", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NegateAwaitedValue", "-(await a)")]
    [InlineData("ComplementAwaitedValue", "~(await a)")]
    [InlineData("AwaitedArrayLength", "(await a).Length")]
    [InlineData("FieldAfterAwait", "(await a).FieldValue")]
    [InlineData("StaticFieldAfterAwait", "await a + Reading.StaticFieldValue")]
    [InlineData("VolatileFieldAfterAwait", "(await a).VolatileValue")]
    public void OrdinaryExpressionFormsRaise(string method, string expected)
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture(method);
        if (method == "VolatileFieldAfterAwait")
            Assert.True(Assert.Single(function.Body.Descendants.OfType<LoadField>()).IsVolatile);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ConfiguredTask", "await a.ConfigureAwait(false)")]
    [InlineData("ConfiguredTaskCapturesContext", "await a.ConfigureAwait(true)")]
    [InlineData("ConfiguredVoidTask", "await a.ConfigureAwait(false)")]
    [InlineData("ConfiguredValueTask", "await a.ConfigureAwait(false)")]
    [InlineData("ConfiguredVoidValueTask", "await a.ConfigureAwait(false)")]
    [InlineData("ConfiguredTaskWithOption", "await a.ConfigureAwait(continueOnCapturedContext)")]
    public void ConfiguredAwaitablesRaise(string method, string expected)
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture(method);
        AwaitExpression await = Assert.Single(function.Body.Descendants.OfType<AwaitExpression>());
        Assert.Equal("ConfigureAwait", Assert.IsType<Call>(await.Operand).Callee.Name);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("WidenBeforeAdd", "(long)(await a) + (long)b", false)]
    [InlineData("WidenBeforeDivide", "(double)(await a) / (double)b", false)]
    [InlineData("CheckedWidenBeforeAdd", "(long)(await a) + (long)b", true)]
    public void ArithmeticConversionsAreKept(string method, string expected, bool isChecked)
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture(method);
        Binary binary = Assert.Single(function.Body.Descendants.OfType<Binary>());
        Assert.IsType<Convert>(binary.Left);
        Assert.IsType<Convert>(binary.Right);
        Assert.Equal(isChecked, binary.IsChecked);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        string output = Assert.IsType<string>(result.Output);
        Assert.Contains(expected, output, StringComparison.Ordinal);
        Assert.Equal(isChecked, output.Contains("checked(", StringComparison.Ordinal));
    }

    [Fact]
    public void IntegerArithmeticIsNotWidened()
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture("IntegerAdd");
        Assert.Empty(function.Body.Descendants.OfType<Convert>());
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("return await a + b;", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AwaitedLength", "(await a).Length")]
    [InlineData("AwaitedIndexer", "(await a)[index]")]
    [InlineData("StaticPropertyAfterAwait", "await a + Environment.TickCount")]
    [InlineData("VirtualPropertyAfterAwait", "(await a).Value")]
    public void PropertyAccessRaises(string method, string expected)
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture(method);
        LoadProperty property = Assert.Single(function.Body.Descendants.OfType<LoadProperty>());
        Assert.Equal(method != "StaticPropertyAfterAwait", property.HasInstance);
        Assert.Equal(method != "StaticPropertyAfterAwait", property.IsVirtual);
        Assert.Equal(method == "AwaitedIndexer" ? 1 : 0, property.IndexArguments.Count);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("InterpolatedOperand")]
    [InlineData("InterpolatedFormats")]
    [InlineData("InterpolatedZeroAlignment")]
    [InlineData("InterpolatedNegativeAlignment")]
    [InlineData("InterpolatedEffects")]
    [InlineData("InterpolatedGeneric")]
    [InlineData("InterpolatedEscapedFormat")]
    [InlineData("YieldOnce")]
    [InlineData("StructOperand")]
    [InlineData("MutableStructOperand")]
    [InlineData("StructFactoryOperand")]
    [InlineData("ClassAwaitableOperand")]
    public void InterpolationAndValueBindsRaise(string method)
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture(method);
        Assert.NotEmpty(function.Body.Descendants.OfType<AwaitExpression>());
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
    }

    [Theory]
    [MemberData(nameof(InventoryCases))]
    public void ExpressionInventoryRaises(string method)
    {
        using MetadataSource source = OpenClassicFixture();
        IrFunction function = IrImporter.Import(source, InventoryFixtureType, method)!;
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(target => IrImporter.Import(source, target)));
        function.CheckInvariant();
        Assert.Single(function.Body.Descendants.OfType<AwaitExpression>());
        DecompilerResult result = CSharpPrinter.Print(function);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Equal(method.Contains("Anonymous", StringComparison.Ordinal)
            ? DecompilationFidelity.Partial : DecompilationFidelity.Full, result.Fidelity);
    }

    [Theory]
    [InlineData("CharPropertyInitializer")]
    [InlineData("CharFieldInitializer")]
    [InlineData("BoolPropertyInitializer")]
    [InlineData("EnumPropertyInitializer")]
    [InlineData("NestedCharInitializer")]
    [InlineData("DefaultGuidInitializer")]
    [InlineData("DefaultDateTimeInitializer")]
    [InlineData("GenericAwait")]
    [InlineData("GenericArrayAwait")]
    [InlineData("GenericConstruct")]
    [InlineData("NullableDefaultReceiver")]
    [InlineData("NullableIntReceiver")]
    [InlineData("NullableGuidReceiver")]
    [InlineData("NullableBoolReceiver")]
    [InlineData("NamedNullableReceiver")]
    [InlineData("AwaitTuple")]
    [InlineData("AwaitNestedTuple")]
    [InlineData("AwaitTupleEffect")]
    [InlineData("MixedAndOr")]
    [InlineData("ShortCircuitBoth")]
    [InlineData("GenericCall")]
    [InlineData("AwaitLongTuple")]
    [InlineData("AndThenOr")]
    [InlineData("AwaitTypedTuple")]
    public void RetainedExpressionConsumersRaise(string method)
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture(method);
        Assert.Single(function.Body.Descendants.OfType<AwaitExpression>());
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.DoesNotContain(".Start<", result.Output, StringComparison.Ordinal);
        Assert.Equal(method == "NestedCharInitializer" ? DecompilationFidelity.Partial
            : DecompilationFidelity.Full, result.Fidelity);
    }

    [Theory]
    [InlineData("ByteArgument")]
    [InlineData("ByteMinimumArgument")]
    [InlineData("ByteMaximumArgument")]
    [InlineData("DecimalConstant")]
    [InlineData("CharacterArgument")]
    [InlineData("CharacterMaximumArgument")]
    [InlineData("EnumArgument")]
    [InlineData("LongEnumArgument")]
    [InlineData("ShortCircuitCall")]
    [InlineData("ShortCircuitOrCall")]
    [InlineData("NestedBooleanChoice")]
    [InlineData("NestedChoiceCalls")]
    [InlineData("ComposedSelectionTypes")]
    [InlineData("Concatenation")]
    [InlineData("IntReceiver")]
    [InlineData("LongReceiver")]
    [InlineData("BoolReceiver")]
    [InlineData("VariableAnd")]
    [InlineData("DateTimeReceiver")]
    [InlineData("DecimalReceiver")]
    [InlineData("NamedIntReceiver")]
    public void SinkAndCompositionExpressionsRaise(string method)
    {
        (IrFunction function, DecompilerResult result) = RaiseExpressionFixture(method);
        Assert.Single(function.Body.Descendants.OfType<AwaitExpression>());
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.DoesNotContain(".Start<", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    [InlineData(ContinuationFixtureType, "GuardedReceiverCleanup")]
    [InlineData(ContinuationFixtureType, "GuardedAwait")]
    [InlineData(ContinuationFixtureType, "InvertedGuardedAwait")]
    [InlineData(ContinuationFixtureType, "ReferenceGuardedAwait")]
    [InlineData(ContinuationFixtureType, "CompoundReceiverCleanup")]
    [InlineData(ContinuationFixtureType, "AwaitReceiverCleanup")]
    [InlineData(ContinuationFixtureType, "AwaitFieldReset")]
    [InlineData(ContinuationFixtureType, "GuardedAwaitFieldReset")]
    [InlineData(ReceiverFixtureType, "AwaitReceiver")]
    [InlineData(ReceiverFixtureType, "AwaitReceiverResult")]
    public void ProductWholeMemberCompilesBackExact(string fixtureType, string methodName)
    {
        using MetadataSource source = OpenClassicFixture();
        var result = Assert.Single(FidelityCheck.Evaluate(source.Path, false, FidelityCheck.ClusterMode.Off,
            type => type == fixtureType, method => method.Method == methodName));
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact && result.UsedProductWholeMember,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("AwaitInitializedStackalloc")]
    [InlineData("AwaitInitializedByteStackalloc")]
    [InlineData("TypeEquality")]
    [InlineData("CoalesceTypeOf")]
    [InlineData("TypeArrayEquality")]
    [InlineData("TypeGenericEquality")]
    [InlineData("TypeArguments")]
    [InlineData("BooleanChoice")]
    [InlineData("BooleanChoiceCalls")]
    [InlineData("NegatedBooleanChoice")]
    [InlineData("BooleanChoiceObjects")]
    [InlineData("BooleanChoiceTypeOf")]
    [InlineData("ComparisonChoice")]
    [InlineData("BooleanChoiceThenCall")]
    [InlineData("NestedInitializer")]
    [InlineData("NestedInitializerEntries")]
    [InlineData("NestedCollectionInitializer")]
    [InlineData("NestedInitializerTypeOf")]
    [InlineData("InterpolatedOperand")]
    [InlineData("InterpolatedFormats")]
    [InlineData("InterpolatedZeroAlignment")]
    [InlineData("InterpolatedNegativeAlignment")]
    [InlineData("InterpolatedEffects")]
    [InlineData("InterpolatedGeneric")]
    [InlineData("InterpolatedEscapedFormat")]
    [InlineData("YieldOnce")]
    [InlineData("StructOperand")]
    [InlineData("MutableStructOperand")]
    [InlineData("StructFactoryOperand")]
    [InlineData("ClassAwaitableOperand")]
    [InlineData("CharPropertyInitializer")]
    [InlineData("CharFieldInitializer")]
    [InlineData("BoolPropertyInitializer")]
    [InlineData("EnumPropertyInitializer")]
    [InlineData("NestedCharInitializer")]
    [InlineData("CharCollectionInitializer")]
    [InlineData("DefaultGuidInitializer")]
    [InlineData("DefaultDateTimeInitializer")]
    [InlineData("GenericAwait")]
    [InlineData("GenericArrayAwait")]
    [InlineData("GenericConstruct")]
    [InlineData("NullableDefaultReceiver")]
    [InlineData("NullableIntReceiver")]
    [InlineData("NullableGuidReceiver")]
    [InlineData("NullableBoolReceiver")]
    [InlineData("NamedNullableReceiver")]
    [InlineData("AwaitTuple")]
    [InlineData("AwaitNestedTuple")]
    [InlineData("AwaitTupleEffect")]
    [InlineData("MixedAndOr")]
    [InlineData("ShortCircuitBoth")]
    [InlineData("GenericCall")]
    [InlineData("DefaultGuidCollectionInitializer")]
    [InlineData("MultiArgumentCollectionInitializer")]
    [InlineData("AwaitLongTuple")]
    [InlineData("AndThenOr")]
    [InlineData("AwaitTypedTuple")]
    [InlineData("ByteArgument")]
    [InlineData("ByteMinimumArgument")]
    [InlineData("ByteMaximumArgument")]
    [InlineData("DecimalConstant")]
    [InlineData("CharacterArgument")]
    [InlineData("CharacterMaximumArgument")]
    [InlineData("EnumArgument")]
    [InlineData("LongEnumArgument")]
    [InlineData("ShortCircuitCall")]
    [InlineData("ShortCircuitOrCall")]
    [InlineData("NestedBooleanChoice")]
    [InlineData("NestedChoiceCalls")]
    [InlineData("ComposedSelectionTypes")]
    [InlineData("Concatenation")]
    [InlineData("IntReceiver")]
    [InlineData("LongReceiver")]
    [InlineData("BoolReceiver")]
    [InlineData("VariableAnd")]
    [InlineData("DateTimeReceiver")]
    [InlineData("DecimalReceiver")]
    [InlineData("NamedIntReceiver")]
    public void ExpressionOutputsCompileBack(string methodName)
        => AssertCompilesBack(ExpressionFixtureType, methodName);

    [Theory]
    [Trait("Speed", "Slow")]
    [MemberData(nameof(InventoryCases))]
    public void ExpressionInventoryCompilesBack(string method)
        => AssertCompilesBack(InventoryFixtureType, method);

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("GenericContextAwait")]
    [InlineData("GenericContextTuple")]
    public void GenericContainerOutputsCompileBack(string method)
        => AssertCompilesBack(GenericExpressionFixtureType, method);

    static void AssertCompilesBack(string fixtureType, string methodName)
    {
        var result = Assert.Single(FidelityCheck.Evaluate(FixtureCatalog.DecompilerClassicAsync.AssemblyPath(),
            type => type == fixtureType, method => method.Method == methodName));
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}\n"
                + (result.FidelityDiff is { Rows.IsDefault: false } diff
                    ? string.Join("\n", diff.Rows.Select(row => $"{row.Kind}: {row.Operation.Display}"))
                    : ""));
    }

    static (IrFunction Function, DecompilerResult Result) RaiseExpressionFixture(string method)
    {
        using MetadataSource source = OpenClassicFixture();
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, ExpressionFixtureType, method));
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            target => IrImporter.Import(source, target)));
        function.CheckInvariant();
        return (function, CSharpPrinter.Print(function));
    }

    static ClassicInverseRequest OpenRequest(MetadataSource source, string methodName)
    {
        IrFunction kickoff = Assert.IsType<IrFunction>(
            IrImporter.Import(source, FixtureType, methodName));
        var seed = Assert.IsType<
            ClassicAsyncRequestAdapterResult.RequestAvailable>(
                kickoff.ClassicAsyncRequest).Request;
        IrFunction execution = Assert.IsType<IrFunction>(
            IrImporter.Import(source, seed.ExecutionMethod.Handle));
        StoreField builder = Assert.Single(
            kickoff.Body.Descendants.OfType<StoreField>(),
            store => store.Field.Name == "<>t__builder"
                && store.Instance is LoadLocalAddress);
        return new ClassicInverseRequest(
            seed.DeclaredMethod,
            seed.ExecutionMethod,
            seed.Relationship,
            seed.AcquisitionGuard,
            kickoff,
            execution,
            Assert.IsType<LoadLocalAddress>(builder.Instance).Index);
    }

    static ClassicInverseRequest CopyRequest(
        ClassicInverseRequest request,
        MetadataMethodAddress? declaredMethod = null,
        MetadataMethodAddress? executionMethod = null,
        StateMachineRelationship? relationship = null,
        IrFunction? executionBody = null,
        int? stateMachineLocal = null)
        => new(
            declaredMethod ?? request.DeclaredMethod,
            executionMethod ?? request.ExecutionMethod,
            relationship ?? request.Relationship,
            request.AcquisitionGuard,
            request.KickoffBody,
            executionBody ?? request.ExecutionBody,
            stateMachineLocal ?? request.StateMachineLocal);

    static InstructionExceptionFlowFacts AvailableFacts(IrFunction function)
        => Assert.IsType<ILInspector.Instructions.InstructionExceptionFlowResult<
            InstructionExceptionFlowFacts>.Available>(
                function.ExceptionFlow).Value;

    static IEnumerable<string> ClassicKickoffMethods(MetadataSource source)
    {
        MetadataReader reader = source.Reader;
        TypeDefinition type = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Single(candidate =>
                reader.StringComparer.Equals(
                    candidate.Namespace,
                    "ILInspector.Decompiler.Fixtures.ClassicAsync")
                && reader.StringComparer.Equals(
                    candidate.Name,
                    "AsyncFixtures"));

        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            IrFunction? function = IrImporter.Import(source, handle);
            if (function?.ClassicAsyncRequest
                is ClassicAsyncRequestAdapterResult.RequestAvailable)
            {
                yield return reader.GetString(
                    reader.GetMethodDefinition(handle).Name);
            }
        }
    }

    static MetadataSource OpenClassicFixture()
        => OpenFixture("ILInspector.Decompiler.Fixtures.ClassicAsync");

    static MetadataSource OpenClassicStateMachinesFixture()
        => OpenFixture("ILInspector.Decompiler.Fixtures.ClassicStateMachines");

    static MetadataSource OpenFixture(string name)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        return MetadataSource.Open(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", name, configuration, name + ".dll")));
    }
}
