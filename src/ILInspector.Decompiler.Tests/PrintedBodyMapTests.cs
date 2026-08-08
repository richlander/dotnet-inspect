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
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(AllocSampleClass).FullName!, methodName);
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

        // Read the emitted spans, not recomputed coordinates. Checking only the
        // count let a map of entirely bogus spans pass.
        var expected = new List<(string Kind, string Text)>();
        foreach (var printed in ranges)
        {
            if (!ranges.TryGetExtent(printed.Node, out _))
                continue;
            int start = printed.Characters.Start.GetOffset(output.Length);
            int end = printed.Characters.End.GetOffset(output.Length);
            expected.Add((printed.Node.GetType().Name, output[start..end].TrimEnd('\r', '\n')));
        }

        Assert.Equal(expected.Count, map.Nodes.Count);
        Assert.Equal(
            expected.Order(),
            map.Nodes.Select(node => (node.Kind, Text(map, node.Extent))).Order());
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
        // Two nodes print the same characters under the same kind, so recovering
        // the join by matching kind and extent could only guess. The id is
        // minted while IrNode identity is alive, which is what makes it exact.
        var first = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var second = new LoadLocal(1, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(first, 3, 7);
        ranges.Record(second, 3, 7);
        ranges.Complete("ab\nefgh\nmn");

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>> { [second] = [new Annotation(Alloc, 12)] });

        Assert.Equal(2, map.Nodes.Count);
        Assert.Equal(map.Nodes[0].Extent, map.Nodes[1].Extent);
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(1, fact.NodeId);
        Assert.Equal(map.Nodes[1].Extent, fact.Extent);
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

    [Fact]
    public void PortableAnnotatedLineIsScalarAndReplays()
    {
        var line = new AnnotatedSourceLine(4, "IL_000C: box int32", 12, SourceLineKind.Il);

        Assert.Equal(4, line.Id);
        Assert.DoesNotContain("alloc.box", line.Text);

        string json = JsonSerializer.Serialize(line);
        var replayed = JsonSerializer.Deserialize<AnnotatedSourceLine>(json);
        Assert.Equal(line, replayed);
        Assert.Equal(line.GetHashCode(), replayed!.GetHashCode());

        // A line is text structure only. Facts live in the document's Facts list
        // and reach a line through a placement, so the same observation seen in
        // two media is one fact rather than two copies on two lines.
        Type[] portablePropertyTypes =
        [
            typeof(int),
            typeof(string),
            typeof(SourceLineKind),
        ];
        foreach (var property in typeof(AnnotatedSourceLine).GetProperties())
            Assert.Contains(property.PropertyType, portablePropertyTypes);
    }

    [Fact]
    public void PortableAnnotatedLineRejectsInvalidConstruction()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AnnotatedSourceLine(0, null!, 0, SourceLineKind.CSharp));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AnnotatedSourceLine(-1, "", 0, SourceLineKind.CSharp));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AnnotatedSourceLine(0, "", -2, SourceLineKind.CSharp));
        Assert.Throws<ArgumentException>(
            () => new AnnotatedSourceLine(0, "", 0, (SourceLineKind)42));
    }

    [Fact]
    public void AnnotatedSourceDocumentSnapshotsValidatesAndReplays()
    {
        var lines = new List<AnnotatedSourceLine>
        {
            new(0, "return new object();", 0, SourceLineKind.CSharp),
            new(1, "IL_0000: newobj ...", 0, SourceLineKind.Il),
        };
        var facts = new List<AnnotatedSourceFact>
        {
            new(
                0,
                "alloc.new",
                "Allocation",
                AnnotationConditionality.Always,
                "object",
                0,
                AnnotatedSourceFactOrigin.Body),
        };
        var placements = new List<AnnotatedSourcePlacement>
        {
            new(0, AnnotatedSourcePlacementTarget.Node, 0),
            new(0, AnnotatedSourcePlacementTarget.Line, 1),
        };
        var document = new AnnotatedSourceDocument(
            lines,
            [new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, new PrintedExtent(0, 7, 0, 19))],
            [],
            facts,
            placements);

        lines.Clear();
        facts.Clear();
        placements.Clear();
        Assert.Equal(2, document.Lines.Count);

        // One observation, two places: the whole point of the normalization.
        var fact = Assert.Single(document.Facts);
        Assert.Equal(2, document.Placements.Count);
        Assert.All(document.Placements, placement => Assert.Equal(fact.Id, placement.FactId));

        string json = JsonSerializer.Serialize(document);
        var replayed = JsonSerializer.Deserialize<AnnotatedSourceDocument>(json);
        Assert.NotNull(replayed);
        Assert.Equal(document, replayed);
        Assert.Equal(document.GetHashCode(), replayed!.GetHashCode());
        Assert.Equal(document.Lines, replayed.Lines);
        Assert.Equal(document.Nodes, replayed.Nodes);
        Assert.Equal(document.Regions, replayed.Regions);
        Assert.Equal(document.Facts, replayed.Facts);
        Assert.Equal(document.Placements, replayed.Placements);
    }

    [Fact]
    public void AnnotatedSourceDocumentAcceptsNodesWithNoFacts()
    {
        // Nodes are text structure, not evidence of an observation. A body with
        // no facts is the ordinary case, and future syntax, comment, and XML-doc
        // producers will only ever add nodes.
        var document = new AnnotatedSourceDocument(
            [new AnnotatedSourceLine(0, "return new object();", 0, SourceLineKind.CSharp)],
            [new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, new PrintedExtent(0, 7, 0, 19))],
            [],
            [],
            []);

        Assert.Single(document.Nodes);
        Assert.Empty(document.Facts);
        Assert.Empty(document.Placements);
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsBrokenIdentity()
    {
        AnnotatedSourceLine[] Lines() =>
        [
            new(0, "return new object();", 0, SourceLineKind.CSharp),
            new(1, "IL_0000: newobj ...", 0, SourceLineKind.Il),
        ];
        var node = new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, new PrintedExtent(0, 7, 0, 19));
        var fact = new AnnotatedSourceFact(
            0,
            "alloc.new",
            "Allocation",
            AnnotationConditionality.Always,
            "object",
            0,
            AnnotatedSourceFactOrigin.Body);
        AnnotatedSourcePlacement Node() => new(0, AnnotatedSourcePlacementTarget.Node, 0);

        // Contiguous ids in list order, on every plane.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            [new AnnotatedSourceLine(1, "x", 0, SourceLineKind.CSharp)],
            [],
            [],
            [],
            []));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node with { Id = 3 }],
            [],
            [],
            []));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact with { Id = 5 }],
            [new AnnotatedSourcePlacement(5, AnnotatedSourcePlacementTarget.Node, 0)]));

        // Facts are deduplicated, so restating one makes "how many times" unanswerable.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact, fact with { Id = 1 }],
            [Node(), new AnnotatedSourcePlacement(1, AnnotatedSourcePlacementTarget.Node, 0)]));

        // ... and so is restating a placement.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact],
            [Node(), Node()]));

        // Dangling ids on either side of the join.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact],
            [new AnnotatedSourcePlacement(4, AnnotatedSourcePlacementTarget.Node, 0)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact],
            [new AnnotatedSourcePlacement(0, AnnotatedSourcePlacementTarget.Node, 9)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact],
            [new AnnotatedSourcePlacement(0, AnnotatedSourcePlacementTarget.Line, 9)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact],
            [new AnnotatedSourcePlacement(0, AnnotatedSourcePlacementTarget.Node, null)]));

        // A fact with no placement at all is a silently dropped observation.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact],
            []));
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsFalsePlacementClaims()
    {
        AnnotatedSourceLine[] Lines() =>
        [
            new(0, "return new object();", 0, SourceLineKind.CSharp),
            new(1, "IL_0000: newobj ...", 0, SourceLineKind.Il),
        ];
        var node = new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, new PrintedExtent(0, 7, 0, 19));
        var fact = new AnnotatedSourceFact(
            0,
            "alloc.new",
            "Allocation",
            AnnotationConditionality.Always,
            "object",
            0,
            AnnotatedSourceFactOrigin.Body);

        // A line placement names an IL line at the fact's own offset. Anything
        // else claims an offset correspondence the payload cannot support.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact],
            [new AnnotatedSourcePlacement(0, AnnotatedSourcePlacementTarget.Line, 0)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact with { SourceOffset = 7 }],
            [new AnnotatedSourcePlacement(0, AnnotatedSourcePlacementTarget.Line, 1)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact with { SourceOffset = -1 }],
            [new AnnotatedSourcePlacement(0, AnnotatedSourcePlacementTarget.Line, 1)]));

        // Unplaced means nowhere, so it neither names a target nor coexists with one.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact],
            [new AnnotatedSourcePlacement(0, AnnotatedSourcePlacementTarget.Unplaced, 0)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [fact],
            [
                new AnnotatedSourcePlacement(0, AnnotatedSourcePlacementTarget.Node, 0),
                new AnnotatedSourcePlacement(0, AnnotatedSourcePlacementTarget.Unplaced, null),
            ]));

        // A member-header fact is about the member, not a part of its body.
        var header = fact with
        {
            Descriptor = "cost.method",
            SourceOffset = -1,
            Origin = AnnotatedSourceFactOrigin.MemberHeader,
        };
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [header],
            [new AnnotatedSourcePlacement(0, AnnotatedSourcePlacementTarget.Node, 0)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [header with { SourceOffset = 0 }],
            [new AnnotatedSourcePlacement(0, AnnotatedSourcePlacementTarget.Unplaced, null)]));

        var document = new AnnotatedSourceDocument(
            Lines(),
            [node],
            [],
            [header],
            [new AnnotatedSourcePlacement(0, AnnotatedSourcePlacementTarget.Unplaced, null)]);
        Assert.Equal(AnnotatedSourceFactOrigin.MemberHeader, Assert.Single(document.Facts).Origin);
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsStructureOffItsMedium()
    {
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            [
                new AnnotatedSourceLine(0, "IL_0002", 2, SourceLineKind.Il),
                new AnnotatedSourceLine(1, "IL_0001", 1, SourceLineKind.Il),
            ],
            [],
            [],
            [],
            []));

        // A C# extent is resolved against the C# lines only, so a document with
        // none of them has nowhere for a C# node or region to land -- however
        // many IL lines it carries.
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceDocument(
            [new AnnotatedSourceLine(0, "IL_0002", 2, SourceLineKind.Il)],
            [new AnnotatedSourceNode(0, "Bad", SourceLineKind.CSharp, new PrintedExtent(0, 0, 0, 7))],
            [],
            [],
            []));

        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceDocument(
            [new AnnotatedSourceLine(0, "IL_0002", 2, SourceLineKind.Il)],
            [],
            [new PrintedRegion(PrintedRegionRole.Construct, new PrintedExtent(0, 0, 0, 7))],
            [],
            []));

        // Medium-local means medium-local both ways: an extent numbered in
        // interleaved coordinates runs off the end of the C# text it is
        // measured against.
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceDocument(
            [
                new AnnotatedSourceLine(0, "int x = 1;", -1, SourceLineKind.CSharp),
                new AnnotatedSourceLine(1, "IL_0000: ldc.i4.1", 0, SourceLineKind.Il),
                new AnnotatedSourceLine(2, "return x;", -1, SourceLineKind.CSharp),
            ],
            [new AnnotatedSourceNode(0, "Block", SourceLineKind.CSharp, new PrintedExtent(0, 0, 2, 9))],
            [],
            [],
            []));

        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceDocument(
            [new AnnotatedSourceLine(0, "x", 0, SourceLineKind.CSharp)],
            [new AnnotatedSourceNode(0, "Bad", SourceLineKind.CSharp, new PrintedExtent(0, -1, 0, 1))],
            [],
            [],
            []));

        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            [new AnnotatedSourceLine(0, "x", -1, SourceLineKind.Il)],
            [],
            [],
            [],
            []));
    }

    [Fact]
    public void AnnotatedSourceDocumentKeepsMultiLineCSharpStructureOffTheInterleave()
    {
        // The regression: a C# node spanning two C# lines that have IL printed
        // between them. Its extent is C#-local, so the characters it selects are
        // the two C# lines and nothing else. Rebased into stream coordinates the
        // same node would run 0..2 and swallow the IL line, which is what the
        // exact-characters contract forbids.
        var document = new AnnotatedSourceDocument(
            [
                new AnnotatedSourceLine(0, "int x = 1;", -1, SourceLineKind.CSharp),
                new AnnotatedSourceLine(1, "IL_0000: ldc.i4.1", 0, SourceLineKind.Il),
                new AnnotatedSourceLine(2, "return x;", -1, SourceLineKind.CSharp),
            ],
            [new AnnotatedSourceNode(0, "Block", SourceLineKind.CSharp, new PrintedExtent(0, 0, 1, 9))],
            [new PrintedRegion(PrintedRegionRole.Body, new PrintedExtent(0, 0, 1, 9))],
            [],
            []);

        string[] csharp =
        [
            .. document.Lines
                .Where(line => line.Kind == SourceLineKind.CSharp)
                .Select(line => line.Text),
        ];
        var extent = Assert.Single(document.Nodes).Extent;
        Assert.Equal("int x = 1;\nreturn x;", Text(csharp, extent));
        Assert.Equal(extent, Assert.Single(document.Regions).Extent);
        Assert.DoesNotContain("IL_0000", Text(csharp, extent), StringComparison.Ordinal);
    }

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
        Assert.Equal("LoadLocal", span.Kind);
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
