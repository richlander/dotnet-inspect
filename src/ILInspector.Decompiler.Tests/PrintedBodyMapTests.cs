using System.Text.Json;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// The rich map the printer builds is keyed by IrNode, so it cannot leave the
// process that built it. These pin the projection that can: an extent, a name,
// and an integer id -- no references, and therefore serialisable.
[Trait("Area", "Printer")]
public class PrintedBodyMapTests
{
    static (string Output, PrintedRangeMap Ranges) Print(string methodName)
        => Print(typeof(AllocSampleClass), methodName);

    static (string Output, PrintedRangeMap Ranges) Print(Type fixtureType, string methodName)
    {
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var function = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!, out var ranges);
        Assert.NotNull(result.Output);
        return (result.Output!, ranges);
    }

    [Theory]
    [InlineData(nameof(AllocSampleClass.SumList))]
    [InlineData(nameof(AllocSampleClass.MakeArray))]
    public void EverySpanSelectsExactlyTheCharactersTheNodePrinted(string methodName)
    {
        // The whole point of the projection is that a consumer holding only text
        // can slice it. If the extent did not select the same characters
        // the node-keyed range does, the payload would be confidently wrong.
        var (output, ranges) = Print(methodName);
        var map = PrintedBodyMap.Create(ranges);
        Assert.NotEmpty(map.Nodes);

        // Independently slice the printer's absolute offsets and compare them
        // with the portable line/column projection. Reusing only TryGetExtent
        // on both sides would let a coordinate conversion defect agree with
        // itself.
        var expected = new HashSet<(string Kind, PrintedExtent Extent)>();
        foreach (var printed in ranges)
        {
            if (!ranges.TryGetExtent(printed.Node, out var extent))
                continue;
            int start = printed.Characters.Start.GetOffset(output.Length);
            int end = printed.Characters.End.GetOffset(output.Length);
            Assert.Equal(
                output[start..end].TrimEnd('\r', '\n'),
                Text(map, extent));
            expected.Add((
                AnnotatedSourceNodeKindProjection.From(printed.Node),
                extent));
        }

        Assert.Equal(expected.Count, map.Nodes.Count);
        Assert.True(expected.SetEquals(map.Nodes.Select(node => (node.Kind, node.Extent))));
    }

    [Fact]
    public void StableKindProjectionMakesAnExplicitDecisionForEveryIrNode()
    {
        var concreteNodes = typeof(IrNode).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(IrNode).IsAssignableFrom(type))
            .OrderBy(type => type.FullName)
            .ToArray();
        var mappings = AnnotatedSourceNodeKindProjection.Mappings
            .OrderBy(pair => pair.Key.FullName)
            .ToArray();

        Assert.NotEmpty(concreteNodes);
        Assert.Equal(concreteNodes, mappings.Select(pair => pair.Key));
        Assert.All(mappings, pair => Assert.True(
            AnnotatedSourceNodeKinds.IsKnown(pair.Value),
            $"{pair.Key.Name} maps to undocumented kind {pair.Value}."));
        Assert.DoesNotContain(mappings, pair => pair.Value == AnnotatedSourceNodeKinds.Unknown);

        Assert.Equal("ConversionExpression", AnnotatedSourceNodeKindProjection.From(
            new Coerce(
                TypeRef.CoreLib("System", "Int64"),
                new Constant(1, TypeRef.CoreLib("System", "Int32")))));
        Assert.Equal("AssignmentStatement", AnnotatedSourceNodeKindProjection.From(
            new StoreStackSlot(0, new Constant(1, TypeRef.CoreLib("System", "Int32")))));
        Assert.Equal("BinaryExpression", AnnotatedSourceNodeKindProjection.From(
            new LogicalBinary(
                LogicalKind.And,
                new Constant(true, TypeRef.CoreLib("System", "Boolean")),
                new Constant(false, TypeRef.CoreLib("System", "Boolean")))));

        var objectType = TypeRef.CoreLib("System", "Object");
        var intType = TypeRef.CoreLib("System", "Int32");
        var indexer = new MethodRef(objectType, "get_Item", objectType, [intType], HasThis: true);
        Assert.Equal("ElementAccessExpression", AnnotatedSourceNodeKindProjection.From(
            new LoadProperty(
                indexer,
                new LoadArgument(0, "items", objectType),
                [new Constant(0, intType)])));
        Assert.Equal("MemberAccessExpression", AnnotatedSourceNodeKindProjection.From(
            new LoadProperty(
                indexer with { Name = "get_Count", ParameterTypes = [] },
                new LoadArgument(0, "items", objectType),
                [])));

        Assert.Equal("TypeOfExpression", AnnotatedSourceNodeKindProjection.From(
            new LoadToken(RuntimeTokenKind.Type, objectType, objectType.ToDisplayString())));
        Assert.Equal("UnsupportedExpression", AnnotatedSourceNodeKindProjection.From(
            new LoadToken(RuntimeTokenKind.Field, null, "C.F")));
    }

    [Fact]
    public void ReplayToleratesKindsAddedByANewerProducer()
    {
        var map = new PrintedBodyMap(
            ["future"],
            [new PrintedNodeSpan(0, "FutureSyntax", new PrintedExtent(0, 0, 0, 6))],
            [],
            []);

        Assert.False(AnnotatedSourceNodeKinds.IsKnown(Assert.Single(map.Nodes).Kind));
    }

    [Theory]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.NegateSum), "-(a + b)", "UnaryExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.NegateSum), "a + b", "BinaryExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.MoneyToInt), "(int)m", "ConversionExpression")]
    [InlineData(typeof(GenericIsInstanceSpecimens<>), nameof(GenericIsInstanceSpecimens<object>.DirectIs), "value is T", "PatternExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.IsNotNullReference), "o is not null", "PatternExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.FloatUnordered), "!(a <= b)", "UnaryExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.ConstantUIntSpan), "new uint[] { 1, 10, 100, 1000, 10000 }", "ArrayCreationExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.AsWithoutPattern), "o as string", "ConversionExpression")]
    [InlineData(typeof(RectangularArraySamples), nameof(RectangularArraySamples.MdGet), "a[i, j]", "ElementAccessExpression")]
    [InlineData(typeof(RectangularArraySamples), nameof(RectangularArraySamples.MdSet), "a[i, j] = v", "AssignmentStatement")]
    [InlineData(typeof(RectangularArraySamples), nameof(RectangularArraySamples.MdNew), "new int[3, 4]", "ArrayCreationExpression")]
    public void RenderSpecializationsRecordTheirSurfaceKind(
        Type fixtureType,
        string methodName,
        string text,
        string expectedKind)
    {
        var (_, ranges) = Print(fixtureType, methodName);
        var map = PrintedBodyMap.Create(ranges);

        Assert.Contains(map.Nodes, node => node.Kind == expectedKind && Text(map, node.Extent) == text);
        Assert.DoesNotContain(
            map.Nodes,
            node => node.Kind is "InvocationExpression" or "ObjectCreationExpression"
                && Text(map, node.Extent) == text);
    }

    [Fact]
    public void ConditionalRenderedAsLogicalAndRecordsBinaryKind()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var diamond = new Conditional(
            new LoadArgument(0, "exists", boolType),
            new Constant(true, boolType),
            new Constant(false, boolType))
        {
            MergedType = boolType,
        };
        var block = new Block(0);
        block.Add(new StoreLocal(0, intType, diamond));
        block.Add(new Return(new LoadLocal(0, intType)));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                intType,
                [new Parameter("exists", boolType)],
                HasThis: false,
                GenericParameterCount: 0),
            [intType],
            container);

        var result = CSharpPrinter.Print(function, out var ranges);

        Assert.NotNull(result.Output);
        Assert.True(ranges.TryGetRange(diamond, out var range));
        Assert.Equal("exists && true", ranges.Output[range]);
        Assert.True(ranges.TryGetNodeKind(diamond, out string? kind));
        Assert.Equal("BinaryExpression", kind);
    }

    [Fact]
    public void IntegerTruthinessContainingPatternTextRecordsBinaryKind()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var objectType = TypeRef.CoreLib("System", "Object");
        var stringType = TypeRef.CoreLib("System", "String");
        var typeTest = new Comparison(
            ComparisonKind.NotEqual,
            isUnsigned: false,
            new IsInstance(
                stringType,
                new LoadArgument(0, "value", objectType)),
            new Constant(null, objectType));
        var integerConditional = new Conditional(
            typeTest,
            new Constant(1, intType),
            new Constant(0, intType))
        {
            MergedType = intType,
        };
        var negated = new LogicalNot(integerConditional);
        var block = new Block(0);
        block.Add(new Return(negated));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                boolType,
                [new Parameter("value", objectType)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        var result = CSharpPrinter.Print(function, out var ranges);

        Assert.NotNull(result.Output);
        Assert.True(ranges.TryGetRange(negated, out var range));
        Assert.Equal("(value is string ? 1 : 0) == 0", ranges.Output[range]);
        Assert.True(ranges.TryGetNodeKind(negated, out string? kind));
        Assert.Equal("BinaryExpression", kind);
    }

    [Fact]
    public void RenderSpecializationKeepsPlacedFactAndNodeKindsEqual()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(CfgSampleClass).FullName!,
            nameof(CfgSampleClass.NegateSum));
        Assert.NotNull(function);
        CSharpPrinter.PrintRaised(function!, out var ranges);
        var addition = Assert.Single(
            function!.Descendants.OfType<Call>(),
            call => AnnotatedSourceNodeKindProjection.OperatorKind(call) == "BinaryExpression");

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [addition] = [new Annotation(Alloc, addition.SourceOffset)],
            });

        var fact = Assert.Single(map.Annotations);
        var node = map.Nodes[Assert.IsType<int>(fact.NodeId)];
        Assert.Equal("BinaryExpression", fact.Kind);
        Assert.Equal(node.Kind, fact.Kind);
        Assert.Equal(node.Extent, fact.Extent);
    }

    [Fact]
    public void NodeIdsAreContiguousAndCanonicallyOrdered()
    {
        // Ids are the whole join. PrintedRangeMap only promises descendants
        // before ancestors, so ids cut from emission order would be reproducible
        // by accident; the canonical order is what makes them a contract.
        var (_, ranges) = Print(nameof(AllocSampleClass.SumList));
        var map = PrintedBodyMap.Create(ranges);

        Assert.NotEmpty(map.Nodes);
        Assert.Equal(Enumerable.Range(0, map.Nodes.Count), map.Nodes.Select(node => node.Id));
        for (int i = 1; i < map.Nodes.Count; i++)
        {
            var previous = map.Nodes[i - 1].Extent;
            var current = map.Nodes[i].Extent;
            Assert.True(
                ComparePosition(previous.StartLine, previous.StartColumn, current.StartLine, current.StartColumn) <= 0,
                "Node extents must be ordered by start position.");
        }
    }

    [Fact]
    public void AFactOnARefusedNodeRemainsPresentAndExplicitlyUnplaced()
    {
        // TwiceTheSameOnALaterLine prints "return y + y;" on a line after the
        // first, so the LoadLocal spelling is ambiguous and the printer
        // deliberately records no range for it. Facts are positive-only --
        // always shown somewhere -- so a fact keyed to that node must still be
        // present, but inheriting an ancestor coordinate would claim characters
        // the fact's node did not establish.
        var source = MetadataSource.Open(typeof(PrintedRangeExpressionFixture).Assembly.Location);
        var fn = IrImporter.Import(source, typeof(PrintedRangeExpressionFixture).FullName!, nameof(PrintedRangeExpressionFixture.TwiceTheSameOnALaterLine))!;
        CSharpPrinter.PrintRaised(fn, out var ranges);

        var refused = fn.Body.Descendants.OfType<LoadLocal>()
            .FirstOrDefault(n => !ranges.TryGetRange(n, out _));
        Assert.NotNull(refused);

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>> { [refused!] = [new Annotation(Alloc, 0, "kept")] });

        var fact = Assert.Single(map.Annotations);
        Assert.Equal("kept", fact.Detail);

        Assert.Null(fact.Extent);
        Assert.Null(fact.NodeId);
    }

    [Fact]
    public void ConditionalityReachesTheEnvelope_SoAReplayRendersTheSameLabel()
    {
        // AnnotationText appends "cached-once" / "per-iteration" to the rendered
        // label, so a payload that dropped conditionality would render a
        // *different* annotation than the in-process renderer -- silently
        // promoting a cached allocation to an unconditional one.
        var (_, ranges) = Print(nameof(AllocSampleClass.SumList));
        var statement = ranges[^1].Node;

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [statement] = [new Annotation(Alloc, 0, "cached", AnnotationConditionality.CachedOnce)],
            });

        var fact = Assert.Single(map.Annotations);
        Assert.Equal(AnnotationConditionality.CachedOnce, fact.Conditionality);

        string json = JsonSerializer.Serialize(map);
        var replayed = JsonSerializer.Deserialize<PrintedBodyMap>(json);
        Assert.Equal(
            AnnotationConditionality.CachedOnce,
            Assert.Single(replayed!.Annotations).Conditionality);
    }

    [Fact]
    public void MapCarriesNoReferenceIntoTheIr()
    {
        // If any member could hand back an IrNode the payload would silently
        // re-acquire the lifetime it exists to shed.
        var (_, ranges) = Print(nameof(AllocSampleClass.SumList));
        var map = PrintedBodyMap.Create(ranges);

        foreach (var property in typeof(PrintedNodeSpan).GetProperties())
            Assert.True(
                property.PropertyType == typeof(string)
                    || property.PropertyType == typeof(int)
                    || property.PropertyType == typeof(PrintedExtent));

        // Enums are permitted: they carry no reference and serialise by value.
        foreach (var property in typeof(PrintedAnnotationSpan).GetProperties())
            Assert.True(
                property.PropertyType == typeof(string)
                    || property.PropertyType == typeof(int)
                    || property.PropertyType == typeof(int?)
                    || property.PropertyType == typeof(PrintedExtent?)
                    || property.PropertyType.IsEnum,
                $"{property.Name} is {property.PropertyType}, which can carry a reference into the IR");

        Assert.NotEmpty(map.Nodes);
    }

    [Fact]
    public void PlacedFactsNameTheExactNodeTheyWereAnchoredTo()
    {
        // Two implementation nodes print one identical surface-syntax element.
        // They normalize to one portable node while identity is still alive, so
        // either implementation node resolves to the same unambiguous id.
        var first = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var second = new LoadLocal(1, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(first, 3, 7);
        ranges.Record(second, 3, 7);
        ranges.Complete("ab\nefgh\nmn");

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>> { [second] = [new Annotation(Alloc, 12)] });

        var node = Assert.Single(map.Nodes);
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(node.Id, fact.NodeId);
        Assert.Equal(node.Extent, fact.Extent);
    }

    [Fact]
    public void ConstructorRejectsBrokenNodeJoins()
    {
        var node = new PrintedNodeSpan(0, "LoadLocal", new PrintedExtent(0, 0, 0, 3));
        var placed = new PrintedAnnotationSpan(
            "alloc.new",
            "Allocation",
            AnnotationConditionality.Always,
            "LoadLocal",
            new PrintedExtent(0, 0, 0, 3),
            null,
            4,
            0);

        // Ids that are not contiguous from 0 in list order make "node 2" mean
        // two different rows depending on how a consumer looks it up.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            ["abc"],
            [node with { Id = 1 }],
            [],
            []));

        // A placed fact with no node id leaves the join to a coordinate re-match.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            ["abc"],
            [node],
            [],
            [placed with { NodeId = null }]));

        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            ["abc"],
            [node],
            [],
            [placed with { NodeId = 7 }]));

        // The id resolves, but not to the thing the fact claims it is.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            ["abc"],
            [node],
            [],
            [placed with { Kind = "NewObject" }]));

        // An unplaced fact naming a node asserts a placement it does not have.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            ["abc"],
            [node],
            [],
            [placed with { Extent = null }]));

        var map = new PrintedBodyMap(["abc"], [node], [], [placed]);
        Assert.Equal(0, Assert.Single(map.Annotations).NodeId);
    }

    // The portable document is a text buffer plus overlays: one string, and
    // absolute UTF-16 spans into it. These pin the buffer's invariants, since a
    // consumer holding only this payload slices text by those spans and has
    // nothing else to check them against.
    const string DocumentInstruction = "IL_0000: newobj instance void object::.ctor()";

    static readonly string DocumentText = $"return new object();\n{DocumentInstruction}";

    static AnnotatedSourceNode AllocationNode() =>
        new(0, "NewObject", SourceLineKind.CSharp, [new AnnotatedSourceSpan(7, 12)]);

    static AnnotatedSourceNode InstructionNode() => new(
        1,
        "Instruction",
        SourceLineKind.Il,
        [new AnnotatedSourceSpan(21, DocumentInstruction.Length)],
        IlOffset: 0);

    static AnnotatedSourceFact AllocationFact() => new(
        0,
        "alloc.new",
        "Allocation",
        AnnotationConditionality.Always,
        "object",
        0,
        AnnotatedSourceFactOrigin.Body);

    [Fact]
    public void AnnotatedSourceDocumentSnapshotsValidatesAndReplays()
    {
        var nodes = new List<AnnotatedSourceNode> { AllocationNode(), InstructionNode() };
        var regions = new List<AnnotatedSourceRegion>
        {
            new(PrintedRegionRole.Body, [new AnnotatedSourceSpan(0, 20)]),
        };
        var facts = new List<AnnotatedSourceFact> { AllocationFact() };
        var targets = new List<AnnotatedSourceTarget> { new(0, 0), new(0, 1) };
        var document = new AnnotatedSourceDocument(DocumentText, nodes, regions, facts, targets);

        nodes.Clear();
        regions.Clear();
        facts.Clear();
        targets.Clear();
        Assert.Equal(2, document.Nodes.Count);
        Assert.Single(document.Regions);

        // Fact -> target -> node -> span -> text is the only join, and it is the
        // same walk in both media.
        var fact = Assert.Single(document.Facts);
        Assert.Equal(2, document.Targets.Count);
        Assert.All(document.Targets, target => Assert.Equal(fact.Id, target.FactId));
        Assert.Equal(
            ["new object()", DocumentInstruction],
            document.Targets.Select(target => Selected(document, document.Nodes[target.NodeId])));

        string json = JsonSerializer.Serialize(document);
        var replayed = JsonSerializer.Deserialize<AnnotatedSourceDocument>(json);
        Assert.NotNull(replayed);
        Assert.Equal(document, replayed);
        Assert.Equal(document.GetHashCode(), replayed!.GetHashCode());
        Assert.Equal(document.Text, replayed.Text);
        Assert.Equal(document.Nodes, replayed.Nodes);
        Assert.Equal(document.Regions, replayed.Regions);
        Assert.Equal(document.Facts, replayed.Facts);
        Assert.Equal(document.Targets, replayed.Targets);

        // Structural equality reaches into the span lists, so a replayed node
        // that selects different characters is a different node.
        Assert.NotEqual(
            document.Nodes[0],
            new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, [new AnnotatedSourceSpan(7, 11)]));
    }

    [Fact]
    public void AnnotatedSourceDocumentAcceptsStructureWithNoFacts()
    {
        // Nodes are text structure, not evidence of an observation. A body with
        // no facts is the ordinary case, and future syntax, comment, and XML-doc
        // producers will only ever add nodes.
        var document = new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode(), InstructionNode()],
            [],
            [],
            []);

        Assert.Equal(2, document.Nodes.Count);
        Assert.Empty(document.Facts);
        Assert.Empty(document.Targets);
    }

    [Fact]
    public void AnnotatedSourceDocumentKeepsFactsThatTargetNothing()
    {
        // A fact with no target is the explicit unanchored case: the observation
        // is real, and nothing in the text was the right thing to point at.
        // Dropping it would lose the observation; inventing a span would turn
        // absence of evidence into a confident, wrong coordinate.
        var header = AllocationFact() with
        {
            Id = 1,
            Descriptor = "cost.method",
            Category = "Cost",
            Detail = null,
            SourceOffset = -1,
            Origin = AnnotatedSourceFactOrigin.MemberHeader,
        };
        var document = new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode()],
            [],
            [AllocationFact() with { SourceOffset = -1 }, header],
            []);

        Assert.Empty(document.Targets);
        Assert.Equal(
            [AnnotatedSourceFactOrigin.Body, AnnotatedSourceFactOrigin.MemberHeader],
            document.Facts.Select(fact => fact.Origin));
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsBrokenIdentity()
    {
        var node = AllocationNode();
        var fact = AllocationFact();

        // Contiguous ids in list order, on both planes.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [new AnnotatedSourceNode(3, "NewObject", SourceLineKind.CSharp, [new AnnotatedSourceSpan(7, 12)])],
            [],
            [],
            []));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [node],
            [],
            [fact with { Id = 5 }],
            [new AnnotatedSourceTarget(5, 0)]));

        // Facts are deduplicated, so restating one makes "how many times" unanswerable.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [node],
            [],
            [fact, fact with { Id = 1 }],
            []));

        // ... and so is restating a target.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [node],
            [],
            [fact],
            [new AnnotatedSourceTarget(0, 0), new AnnotatedSourceTarget(0, 0)]));

        // Dangling ids on either side of the join.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [node],
            [],
            [fact],
            [new AnnotatedSourceTarget(4, 0)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [node],
            [],
            [fact],
            [new AnnotatedSourceTarget(0, 9)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [node],
            [],
            [fact],
            [new AnnotatedSourceTarget(0, -1)]));
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsFalseTargetClaims()
    {
        var fact = AllocationFact();

        // Targeting an instruction claims the fact is about that instruction, so
        // the offsets have to agree.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode(), InstructionNode()],
            [],
            [fact with { SourceOffset = 7 }],
            [new AnnotatedSourceTarget(0, 1)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode(), InstructionNode()],
            [],
            [fact with { SourceOffset = -1 }],
            [new AnnotatedSourceTarget(0, 1)]));

        // A C# node carries no offset to agree with, so a body fact may target
        // it whatever its own offset is.
        var document = new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode(), InstructionNode()],
            [],
            [fact with { SourceOffset = -1 }],
            [new AnnotatedSourceTarget(0, 0)]);
        Assert.Equal(0, Assert.Single(document.Targets).NodeId);

        // A member-header fact is about the member, not a part of its body.
        var header = fact with
        {
            Descriptor = "cost.method",
            SourceOffset = -1,
            Origin = AnnotatedSourceFactOrigin.MemberHeader,
        };
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode()],
            [],
            [header],
            [new AnnotatedSourceTarget(0, 0)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode()],
            [],
            [header with { SourceOffset = 0 }],
            []));

        var headerOnly = new AnnotatedSourceDocument(DocumentText, [AllocationNode()], [], [header], []);
        Assert.Equal(AnnotatedSourceFactOrigin.MemberHeader, Assert.Single(headerOnly.Facts).Origin);
        Assert.Empty(headerOnly.Targets);
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsSpansThatAreNotCoordinates()
    {
        // A span that selects nothing, runs backwards, doubles back over its
        // predecessor, or leaves the buffer is not a coordinate: a consumer
        // slicing text by it would throw, or worse, select the wrong characters.
        Assert.Throws<ArgumentException>(
            () => new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, []));
        Assert.Throws<ArgumentException>(
            () => new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, [new AnnotatedSourceSpan(7, 0)]));
        Assert.Throws<ArgumentException>(
            () => new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, [new AnnotatedSourceSpan(7, -3)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, [new AnnotatedSourceSpan(-1, 4)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "NewObject",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(7, 12), new AnnotatedSourceSpan(0, 3)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "NewObject",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(0, 12), new AnnotatedSourceSpan(7, 3)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "NewObject",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(0, 7), new AnnotatedSourceSpan(7, 3)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceRegion(PrintedRegionRole.Body, []));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceRegion(
            PrintedRegionRole.Body,
            [new AnnotatedSourceSpan(0, 7), new AnnotatedSourceSpan(7, 3)]));

        // Bounds are the document's, because only the document holds the text.
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [new AnnotatedSourceNode(
                0,
                "NewObject",
                SourceLineKind.CSharp,
                [new AnnotatedSourceSpan(DocumentText.Length - 2, 8)])],
            [],
            [],
            []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceDocument(
            "",
            [],
            [new AnnotatedSourceRegion(PrintedRegionRole.Body, [new AnnotatedSourceSpan(0, 4)])],
            [],
            []));

        // A span whose end overflows int is the hostile case: computed as
        // Start + Length it wraps negative and reads as comfortably inside the
        // buffer, so the document would be accepted and the failure deferred to
        // whichever consumer sliced by it. Bounds are checked by subtraction, so
        // it is rejected here.
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [new AnnotatedSourceNode(
                0,
                "NewObject",
                SourceLineKind.CSharp,
                [new AnnotatedSourceSpan(int.MaxValue, 1)])],
            [],
            [],
            []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [],
            [new AnnotatedSourceRegion(
                PrintedRegionRole.Body,
                [new AnnotatedSourceSpan(0, int.MaxValue)])],
            [],
            []));

        // Ordering is decided against the same widened end, so a wrapped
        // predecessor cannot make an overlapping successor look ordered.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "NewObject",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(int.MaxValue - 1, 2), new AnnotatedSourceSpan(0, 3)]));
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsTextThatIsNotWellFormedUtf16()
    {
        // A lone surrogate has no UTF-8 form, so System.Text.Json writes U+FFFD
        // for it: the document that replays is a different string, and every
        // absolute span past the substitution names characters it was not minted
        // for. Producers contain the hazard as a visible ASCII \uXXXX before a
        // document exists, so a raw unpaired code unit here is a producer bug --
        // rejected, never repaired, because repairing it would silently move the
        // coordinates the caller already computed.
        static AnnotatedSourceDocument Make(string text) => new(text, [], [], [], []);

        var lone = Assert.Throws<ArgumentException>(() => Make("return \ud800;"));
        Assert.Equal("Text", lone.ParamName);
        Assert.Contains("index 7", lone.Message, StringComparison.Ordinal);
        Assert.Contains("U+D800", lone.Message, StringComparison.Ordinal);

        var low = Assert.Throws<ArgumentException>(() => Make("return \udc00;"));
        Assert.Equal("Text", low.ParamName);
        Assert.Contains("index 7", low.Message, StringComparison.Ordinal);
        Assert.Contains("U+DC00", low.Message, StringComparison.Ordinal);

        // A high surrogate in the last slot has nothing after it to pair with,
        // which is the case a lookahead written without a bounds check misses.
        var terminal = Assert.Throws<ArgumentException>(() => Make("return;\ud83d"));
        Assert.Equal("Text", terminal.ParamName);
        Assert.Contains("index 7", terminal.Message, StringComparison.Ordinal);

        // A pair in the wrong order is two lone halves, not a scalar.
        Assert.Throws<ArgumentException>(() => Make("\udc00\ud800"));

        // The rejection is the buffer's, not the span's: the text is refused
        // before any overlay is even consulted.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            $"{DocumentText}\ud800",
            [AllocationNode(), InstructionNode()],
            [],
            [],
            []));
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsOverlayTextThatIsNotWellFormedUtf16()
    {
        static AnnotatedSourceDocument Make(
            AnnotatedSourceNode node,
            AnnotatedSourceFact fact) => new(
                DocumentText,
                [node],
                [],
                [fact],
                []);

        var kind = Assert.Throws<ArgumentException>(
            () => Make(
                new AnnotatedSourceNode(
                    0,
                    "New\ud800Object",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(7, 12)]),
                AllocationFact()));
        Assert.Equal("Nodes", kind.ParamName);
        Assert.Contains("Node 0 kind", kind.Message, StringComparison.Ordinal);

        var descriptor = Assert.Throws<ArgumentException>(
            () => Make(AllocationNode(), AllocationFact() with { Descriptor = "alloc.\ud800" }));
        Assert.Equal("Facts", descriptor.ParamName);
        Assert.Contains("Fact 0 descriptor", descriptor.Message, StringComparison.Ordinal);

        var category = Assert.Throws<ArgumentException>(
            () => Make(AllocationNode(), AllocationFact() with { Category = "Alloc\udc00ation" }));
        Assert.Equal("Facts", category.ParamName);
        Assert.Contains("Fact 0 category", category.Message, StringComparison.Ordinal);

        var detail = Assert.Throws<ArgumentException>(
            () => Make(AllocationNode(), AllocationFact() with { Detail = "obj\ud800ect" }));
        Assert.Equal("Facts", detail.ParamName);
        Assert.Contains("Fact 0 detail", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnnotatedSourceDocumentKeepsSupplementaryCharactersExact()
    {
        // Well-formed is the rule, not "ASCII only": a paired surrogate is one
        // scalar the encode round-trips, so it stays raw. It still costs two
        // code units, and the span currency counts code units, so the
        // coordinates on either side of it must account for both.
        const string Emoji = "\U0001F600";
        string text = $"return \"{Emoji}\";\n{DocumentInstruction}";
        int literalStart = text.IndexOf('"');
        int instructionStart = text.IndexOf('\n') + 1;
        Assert.Equal(2, Emoji.Length);

        var document = new AnnotatedSourceDocument(
            text,
            [
                new AnnotatedSourceNode(0, "String", SourceLineKind.CSharp, [new AnnotatedSourceSpan(literalStart, Emoji.Length + 2)]),
                new AnnotatedSourceNode(
                    1,
                    "Instruction",
                    SourceLineKind.Il,
                    [new AnnotatedSourceSpan(instructionStart, DocumentInstruction.Length)],
                    IlOffset: 0),
            ],
            [new AnnotatedSourceRegion(PrintedRegionRole.Body, [new AnnotatedSourceSpan(0, text.Length)])],
            [AllocationFact()],
            [new AnnotatedSourceTarget(0, 0), new AnnotatedSourceTarget(0, 1)]);

        Assert.Equal($"\"{Emoji}\"", Selected(document, document.Nodes[0]));
        Assert.Equal(DocumentInstruction, Selected(document, document.Nodes[1]));

        // The instruction span sits after the pair, so it is only right if both
        // of its code units were counted: the literal's four, then `;` and the
        // line break.
        Assert.Equal(instructionStart, literalStart + Emoji.Length + 2 + 2);

        string json = JsonSerializer.Serialize(document);
        var replayed = JsonSerializer.Deserialize<AnnotatedSourceDocument>(json);
        Assert.NotNull(replayed);
        Assert.Equal(document, replayed);
        Assert.Equal(text, replayed!.Text);
        Assert.DoesNotContain('\uFFFD', replayed.Text);
        Assert.Equal(
            [$"\"{Emoji}\"", DocumentInstruction],
            replayed.Nodes.Select(node => Selected(replayed, node)));
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsMisplacedIlOffsets()
    {
        // The offset is what makes an instruction node addressable by a fact, so
        // it belongs to IL text only and orders the instruction stream.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "NewObject",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(7, 12)],
            IlOffset: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceNode(
            0,
            "Instruction",
            SourceLineKind.Il,
            [new AnnotatedSourceSpan(21, 4)],
            IlOffset: -1));

        // "Instruction" is a claim, not a label: it holds exactly when the node
        // is IL text carrying the offset it disassembles. An offset-bearing
        // Block would let a fact anchor to something that is not one
        // instruction; an offsetless Instruction claims to be one and gives a
        // consumer nothing to resolve; and a C# Instruction claims C# text
        // disassembles.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "Block",
            SourceLineKind.Il,
            [new AnnotatedSourceSpan(21, 4)],
            IlOffset: 0));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "Instruction",
            SourceLineKind.Il,
            [new AnnotatedSourceSpan(21, 4)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "Instruction",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(7, 12)],
            IlOffset: 0));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "Instruction",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(7, 12)]));

        // The kind is matched ordinally, so a case variant is a different kind
        // and follows the ordinary offsetless rule.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "instruction",
            SourceLineKind.Il,
            [new AnnotatedSourceSpan(21, 4)],
            IlOffset: 0));

        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [
                new AnnotatedSourceNode(0, "Instruction", SourceLineKind.Il, [new AnnotatedSourceSpan(21, 8)], 4),
                new AnnotatedSourceNode(1, "Instruction", SourceLineKind.Il, [new AnnotatedSourceSpan(30, 8)], 4),
            ],
            [],
            [],
            []));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [
                new AnnotatedSourceNode(0, "Instruction", SourceLineKind.Il, [new AnnotatedSourceSpan(21, 8)], 4),
                new AnnotatedSourceNode(1, "Instruction", SourceLineKind.Il, [new AnnotatedSourceSpan(30, 8)], 2),
            ],
            [],
            [],
            []));

        // A future structural IL node carries no offset, and must not have to
        // invent one to sit between two instructions.
        var document = new AnnotatedSourceDocument(
            DocumentText,
            [
                new AnnotatedSourceNode(0, "Instruction", SourceLineKind.Il, [new AnnotatedSourceSpan(21, 8)], 0),
                new AnnotatedSourceNode(1, "Block", SourceLineKind.Il, [new AnnotatedSourceSpan(30, 8)]),
                new AnnotatedSourceNode(2, "Instruction", SourceLineKind.Il, [new AnnotatedSourceSpan(39, 5)], 5),
            ],
            [],
            [],
            []);
        Assert.Equal([0, null, 5], document.Nodes.Select(node => node.IlOffset));
        Assert.Equal("Instruction", AnnotatedSourceNode.InstructionKind);
    }

    [Fact]
    public void AnnotatedSourceDocumentSplitsStructureAroundInterleavedIl()
    {
        // The reason spans are a list. This C# construct is printed across two
        // lines with an IL line woven between them, so its exact characters are
        // two runs of the buffer. One span from the first character to the last
        // would swallow the instruction, which is text the construct does not
        // contain.
        const string text = "int x = 1;\nIL_0000: ldc.i4.1\nreturn x;";
        var document = new AnnotatedSourceDocument(
            text,
            [
                new AnnotatedSourceNode(
                    0,
                    "Block",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 10), new AnnotatedSourceSpan(29, 9)]),
                new AnnotatedSourceNode(
                    1,
                    "Instruction",
                    SourceLineKind.Il,
                    [new AnnotatedSourceSpan(11, 17)],
                    IlOffset: 0),
            ],
            [
                new AnnotatedSourceRegion(
                    PrintedRegionRole.Body,
                    [new AnnotatedSourceSpan(0, 10), new AnnotatedSourceSpan(29, 9)]),
            ],
            [],
            []);

        var block = document.Nodes[0];
        Assert.Equal(2, block.Spans.Count);
        Assert.Equal("int x = 1;return x;", Selected(document, block));
        Assert.DoesNotContain("IL_0000", Selected(document, block), StringComparison.Ordinal);
        Assert.Equal("IL_0000: ldc.i4.1", Selected(document, document.Nodes[1]));
        Assert.Equal(block.Spans, Assert.Single(document.Regions).Spans);
    }

    static string Selected(AnnotatedSourceDocument document, AnnotatedSourceNode node) => string.Concat(
        node.Spans.Select(span => document.Text.Substring(span.Start, span.Length)));

    [Fact]
    public void SurvivesSerialisationAndReplays()
    {
        var (_, ranges) = Print(nameof(AllocSampleClass.SumList));
        var map = PrintedBodyMap.Create(ranges);

        string json = JsonSerializer.Serialize(map);
        var replayed = JsonSerializer.Deserialize<PrintedBodyMap>(json);

        Assert.NotNull(replayed);
        Assert.NotEmpty(map.Nodes);
        Assert.NotEmpty(replayed!.Nodes);
        Assert.Equal(map.Lines, replayed!.Lines);
        Assert.Equal(map.Nodes, replayed.Nodes);
        Assert.Equal(map.Regions, replayed.Regions);
        Assert.Equal(map.Annotations, replayed.Annotations);

        // Replay proper: the round-tripped payload alone still selects the same
        // characters, with nothing from the decompiler in scope.
        foreach (var span in replayed.Nodes)
            Assert.NotEmpty(Text(replayed, span.Extent));
    }

    [Fact]
    public void TwoIndependentPrintsProduceIdenticalPayloads()
    {
        // Dictionary enumeration order is not a contract and List.Sort is not
        // stable, so a partial comparator would make the payload differ between
        // runs -- which would later read as a real change. Node ids are cut from
        // that same order, so they inherit the requirement.
        var (_, first) = Print(nameof(AllocSampleClass.SumList));
        var (_, second) = Print(nameof(AllocSampleClass.SumList));

        Assert.Equal(
            JsonSerializer.Serialize(PrintedBodyMap.Create(first)),
            JsonSerializer.Serialize(PrintedBodyMap.Create(second)));
    }

    static readonly AnnotationDescriptor Alloc =
        new("alloc.new", AnnotationCategory.Allocation, "Allocation");

    static readonly AnnotationDescriptor Box =
        new("alloc.box", AnnotationCategory.Allocation, "Boxing");

    static (PrintedRangeMap Ranges, LoadLocal First, LoadLocal Second) TwoNodesOnOneLine()
    {
        var first = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var second = new LoadLocal(1, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(first, 3, 7);
        ranges.Record(second, 9, 13);
        ranges.Complete("ab\nefgh__ijkl\nmn");
        return (ranges, first, second);
    }

    [Fact]
    public void FactsArePositionedAtTheNodeTheyWereFoundOn()
    {
        var (ranges, first, second) = TwoNodesOnOneLine();
        var annotations = new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
        {
            [first] = [new Annotation(Alloc, 12, "List<int>")],
            [second] = [new Annotation(Box, 34, "int")],
        };

        var map = PrintedBodyMap.Create(ranges, annotations);

        Assert.Equal(2, map.Annotations.Count);
        var a = map.Annotations[0];
        var b = map.Annotations[1];

        Assert.Equal("alloc.new", a.Descriptor);
        Assert.Equal("Allocation", a.Category);
        Assert.Equal("List<int>", a.Detail);
        Assert.Equal(12, a.SourceOffset);
        Assert.Equal("efgh", Text(map, a.Extent!.Value));
        Assert.Equal(0, a.NodeId);
        Assert.Equal(map.Nodes[0].Extent, a.Extent);

        Assert.Equal("alloc.box", b.Descriptor);
        Assert.Equal("ijkl", Text(map, b.Extent!.Value));
        Assert.Equal(1, b.NodeId);
        Assert.Equal(map.Nodes[1].Extent, b.Extent);
    }

    [Fact]
    public void OrderingDistinguishesEveryFieldThatCanDiffer()
    {
        // Any pair the comparison calls equal may come out in either order, so a
        // comparison that stops short of a total order makes the serialised
        // payload differ between runs over identical input. Each pair below
        // differs in exactly one field.
        var baseline = new PrintedAnnotationSpan(
            "alloc.new",
            "Allocation",
            AnnotationConditionality.Always,
            "NewObject",
            new PrintedExtent(3, 7, 3, 19),
            "List<int>",
            40,
            2);

        Assert.NotEqual(0, PrintedBodyMap.Compare(
            baseline,
            baseline with { Extent = baseline.Extent!.Value with { StartLine = 4 } }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(
            baseline,
            baseline with { Extent = baseline.Extent!.Value with { StartColumn = 8 } }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(
            baseline,
            baseline with { Extent = baseline.Extent!.Value with { EndLine = 4 } }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(
            baseline,
            baseline with { Extent = baseline.Extent!.Value with { EndColumn = 20 } }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Extent = null }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Descriptor = "alloc.box" }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Category = "Unsafety" }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { SourceOffset = 41 }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Kind = "Box" }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Conditionality = AnnotationConditionality.PerIteration }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Detail = "int" }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { NodeId = 3 }));

        Assert.Equal(0, PrintedBodyMap.Compare(baseline, baseline));
    }

    [Fact]
    public void FactOrderingDoesNotDependOnDictionaryOrder()
    {
        // List.Sort is unstable and dictionary enumeration order is not a
        // contract, so a comparator that stops short of a total order would let
        // the payload differ between two runs over identical input.
        var (ranges, first, second) = TwoNodesOnOneLine();

        Dictionary<IrNode, IReadOnlyList<IAnnotation>> forward = new()
        {
            [first] = [new Annotation(Alloc, 12, "a"), new Annotation(Box, 12, "b")],
            [second] = [new Annotation(Alloc, 34, "c")],
        };
        Dictionary<IrNode, IReadOnlyList<IAnnotation>> reversed = new()
        {
            [second] = [new Annotation(Alloc, 34, "c")],
            [first] = [new Annotation(Box, 12, "b"), new Annotation(Alloc, 12, "a")],
        };

        Assert.Equal(
            PrintedBodyMap.Create(ranges, forward).Annotations,
            PrintedBodyMap.Create(ranges, reversed).Annotations);
    }

    [Fact]
    public void AFactOnAStraddlingNodeKeepsItsExactMultiLineExtent()
    {
        // The old line/column/length shape could only report this as unknown.
        // End coordinates preserve the exact characters instead.
        var node = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(node, 2, 12);
        ranges.Complete("ab\ncdefgh\nij");

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>> { [node] = [new Annotation(Alloc, 4)] });

        var span = Assert.Single(map.Nodes);
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(span.Extent, fact.Extent);
        Assert.Equal(span.Id, fact.NodeId);
        Assert.Equal("\ncdefgh\nij", Text(map, span.Extent));
    }

    [Fact]
    public void ARangeEndingWithItsLineBreakIsPlacedRatherThanRefused()
    {
        // A statement's range runs to the end of the line it printed, newline
        // included. Treating that as crossing a line break would refuse every
        // statement in the body, and every statement-anchored fact with it.
        var node = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(node, 3, 10);
        ranges.Complete("ab\ncdefgh\nij");

        Assert.True(ranges.TryGetLineColumn(node, out int line, out int column, out int length));
        Assert.Equal(1, line);
        Assert.Equal(0, column);
        Assert.Equal(6, length);

        var map = PrintedBodyMap.Create(ranges);
        var span = Assert.Single(map.Nodes);
        Assert.Equal("cdefgh", Text(map, span.Extent));
    }

    [Fact]
    public void ARangeOfNothingButALineBreakIsRefused()
    {
        var node = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(node, 2, 3);
        ranges.Complete("ab\ncdefgh\nij");

        Assert.False(ranges.TryGetLineColumn(node, out _, out _, out _));
    }

    [Fact]
    public void ARangeThatCrossesALineBreakKeepsItsExactExtent()
    {
        // Reporting only its first line would understate the extent. The
        // portable map now carries both endpoints instead.
        var node = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(node, 2, 12);
        ranges.Complete("ab\ncdefgh\nij");

        Assert.False(ranges.TryGetLineColumn(node, out _, out _, out _));
        Assert.True(ranges.TryGetExtent(node, out var extent));
        Assert.Equal(new PrintedExtent(0, 2, 2, 2), extent);
        var map = PrintedBodyMap.Create(ranges);
        Assert.Equal("\ncdefgh\nij", Text(map, Assert.Single(map.Nodes).Extent));
    }

    [Fact]
    public void ASingleLineRangeIsPlacedAtItsOwnColumn()
    {
        var node = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(node, 5, 9);
        ranges.Complete("ab\ncdefgh\nij");

        Assert.True(ranges.TryGetLineColumn(node, out int line, out int column, out int length));
        Assert.Equal(1, line);
        Assert.Equal(2, column);
        Assert.Equal(4, length);

        var map = PrintedBodyMap.Create(ranges);
        var span = Assert.Single(map.Nodes);
        Assert.Equal("NameExpression", span.Kind);
        Assert.Equal("efgh", Text(map, span.Extent));
    }

    static int ComparePosition(int line, int column, int otherLine, int otherColumn)
    {
        int c = line.CompareTo(otherLine);
        return c != 0 ? c : column.CompareTo(otherColumn);
    }

    static string Text(PrintedBodyMap map, PrintedExtent extent) => Text(map.Lines, extent);

    static string Text(IReadOnlyList<string> lines, PrintedExtent extent)
    {
        if (extent.StartLine == extent.EndLine)
        {
            return lines[extent.StartLine][extent.StartColumn..extent.EndColumn];
        }

        var selected = new List<string>
        {
            lines[extent.StartLine][extent.StartColumn..],
        };
        for (int line = extent.StartLine + 1; line < extent.EndLine; line++)
            selected.Add(lines[line]);
        selected.Add(lines[extent.EndLine][..extent.EndColumn]);
        return string.Join('\n', selected);
    }
}
