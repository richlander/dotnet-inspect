using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using InertText;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class FindProgressiveTsvTests
{
    private static readonly string JsonLibrary = typeof(JsonSerializer).Assembly.Location;

    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] args) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });

    [Fact]
    public async Task DefaultAndExplicitTsvHaveIdenticalUnifiedRows()
    {
        var defaultResult = await Run("find", "JsonSerializer*", "--library", JsonLibrary);
        var explicitResult = await Run("find", "JsonSerializer*", "--library", JsonLibrary, "--tsv");
        Assert.Equal(0, defaultResult.ExitCode);
        Assert.Equal(defaultResult.Output, explicitResult.Output);
        string[] lines = defaultResult.Output.TrimEnd('\n').Split('\n');
        Assert.Equal(string.Join('\t', FindDiscoveryTsvWriter.Columns).ToLowerInvariant(), lines[0]);
        Assert.Contains("System.Text.Json.JsonSerializer\ttype\t", defaultResult.Output);
        Assert.All(lines, line => Assert.Equal(9, line.Split('\t').Length));
        Assert.Single(lines, line => line.StartsWith("coordinate\t", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImplicitMembersAreNotOmittedFromDefaultTsv()
    {
        var result = await Run("find", "Serialize", "--library", JsonLibrary);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("System.Text.Json.JsonSerializer.Serialize\tmember\t", result.Output);
        Assert.DoesNotContain("appear only", result.Error);
        Assert.DoesNotContain("## Members", result.Output);
    }

    [Fact]
    public async Task ProgressiveMemberRowsAreNotWrittenTwice()
    {
        var result = await Run(
            "find", ".Serialize", "--library", JsonLibrary, "--tsv");

        Assert.Equal(0, result.ExitCode);
        string[] lines = result.Output.TrimEnd('\n').Split('\n');
        string[] rows = lines.Skip(1).ToArray();
        Assert.NotEmpty(rows);
        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task JsonlPublishesBroadenedMemberFindings()
    {
        var result = await Run(
            "find", ".Serialize", "--library", JsonLibrary, "--jsonl");

        Assert.Equal(0, result.ExitCode);
        var rows = new List<JsonElement>();
        foreach (string line in result.Output.TrimEnd('\n').Split('\n'))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            rows.Add(document.RootElement.Clone());
        }
        Assert.Contains(
            rows,
            row => row.GetProperty("kind").GetString() == "member");
        Assert.DoesNotContain("appear only", result.Error);
    }

    [Fact]
    public async Task ProgressiveProjectionKeepsRequestedColumns()
    {
        var result = await Run(
            "find", "JsonSerializer*", "--library", JsonLibrary,
            "--tsv", "--columns", "Coordinate,Kind");

        Assert.Equal(0, result.ExitCode);
        string[] lines = result.Output.TrimEnd('\n').Split('\n');
        Assert.Equal("coordinate\tkind", lines[0]);
        Assert.Contains(lines.Skip(1), line =>
            line.StartsWith("System.Text.Json.JsonSerializer\ttype", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.Equal(2, line.Split('\t').Length));
    }

    [Fact]
    public void WriterRejectsDuplicateProjectionNames()
    {
        using var output = new StringWriter();

        var exception = Assert.Throws<ArgumentException>(() =>
            new FindDiscoveryTsvWriter(
                output, showHeader: true, ["Coordinate", "coordinate"], fields: null));

        Assert.Equal("Duplicate column name: coordinate", exception.Message);
    }

    [Fact]
    public async Task SelectionKeepsEstablishedBufferedPathAndProjection()
    {
        var result = await Run("find", ".Serialize", "--library", JsonLibrary,
            "-n", "2", "--columns", "Coordinate,Kind", "--no-header");
        Assert.Equal(0, result.ExitCode);
        string[] rows = result.Output.TrimEnd().Split('\n');
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal(2, row.Split('\t').Length);
            Assert.EndsWith("\tmember", row);
        });
    }

    [Theory]
    [InlineData("--tsv", 2)]
    [InlineData("--jsonl", 1)]
    public async Task EcosystemSelectionPublishesOnlyTheSelectedWindow(
        string format,
        int expectedLines)
    {
        var result = await Run(
            "find", "System.*",
            "--ecosystem", "runtime",
            "--rows", "2..2",
            format);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            expectedLines,
            result.Output.TrimEnd('\n').Split('\n').Length);
    }

    [Theory]
    [InlineData("--markdown", "## Results")]
    [InlineData("--json", "\"type\"")]
    public async Task ExplicitFormatsRetainTheirExistingShape(string format, string expected)
    {
        var result = await Run("find", "JsonSerializer*", "--library", JsonLibrary, format);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(expected, result.Output);
        Assert.DoesNotContain("coordinate\t", result.Output);
    }

    [Fact]
    public void WriterPublishesFirstRowThenFlushesPartialBatchWithStableHeaderAndSafeCells()
    {
        using var output = new StringWriter();
        var writer = new FindDiscoveryTsvWriter(output);
        var type = new TypeFindResult
        {
            FullName = "System.Text.Json.JsonSerializer",
            Kind = "class",
            Pattern = "JsonSerializer*",
            Source = "runtime",
            Library = "System.Text.Json",
        };
        writer.Write(FindDiscoveryOutput.Project(type));
        string first = output.ToString();
        Assert.Contains(type.FullName, first);
        writer.Write(new FindDiscoveryRow(
            Field("System.Text.Json.JsonSerializer.Serialize"),
            Field("member"), Field("runtime"), Field("System.Text.Json"),
            Field("tab\tnewline\n"), Field("method"), Field(""), Field("direct"), Field("")));
        Assert.Equal(first, output.ToString());
        writer.Flush();
        string[] flushedLines = output.ToString().TrimEnd('\n').Split('\n');
        Assert.Equal(3, flushedLines.Length);
        Assert.Equal(first, string.Join('\n', flushedLines.Take(2)) + "\n");
        Assert.Contains(@"tab\^Inewline\^J", flushedLines[2]);
        writer.Write(FindDiscoveryOutput.Project(type));
        Assert.Equal(3, output.ToString().TrimEnd('\n').Split('\n').Length);
        writer.Dispose();
        string[] lines = output.ToString().TrimEnd('\n').Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.All(lines, line => Assert.Equal(9, line.Split('\t').Length));
    }

    [Fact]
    public void WriterPublishesFullBatchWithoutWaitingForCompletion()
    {
        using var output = new StringWriter();
        using var writer = new FindDiscoveryTsvWriter(output);
        FindDiscoveryRow row = Row("System.String");
        writer.Write(row);
        string first = output.ToString();
        for (int index = 1; index < 64; index++)
            writer.Write(Row($"System.String.Member{index}"));
        Assert.Equal(first, output.ToString());

        writer.Write(Row("System.String.Member64"));

        string[] lines = output.ToString().TrimEnd('\n').Split('\n');
        Assert.Equal(66, lines.Length);
        Assert.Single(lines, line =>
            line.StartsWith("coordinate\t", StringComparison.Ordinal));
        Assert.Equal("System.String.Member64", lines[^1].Split('\t')[0]);
    }

    [Fact]
    public void JsonlWriterPublishesEachSettledRowWithoutAHeader()
    {
        using var output = new StringWriter();
        using var writer = new FindDiscoveryTsvWriter(
            output,
            showHeader: true,
            projection: null,
            jsonl: true);

        writer.Write(Row("System.String"));
        string first = output.ToString();
        Assert.Single(first.TrimEnd('\n').Split('\n'));
        using (JsonDocument document = JsonDocument.Parse(first))
        {
            Assert.Equal(
                "System.String",
                document.RootElement.GetProperty("coordinate").GetString());
        }

        writer.Write(Row("System.String.Concat"));
        writer.Flush();
        string[] lines = output.ToString().TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => JsonDocument.Parse(line).Dispose());
    }

    [Fact]
    public void EcosystemPrefixDemandsFollowEveryBoundedLineageLayer()
    {
        var layers = FindCommand.GetNamedLayers(
            [EcosystemPackIds.Aspire]);

        var demands = FindCommand.CreateEcosystemRequest(
            new FindOptions(),
            ["AddProject"],
            layers,
            demandPrefixes: true).Prefixes;

        Assert.Equal(
            [
                "Aspire.",
                "Microsoft.AspNetCore.",
                "Microsoft.Extensions.",
                "System.",
            ],
            demands.Select(demand => demand.Prefix));
        var blocking = FindCommand.CreateEcosystemRequest(
            new FindOptions(), ["AddProject"], layers, demandPrefixes: false);
        Assert.Empty(blocking.PrefixDemand);
        Assert.Empty(blocking.Prefixes);
    }

    [Fact]
    public void EcosystemRequestKeepsQuestionAndBoundAcrossHostDemandPolicy()
    {
        var layers = FindCommand.GetNamedLayers([EcosystemPackIds.Aspire]);
        var options = new FindOptions { Limit = 50, Members = true };
        var streaming = FindCommand.CreateEcosystemRequest(
            options, [".Add*"], layers, demandPrefixes: true);
        var blocking = FindCommand.CreateEcosystemRequest(
            options, [".Add*"], layers, demandPrefixes: false);

        Assert.Equal(50, streaming.MaximumRows);
        Assert.Equal(500, streaming.MaximumPrefixPackages);
        Assert.NotEmpty(streaming.Prefixes);
        Assert.Empty(blocking.Prefixes);
        Assert.Equal("Add*",
            Assert.Single(Assert.IsType<MemberFindQuestion>(
                streaming.Question).Patterns).Text);
        Assert.NotEqual(streaming.Identity, blocking.Identity);
    }

    [Fact]
    public async Task MemberRowsArePublishedBeforeServiceCompletion()
    {
        bool completed = false;
        List<MemberFindResult> observed = [];
        using var http = new HttpClient();
        var options = new FindOptions
        {
            Assemblies = [JsonLibrary],
            Members = true,
            OnMemberRow = row =>
            {
                Assert.False(completed);
                observed.Add(row);
            },
        };
        var result = await MemberSearchService.FindMembersAsync(
            options, ["Serialize"], new VerboseLogger(false), http,
            TestContext.Current.CancellationToken);
        completed = true;
        Assert.NotEmpty(observed);
        Assert.Equal(result.Rows, observed);
    }

    [Fact]
    public async Task WildcardTypeRowsArePublishedBeforeServiceCompletion()
    {
        bool completed = false;
        List<TypeFindResult> observed = [];
        using var http = new HttpClient();
        var result = await TypeSearchService.FindTypesAsync(
            new FindOptions
            {
                Assemblies = [JsonLibrary],
                OnTypeRow = row =>
                {
                    Assert.False(completed);
                    observed.Add(row);
                },
            },
            ["JsonSerializer*"], new VerboseLogger(false), http,
            TestContext.Current.CancellationToken);
        completed = true;
        Assert.NotEmpty(observed);
        Assert.Equal(result.Rows, observed);
    }

    [Fact]
    public async Task CancellationAfterFirstSourcePreservesPrintedRowsAndAvoidsNextSource()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var output = new StringWriter();
        using var writer = new FindDiscoveryTsvWriter(output);
        using var http = new HttpClient();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MemberSearchService.FindMembersAsync(
                new FindOptions
                {
                    Members = true,
                    Assemblies = [JsonLibrary, typeof(object).Assembly.Location],
                    OnMemberRow = row =>
                    {
                        writer.Write(FindDiscoveryOutput.Project(row));
                        cancellation.Cancel();
                    },
                },
                ["*"], new VerboseLogger(false), http, cancellation.Token));
        Assert.Contains("System.Text.Json.", output.ToString());
        Assert.DoesNotContain("\tSystem.Private.CoreLib\t", output.ToString());
    }

    [Fact]
    public async Task StrictWindowFailureDoesNotPublishUnselectedRows()
    {
        var result = await Run("find", ".Serialize", "--library", JsonLibrary,
            "--rows", "1000000..1000001");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("row selection", result.Error);
    }

    private static InertString Field(string value) => new(TextPolicy.Field, value);

    private static FindDiscoveryRow Row(string coordinate) =>
        new(
            Field(coordinate), Field("member"), Field("runtime"),
            Field("System.Private.CoreLib"), Field("*"), Field("method"),
            Field(""), Field("direct"), Field(""));
}
