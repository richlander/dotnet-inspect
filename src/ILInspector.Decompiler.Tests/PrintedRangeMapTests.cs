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
    public void RangeText_SitsAtTheExactColumnItClaims(string methodName)
    {
        // Catches an off-by-N start. A statement's slice is its whole line, but an
        // expression's is a fragment of one, so "the slice equals the line" is no
        // longer the invariant; "the slice is at the column it claims" is, and it
        // is the stronger check — it pins the column a caret will be drawn at,
        // which equality of trimmed text would not.
        var (output, ranges) = Print(methodName);
        var lines = output.Replace("\r\n", "\n").Split('\n');

        foreach (var range in ranges)
        {
            var (start, end) = Offsets(range, output.Length);
            string firstSliceLine = output[start..end].Replace("\r\n", "\n").Split('\n')[0];
            if (firstSliceLine.Trim().Length == 0)
                continue;

            Assert.True(ranges.TryGetLine(range.Node, out int line));
            int lineStart = output.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
            int column = start - lineStart;

            Assert.InRange(column, 0, lines[line].Length);
            Assert.True(
                column + firstSliceLine.Length <= lines[line].Length,
                $"{range.Node.GetType().Name} claims past the end of line {line}");
            Assert.Equal(firstSliceLine, lines[line].Substring(column, firstSliceLine.Length));

            // The check above derives the column from the same offset it verifies,
            // so a uniform shift would move both together and slip through. This
            // does not: a range that begins or ends inside an identifier is
            // off-by-something no matter how self-consistent its arithmetic is.
            static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
            string slice = output[start..end];
            if (IsWordChar(slice[0]) && start > 0)
                Assert.False(
                    IsWordChar(output[start - 1]),
                    $"{range.Node.GetType().Name} starts inside an identifier: ...{output[Math.Max(0, start - 8)..end]}");
            if (IsWordChar(slice[^1]) && end < output.Length)
                Assert.False(
                    IsWordChar(output[end]),
                    $"{range.Node.GetType().Name} ends inside an identifier: {output[start..Math.Min(output.Length, end + 8)]}...");
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

    [Fact]
    public void Expression_ClaimsExactlyItsOwnCharacters_NotTheWholeStatement()
    {
        // The reason this work exists. Only the allocation allocates, but until an
        // expression owned characters of its own, the tightest thing anything
        // could point at was the statement -- so a caret under
        // `sink.Add(new object());` claimed the call allocates, which is false.
        var (output, ranges) = Print(
            typeof(PrintedRangeExpressionFixture),
            nameof(PrintedRangeExpressionFixture.AddOne));

        var allocation = Assert.Single(ranges, r => r.Node is NewObject);
        var (start, end) = Offsets(allocation, output.Length);
        Assert.Equal("new object()", output[start..end]);

        // Strictly inside the statement, and strictly smaller: a range equal to
        // its statement would pass a containment check while claiming nothing new.
        var statement = Assert.Single(ranges, r => r.Node is ExpressionStatement);
        var (stmtStart, stmtEnd) = Offsets(statement, output.Length);
        Assert.InRange(start, stmtStart, stmtEnd);
        Assert.InRange(end, start, stmtEnd);
        Assert.True(end - start < stmtEnd - stmtStart, "the expression claimed its whole statement");
        Assert.Contains("sink.Add(new object());", output);
    }

    [Fact]
    public void RepeatedExpression_ClaimsNothing_RatherThanGuessWhichOccurrenceIsWhich()
    {
        // `x + x` prints both operands identically, and composition order does not
        // say which characters belong to which node. Recording either one would be
        // a coin flip that reads as certainty, so neither is recorded and the
        // consumer falls back to the statement -- exactly what it had before.
        var (output, ranges) = Print(
            typeof(PrintedRangeExpressionFixture),
            nameof(PrintedRangeExpressionFixture.TwiceTheSame));

        Assert.Contains("x + x", output);
        Assert.DoesNotContain(ranges, r => r.Node is LoadArgument);

        // The gate is about ambiguity, not about arguments being unrecordable:
        // the same node kind, printed once, does claim its characters.
        var (singleOutput, singleRanges) = Print(
            typeof(PrintedRangeExpressionFixture),
            nameof(PrintedRangeExpressionFixture.OnlyOnce));
        var argument = Assert.Single(singleRanges, r => r.Node is LoadArgument);
        var (argStart, argEnd) = Offsets(argument, singleOutput.Length);
        Assert.Equal("x", singleOutput[argStart..argEnd]);
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

/// <summary>
/// Statements whose interesting part is smaller than the statement. See
/// <see cref="PrintedRangeMapTests.Expression_ClaimsExactlyItsOwnCharacters_NotTheWholeStatement"/>
/// and <see cref="PrintedRangeMapTests.RepeatedExpression_ClaimsNothing_RatherThanGuessWhichOccurrenceIsWhich"/>.
/// </summary>
public static class PrintedRangeExpressionFixture
{
    /// <summary>The allocation is one call argument, not the whole statement.</summary>
    public static void AddOne(List<object> sink) => sink.Add(new object());

    /// <summary>Both operands print as the same characters.</summary>
    public static int TwiceTheSame(int x) => x + x;

    /// <summary>The same node kind as above, printed once, so it is unambiguous.</summary>
    public static int OnlyOnce(int x) => x + 1;
}
