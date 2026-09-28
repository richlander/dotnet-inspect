using System.Text.Json;

namespace Inspector.Text.Tests;

public sealed class DecodedTextDocumentTests
{
    [Fact]
    public void PullsReconstructExactDecodedText()
    {
        const string text =
            "A\r\n😀B\rC\nD\u0085E\u2028F\u2029G";
        var document = new DecodedTextDocument(text);

        IReadOnlyList<DecodedTextLine> lines =
            Drain(
                document,
                new DecodedTextPullLimits(
                    maximumCandidateRows: 2,
                    maximumUtf16CodeUnits: 8,
                    maximumJsonEncodedUtf8Bytes: 32),
                out int exactLineCount);

        Assert.Equal(7, exactLineCount);
        Assert.Equal(text, Reconstruct(lines));
        Assert.Collection(
            lines,
            line => AssertLine(
                line,
                1,
                0,
                "A",
                DecodedTextLineTerminator.CarriageReturnLineFeed),
            line => AssertLine(
                line,
                2,
                3,
                "😀B",
                DecodedTextLineTerminator.CarriageReturn),
            line => AssertLine(
                line,
                3,
                7,
                "C",
                DecodedTextLineTerminator.LineFeed),
            line => AssertLine(
                line,
                4,
                9,
                "D",
                DecodedTextLineTerminator.NextLine),
            line => AssertLine(
                line,
                5,
                11,
                "E",
                DecodedTextLineTerminator.LineSeparator),
            line => AssertLine(
                line,
                6,
                13,
                "F",
                DecodedTextLineTerminator.ParagraphSeparator),
            line => AssertLine(
                line,
                7,
                15,
                "G",
                DecodedTextLineTerminator.None));
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("\n", 2)]
    [InlineData("A\n", 2)]
    [InlineData("A", 1)]
    public void EmptyAndFinalEmptyLinesAreCoordinates(
        string text,
        int expectedLineCount)
    {
        var document = new DecodedTextDocument(text);

        IReadOnlyList<DecodedTextLine> lines =
            Drain(
                document,
                DecodedTextPullLimits.ForCandidateRows(
                    maximumCandidateRows: 1),
                out int exactLineCount);

        Assert.Equal(expectedLineCount, exactLineCount);
        Assert.Equal(text, Reconstruct(lines));
        Assert.Equal(expectedLineCount, lines.Count);
    }

    [Fact]
    public void DifferentPullBoundsPreserveRowsAndCompletion()
    {
        const string text =
            "one\nsecond\r\nthree\u2028four\nfive";
        var document = new DecodedTextDocument(text);
        DecodedTextLine[] expected =
        [
            .. Drain(
                document,
                DecodedTextPullLimits.ForCandidateRows(
                    maximumCandidateRows: int.MaxValue),
                out int expectedCount),
        ];

        DecodedTextPullLimits[] limitsCases =
        [
            new(1, 64, 128),
            new(3, 12, 48),
            new(10, 64, 128),
        ];
        foreach (DecodedTextPullLimits limits in limitsCases)
        {
            IReadOnlyList<DecodedTextLine> actual =
                Drain(document, limits, out int count);

            Assert.Equal(expectedCount, count);
            Assert.Equal(
                expected.Select(LineValue),
                actual.Select(LineValue));
        }
    }

    [Fact]
    public void NormalPullsRespectEveryMaximum()
    {
        const string text =
            "aa\nbbb\ncccc\nfive\nsix";
        var document = new DecodedTextDocument(text);
        var limits = new DecodedTextPullLimits(
            maximumCandidateRows: 2,
            maximumUtf16CodeUnits: 9,
            maximumJsonEncodedUtf8Bytes: 12);
        DecodedTextPosition? position = document.Start;
        int batches = 0;

        while (position is not null)
        {
            DecodedTextBatch batch = document.Pull(position, limits);

            Assert.InRange(
                batch.Lines.Length,
                1,
                limits.MaximumCandidateRows);
            Assert.InRange(
                batch.Utf16CodeUnits,
                0,
                limits.MaximumUtf16CodeUnits);
            Assert.InRange(
                batch.JsonEncodedUtf8Bytes,
                0,
                limits.MaximumJsonEncodedUtf8Bytes);
            batches++;
            position = batch.Continuation;
        }

        Assert.True(batches > 1);
    }

