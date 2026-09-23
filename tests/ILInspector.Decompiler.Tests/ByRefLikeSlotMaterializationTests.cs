using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ByRefLikeSlotMaterializationTests
{
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef Span = TypeRef.CoreLib("System", "Span`1");
    static readonly TypeRef SpanInt32 = TypeRef.GenericInstance(Span, [Int32]);
    static readonly TypeRef RefStruct = TypeRef.Definition("Samples", "Samples", "RefStruct");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExactByRefLikeValuePreservesProducerAndNominalType(
        bool generic,
        bool untypedLoad)
    {
        var definition = generic ? Span : RefStruct;
        var type = generic ? SpanInt32 : RefStruct;
        var producer = new DefaultValue(type);
        var function = Function(type,
            new StoreStackSlot(0, producer),
            new Return(new LoadStackSlot(0, untypedLoad ? null : type)));
        KnowByRefLikeValue(function, definition);

        Assert.False(CoercionDomain.InDomain(type, function.TypeShapes));
        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.WillMaterialize, decision.Vetoes.ToString());
        var invariant = SlotMaterializationInvariant.Capture(function);

        new SlotMaterializationPass().Run(function, PassContext.None);

        invariant.Check();
        Assert.Equal(type, Assert.Single(function.Locals));
        Assert.Same(producer, Assert.Single(function.Descendants.OfType<StoreLocal>()).Value);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData("unknown-shape")]
    [InlineData("unspellable-name")]
    [InlineData("unsupported-argument")]
    public void IncompleteByRefLikeEvidenceRemainsDeferred(string boundary)
    {
        var definition = boundary switch
        {
            "unspellable-name" => TypeRef.Definition("Samples", "Samples", "<RefStruct>"),
            _ => Span,
        };
        var type = boundary == "unsupported-argument"
            ? TypeRef.GenericInstance(definition, [TypeRef.Unsupported("test argument")])
            : definition == Span ? SpanInt32 : definition;
        var function = Function(type,
            new StoreStackSlot(0, new DefaultValue(type)),
            new Return(new LoadStackSlot(0, type)));
        if (boundary != "unknown-shape")
            function.TypeShapes = new Dictionary<TypeRef, TypeShape>
            {
                [definition] = TypeShape.ValueType,
            };
        function.ByRefLikeTypes = new HashSet<TypeRef> { definition };

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Fact]
    public void CanonicalSpanIdentityEstablishesByRefLikeStorage()
    {
        var function = Function(SpanInt32,
            new StoreStackSlot(0, new DefaultValue(SpanInt32)),
            new Return(new LoadStackSlot(0, SpanInt32)));
        function.TypeShapes = new Dictionary<TypeRef, TypeShape>
        {
            [Span] = TypeShape.ValueType,
        };

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));

        Assert.True(decision.WillMaterialize, decision.Vetoes.ToString());
    }

    [Fact]
    public void ByRefLikeStorageDoesNotInferConversions()
    {
        var spanByte = TypeRef.GenericInstance(Span, [Byte]);
        var function = Function(SpanInt32,
            new StoreStackSlot(0, new DefaultValue(spanByte)),
            new Return(new LoadStackSlot(0, SpanInt32)));
        KnowByRefLikeValue(function, Span);

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ByRefLikeCopyComponentsRemainAtomic(bool incomplete)
    {
        var function = Function(SpanInt32,
            new StoreStackSlot(0, new DefaultValue(SpanInt32)),
            new StoreStackSlot(1, new LoadStackSlot(0, SpanInt32)),
            incomplete
                ? new ExpressionStatement(new LoadStackSlot(1, type: null))
                : new Return(new LoadStackSlot(1, SpanInt32)));
        KnowByRefLikeValue(function, Span);
        var decisions = SlotMaterializationPass.Analyze(function);

        Assert.Equal(2, decisions.Count);
        if (incomplete)
        {
            Assert.All(decisions, decision => Assert.True(
                decision.Vetoes.HasFlag(SlotMaterializationVeto.IncompleteCopyComponent)));
            AssertRetained(function);
            return;
        }

        Assert.All(decisions, decision =>
            Assert.True(decision.WillMaterialize, decision.Vetoes.ToString()));
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.Equal([SpanInt32, SpanInt32], function.Locals);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void RepeatedStoresPreserveResidualOutputAndOccurrenceOrder()
    {
        var first = new DefaultValue(SpanInt32);
        var second = new DefaultValue(SpanInt32);
        var function = Function(SpanInt32,
            new StoreStackSlot(0, first),
            new ExpressionStatement(new LoadStackSlot(0, SpanInt32)),
            new StoreStackSlot(0, second),
            new Return(new LoadStackSlot(0, SpanInt32)));
        KnowByRefLikeValue(function, Span);
        string before = CSharpPrinter.Print(function).Output!;
        var invariant = SlotMaterializationInvariant.Capture(function);

        new SlotMaterializationPass().Run(function, PassContext.None);

        invariant.Check();
        Assert.Equal(before, CSharpPrinter.Print(function).Output);
        Assert.Same(first, function.Descendants.OfType<StoreLocal>().First().Value);
        Assert.Same(second, function.Descendants.OfType<StoreLocal>().Last().Value);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void CrossBlockStoresPreserveResidualDeclarationPlacement()
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new ConditionalBranch(
            new LoadArgument(0, "choose", Boolean),
            4));
        entry.Add(new Branch(8));
        var first = new Block(4);
        first.Add(new StoreStackSlot(0, new DefaultValue(SpanInt32)));
        first.Add(new Branch(12));
        var second = new Block(8);
        second.Add(new StoreStackSlot(0, new DefaultValue(SpanInt32)));
        second.Add(new Branch(12));
        var join = new Block(12);
        join.Add(new Return(new LoadStackSlot(0, SpanInt32)));
        foreach (var block in (Block[])[entry, first, second, join])
            body.Add(block);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(
                SpanInt32,
                [new Parameter("choose", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);
        KnowByRefLikeValue(function, Span);
        string before = CSharpPrinter.Print(function).Output!;

        new SlotMaterializationPass().Run(function, PassContext.None);

        string after = CSharpPrinter.Print(function).Output!;
        Assert.Equal(before, after);
        Assert.Contains("Span<int> S_0;", after);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(nameof(ByRefLikeSlotMaterializationSamples.ReadSpan))]
    [InlineData(nameof(ByRefLikeSlotMaterializationSamples.ReadReadOnlySpan))]
    [InlineData(nameof(ByRefLikeSlotMaterializationSamples.ReadCustom))]
    public void CompilerProducedByRefLikeStorageMaterializes(string method)
    {
        string path = typeof(ByRefLikeSlotMaterializationSamples).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(
            source,
            typeof(ByRefLikeSlotMaterializationSamples).FullName!,
            method);
        var decisions = SlotMaterializationPass.Analyze(function)
            .Where(decision => decision.WillMaterialize
                && decision.Type is { } type
                && function.ByRefLikeTypes.Contains(
                    type.Kind == TypeRefKind.GenericInstance ? type.ElementType! : type))
            .ToArray();

        Assert.NotEmpty(decisions);
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.All(decisions, decision => Assert.Contains(decision.Type!, function.Locals));
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void RealRoslynReadOnlySpanStoragePreservesOutput()
    {
        string path = typeof(SyntaxTree).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(
            source,
            "Roslyn.Utilities.PathUtilities",
            "EnsureTrailingSeparator");
        var decision = Assert.Single(SlotMaterializationPass.Analyze(function),
            decision => decision.Type is
            {
                Kind: TypeRefKind.GenericInstance,
                ElementType.Name: "ReadOnlySpan`1",
            });
        string before = CSharpPrinter.Print(function).Output!;

        Assert.True(decision.WillMaterialize, decision.Vetoes.ToString());
        new SlotMaterializationPass().Run(function, PassContext.None);

        string after = CSharpPrinter.Print(function).Output!;
        Assert.Equal(before, after);
        Assert.Contains("ReadOnlySpan<char> S_256 = (ReadOnlySpan<char>)s;", after);
        Assert.DoesNotContain(function.Descendants.OfType<StoreStackSlot>(),
            store => store.Slot == decision.Slot);
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(),
            load => load.Slot == decision.Slot);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void CompilerProducedByRefLikeStorageRecompilesExactly()
    {
        var results = FidelityCheck.Evaluate(
            typeof(ByRefLikeSlotMaterializationSamples).Assembly.Location,
            type => type == typeof(ByRefLikeSlotMaterializationSamples).FullName).ToArray();
        Assert.Equal(5, results.Length);
        Assert.All(results, result => Assert.True(
            result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }

    static void KnowByRefLikeValue(IrFunction function, TypeRef definition)
    {
        function.TypeShapes = new Dictionary<TypeRef, TypeShape>
        {
            [definition] = TypeShape.ValueType,
        };
        function.ByRefLikeTypes = new HashSet<TypeRef> { definition };
    }
}
