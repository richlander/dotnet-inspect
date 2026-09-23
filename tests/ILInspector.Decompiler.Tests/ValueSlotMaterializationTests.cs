using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ValueSlotMaterializationTests
{
    static readonly TypeRef Value = TypeRef.Definition("Samples", "Samples", "Value");
    static readonly TypeRef GenericValue = TypeRef.Definition("Samples", "Samples", "Value`1");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExactValuePreservesProducerAndNominalType(bool generic, bool untypedLoad)
    {
        var type = generic ? TypeRef.GenericInstance(GenericValue, [Int32]) : Value;
        var value = new DefaultValue(type);
        var function = Function(type,
            new StoreStackSlot(0, value),
            new Return(new LoadStackSlot(0, untypedLoad ? null : type)));
        KnowValueType(function, generic ? GenericValue : Value);

        Assert.False(CoercionDomain.InDomain(type, function.TypeShapes));
        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.Equal(type, Assert.Single(function.Locals));
        Assert.Same(value, Assert.Single(function.Descendants.OfType<StoreLocal>()).Value);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("open-generic")]
    [InlineData("wrong-arity")]
    [InlineData("out-of-scope")]
    [InlineData("byref-argument")]
    [InlineData("name")]
    public void UnsupportedValueStorageRemainsDeferred(string shape)
    {
        var definition = shape switch
        {
            "name" => TypeRef.Definition("Samples", "Samples", "<Invalid>"),
            "open-generic" or "wrong-arity" or "out-of-scope" or "byref-argument" => GenericValue,
            _ => Value,
        };
        var type = shape switch
        {
            "wrong-arity" => TypeRef.GenericInstance(definition, [Int32, Int32]),
            "out-of-scope" => TypeRef.GenericInstance(definition, [TypeRef.MethodGenericParameter(0, "T")]),
            "byref-argument" => TypeRef.GenericInstance(definition, [TypeRef.ByRef(Int32)]),
            _ => definition,
        };
        var function = Function(type,
            new StoreStackSlot(0, new DefaultValue(type)),
            new Return(new LoadStackSlot(0, type)));
        if (shape != "unknown")
            KnowValueType(function, definition);
        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes
            .HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Theory]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    [InlineData("sign", false)]
    [InlineData("width", false)]
    public void ExactStorageDoesNotInferValueConversions(string difference, bool mixed)
    {
        var definition = difference == "struct" ? Value : TypeRef.CoreLib("System", "Nullable`1");
        var target = difference == "struct" ? Value : TypeRef.GenericInstance(definition, [Int32]);
        var source = difference switch
        {
            "struct" => TypeRef.Definition("Samples", "Samples", "OtherValue"),
            "sign" => TypeRef.GenericInstance(definition, [TypeRef.CoreLib("System", "UInt32")]),
            "width" => TypeRef.GenericInstance(definition, [TypeRef.CoreLib("System", "Int64")]),
            _ => throw new ArgumentOutOfRangeException(nameof(difference)),
        };
        var statements = new List<IrNode>
        {
            new StoreStackSlot(0, new DefaultValue(source)),
            new Return(new LoadStackSlot(0, target)),
        };
        if (mixed)
        {
            statements.Insert(0, new StoreStackSlot(0, new DefaultValue(target)));
            statements.Insert(1, new ExpressionStatement(new LoadStackSlot(0, target)));
        }
        var function = Function(target, [.. statements]);
        KnowValueType(function, definition);
        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes
            .HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValueCopyComponentsRemainAtomic(bool incomplete)
    {
        var function = Function(Value,
            new StoreStackSlot(0, new DefaultValue(Value)),
            new StoreStackSlot(1, new LoadStackSlot(0, Value)),
            incomplete
                ? new ExpressionStatement(new LoadStackSlot(1, type: null))
                : new Return(new LoadStackSlot(1, Value)));
        KnowValueType(function, Value);
        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.Equal(2, decisions.Count);
        if (incomplete)
        {
            Assert.All(decisions, decision =>
                Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.IncompleteCopyComponent)));
            AssertRetained(function);
            return;
        }

        Assert.All(decisions, decision => Assert.True(decision.WillMaterialize));
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.Equal([Value, Value], function.Locals);
        var copy = Assert.IsType<LoadLocal>(function.Descendants.OfType<StoreLocal>().Last().Value);
        Assert.Equal(0, copy.Index);
        Assert.Equal(Value, copy.ResultType);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void ValueBoxKeepsItsTypedOperand()
    {
        var box = new Box(Value, new LoadStackSlot(0, Value));
        var function = Function(Object,
            new StoreStackSlot(0, new DefaultValue(Value)),
            new Return(box));
        KnowValueType(function, Value);
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.Same(box, Assert.Single(function.Descendants.OfType<Box>()));
        Assert.Equal(Value, Assert.IsType<LoadLocal>(box.Operand).ResultType);
        Assert.Equal(Value, box.Type);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(nameof(ValueSlotMaterializationSamples.ReadDateTime))]
    [InlineData(nameof(ValueSlotMaterializationSamples.ReadNullable))]
    [InlineData(nameof(ValueSlotMaterializationSamples.ReadGenericValue))]
    [InlineData(nameof(ValueSlotMaterializationSamples.ReadMutableValue))]
    [InlineData(nameof(ValueSlotMaterializationSamples.CopyThenMutate))]
    [InlineData(nameof(ValueSlotMaterializationSamples.BoxValue))]
    [InlineData(nameof(ValueSlotMaterializationSamples.BoxNullable))]
    public void CompilerProducedValueStorageMaterializes(string method)
    {
        string path = typeof(ValueSlotMaterializationSamples).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(source, typeof(ValueSlotMaterializationSamples).FullName!, method);
        var decisions = SlotMaterializationPass.Analyze(function).Where(decision =>
            decision.WillMaterialize && decision.Type is { } type
            && !CoercionDomain.InDomain(type, function.TypeShapes)
            && function.TypeShapes.GetValueOrDefault(
                type.Kind == TypeRefKind.GenericInstance ? type.ElementType! : type) == TypeShape.ValueType).ToArray();
        Assert.NotEmpty(decisions);
        var boxes = function.Descendants.OfType<Box>().ToArray();
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.All(decisions, decision => Assert.Contains(decision.Type!, function.Locals));
        Assert.All(boxes, box => Assert.Equal(box.Type, box.Operand.ResultType));
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void RealRoslynImmutableArrayReadMaterializes()
    {
        string path = typeof(SyntaxTree).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(source,
            "Microsoft.CodeAnalysis.Collections.RoslynImmutableInterlocked", "VolatileRead");
        Assert.Contains(SlotMaterializationPass.Analyze(function), decision =>
            decision.WillMaterialize
            && decision.Type is { Kind: TypeRefKind.GenericInstance, ElementType.Name: "ImmutableArray`1" });
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void ValueSwapKeepsTheExistingRaise()
    {
        string path = typeof(ValueSlotMaterializationSamples).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(source, typeof(ValueSlotMaterializationSamples).FullName!,
            nameof(ValueSlotMaterializationSamples.SwapValues));
        var pending = Assert.Single(SlotMaterializationPass.Analyze(function),
            decision => decision.Vetoes == SlotMaterializationVeto.PendingStorageSwap);
        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == pending.Slot);
        new SwapIdiomPass().Run(function, PassContext.None);
        Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void CompilerProducedValuesRecompileExactly()
    {
        var results = FidelityCheck.Evaluate(typeof(ValueSlotMaterializationSamples).Assembly.Location,
            type => type == typeof(ValueSlotMaterializationSamples).FullName).ToArray();
        Assert.Equal(8, results.Length);
        Assert.All(results, result => Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }

    static void KnowValueType(IrFunction function, TypeRef definition)
        => function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [definition] = TypeShape.ValueType };
}
