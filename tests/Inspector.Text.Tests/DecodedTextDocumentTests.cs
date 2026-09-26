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
                new DecodedTextPullRequest(
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
                DecodedTextPullRequest.Unbounded(
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
                DecodedTextPullRequest.Unbounded(
                    maximumCandidateRows: int.MaxValue),
                out int expectedCount),
        ];

        DecodedTextPullRequest[] requests =
        [
            new(1, 64, 128),
            new(3, 12, 48),
            new(10, 64, 128),
        ];
        foreach (DecodedTextPullRequest request in requests)
        {
            IReadOnlyList<DecodedTextLine> actual =
                Drain(document, request, out int count);

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
        var request = new DecodedTextPullRequest(
            maximumCandidateRows: 2,
            maximumUtf16CodeUnits: 9,
            maximumJsonEncodedUtf8Bytes: 12);
        DecodedTextPosition? position = document.Start;
        int batches = 0;

        while (position is not null)
        {
            DecodedTextBatch batch = document.Pull(position, request);

            Assert.Equal(DecodedTextBatchKind.Rows, batch.Kind);
            Assert.InRange(
                batch.Lines.Length,
                1,
                request.MaximumCandidateRows);
            Assert.InRange(
                batch.Utf16CodeUnits,
                0,
                request.MaximumUtf16CodeUnits);
            Assert.InRange(
                batch.JsonEncodedUtf8Bytes,
                0,
                request.MaximumJsonEncodedUtf8Bytes);
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
        var request = new DecodedTextPullRequest(
            maximumCandidateRows: 10,
            maximumUtf16CodeUnits: 16,
            maximumJsonEncodedUtf8Bytes: 64);

        DecodedTextBatch batch = document.Pull(
            document.Start,
            request);

        Assert.Single(batch.Lines);
        Assert.False(batch.IsComplete);
        Assert.Null(batch.ExactLineCount);
        Assert.NotNull(batch.Continuation);
    }

    [Fact]
    public void OversizedLineProducesBoundedFragments()
    {
        const string text =
            "ab😀cd\u2028tail";
        var document = new DecodedTextDocument(text);
        var pullRequest = new DecodedTextPullRequest(
            maximumCandidateRows: 4,
            maximumUtf16CodeUnits: 4,
            maximumJsonEncodedUtf8Bytes: 16);

        DecodedTextBatch batch = document.Pull(
            document.Start,
            pullRequest);
        DecodedTextLine line = Assert.Single(batch.Lines);

        Assert.Equal(
            DecodedTextBatchKind.OversizedLine,
            batch.Kind);
        Assert.False(batch.IsComplete);

        var fragmentRequest = new DecodedTextFragmentRequest(
            maximumUtf16CodeUnits: 3,
            maximumJsonEncodedUtf8Bytes: 12);
        var fragments = new List<DecodedTextLineFragment>();
        int? offset = 0;
        while (offset is not null)
        {
            DecodedTextLineFragment fragment =
                line.ReadFragment(
                    offset.Value,
                    fragmentRequest);
            Assert.InRange(
                fragment.Utf16CodeUnits,
                0,
                fragmentRequest.MaximumUtf16CodeUnits);
            Assert.InRange(
                fragment.JsonEncodedUtf8Bytes,
                0,
                fragmentRequest.MaximumJsonEncodedUtf8Bytes);
            Assert.False(
                fragment.Content.Length > 0
                && char.IsLowSurrogate(fragment.Content.Span[0]));
            Assert.False(
                fragment.Content.Length > 0
                && char.IsHighSurrogate(fragment.Content.Span[^1]));
            fragments.Add(fragment);
            offset = fragment.NextContentOffset;
        }

        Assert.True(fragments.Count > 1);
        Assert.All(
            fragments[..^1],
            fragment =>
            {
                Assert.False(fragment.IsLineComplete);
                Assert.Equal(
                    DecodedTextLineTerminator.None,
                    fragment.Terminator);
            });
        Assert.Equal(
            DecodedTextLineTerminator.LineSeparator,
            fragments[^1].Terminator);
        Assert.Equal(
            "ab😀cd\u2028",
            string.Concat(
                fragments.Select(
                    fragment =>
                        fragment.Content.ToString()
                        + fragment.TerminatorText)));
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
                    DecodedTextPullRequest.Unbounded(1)));

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
                        DecodedTextPullRequest.Unbounded(1))
                    .Lines);
        int expected =
            JsonEncodedText.Encode(text).EncodedUtf8Bytes.Length;

        Assert.Equal(expected, line.JsonEncodedUtf8Bytes);
    }

    [Fact]
    public void FragmentFailsWhenLimitsCannotMakeProgress()
    {
        var document = new DecodedTextDocument("😀");
        DecodedTextLine line =
            Assert.Single(
                document.Pull(
                        document.Start,
                        new DecodedTextPullRequest(1, 1, 1))
                    .Lines);

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => line.ReadFragment(
                    0,
                    new DecodedTextFragmentRequest(1, 1)));

        Assert.Contains(
            "cannot contain",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static IReadOnlyList<DecodedTextLine> Drain(
        DecodedTextDocument document,
        DecodedTextPullRequest request,
        out int exactLineCount)
    {
        var lines = new List<DecodedTextLine>();
        DecodedTextPosition? position = document.Start;
        int? count = null;
        while (position is not null)
        {
            DecodedTextBatch batch = document.Pull(position, request);
            if (batch.Kind == DecodedTextBatchKind.OversizedLine)
            {
                throw new InvalidOperationException(
                    "The test request unexpectedly required fragmentation.");
            }

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
