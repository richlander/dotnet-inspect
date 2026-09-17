using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class NamedReferenceSlotMaterializationTests
{
    static readonly TypeRef Reference = TypeRef.Definition("Samples", "Samples", "Reference");
    static readonly TypeRef GenericReference = TypeRef.Definition("Samples", "Samples", "Reference`1");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExactNamedReferencePreservesProducer(bool generic, bool untypedLoad)
    {
        var type = generic ? TypeRef.GenericInstance(GenericReference, [Int32]) : Reference;
        var value = new Constant(null, type);
        var function = Function(type,
            new StoreStackSlot(0, value),
            new Return(new LoadStackSlot(0, untypedLoad ? null : type)));
        function.TypeShapes = new Dictionary<TypeRef, TypeShape>
        {
            [generic ? GenericReference : Reference] = TypeShape.Reference,
        };

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

    [Fact]
    public void UnknownNamedShapeRemainsDeferred()
    {
        var function = Function(Reference,
            new StoreStackSlot(0, new Constant(null, Reference)),
            new Return(new LoadStackSlot(0, Reference)));
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [Reference] = TypeShape.Unknown };

        Assert.Equal(SlotMaterializationVeto.OutsideCoercionDomain,
            Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes);
        AssertRetained(function);
    }

    [Theory]
    [InlineData("open-generic")]
    [InlineData("wrong-arity")]
    [InlineData("out-of-scope")]
    [InlineData("byref-argument")]
    [InlineData("unsupported-argument")]
    [InlineData("name")]
    public void UnspellableReferenceTypesRemainSlots(string shape)
    {
        var definition = shape == "name"
            ? TypeRef.Definition("Samples", "Samples", "<Invalid>") : GenericReference;
        var type = shape switch
        {
            "open-generic" or "name" => definition,
            "wrong-arity" => TypeRef.GenericInstance(definition, [Int32, Int32]),
            "out-of-scope" => TypeRef.GenericInstance(definition, [TypeRef.MethodGenericParameter(0, "T")]),
            "byref-argument" => TypeRef.GenericInstance(definition, [TypeRef.ByRef(Int32)]),
            "unsupported-argument" => TypeRef.GenericInstance(definition, [TypeRef.Unsupported("test argument")]),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var function = Function(type,
            new StoreStackSlot(0, new Constant(null, type)),
            new Return(new LoadStackSlot(0, type)));
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [definition] = TypeShape.Reference };

        Assert.Equal(SlotMaterializationVeto.OutsideCoercionDomain,
            Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes);
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonExactProducersAreNotReferenceConversions(bool mixed)
    {
        var statements = new List<IrNode>
        {
            new StoreStackSlot(0, new Constant(null, Object)),
            new Return(new LoadStackSlot(0, Reference)),
        };
        if (mixed)
        {
            statements.Insert(0, new StoreStackSlot(0, new Constant(null, Reference)));
            statements.Insert(1, new ExpressionStatement(new LoadStackSlot(0, Reference)));
        }
        var function = Function(Reference, [.. statements]);
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [Reference] = TypeShape.Reference };

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes
            .HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedReferenceCopyComponentsRemainAtomic(bool incomplete)
    {
        var function = Function(Reference,
            new StoreStackSlot(0, new Constant(null, Reference)),
            new StoreStackSlot(1, new LoadStackSlot(0, Reference)),
            incomplete
                ? new ExpressionStatement(new LoadStackSlot(1, type: null))
                : new Return(new LoadStackSlot(1, Reference)));
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [Reference] = TypeShape.Reference };
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
        Assert.Equal([Reference, Reference], function.Locals);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(nameof(NamedReferenceSlotMaterializationSamples.ReadClass))]
    [InlineData(nameof(NamedReferenceSlotMaterializationSamples.ReadInterface))]
    [InlineData(nameof(NamedReferenceSlotMaterializationSamples.ReadDelegate))]
    [InlineData(nameof(NamedReferenceSlotMaterializationSamples.ReadGenericClass))]
    [InlineData(nameof(NamedReferenceSlotMaterializationSamples.ReadGenericInterface))]
    [InlineData(nameof(NamedReferenceSlotMaterializationSamples.ReadGenericDelegate))]
    [InlineData(nameof(NamedReferenceSlotMaterializationSamples.AllocateAndObserve))]
    [InlineData(nameof(NamedReferenceSlotMaterializationSamples.CovariantReturn))]
    public void CompilerProducedNamedReferencesMaterialize(string method)
    {
        string path = typeof(NamedReferenceSlotMaterializationSamples).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(source, typeof(NamedReferenceSlotMaterializationSamples).FullName!, method);
        var decisions = SlotMaterializationPass.Analyze(function)
            .Where(decision => decision.WillMaterialize
                && decision.Type?.Kind is TypeRefKind.Definition or TypeRefKind.GenericInstance).ToArray();
        Assert.NotEmpty(decisions);
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.All(decisions, decision => Assert.Contains(decision.Type!, function.Locals));
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void RealRoslynSyntaxNodeMaterializes()
    {
        using var source = MetadataSource.Open(typeof(SyntaxTree).Assembly.Location);
        var function = RaiseToMaterialization(source, "Microsoft.CodeAnalysis.ChildSyntaxList", "GetHashCode");
        Assert.Contains(SlotMaterializationPass.Analyze(function),
            decision => decision.WillMaterialize && decision.Type is { Name: "SyntaxNode" });
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void ClassSwapKeepsTheExistingRaise()
    {
        string path = typeof(NamedReferenceSlotMaterializationSamples).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(source, typeof(NamedReferenceSlotMaterializationSamples).FullName!,
            nameof(NamedReferenceSlotMaterializationSamples.SwapClasses));
        var pending = Assert.Single(SlotMaterializationPass.Analyze(function),
            decision => decision.Vetoes == SlotMaterializationVeto.PendingStorageSwap);
        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == pending.Slot);
        new SwapIdiomPass().Run(function, PassContext.None);
        Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void CompilerProducedUnresolvedReferenceRemainsDeferred()
    {
        using var source = MetadataSource.Open(typeof(NamedReferenceSlotMaterializationSamples).Assembly.Location);
        var function = RaiseToMaterialization(source, typeof(NamedReferenceSlotMaterializationSamples).FullName!,
            nameof(NamedReferenceSlotMaterializationSamples.ReadClass));
        var decision = Assert.Single(SlotMaterializationPass.Analyze(function),
            decision => decision.Type is { Name: "Uri" });
        Assert.Equal(TypeShape.Unknown, function.TypeShapes.GetValueOrDefault(decision.Type!));
        Assert.Equal(SlotMaterializationVeto.OutsideCoercionDomain, decision.Vetoes);
        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == decision.Slot);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void CompilerProducedNamedReferencesRecompileExactly()
    {
        var results = FidelityCheck.Evaluate(
            typeof(NamedReferenceSlotMaterializationSamples).Assembly.Location,
            type => type == typeof(NamedReferenceSlotMaterializationSamples).FullName,
            method => method.Method != nameof(NamedReferenceSlotMaterializationSamples.ReadUnconstrained)).ToArray();
        Assert.Equal(9, results.Length);
        Assert.All(results, result => Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }
}
