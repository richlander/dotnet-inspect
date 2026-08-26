using System.Text.Json;

namespace DotnetInspector.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task ItemLimitsUseDeclaredRowsAcrossFormats()
    {
        string[] common =
        [
            "library",
            TestAssemblyPath,
            "-S",
            "Performance: Boxing",
            "--where",
            "Member=BoxInt(int)",
            "--columns",
            "Member;Confidence",
            "-n",
            "1",
            "--tips",
            "q",
        ];

        var natural = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance: Boxing",
            "--columns", "Member;Confidence",
            "-n", "1",
            "--jsonl",
            "--tips", "q");
        var markdown = await RunAppAsync(common);
        var tsv = await RunAppAsync([.. common, "--tsv"]);
        var jsonl = await RunAppAsync([.. common, "--jsonl"]);
        var multiSection = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Library Info,Performance: Boxing",
            "--where", "Member=BoxInt(int)",
            "-n", "1",
            "--tips", "q");

        Assert.All(
            new[] { natural, markdown, tsv, jsonl, multiSection },
            result =>
            {
                Assert.Equal(0, result.Exit);
                if (!string.IsNullOrEmpty(result.Error))
                    AssertOnlyPerformanceAnalysisWarnings(result.Error);
            });

        var naturalRow = JsonDocument.Parse(Assert.Single(SplitOutputLines(natural.Output))).RootElement;
        var markdownRow = FirstMarkdownTableRow(markdown.Output);
        var tsvRow = Assert.Single(SplitOutputLines(tsv.Output).Skip(1));
        var jsonlRow = JsonDocument.Parse(Assert.Single(SplitOutputLines(jsonl.Output))).RootElement;

        string naturalMember = naturalRow.GetProperty("member").GetString()!;
        string selectedMember = jsonlRow.GetProperty("member").GetString()!;
        Assert.NotEqual(naturalMember, selectedMember);

        Assert.Equal(selectedMember, markdownRow[0]);
        Assert.Equal(jsonlRow.GetProperty("confidence").GetString(), markdownRow[1]);

        string[] tsvCells = tsvRow.Split('\t');
        Assert.Equal(selectedMember, tsvCells[0]);
        Assert.Equal(jsonlRow.GetProperty("confidence").GetString(), tsvCells[1]);

        Assert.Contains("## Library Info", multiSection.Output, StringComparison.Ordinal);
        Assert.Contains("## Performance: Boxing", multiSection.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TopRequiresRankingOrder()
    {
        var sequence = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "--top", "1",
            "--tsv",
            "--tips", "q");
        var rankingDefault = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Top Leverage",
            "--top", "1",
            "--tsv",
            "--tips", "q");
        var explicitOrder = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Shape=box-value-type",
            "--order-by", "RootReach desc",
            "--top", "1",
            "--json",
            "--tips", "q");

        Assert.Equal(1, sequence.Exit);
        Assert.Empty(sequence.Output);
        Assert.Contains(
            "Use --top N with --order-by, or use -n N for a positional limit.",
            sequence.Error,
            StringComparison.Ordinal);

        Assert.Equal(0, rankingDefault.Exit);
        Assert.Single(SplitOutputLines(rankingDefault.Output).Skip(1));
        Assert.Equal(0, explicitOrder.Exit);
        Assert.NotEmpty(PerformanceRows(explicitOrder.Output));

        var root = CommandLineBuilder.CreateRootCommand();
        Assert.Contains(
            root.Parse(["library", TestAssemblyPath, "-S", "Top Leverage", "--top", "0"]).Errors,
            error => error.Message.Contains("--top must be a positive integer.", StringComparison.Ordinal));
        Assert.Contains(
            root.Parse(["library", TestAssemblyPath, "-S", "Top Leverage", "--top", "-1"]).Errors,
            error => error.Message.Contains("--top must be a positive integer.", StringComparison.Ordinal)
                     || error.Message.Contains("Cannot parse value", StringComparison.Ordinal));
        Assert.Contains(
            root.Parse(["library", TestAssemblyPath, "-S", "Top Leverage", "--top", "1", "--top", "2"]).Errors,
            error => error.Message.Contains("Specify --top only once.", StringComparison.Ordinal));
        Assert.Contains(
            root.Parse(["library", TestAssemblyPath, "-S", "Top Leverage", "--top", "999999999999999999999"]).Errors,
            error => error.Message.Contains("Cannot parse argument", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OrdinaryLineWindowsApplyAfterRendering()
    {
        var full = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "--table",
            "--rows", "1..3",
            "--tips", "q");
        var head = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "--table",
            "--rows", "1..3",
            "-n", "2",
            "--lines",
            "--tips", "q");
        var tail = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "--table",
            "--rows", "1..3",
            "-n", "1",
            "--tail-lines",
            "--tips", "q");
        var rankedFull = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Top Leverage",
            "--table",
            "--top", "2",
            "--tips", "q");
        var rankedHead = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Top Leverage",
            "--table",
            "--top", "2",
            "-n", "2",
            "--lines",
            "--tips", "q");

        Assert.All(
            new[] { full, head, tail, rankedFull, rankedHead },
            result =>
            {
                Assert.Equal(0, result.Exit);
                Assert.Empty(result.Error);
            });

        Assert.Equal(TakeRenderedLines(full.Output, 2, tail: false), head.Output);
        Assert.Equal(TakeRenderedLines(full.Output, 1, tail: true), tail.Output);
        Assert.Equal(TakeRenderedLines(rankedFull.Output, 2, tail: false), rankedHead.Output);
    }

    [Fact]
    public async Task NonPrintJsonRejectsLineWindows()
    {
        var rejected = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "--json",
            "-n", "2",
            "--lines",
            "--tips", "q");

        Assert.Equal(1, rejected.Exit);
        Assert.Empty(rejected.Output);
        Assert.Contains(
            "Document --json cannot be combined with --lines.",
            rejected.Error,
            StringComparison.Ordinal);

        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.PrintJsonLines",
            "README.md",
            "alpha\nbeta\ngamma\n");
        try
        {
            var printed = await RunAppAsync(
                "package", packagePath,
                "-S", "Package README file",
                "--print",
                "--json",
                "-n", "2",
                "--lines",
                "--tips", "q");

            Assert.Equal(0, printed.Exit);
            Assert.Empty(printed.Error);
            using var document = JsonDocument.Parse(printed.Output);
            Assert.Equal(
                "alpha\nbeta\n",
                document.RootElement.GetProperty("content").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string[] FirstMarkdownTableRow(string output) =>
        SplitOutputLines(output)
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(ParsePipeCells)
            .First();

    private static string[] ParsePipeCells(string line) =>
        line
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(cell => System.Net.WebUtility.HtmlDecode(cell).Trim('`'))
            .ToArray();

    private static string TakeRenderedLines(string text, int count, bool tail)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1];

        IEnumerable<string> selected = tail
            ? lines.Skip(Math.Max(0, lines.Length - count))
            : lines.Take(count);

        return string.Join('\n', selected) + (selected.Any() ? "\n" : string.Empty);
    }
}
