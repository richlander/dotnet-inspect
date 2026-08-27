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

    // Plain document `--json` (not `--jsonl`) previously bypassed -n/-N entirely for
    // `type`/`member`: WriteJsonTypeOutput serialized every filtered member regardless of
    // options.Rows. Closes the coverage gap: ItemLimitsUseDeclaredRowsAcrossFormats above never
    // exercised plain --json, only --jsonl/--tsv/markdown/multi-section.
    [Fact]
    public async Task DocumentJsonAppliesItemWindowForTypeMembers()
    {
        var unwindowed = await RunAppAsync(
            "type", "DotnetInspector.Tests.CacheCommandTests",
            "--library", TestAssemblyPath,
            "-S", "Methods",
            "--json",
            "--tips", "q");
        var windowed = await RunAppAsync(
            "type", "DotnetInspector.Tests.CacheCommandTests",
            "--library", TestAssemblyPath,
            "-S", "Methods",
            "-n", "1",
            "--json",
            "--tips", "q");
        var markdown = await RunAppAsync(
            "type", "DotnetInspector.Tests.CacheCommandTests",
            "--library", TestAssemblyPath,
            "-S", "Methods",
            "-n", "1",
            "--tips", "q");

        Assert.Equal(0, unwindowed.Exit);
        Assert.Equal(0, windowed.Exit);
        Assert.Equal(0, markdown.Exit);

        var unwindowedMembers = JsonDocument.Parse(unwindowed.Output).RootElement.GetProperty("members");
        var windowedMembers = JsonDocument.Parse(windowed.Output).RootElement.GetProperty("members");

        Assert.True(unwindowedMembers.GetArrayLength() > 1, "fixture needs 2+ methods to prove windowing");
        Assert.Equal(1, windowedMembers.GetArrayLength());

        string windowedName = windowedMembers[0].GetProperty("name").GetString()!;
        string? markdownFirstMember = SplitOutputLines(markdown.Output)
            .SkipWhile(line => !line.StartsWith("| ----", StringComparison.Ordinal))
            .Skip(1)
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries)[1])
            .FirstOrDefault();
        Assert.Equal(markdownFirstMember, windowedName);
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
            "--table",
            "--tips", "q");
        var rankingWildcard = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Top Lever*",
            "--top", "1",
            "--table",
            "--tips", "q");
        var explicitOrder = await RunAppAsync(
            "library", "System.Text.Json",
            "-S", "Performance: Boxing",
            "--order-by", "RootReach desc",
            "--top", "1",
            "--json",
            "--tips", "q");
        var explicitOrderMarkdown = await RunAppAsync(
            "library", "System.Text.Json",
            "-S", "Performance: Boxing",
            "--order-by", "RootReach desc",
            "--top", "1",
            "--markdown",
            "--tips", "q");
        var head = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Top Leverage",
            "-n", "1",
            "--table",
            "--tips", "q");
        var tail = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Top Leverage",
            "-n", "1",
            "--tail",
            "--table",
            "--tips", "q");
        var tsv = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Top Leverage",
            "--top", "1",
            "--tsv",
            "--tips", "q");
        var jsonl = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Top Leverage",
            "--top", "1",
            "--jsonl",
            "--tips", "q");

        Assert.Equal(1, sequence.Exit);
        Assert.Empty(sequence.Output);
        Assert.Contains(
            "Use --top N with --order-by, or use -n N for a positional limit.",
            sequence.Error,
            StringComparison.Ordinal);

        Assert.Equal(0, rankingDefault.Exit);
        Assert.Contains(
            "top 1 by Callers desc, RootReach desc, Fanout desc, LoopCalls desc",
            rankingDefault.Output,
            StringComparison.Ordinal);
        Assert.Equal(0, rankingWildcard.Exit);
        Assert.Contains(
            "top 1 by Callers desc, RootReach desc, Fanout desc, LoopCalls desc",
            rankingWildcard.Output,
            StringComparison.Ordinal);
        Assert.Equal(0, explicitOrder.Exit);
        Assert.NotEmpty(PerformanceRows(explicitOrder.Output));
        Assert.DoesNotContain("top 1 by", explicitOrder.Output, StringComparison.Ordinal);
        Assert.Equal(0, explicitOrderMarkdown.Exit);
        Assert.Contains("top 1 by RootReach desc", explicitOrderMarkdown.Output, StringComparison.Ordinal);
        Assert.Equal(0, head.Exit);
        Assert.Contains("first 1", head.Output, StringComparison.Ordinal);
        Assert.Equal(0, tail.Exit);
        Assert.Contains("last 1", tail.Output, StringComparison.Ordinal);
        Assert.Equal(0, tsv.Exit);
        Assert.DoesNotContain("top 1 by", tsv.Output, StringComparison.Ordinal);
        Assert.Equal(0, jsonl.Exit);
        Assert.DoesNotContain("top 1 by", jsonl.Output, StringComparison.Ordinal);

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
    public async Task HumanRowWindowNotes_AreSuppressedForPrint()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.WindowNotes.Print",
            "README.md",
            "readme",
            null,
            null,
            ("skills/alpha/SKILL.md", "# Alpha skill"),
            ("skills/beta/SKILL.md", "# Beta skill"));
        try
        {
            var markdown = await RunAppAsync(
                "package", packagePath,
                "-S", "Package skill files",
                "-n", "1",
                "--tips", "q");
            var printed = await RunAppAsync(
                "package", packagePath,
                "-S", "Package skill files",
                "--print",
                "--row", "1",
                "--bare",
                "--tips", "q");

            Assert.Equal(0, markdown.Exit);
            Assert.Contains("first 1", markdown.Output, StringComparison.Ordinal);
            Assert.Equal(0, printed.Exit);
            Assert.Equal("# Alpha skill", printed.Output);
            Assert.DoesNotContain("first 1", printed.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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
