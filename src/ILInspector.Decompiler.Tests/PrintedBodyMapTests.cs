using System.Text.Json;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// The rich map the printer builds is keyed by IrNode, so it cannot leave the
// process that built it. These pin the projection that can: line, column,
// length, and a name -- no references, and therefore serialisable.
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
        // can slice it. If line/column/length did not select the same characters
        // the node-keyed range does, the payload would be confidently wrong.
        var (output, ranges) = Print(methodName);
        var map = PrintedBodyMap.Create(ranges);
        Assert.NotEmpty(map.Nodes);

        int expected = 0;
        foreach (var printed in ranges)
        {
            int start = printed.Characters.Start.GetOffset(output.Length);
            int end = printed.Characters.End.GetOffset(output.Length);
            if (!ranges.TryGetLineColumn(printed.Node, out int line, out int column, out int length))
                continue;

            expected++;
            Assert.Equal(output[start..end], map.Lines[line].Substring(column, length));
        }

        Assert.Equal(expected, map.Nodes.Count);
    }

    [Fact]
    public void MapCarriesNoReferenceIntoTheIr()
    {
        // If any member could hand back an IrNode the payload would silently
        // re-acquire the lifetime it exists to shed.
        var (_, ranges) = Print(nameof(AllocSampleClass.SumList));
        var map = PrintedBodyMap.Create(ranges);

        foreach (var property in typeof(PrintedNodeSpan).GetProperties())
            Assert.True(property.PropertyType == typeof(string) || property.PropertyType == typeof(int));

        foreach (var property in typeof(PrintedAnnotationSpan).GetProperties())
            Assert.True(property.PropertyType == typeof(string) || property.PropertyType == typeof(int));

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
        Assert.Equal(map.Annotations, replayed.Annotations);

        // Replay proper: the round-tripped payload alone still selects the same
        // characters, with nothing from the decompiler in scope.
        foreach (var span in replayed.Nodes)
            Assert.Equal(span.Length, replayed.Lines[span.Line].Substring(span.Column, span.Length).Length);
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
        Assert.Equal("List<int>", a.Detail);
        Assert.Equal(12, a.SourceOffset);
        Assert.Equal("efgh", map.Lines[a.Line].Substring(a.Column, a.Length));

        Assert.Equal("alloc.box", b.Descriptor);
        Assert.Equal("ijkl", map.Lines[b.Line].Substring(b.Column, b.Length));
    }

    [Fact]
    public void OrderingDistinguishesEveryFieldThatCanDiffer()
    {
        // Any pair the comparison calls equal may come out in either order, so a
        // comparison that stops short of a total order makes the serialised
        // payload differ between runs over identical input. Each pair below
        // differs in exactly one field.
        var baseline = new PrintedAnnotationSpan("alloc.new", "NewObject", 3, 7, 12, "List<int>", 40);

        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Line = 4 }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Column = 8 }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Descriptor = "alloc.box" }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { SourceOffset = 41 }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Length = 13 }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Kind = "Box" }));
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
    public void AFactOnAStraddlingNodeKeepsItsLineAndSaysTheLengthIsUnknown()
    {
        // Dropping the fact would lose a real observation, so the position
        // degrades instead -- and says so, rather than reporting a zero length a
        // caller would have to guess the meaning of.
        var node = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(node, 2, 12);
        ranges.Complete("ab\ncdefgh\nij");

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>> { [node] = [new Annotation(Alloc, 4)] });

        Assert.Empty(map.Nodes);
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(0, fact.Line);
        Assert.Equal(-1, fact.Length);
    }

    [Fact]
    public void ARangeThatCrossesALineBreakIsRefusedRatherThanTruncated()
    {
        // Reporting its first line would understate the extent, and a caller
        // slicing by that position would silently get the wrong characters.
        var node = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(node, 2, 12);
        ranges.Complete("ab\ncdefgh\nij");

        Assert.False(ranges.TryGetLineColumn(node, out _, out _, out _));
        Assert.Empty(PrintedBodyMap.Create(ranges).Nodes);
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
        Assert.Equal("efgh", map.Lines[span.Line].Substring(span.Column, span.Length));
    }
}
