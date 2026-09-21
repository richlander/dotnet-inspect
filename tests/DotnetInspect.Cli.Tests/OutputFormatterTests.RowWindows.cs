using DotnetInspect.Cli.Models;
using System.Reflection;
using System.Text.Json;
using DotnetInspect.Cli.Views;
using DotnetInspect.Cli;
using DotnetInspect.Cli.Commands;
using ILInspector.Analysis;
using Inspector.Findings;
using ILInspector.Metadata;
using InertText;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using Markout;

namespace DotnetInspect.Cli.Tests;

public partial class OutputFormatterTests
{
    [Fact]
    public void LimitRenderedTableRows_TsvKeepsHeaderAndLimitsDataRows()
    {
        var tsv = "name\tcount\nA\t1\nB\t2\nC\t3\n";

        var output = OutputFormatter.LimitRenderedTableRows(tsv, RowWindow.Head(2), hasHeader: true).ReplaceLineEndings("\n");

        Assert.Equal("name\tcount\nA\t1\nB\t2\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_PreservesTheLineEndingsItWasGiven()
    {
        // Row limiting selects which rows survive. It is not licensed to rewrite the
        // line endings, which would make the same output differ byte for byte between
        // a run with a row window and one without -- the unlimited fast path returns
        // the string untouched.
        var crlf = "name\tcount\r\nA\t1\r\nB\t2\r\nC\t3\r\n";

        var windowed = OutputFormatter.LimitRenderedTableRows(crlf, RowWindow.Head(2), hasHeader: true);
        Assert.Equal("name\tcount\r\nA\t1\r\nB\t2\r\n", windowed);

        // An open-ended range keeps every row, so it must be byte-identical to the input.
        var openEnded = OutputFormatter.LimitRenderedTableRows(crlf, RowWindow.Range(1, null), hasHeader: true);
        Assert.Equal(crlf, openEnded);

        var lf = "name\tcount\nA\t1\nB\t2\nC\t3\n";
        Assert.Equal("name\tcount\nA\t1\nB\t2\n", OutputFormatter.LimitRenderedTableRows(lf, RowWindow.Head(2), hasHeader: true));
    }

    [Fact]
    public void LimitRenderedTableRows_MarkdownPreservesTheLineEndingsItWasGiven()
    {
        var markdown = "| name | count |\r\n| --- | --- |\r\n| A | 1 |\r\n| B | 2 |\r\n| C | 3 |\r\n";

        var output = OutputFormatter.LimitRenderedTableRows(markdown, RowWindow.Head(2), hasHeader: true);

        Assert.Equal("| name | count |\r\n| --- | --- |\r\n| A | 1 |\r\n| B | 2 |\r\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_TsvWithoutHeaderLimitsFromFirstLine()
    {
        var tsv = "A\t1\nB\t2\nC\t3\n";

        var output = OutputFormatter.LimitRenderedTableRows(tsv, RowWindow.Head(2), hasHeader: false).ReplaceLineEndings("\n");

        Assert.Equal("A\t1\nB\t2\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_JsonlHasNoHeaderLineEvenWhenHeaderRequested()
    {
        var jsonl = "{\"name\":\"A\"}\n{\"name\":\"B\"}\n{\"name\":\"C\"}\n";

        // hasHeader is true (callers pass !--no-header) but jsonl rows are self-describing.
        var output = OutputFormatter.LimitRenderedTableRows(jsonl, RowWindow.Head(2), hasHeader: true).ReplaceLineEndings("\n");

        Assert.Equal("{\"name\":\"A\"}\n{\"name\":\"B\"}\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_MarkdownDelegatesToMarkdownLimiter()
    {
        var markdown = "| Name |\n| ---- |\n| A |\n| B |\n| C |\n";

        var output = OutputFormatter.LimitRenderedTableRows(markdown, RowWindow.Head(2), hasHeader: true).ReplaceLineEndings("\n");

        Assert.Contains("| A |", output);
        Assert.Contains("| B |", output);
        Assert.DoesNotContain("| C |", output);
    }

    [Fact]
    public void LimitRenderedTableRows_NullLimitIsUnchanged()
    {
        var tsv = "name\tcount\nA\t1\nB\t2\n";

        Assert.Equal(tsv, OutputFormatter.LimitRenderedTableRows(tsv, null, hasHeader: true));
    }

    [Fact]
    public void LimitMarkdownTableRows_LimitsEachTable()
    {
        const string markdown = """
        # Title

        ## First

        | Name |
        | ---- |
        | A |
        | B |
        | C |

        ## Second

        | Value |
        | ----- |
        | 1 |
        | 2 |
        """;

        var output = MarkdownTableRowLimiter.Apply(markdown, RowWindow.Head(2));

        Assert.Contains("| A |", output);
        Assert.Contains("| B |", output);
        Assert.DoesNotContain("| C |", output);
        Assert.Contains("| 1 |", output);
        Assert.Contains("| 2 |", output);
    }

    [Fact]
    public void LimitMarkdownTableRows_IgnoresCodeFences()
    {
        const string markdown = """
        ```md
        | Name |
        | ---- |
        | A |
        | B |
        ```

        | Name |
        | ---- |
        | A |
        | B |
        """;

        var output = MarkdownTableRowLimiter.Apply(markdown, RowWindow.Head(1));

        Assert.Contains("| B |\n```", output.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("| B |\n", output.ReplaceLineEndings("\n").Split("```")[2]);
    }

    [Fact]
    public void LimitMarkdownTableRows_TailKeepsLastRowsAndHeader()
    {
        var markdown = "| Name |\n| ---- |\n| A |\n| B |\n| C |\n";

        var output = MarkdownTableRowLimiter.Apply(markdown, RowWindow.Tail(2)).ReplaceLineEndings("\n");

        Assert.Contains("| Name |", output);
        Assert.Contains("| ---- |", output);
        Assert.DoesNotContain("| A |", output);
        Assert.Contains("| B |", output);
        Assert.Contains("| C |", output);
    }

    [Fact]
    public void LimitMarkdownTableRows_TailWiderThanTableKeepsAllRows()
    {
        var markdown = "| Name |\n| ---- |\n| A |\n| B |\n";

        var output = MarkdownTableRowLimiter.Apply(markdown, RowWindow.Tail(10)).ReplaceLineEndings("\n");

        Assert.Contains("| A |", output);
        Assert.Contains("| B |", output);
    }

    [Fact]
    public void LimitRenderedTableRows_TsvTailKeepsHeaderAndLastRows()
    {
        var tsv = "name\tcount\nA\t1\nB\t2\nC\t3\n";

        var output = OutputFormatter.LimitRenderedTableRows(tsv, RowWindow.Tail(2), hasHeader: true).ReplaceLineEndings("\n");

        Assert.Equal("name\tcount\nB\t2\nC\t3\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_JsonlTailKeepsLastRows()
    {
        var jsonl = "{\"name\":\"A\"}\n{\"name\":\"B\"}\n{\"name\":\"C\"}\n";

        var output = OutputFormatter.LimitRenderedTableRows(jsonl, RowWindow.Tail(2), hasHeader: true).ReplaceLineEndings("\n");

        Assert.Equal("{\"name\":\"B\"}\n{\"name\":\"C\"}\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_JsonlZeroWindowEmitsNothing()
    {
        var jsonl = "{\"name\":\"A\"}\n{\"name\":\"B\"}\n{\"name\":\"C\"}\n";

        var tail = OutputFormatter.LimitRenderedTableRows(jsonl, RowWindow.Tail(0), hasHeader: true);
        var head = OutputFormatter.LimitRenderedTableRows(jsonl, RowWindow.Head(0), hasHeader: true);

        Assert.Equal(string.Empty, tail);
        Assert.Equal(string.Empty, head);
    }

    [Fact]
    public void LimitRenderedTableRows_TsvNoHeaderZeroWindowEmitsNothing()
    {
        var tsv = "A\t1\nB\t2\nC\t3\n";

        var output = OutputFormatter.LimitRenderedTableRows(tsv, RowWindow.Tail(0), hasHeader: false);

        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void LimitRenderedTableRows_TsvHeaderZeroWindowKeepsHeader()
    {
        var tsv = "name\tcount\nA\t1\nB\t2\n";

        var output = OutputFormatter.LimitRenderedTableRows(tsv, RowWindow.Tail(0), hasHeader: true).ReplaceLineEndings("\n");

        Assert.Equal("name\tcount\n", output);
    }

    [Theory]
    [InlineData("FIRST", RowSelectorKind.First)]
    [InlineData("last", RowSelectorKind.Last)]
    [InlineData("Last", RowSelectorKind.Last)]
    [InlineData("3", RowSelectorKind.Index)]
    public void RowSelector_ParsesKeywordsAndIndex(string token, RowSelectorKind expected)
    {
        Assert.True(RowSelector.TryParse(token, out var selector));
        Assert.Equal(expected, selector.Kind);
    }

    [Theory]
    [InlineData("firstish")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abc")]
    [InlineData(null)]
    public void RowSelector_RejectsInvalidTokens(string? token)
    {
        Assert.False(RowSelector.TryParse(token, out _));
    }

    [Fact]
    public void RowSelector_ResolvesFirstLastAndIndex()
    {
        int[] contiguous = [1, 2, 3, 4, 5, 6, 7];
        Assert.Equal(1, RowSelector.First.Resolve(contiguous));
        Assert.Equal(7, RowSelector.Last.Resolve(contiguous));
        Assert.Equal(3, RowSelector.FromIndex(3).Resolve(contiguous));
    }

    [Fact]
    public void RowSelector_ResolvesEndpointsOfGappedRows()
    {
        // first/last name the endpoints of the rendered sequence, not 1 and the count.
        int[] gapped = [2, 5, 9];
        Assert.Equal(2, RowSelector.First.Resolve(gapped));
        Assert.Equal(9, RowSelector.Last.Resolve(gapped));
        Assert.Equal(5, RowSelector.FromIndex(5).Resolve(gapped));
    }

    [Fact]
    public void RowNumbering_DescribesContiguousAndGappedRows()
    {
        Assert.Equal("1 through 4", RowNumbering.Describe([1, 2, 3, 4]));
        Assert.Equal("2, 5, 9", RowNumbering.Describe([2, 5, 9]));
        Assert.Equal("7", RowNumbering.Describe([7]));
        Assert.Equal("none", RowNumbering.Describe([]));
    }

    [Fact]
    public void RowNumbering_IndexOfFindsRowByDisplayedNumber()
    {
        int[] gapped = [2, 5, 9];
        Assert.Equal(1, RowNumbering.IndexOf(gapped, 5));
        Assert.Equal(-1, RowNumbering.IndexOf(gapped, 3));
    }

    [Fact]
    public void BuildRowWindow_CountWithoutDirectionIsLeadingWindow()
    {
        var window = SharedOptions.BuildRowWindow("3", fromEnd: false);
        Assert.Equal(RowWindow.Head(3), window);
    }

    [Fact]
    public void BuildRowWindow_CountWithTailIsTrailingWindow()
    {
        var window = SharedOptions.BuildRowWindow("3", fromEnd: true);
        Assert.Equal(RowWindow.Tail(3), window);
    }

    [Fact]
    public void BuildRowWindow_WithoutRowsIsNull()
    {
        Assert.Null(SharedOptions.BuildRowWindow(null, fromEnd: false));
        Assert.Null(SharedOptions.BuildRowWindow(null, fromEnd: true));
    }

    [Fact]
    public void BuildRowWindow_RangeIsAbsolute_NotACountFromAnEnd()
    {
        // The distinction the grammar exists for: 2..10 names rows, 9 counts them.
        // A window built from the range must not collapse into a count, or a table
        // shorter than 10 rows would silently return a different set of rows.
        Assert.Equal(RowWindow.Range(2, 10), SharedOptions.BuildRowWindow("2..10", fromEnd: false));
        Assert.NotEqual(RowWindow.Head(9), SharedOptions.BuildRowWindow("2..10", fromEnd: false));
    }

    [Fact]
    public void BuildRowWindow_OpenRangeHasNoEnd()
        => Assert.Equal(RowWindow.Range(10, null), SharedOptions.BuildRowWindow("10..", fromEnd: false));

    [Fact]
    public void BuildRowWindow_StartPlusCountResolvesToItsInclusiveEnd()
        => Assert.Equal(RowWindow.Range(2, 11), SharedOptions.BuildRowWindow("2+10", fromEnd: false));

    [Fact]
    public void BuildRowWindow_RejectsADirectionOnARange()
    {
        var ex = Assert.Throws<RowWindowValidationException>(
            () => SharedOptions.BuildRowWindow("2..10", fromEnd: true));
        Assert.Contains("already names which rows to keep", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRowWindow_RejectsAMalformedSpec()
    {
        var ex = Assert.Throws<RowWindowValidationException>(
            () => SharedOptions.BuildRowWindow("2:10", fromEnd: false));
        Assert.Contains("':'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LimitMarkdownTableRows_KeepsInteriorSeparatorInPlace()
    {
        // A pathological table with an interior separator: the separator must stay in
        // its original position rather than being hoisted after the windowed rows.
        var markdown = "| Name |\n| ---- |\n| A |\n| ---- |\n| B |\n| C |\n";

        var output = MarkdownTableRowLimiter.Apply(markdown, RowWindow.Head(2)).ReplaceLineEndings("\n");

        // Head window of 2 data rows keeps A and B; the interior separator sits between them.
        Assert.Equal("| Name |\n| ---- |\n| A |\n| ---- |\n| B |\n", output);
    }
}
