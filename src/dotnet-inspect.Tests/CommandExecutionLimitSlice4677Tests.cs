using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Output;

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
    public async Task DocumentJsonAppliesItemWindowIndependentlyToTypeRowSets()
    {
        var windowed = await RunAppAsync(
            "type", typeof(ItemWindowTypeFixture).FullName!,
            "--library", TestAssemblyPath,
            "-S", "Interfaces,Methods",
            "-n", "1",
            "--json",
            "--tips", "q");
        var unsupported = await RunAppAsync(
            "type", typeof(ItemWindowTypeFixture).FullName!,
            "--library", TestAssemblyPath,
            "-S", "Methods,Properties",
            "-n", "1",
            "--json",
            "--tips", "q");

        Assert.Equal(0, windowed.Exit);
        Assert.Empty(windowed.Error);
        using var document = JsonDocument.Parse(windowed.Output);
        Assert.Equal(
            1,
            document.RootElement.GetProperty("interfaces").GetArrayLength());
        Assert.Equal(
            1,
            document.RootElement.GetProperty("members").GetArrayLength());

        Assert.Equal(1, unsupported.Exit);
        Assert.Empty(unsupported.Output);
        Assert.Contains(
            "cannot apply an item window independently to multiple member sections",
            unsupported.Error,
            StringComparison.Ordinal);
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
        var inapplicableOrder = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "--order-by", "RootReach desc",
            "--top", "1",
            "--table",
            "--tips", "q");
        var inapplicableOrderOnly = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "--order-by", "RootReach desc",
            "--table",
            "--tips", "q");
        var legacyPerformanceAlias = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--top", "1",
            "--json",
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
            "Section 'References' does not support --top. Use -n N for a positional limit.",
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
        Assert.All(
            new[] { inapplicableOrder, inapplicableOrderOnly },
            result =>
            {
                Assert.Equal(1, result.Exit);
                Assert.Empty(result.Output);
                Assert.Contains(
                    "Section 'References' has no ranking order, so --top/--order-by do not apply.",
                    result.Error,
                    StringComparison.Ordinal);
            });
        Assert.Equal(1, legacyPerformanceAlias.Exit);
        Assert.Empty(legacyPerformanceAlias.Output);
        Assert.Contains(
            "Section 'Performance Triage' does not support --top.",
            legacyPerformanceAlias.Error,
            StringComparison.Ordinal);
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
    public async Task EnvironmentJsonRejectsRenderedLineWindows()
    {
        string? originalFormat =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                "json");
            var result = await RunAppAsync(
                "library", TestAssemblyPath,
                "-S", "References",
                "-n", "2",
                "--lines",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "Document --json cannot be combined with --lines.",
                result.Error,
                StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                originalFormat);
        }
    }

    [Fact]
    public async Task BarePrintSuppressesEnvironmentJsonlBeforeLineWindowing()
    {
        string? originalFormat =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        var (packagePath, tempDirectory) = CreateLocalReadmePackage(
            "Test.Bare.Print.Lines",
            "README.md",
            "readme",
            null,
            null,
            ("skills/alpha/SKILL.md", "# Alpha skill\nsecond\nthird"));
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                "jsonl");
            var result = await RunAppAsync(
                "package", packagePath,
                "-S", "Package skill files",
                "--print",
                "--row", "1",
                "--bare",
                "-n", "1",
                "--lines",
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Equal("# Alpha skill\n", result.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                originalFormat);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SeparatedFalseBooleanDoesNotBecomeImplicitTarget()
    {
        var explicitCommand = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "--json", "false",
            "--tips", "q");
        var implicitCommand = await RunAppAsync(
            "--json", "false",
            TestAssemblyPath,
            "-S", "References",
            "--tips", "q");

        Assert.Equal(0, explicitCommand.Exit);
        Assert.Equal(0, implicitCommand.Exit);
        Assert.Empty(explicitCommand.Error);
        Assert.Empty(implicitCommand.Error);
        Assert.Equal(explicitCommand.Output, implicitCommand.Output);
    }

    [Fact]
    public void FileOutputComposesAbsoluteRowsAndRenderedLines()
    {
        var tempDirectory =
            Directory.CreateTempSubdirectory("item-line-output-");
        try
        {
            string outputPath =
                Path.Combine(tempDirectory.FullName, "rows.txt");
            var root = CommandLineBuilder.CreateRootCommand();
            DotnetInspector.CommandLine.ArgumentPreprocessor.PreprocessArgs(
                ["library", TestAssemblyPath, "-n", "2", "--lines"],
                root);

            OutputDestination.Write(
                outputPath,
                writer =>
                {
                    foreach (string row in RowWindow.Apply(
                                 RowWindow.Range(2, 3),
                                 new[] { "first", "second", "third" }))
                    {
                        writer.WriteLine(row);
                    }
                });

            Assert.Equal(
                "second\nthird\n",
                File.ReadAllText(outputPath));
        }
        finally
        {
            DotnetInspector.CommandLine.ArgumentPreprocessor.Reset();
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AggregateDocumentJsonRejectsUnsupportedItemWindows()
    {
        var (packagePath, tempDirectory) = CreateLocalLibPackage();
        try
        {
            var multiple = await RunAppAsync(
                "package",
                packagePath,
                packagePath,
                "-n", "1",
                "--json",
                "--tips", "q");
            var allLibraries = await RunAppAsync(
                "package",
                packagePath,
                "--all-libraries",
                "-n", "1",
                "--json",
                "--tips", "q");

            Assert.All(
                new[] { multiple, allLibraries },
                result =>
                {
                    Assert.Equal(1, result.Exit);
                    Assert.Empty(result.Output);
                    Assert.Contains(
                        "Document --json item windows are not yet supported",
                        result.Error,
                        StringComparison.Ordinal);
                });
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
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

    public sealed class ItemWindowTypeFixture : IDisposable, ICloneable
    {
        public string Name { get; set; } = "";

        public object Clone() => new ItemWindowTypeFixture { Name = Name };

        public void Dispose()
        {
        }

        public void First()
        {
        }

        public void Second()
        {
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
        var concatenated = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "--table",
            "--rows", "1..3",
            "-n2",
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
            new[] { full, head, concatenated, tail, rankedFull, rankedHead },
            result =>
            {
                Assert.Equal(0, result.Exit);
                Assert.Empty(result.Error);
            });

        Assert.Equal(TakeRenderedLines(full.Output, 2, tail: false), head.Output);
        Assert.Equal(head.Output, concatenated.Output);
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

        var falseJson = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "--json=false",
            "-n", "2",
            "--lines",
            "--tips", "q");
        var plainLineWindow = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "-n", "2",
            "--lines",
            "--tips", "q");
        Assert.Equal(0, falseJson.Exit);
        Assert.Equal(0, plainLineWindow.Exit);
        Assert.Equal(plainLineWindow.Output, falseJson.Output);
        Assert.Empty(falseJson.Error);
        Assert.Empty(plainLineWindow.Error);

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

            var falseJsonl = await RunAppAsync(
                "package", packagePath,
                "-S", "Package README file",
                "--print",
                "--jsonl=false",
                "-n", "2",
                "--lines",
                "--tips", "q");
            Assert.Equal(0, falseJsonl.Exit);
            Assert.Equal("alpha\nbeta\n", falseJsonl.Output);
            Assert.Empty(falseJsonl.Error);

            var unsupportedDocumentJson = await RunAppAsync(
                "package", packagePath,
                "-S", "Files",
                "--json",
                "-n", "1",
                "--tips", "q");
            Assert.Equal(1, unsupportedDocumentJson.Exit);
            Assert.Empty(unsupportedDocumentJson.Output);
            Assert.Contains(
                "Use --jsonl for row-shaped JSON output.",
                unsupportedDocumentJson.Error,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DirectOutputPathsApplyOrRejectItemWindows()
    {
        var libraryJson = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "--json",
            "-n", "1",
            "--tips", "q");
        Assert.Equal(1, libraryJson.Exit);
        Assert.Empty(libraryJson.Output);
        Assert.Contains(
            "Use --jsonl for row-shaped JSON output.",
            libraryJson.Error,
            StringComparison.Ordinal);

        var nameOnly = await RunAppAsync(
            "diff",
            "--library",
            $"{FixtureCatalog.DiffPair.OldAssemblyPath()}..{FixtureCatalog.DiffPair.NewAssemblyPath()}",
            "--name-only",
            "-n", "1",
            "--tips", "q");
        Assert.Equal(0, nameOnly.Exit);
        Assert.Empty(nameOnly.Error);
        Assert.Collection(
            SplitOutputLines(nameOnly.Output),
            line => Assert.Equal("first 1", line),
            line => Assert.StartsWith(
                "DiffFixtureSample.",
                line,
                StringComparison.Ordinal));
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