    [Fact]
    public void ShortPullDoesNotImplyCompletion()
    {
        var document = new DecodedTextDocument(
            "small\nthis row makes the content bound stop the prior batch\nlast");
        var limits = new DecodedTextPullLimits(
            maximumCandidateRows: 10,
            maximumUtf16CodeUnits: 16,
            maximumJsonEncodedUtf8Bytes: 64);

        DecodedTextBatch batch = document.Pull(
            document.Start,
            limits);

        Assert.Single(batch.Lines);
        Assert.False(batch.IsComplete);
        Assert.Null(batch.ExactLineCount);
        Assert.NotNull(batch.Continuation);
    }

    [Theory]
    [InlineData("abcdef", 5, 100, 6, 6)]
    [InlineData("<", 10, 5, 1, 6)]
    public void LineExceedingPullLimitsFailsVisibly(
        string text,
        int maximumUtf16CodeUnits,
        int maximumJsonEncodedUtf8Bytes,
        int expectedUtf16CodeUnits,
        int expectedJsonEncodedUtf8Bytes)
    {
        var document = new DecodedTextDocument(text);
        var limits = new DecodedTextPullLimits(
            maximumCandidateRows: 1,
            maximumUtf16CodeUnits,
            maximumJsonEncodedUtf8Bytes);

        DecodedTextLineLimitException exception =
            Assert.Throws<DecodedTextLineLimitException>(
                () => document.Pull(document.Start, limits));

        Assert.Equal(1, exception.LineNumber);
        Assert.Equal(
            expectedUtf16CodeUnits,
            exception.Utf16CodeUnits);
        Assert.Equal(
            maximumUtf16CodeUnits,
            exception.MaximumUtf16CodeUnits);
        Assert.Equal(
            expectedJsonEncodedUtf8Bytes,
            exception.JsonEncodedUtf8Bytes);
        Assert.Equal(
            maximumJsonEncodedUtf8Bytes,
            exception.MaximumJsonEncodedUtf8Bytes);
    }

    [Fact]
    public void PullReturnsBoundedPrefixBeforeOverLimitLine()
    {
        var document = new DecodedTextDocument(
            "first\nsecond line is too long");
        var limits = new DecodedTextPullLimits(
            maximumCandidateRows: 10,
            maximumUtf16CodeUnits: 10,
            maximumJsonEncodedUtf8Bytes: 100);

        DecodedTextBatch batch =
            document.Pull(document.Start, limits);

        Assert.Equal("first", Assert.Single(batch.Lines).Content.ToString());
        Assert.NotNull(batch.Continuation);
        DecodedTextLineLimitException exception =
            Assert.Throws<DecodedTextLineLimitException>(
                () => document.Pull(batch.Continuation, limits));
        Assert.Equal(2, exception.LineNumber);
    }

    [Fact]
    public void PullPositionIsBoundToOneDocument()
    {
        var first = new DecodedTextDocument("first");
        var second = new DecodedTextDocument("second");

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => second.Pull(
                    first.Start,
                    DecodedTextPullLimits.ForCandidateRows(1)));

