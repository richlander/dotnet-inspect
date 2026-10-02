using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Presentation;
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
    public void WriterPublishesEachRowBeforeCompletionWithStableHeaderAndSafeCells()
    {
        using var output = new StringWriter();
        using var writer = new FindDiscoveryTsvWriter(output);
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
        writer.Write(FindDiscoveryOutput.Project(type));
        string[] lines = output.ToString().TrimEnd('\n').Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.Equal(first, string.Join('\n', lines.Take(2)) + "\n");
        Assert.Contains(@"tab\^Inewline\^J", lines[2]);
        Assert.All(lines, line => Assert.Equal(9, line.Split('\t').Length));
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
}
