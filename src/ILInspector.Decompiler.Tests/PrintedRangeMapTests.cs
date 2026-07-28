using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// The printer is the only component that can say which characters belong to
// which node, so these pin the recording itself: bounds, the line projection
// that replaced the old per-statement rescan, and the nesting that a wrapper
// around the emission body is what makes correct.
[Trait("Area", "Printer")]
public class PrintedRangeMapTests
{
    static (string Output, PrintedRangeMap Ranges) Print(string methodName)
        => Print(typeof(AllocSampleClass), methodName);

    static (string Output, PrintedRangeMap Ranges) Print(Type declaringType, string methodName)
    {
        var source = MetadataSource.Open(declaringType.Assembly.Location);
        var function = IrImporter.Import(source, declaringType.FullName!, methodName);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!, out var ranges);
        Assert.NotNull(result.Output);
        return (result.Output!, ranges);
    }

    static IrFunction Import(Type declaringType, string methodName)
    {
        var source = MetadataSource.Open(declaringType.Assembly.Location);
        var function = IrImporter.Import(source, declaringType.FullName!, methodName);
        Assert.NotNull(function);
        return function!;
    }

    static (int Start, int End) Offsets(PrintedRange range, int length)
        => (range.Characters.Start.GetOffset(length), range.Characters.End.GetOffset(length));

    [Theory]
    [InlineData(nameof(AllocSampleClass.SumList))]
    [InlineData(nameof(AllocSampleClass.SumEnumerable))]
    [InlineData(nameof(AllocSampleClass.MakeArray))]
    public void EveryRange_LiesInsideTheOutputItIndexes(string methodName)
    {
        // Nothing clamps ranges to the returned string, because a sweep of 62,838
        // ranges found none that overruns it. This is the invariant that lets
        // consumers slice without checking, so it is pinned rather than assumed.
        var (output, ranges) = Print(methodName);
        Assert.NotEmpty(ranges);

        foreach (var range in ranges)
        {
            var (start, end) = Offsets(range, output.Length);
            Assert.InRange(start, 0, output.Length);
            Assert.InRange(end, start, output.Length);
            Assert.True(end > start, $"{range.Node.GetType().Name} recorded an empty range");
        }
    }

    [Fact]
    public void SuppressedChainCall_IsNotRecorded_BecauseItPrintsNothing()
    {
        // An implicit parameterless base() has no rendered form, so unlike a
        // chain call with arguments — lifted onto the signature and never walked
        // — it stays in the body and emits nothing. Recording it would stamp a
        // zero-width range that resolves to the line of the *next* statement. A
        // sweep found 890 of these across 9,114 methods, so the shape is
        // ordinary rather than exotic.
        var function = Import(typeof(PrintedRangeChainFixture), ".ctor");
        var chainCalls = function.Body.Descendants
            .OfType<ExpressionStatement>()
            .Where(statement => statement.Expression is Call { Callee.Name: ".ctor" })
            .ToList();
        Assert.NotEmpty(chainCalls);

        var result = CSharpPrinter.PrintRaised(function, out var ranges);
        Assert.NotNull(result.Output);

        foreach (var chainCall in chainCalls)
            Assert.False(ranges.TryGetRange(chainCall, out _));

        // Not vacuous by way of an empty body: the store that follows the
        // suppressed chain call is recorded, and its range is the printed text.
        Assert.NotEmpty(ranges);
        var store = Assert.Single(ranges, range => range.Node is StoreField);
        Assert.Equal("this.Name = name;", result.Output![store.Characters].Trim());
    }

    [Fact]
    public void SuppressedChainCall_KeepsItsInsertionPoint_SoItsIlStillHasSomewhereToGo()
    {
        // Printing nothing makes "which characters did this node emit" undefined,
        // but it leaves "where in emission order does it sit" perfectly defined.
        // Those are separate questions and get separate members, because the
        // node's own opcodes have no other owner: without the insertion point the
        // mixed IL view has no line to render them against and drops them.
        var function = Import(typeof(PrintedRangeChainFixture), ".ctor");
        var chainCall = Assert.Single(
            function.Body.Descendants.OfType<ExpressionStatement>(),
            statement => statement.Expression is Call { Callee.Name: ".ctor" });

        var result = CSharpPrinter.PrintRaised(function, out var ranges);
        Assert.NotNull(result.Output);

        Assert.False(ranges.TryGetRange(chainCall, out _));
        Assert.False(ranges.TryGetLine(chainCall, out _));
        Assert.True(ranges.TryGetInsertionLine(chainCall, out int line));

        // The implicit base() runs before the first statement, so it inserts at
        // the line that statement prints on — IL placed against it belongs above.
        Assert.Equal(0, line);
        var store = Assert.Single(ranges, range => range.Node is StoreField);
        Assert.True(ranges.TryGetLine(store.Node, out int storeLine));
        Assert.Equal(storeLine, line);
    }

    [Fact]
    public void PrintedNode_HasNoInsertionPoint_BecauseItHasARange()
    {
        // The two channels are disjoint: asking for an insertion point on a node
        // that printed text is a category error, and answering it would let a
        // consumer take a position where it should have taken a range.
        var (_, ranges) = Print(nameof(AllocSampleClass.SumList));

        Assert.NotEmpty(ranges);
        foreach (var range in ranges)
            Assert.False(ranges.TryGetInsertionLine(range.Node, out _));
    }

    [Theory]
    [InlineData(nameof(AllocSampleClass.SumList))]
    [InlineData(nameof(AllocSampleClass.SumEnumerable))]
    public void TryGetLine_AgreesWithCountingNewlinesIndependently(string methodName)
    {
        // The line is now a projection of the range rather than separately
        // recorded, so it has to reproduce what the old scan computed.
        var (output, ranges) = Print(methodName);

        foreach (var range in ranges)
        {
            var (start, _) = Offsets(range, output.Length);
            int expected = output[..start].Count(c => c == '\n');
            Assert.True(ranges.TryGetLine(range.Node, out int line));
            Assert.Equal(expected, line);
        }
    }

    [Theory]
    [InlineData(nameof(AllocSampleClass.SumList))]
    [InlineData(nameof(AllocSampleClass.SumEnumerable))]
    public void RangeText_StartsAtTheStatementOnItsOwnLine(string methodName)
    {
        // Catches an off-by-N start: the recorded slice, once its leading indent
        // is dropped, must be what the line at that position actually reads.
        var (output, ranges) = Print(methodName);
        var lines = output.Replace("\r\n", "\n").Split('\n');

        foreach (var range in ranges)
        {
            var (start, end) = Offsets(range, output.Length);
            string slice = output[start..end].TrimStart();
            if (slice.Length == 0)
                continue;
            Assert.True(ranges.TryGetLine(range.Node, out int line));
            string firstSliceLine = slice.Replace("\r\n", "\n").Split('\n')[0];
            Assert.Equal(lines[line].TrimStart(), firstSliceLine);
        }
    }

    [Fact]
    public void NestedStatement_IsContainedByItsPrintedAncestor()
    {
        // A foreach body sits inside the loop's own printed range. This is the
        // property a wrapper around the emission body buys and an inline record
        // at the top of that body cannot.
        var (output, ranges) = Print(nameof(AllocSampleClass.SumList));

        var contained = ranges
            .Where(range => NearestRecordedAncestor(range.Node, ranges) is not null)
            .ToList();
        Assert.NotEmpty(contained);

        foreach (var range in contained)
        {
            var ancestor = NearestRecordedAncestor(range.Node, ranges)!;
            Assert.True(ranges.TryGetRange(ancestor, out var outer));
            var (start, end) = Offsets(range, output.Length);
            int outerStart = outer.Start.GetOffset(output.Length);
            int outerEnd = outer.End.GetOffset(output.Length);
            Assert.InRange(start, outerStart, outerEnd);
            Assert.InRange(end, start, outerEnd);
        }
    }

    [Fact]
    public void EnumerationOrder_IsEmissionCompletion_SoAncestorsFollowDescendants()
    {
        // The documented contract. Ordering by start position is deliberately not
        // promised, so this pins what is promised instead.
        var (_, ranges) = Print(nameof(AllocSampleClass.SumList));

        var position = new Dictionary<IrNode, int>();
        for (int i = 0; i < ranges.Count; i++)
            position[ranges[i].Node] = i;

        foreach (var (node, _) in ranges)
            if (NearestRecordedAncestor(node, ranges) is { } ancestor)
                Assert.True(position[node] < position[ancestor]);
    }

    [Fact]
    public void FailedPrint_YieldsAnEmptyMapRatherThanRangesIntoNothing()
    {
        Assert.Empty(PrintedRangeMap.Empty);
        Assert.Equal("", PrintedRangeMap.Empty.Output);
        Assert.False(PrintedRangeMap.Empty.TryGetLine(new Return(null), out _));
    }

    static IrNode? NearestRecordedAncestor(IrNode node, PrintedRangeMap ranges)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            if (ranges.TryGetRange(current, out _))
                return current;
        return null;
    }
}

/// <summary>
/// A constructor whose chain call the printer walks but never prints: the
/// implicit <c>object::.ctor()</c> has no rendered form, so the statement
/// stays in the body and emits nothing. See
/// <see cref="PrintedRangeMapTests.SuppressedChainCall_IsNotRecorded_BecauseItPrintsNothing"/>.
/// </summary>
public sealed class PrintedRangeChainFixture
{
    public PrintedRangeChainFixture(string name) => Name = name;

    public string Name { get; }
}