        Assert.Contains(
            "different decoded document",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void JsonMetricMatchesDefaultJsonEncoder()
    {
        const string text =
            "plain\t😀\u2028";
        var document = new DecodedTextDocument(text);
        DecodedTextLine line =
            Assert.Single(
                document.Pull(
                        document.Start,
                        DecodedTextPullLimits.ForCandidateRows(1))
                    .Lines);
        int expected =
            JsonEncodedText.Encode(text).EncodedUtf8Bytes.Length;

        Assert.Equal(expected, line.JsonEncodedUtf8Bytes);
    }

    [Fact]
    public void ForwardCursorMatchesBoundedPull()
    {
        const string text =
            "A\r\n😀B\rC\nD\u0085E\u2028F\u2029G";
        var document = new DecodedTextDocument(text);
        IReadOnlyList<DecodedTextLine> expected =
            Drain(
                document,
                DecodedTextPullLimits.ForCandidateRows(2),
                out int expectedCount);
        var actual = new List<(
            int Number,
            int Start,
            string Content,
            DecodedTextLineTerminator Terminator,
            int Utf16CodeUnits,
            int JsonEncodedUtf8Bytes)>();
        DecodedTextCursor cursor = document.StartCursor;

        while (document.TryReadLine(
            ref cursor,
            out DecodedTextLineSlice line))
        {
            actual.Add(
                (
                    line.Number,
                    line.Start,
                    line.Content.ToString(),
                    line.Terminator,
                    line.Utf16CodeUnits,
                    line.JsonEncodedUtf8Bytes));
        }

        Assert.True(cursor.IsComplete);
        Assert.Equal(expectedCount, cursor.ExactLineCount);
        Assert.Equal(
            expected.Select(
                line => (
                    line.Number,
                    line.Start,
                    line.Content.ToString(),
                    line.Terminator,
                    line.Utf16CodeUnits,
                    line.JsonEncodedUtf8Bytes)),
            actual);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("\n", 2)]
    [InlineData("A\n", 2)]
    [InlineData("A", 1)]
    public void ForwardCursorDisclosesCountOnlyAtCompletion(
        string text,
        int expectedLineCount)
    {
        var document = new DecodedTextDocument(text);
        DecodedTextCursor cursor = document.StartCursor;

        Assert.False(cursor.IsComplete);
        Assert.Null(cursor.ExactLineCount);

        int observed = 0;
        while (document.TryReadLine(
            ref cursor,
            out DecodedTextLineSlice line))
        {
            observed++;
            Assert.Equal(observed, line.Number);
        }

        Assert.Equal(expectedLineCount, observed);
        Assert.True(cursor.IsComplete);
        Assert.Equal(expectedLineCount, cursor.ExactLineCount);
        Assert.False(
            document.TryReadLine(
                ref cursor,
                out DecodedTextLineSlice completedLine));
        Assert.Equal(default, completedLine);
    }

    [Fact]
    public void ForwardCursorIsBoundToOneDocument()
    {
        var first = new DecodedTextDocument("first");
        var second = new DecodedTextDocument("second");
        DecodedTextCursor cursor = first.StartCursor;

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => second.TryReadLine(
                    ref cursor,
                    out DecodedTextLineSlice _));

        Assert.Contains(
            "different decoded document",
            exception.Message,
            StringComparison.Ordinal);
        Assert.False(cursor.IsComplete);
        Assert.True(
            first.TryReadLine(
                ref cursor,
                out DecodedTextLineSlice line));
        Assert.Equal("first", line.Content.ToString());
    }

    [Fact]
    public void ForwardCursorCopiesAdvanceIndependently()
    {
        var document = new DecodedTextDocument("first\nsecond");
        DecodedTextCursor first = document.StartCursor;
        DecodedTextCursor second = first;

        Assert.True(
            document.TryReadLine(
                ref first,
                out DecodedTextLineSlice firstLine));
        Assert.True(
            document.TryReadLine(
                ref second,
                out DecodedTextLineSlice repeatedLine));

        Assert.Equal(firstLine.Number, repeatedLine.Number);
        Assert.Equal(
            firstLine.Content.ToString(),
            repeatedLine.Content.ToString());
        Assert.True(
            document.TryReadLine(
                ref first,
                out DecodedTextLineSlice nextLine));
        Assert.Equal("second", nextLine.Content.ToString());
        Assert.True(first.IsComplete);
        Assert.False(second.IsComplete);
    }

