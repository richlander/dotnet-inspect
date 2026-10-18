using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
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
            "find", ".Serialize", "--ecosystem", "runtime",
            "-n", "2", "--jsonl");

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
    [InlineData("-n", "2", 2)]
    [InlineData("--rows", "2..2", 1)]
    public async Task EcosystemMemberSelectionUsesBufferedJsonlRows(
        string selection,
        string value,
        int expectedLines)
    {
        var result = await Run(
            "find", ".ToString",
            "--ecosystem", "runtime",
            selection, value,
            "--jsonl");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            expectedLines,
            result.Output.TrimEnd('\n').Split('\n').Length);
        Assert.DoesNotContain("output format is blocking", result.Error);
        Assert.DoesNotContain("Cannot select member rows", result.Error);
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
        Assert.Equal(10, streaming.MaximumPrefixPackages);
        Assert.NotEmpty(streaming.Prefixes);
        Assert.Empty(blocking.Prefixes);
        Assert.Equal("Add*",
            Assert.Single(Assert.IsType<MemberFindQuestion>(
                streaming.Question).Patterns).Text);
        Assert.NotEqual(streaming.Identity, blocking.Identity);
    }

    [Fact]
    public void EcosystemRequestUsesExplicitPrefixCandidateBound()
    {
        var layers = FindCommand.GetNamedLayers([EcosystemPackIds.Aspire]);
        var options = new FindOptions
        {
            MaximumEcosystemPrefixPackages = 7,
        };

        var request = FindCommand.CreateEcosystemRequest(
            options, [".Add*"], layers, demandPrefixes: true);

        Assert.Equal(7, request.MaximumPrefixPackages);
    }

    [Fact]
    public async Task EcosystemPrefixEnumerationUsesSelectedSource()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new PrefixSourceHandler();
        using var client = new HttpClient(handler);
        var context = new CommandContext(verbose: false, client);
        var options = new FindOptions
        {
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };
        var pages = new List<EcosystemFindPrefixPage>();

        await foreach (EcosystemFindPrefixPage page in
            FindCommand.EnumeratePrefixAsync(
                new("Contoso."),
                1,
                options,
                context,
                TestContext.Current.CancellationToken))
        {
            pages.Add(page);
        }

        EcosystemFindCandidate candidate =
            Assert.Single(pages.SelectMany(page => page.Candidates));
        Assert.Equal("Contoso.Private", candidate.PackageId);
        Assert.All(
            handler.Requested,
            uri => Assert.Equal("private.example", uri.Host));
    }

    [Fact]
    public async Task EcosystemPrefixEnumerationHonorsCancellation()
    {
        var handler = new BlockingSourceHandler();
        using var client = new HttpClient(handler);
        var context = new CommandContext(verbose: false, client);
        var options = new FindOptions
        {
            SourceOptions = new NuGetSourceOptions
            {
                Sources = ["https://private.example/v3/index.json"],
            },
        };
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

        Task operation = CollectPrefixPagesAsync(
            options,
            context,
            cancellation.Token);
        await handler.RequestStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation);
    }

    [Fact]
    public async Task EcosystemPrefixEnumerationKeepsHealthySourceCandidates()
    {
        var handler = new MixedPrefixSourceHandler();
        using var client = new HttpClient(handler);
        var context = new CommandContext(verbose: false, client);
        var options = new FindOptions
        {
            SourceOptions = new NuGetSourceOptions
            {
                Sources =
                [
                    "https://failed.example/v3/index.json",
                    "https://healthy.example/v3/index.json",
                ],
            },
        };
        var pages = new List<EcosystemFindPrefixPage>();

        await foreach (EcosystemFindPrefixPage page in
            FindCommand.EnumeratePrefixAsync(
                new("Contoso."),
                1,
                options,
                context,
                TestContext.Current.CancellationToken))
        {
            pages.Add(page);
        }

        EcosystemFindPrefixPage failed = Assert.Single(
            pages,
            page => page.Completion
                == EcosystemFindPrefixPageCompletion.Failed);
        Assert.Contains("failed.example", failed.FailureSource);
        EcosystemFindCandidate candidate = Assert.Single(
            pages.SelectMany(page => page.Candidates));
        Assert.Equal("Contoso.Healthy", candidate.PackageId);
    }

    [Fact]
    public async Task EcosystemPrefixEnumerationYieldsBeforeNextSourcePage()
    {
        var handler = new PagedPrefixSourceHandler();
        using var client = new HttpClient(handler);
        var context = new CommandContext(verbose: false, client);
        var options = new FindOptions
        {
            SourceOptions = new NuGetSourceOptions
            {
                Sources = ["https://paged.example/v3/index.json"],
            },
            Limit = 1,
        };
        var layers =
            FindCommand.GetNamedLayers([EcosystemPackIds.Aspire]);
        EcosystemFindSearchRequest request =
            FindCommand.CreateEcosystemRequest(
                options,
                ["Thing"],
                layers,
                demandPrefixes: true);
        using var session = new EcosystemFindSearchSession<string>(
            request,
            (_, _) => Task.FromResult(new EcosystemFindBlock<string>(
                "bounded", 0)),
            (prefix, take, token) => FindCommand.EnumeratePrefixAsync(
                prefix,
                take,
                options,
                context,
                token),
            (candidate, _, _) => Task.FromResult(
                new EcosystemFindBlock<string>(candidate.PackageId, 1)));

        EcosystemFindSearchSummary<string> result =
            await session.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EcosystemFindCompletion.RowLimitReached,
            result.Completion);
        Assert.Equal(
            "Aspire.First",
            Assert.Single(result.PrefixBlocks).Content);
        Assert.Equal(1, handler.SearchRequests);
    }

    [Fact]
    public void PrefixTypeRowsExcludePackageLocalAndRelativeResults()
    {
        List<TypeFindResult> rows =
        [
            new()
            {
                FullName = "Contoso.Exact",
                Pattern = "Contoso.Exact",
                Match = TypeFindMatchKind.Exact,
            },
            new()
            {
                FullName = "Contoso.Similar",
                Pattern = "Contoso.Exact",
                Match = TypeFindMatchKind.Partial,
            },
            new()
            {
                Pattern = "Missing",
                Match = TypeFindMatchKind.NotFound,
            },
        ];

        List<TypeFindResult> durable =
            FindCommand.DurablePrefixTypeRows(rows, []);

        Assert.Equal(
            ["Contoso.Exact"],
            durable.Select(row => row.FullName));
    }

    [Fact]
    public async Task ZeroRowPrefixFailureIsReportedFromTerminalSummary()
    {
        var capture = await ConsoleCapture.RunAsync(() =>
        {
            FindCommand.ReportEcosystemFailures(
            [
                new(
                    "Prefix",
                    "Contoso.Empty",
                    "Package settlement failed."),
            ]);
            return Task.FromResult(0);
        });

        Assert.Equal(0, capture.ExitCode);
        Assert.Contains(
            "Prefix source \"Contoso.Empty\" failed: "
                + "Package settlement failed.",
            capture.Error);
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

    private static async Task CollectPrefixPagesAsync(
        FindOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        await foreach (EcosystemFindPrefixPage _ in
            FindCommand.EnumeratePrefixAsync(
                new("Contoso."),
                1,
                options,
                context,
                cancellationToken))
        {
        }
    }

    private sealed class PrefixSourceHandler : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requested.Add(uri);
            string content = uri.AbsolutePath switch
            {
                "/v3/index.json" =>
                    """
                    {"resources":[{"@id":"https://private.example/v3/query","@type":"SearchQueryService"}]}
                    """,
                "/v3/query" =>
                    """
                    {"data":[{"id":"Contoso.Private","version":"1.0.0"}]}
                    """,
                _ => throw new InvalidOperationException(
                    $"Unexpected request: {uri}"),
            };
            return Task.FromResult(new HttpResponseMessage(
                System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    content,
                    System.Text.Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private sealed class BlockingSourceHandler : HttpMessageHandler
    {
        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation was ignored.");
        }
    }

    private sealed class MixedPrefixSourceHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            if (uri.Host == "failed.example")
            {
                return Task.FromResult(new HttpResponseMessage(
                    System.Net.HttpStatusCode.ServiceUnavailable));
            }

            string content = uri.AbsolutePath switch
            {
                "/v3/index.json" =>
                    """
                    {"resources":[{"@id":"https://healthy.example/v3/query","@type":"SearchQueryService"}]}
                    """,
                "/v3/query" =>
                    """
                    {"data":[{"id":"Contoso.Healthy","version":"1.0.0"}]}
                    """,
                _ => throw new InvalidOperationException(
                    $"Unexpected request: {uri}"),
            };
            return Task.FromResult(new HttpResponseMessage(
                System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    content,
                    System.Text.Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private sealed class PagedPrefixSourceHandler : HttpMessageHandler
    {
        public int SearchRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            string content;
            if (uri.AbsolutePath == "/v3/index.json")
            {
                content =
                    """
                    {"resources":[{"@id":"https://paged.example/v3/query","@type":"SearchQueryService"}]}
                    """;
            }
            else if (uri.AbsolutePath == "/v3/query")
            {
                SearchRequests++;
                IEnumerable<object> data = Enumerable.Range(0, 20).Select(
                    index => new
                    {
                        id = index == 0
                            ? "Aspire.First"
                            : $"Other.Package.{index}",
                        version = "1.0.0",
                    });
                content = JsonSerializer.Serialize(new { data });
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unexpected request: {uri}");
            }

            return Task.FromResult(new HttpResponseMessage(
                System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    content,
                    System.Text.Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
