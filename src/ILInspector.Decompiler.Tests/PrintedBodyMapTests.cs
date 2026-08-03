using System.Text.Json;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// The rich map the printer builds is keyed by IrNode, so it cannot leave the
// process that built it. These pin the projection that can: an extent and a
// name -- no references, and therefore serialisable.
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
        int emitted = 0;
        foreach (var printed in ranges)
        {
            int start = printed.Characters.Start.GetOffset(output.Length);
            int end = printed.Characters.End.GetOffset(output.Length);
            if (!ranges.TryGetExtent(printed.Node, out _))
                continue;

            var span = map.Nodes[emitted++];
            Assert.Equal(output[start..end].TrimEnd('\r', '\n'), Text(map, span.Extent));
            Assert.Equal(printed.Node.GetType().Name, span.Kind);
        }

        Assert.Equal(emitted, map.Nodes.Count);
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
                    || property.PropertyType == typeof(PrintedExtent));

        // Enums are permitted: they carry no reference and serialise by value.
        foreach (var property in typeof(PrintedAnnotationSpan).GetProperties())
            Assert.True(
                property.PropertyType == typeof(string)
                    || property.PropertyType == typeof(int)
                    || property.PropertyType == typeof(PrintedExtent?)
                    || property.PropertyType.IsEnum,
                $"{property.Name} is {property.PropertyType}, which can carry a reference into the IR");

        Assert.NotEmpty(map.Nodes);
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
        // runs -- which would later read as a real change.
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

        Assert.Equal("alloc.box", b.Descriptor);
        Assert.Equal("ijkl", Text(map, b.Extent!.Value));
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
            40);

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

    static string Text(PrintedBodyMap map, PrintedExtent extent)
    {
        if (extent.StartLine == extent.EndLine)
        {
            return map.Lines[extent.StartLine][extent.StartColumn..extent.EndColumn];
        }

        var selected = new List<string>
        {
            map.Lines[extent.StartLine][extent.StartColumn..],
        };
        for (int line = extent.StartLine + 1; line < extent.EndLine; line++)
            selected.Add(map.Lines[line]);
        selected.Add(map.Lines[extent.EndLine][..extent.EndColumn]);
        return string.Join('\n', selected);
    }
}