    [Theory]
    [InlineData("abcdef", 5, 100, 6, 6)]
    [InlineData("<", 10, 5, 1, 6)]
    public void ForwardSliceEnforcesLineLimits(
        string text,
        int maximumUtf16CodeUnits,
        int maximumJsonEncodedUtf8Bytes,
        int expectedUtf16CodeUnits,
        int expectedJsonEncodedUtf8Bytes)
    {
        var document = new DecodedTextDocument(text);
        DecodedTextCursor cursor = document.StartCursor;
        Assert.True(
            document.TryReadLine(
                ref cursor,
                out DecodedTextLineSlice line));
        var limits = new DecodedTextPullLimits(
            maximumCandidateRows: 1,
            maximumUtf16CodeUnits,
            maximumJsonEncodedUtf8Bytes);

        DecodedTextLineLimitException exception =
            Assert.Throws<DecodedTextLineLimitException>(
                () => line.EnsureWithin(limits));

        Assert.Equal(1, exception.LineNumber);
        Assert.Equal(
            expectedUtf16CodeUnits,
            exception.Utf16CodeUnits);
        Assert.Equal(
            maximumUtf16CodeUnits,
            exception.MaximumUtf16CodeUnits);
        Assert.Equal(
            expectedJsonEncodedUtf8Bytes,
            exception.JsonEncodedUtf8Bytes);
        Assert.Equal(
            maximumJsonEncodedUtf8Bytes,
            exception.MaximumJsonEncodedUtf8Bytes);
    }

    [Fact]
    public void ForwardCursorAllocatesNoPerLineObjects()
    {
        string text = string.Concat(
            Enumerable.Repeat("content\r\n", 1_024));
        var document = new DecodedTextDocument(text);
        long expectedChecksum = DrainCursor(document);
        DecodedTextCursor cursor = document.StartCursor;
        long before = GC.GetAllocatedBytesForCurrentThread();

        long checksum = DrainCursor(document, ref cursor);

        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(document);
        Assert.Equal(expectedChecksum, checksum);
        Assert.Equal(1_025, cursor.ExactLineCount);
        Assert.Equal(0, allocated);
    }

    private static IReadOnlyList<DecodedTextLine> Drain(
        DecodedTextDocument document,
        DecodedTextPullLimits limits,
        out int exactLineCount)
    {
        var lines = new List<DecodedTextLine>();
        DecodedTextPosition? position = document.Start;
        int? count = null;
        while (position is not null)
        {
            DecodedTextBatch batch = document.Pull(position, limits);
            lines.AddRange(batch.Lines);
            count = batch.ExactLineCount;
            position = batch.Continuation;
        }

        exactLineCount =
            count
            ?? throw new InvalidOperationException(
                "A completed drain did not disclose exact Count.");
        return lines;
    }

    private static long DrainCursor(
        DecodedTextDocument document)
    {
        DecodedTextCursor cursor = document.StartCursor;
        return DrainCursor(document, ref cursor);
    }

    private static long DrainCursor(
        DecodedTextDocument document,
        ref DecodedTextCursor cursor)
    {
        long checksum = 0;
        while (document.TryReadLine(
            ref cursor,
            out DecodedTextLineSlice line))
        {
            checksum = checked(
                checksum
                + line.Number
                + line.Start
                + line.Content.Length
                + line.Utf16CodeUnits);
        }

        return checksum;
    }

    private static string Reconstruct(
        IEnumerable<DecodedTextLine> lines) =>
        string.Concat(
            lines.Select(
                line =>
                    line.Content.ToString()
                    + line.TerminatorText));

    private static (
        int Number,
        int Start,
        string Content,
        DecodedTextLineTerminator Terminator)
        LineValue(DecodedTextLine line) =>
        (
            line.Number,
            line.Start,
            line.Content.ToString(),
            line.Terminator);

    private static void AssertLine(
        DecodedTextLine line,
        int number,
        int start,
        string content,
        DecodedTextLineTerminator terminator)
    {
        Assert.Equal(number, line.Number);
        Assert.Equal(start, line.Start);
        Assert.Equal(content, line.Content.ToString());
        Assert.Equal(terminator, line.Terminator);
    }
}
