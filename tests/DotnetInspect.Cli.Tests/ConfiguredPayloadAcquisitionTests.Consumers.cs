using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;

using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Output;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string RangeType = "DiffFixtureSample.BodyStateSample";

    [Theory]
    [InlineData("1.0.0..3.0.0", "last", "3.0.0", false)]
    [InlineData("3.0.0..1.0.0", "#2", "2.0.0", true)]
    [InlineData("1.0.0..3.0.0", "2.0.0", "2.0.0", false)]
    public async Task ApiRange_LocalFeedSelectsTheRequestedAddress(
        string endpoints, string selector, string expected, bool fileUri)
    {
        const string Id = "range.api.local";
        string source = Path.Combine(_root, "api-range");
        foreach (string version in new[] { "1.0.0", "2.0.0", "3.0.0" })
            WriteApiPackage(source, Id, version);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            throw new InvalidOperationException("Local API range opened an HTTP transport."));

        var result = await RunCommandAsync(
            ["type", RangeType, "--package", $"{Id}@{endpoints}", "--at", selector,
                "--source", fileUri ? new Uri(source).AbsoluteUri : source, "--tips", "q"]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Contains(RangeType, result.Output);
        Assert.Contains($"{Id} {expected}", result.Output);
    }

    [Fact]
    public async Task ApiRange_MissingAddressDoesNotDiscoverOrAcquire()
    {
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            throw new InvalidOperationException("An unaddressed API range opened a transport."));

        var result = await RunCommandAsync(
            ["type", RangeType, "--package", "range.api.noaddress@1.0.0..3.0.0",
                "--source", FirstFeed, "--tips", "q"]);

        Assert.Equal(1, result.Exit);
        Assert.Contains("requires --at", result.Error);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("history")]
    public async Task RangeConsumers_UnreadablePeerFailsBeforePayload(string command)
    {
        const string Id = "range.consumer.partial";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(FirstFeed, Id, ["1.0.0", "3.0.0"],
                _ => throw new InvalidOperationException("Partial discovery reached a payload."),
                requests));
        string missing = Path.Combine(_root, "missing-peer");
        List<string> args = command == "type"
            ? ["type", RangeType]
            : ["diff", "--history", "--type", RangeType, "--finding", "api.type"];
        args.AddRange(["--package", $"{Id}@1.0.0..3.0.0", "--at", "first",
            "--source", FirstFeed, "--source", missing, "--tips", "q"]);

        var result = await RunCommandAsync([.. args]);

        Assert.Equal(1, result.Exit);
        Assert.Contains(
            "discovery",
            result.Error,
            StringComparison.OrdinalIgnoreCase);
        if (command == "type")
            Assert.Contains(missing, result.Error);
        Assert.DoesNotContain(requests, request => request.EndsWith(".nupkg", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("full", 3)]
    [InlineData("checkpoints", 2)]
    [InlineData("adaptive", 2)]
    [InlineData("adaptive-large-budget", 2)]
    [InlineData("survey", 2)]
    [InlineData("all", 3)]
    [InlineData("endpoints", 2)]
    [InlineData("midpoint", 1)]
    public async Task DiffHistoryRange_OneDiscoveryAcquiresOnlyAuthorizedAddresses(string selection, int payloadCount)
    {
        const string Id = "range.diff-history.selection";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(FirstFeed, Id, ["1.0.0", "2.0.0", "3.0.0"],
                version => CreateApiPackage(Id, version), requests));
        List<string> args = ["diff", "--history", "--package", $"{Id}@1.0.0..3.0.0",
            "--type", RangeType, "--finding", "api.type", "--source", FirstFeed, "--tips", "q"];
        if (selection == "checkpoints")
            args.AddRange(["--at", "first", "--at", "last"]);
        else if (selection == "adaptive")
            args.AddRange(["--max-probes", "2"]);
        else if (selection == "adaptive-large-budget")
            args.AddRange([
                "--max-probes",
                "2147483647",
                "--json",
            ]);
        else if (selection == "survey")
            args.AddRange(["--sample-percent", "50"]);
        else if (selection == "all")
            args.AddRange(["--at", "all"]);
        else if (selection is "endpoints" or "midpoint")
            args.AddRange(["--at", selection]);

        var result = await RunCommandAsync([.. args]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal(1, requests.Count(request =>
            request.EndsWith($"/{Id}/index.json", StringComparison.Ordinal)));
        Assert.Equal(payloadCount, requests.Count(request =>
            request.EndsWith(".nupkg", StringComparison.Ordinal)));
        if (selection is "full" or "all")
        {
            Assert.DoesNotContain("Unevaluated", result.Output);
        }
        if (selection == "adaptive")
            Assert.Contains("## Outcome", result.Output);
        if (selection == "adaptive-large-budget")
        {
            using var content = JsonDocument.Parse(result.Output);
            JsonElement history = content.RootElement
                .GetProperty("document")
                .GetProperty("content");
            Assert.Equal(
                int.MaxValue,
                history.GetProperty("evaluation_plan")
                    .GetProperty("maximum_probes")
                    .GetInt32());
            Assert.Equal(
                int.MaxValue,
                history.GetProperty("authorized_probe_count").GetInt32());
            Assert.Equal(
                2,
                history.GetProperty("evaluations").GetArrayLength());
        }
    }

    [Fact]
    public async Task
        DiffHistoryRange_SurveyUsesPercentageAndAbsoluteCap()
    {
        const string Id = "range.diff-history.survey";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(
                FirstFeed,
                Id,
                [
                    "1.0.0",
                    "2.0.0",
                    "3.0.0",
                    "4.0.0",
                    "5.0.0",
                    "6.0.0",
                    "7.0.0",
                    "8.0.0",
                ],
                version => CreateApiPackage(Id, version),
                requests));

        var result = await RunCommandAsync(
            [
                "diff",
                "--history",
                "--package", $"{Id}@1.0.0..8.0.0",
                "--type", RangeType,
                "--finding", "api.type",
                "--source", FirstFeed,
                "--sample-percent", "50",
                "--max-probes", "3",
                "--json",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal(
            3,
            requests.Count(static request =>
                request.EndsWith(".nupkg", StringComparison.Ordinal)));
        Assert.Contains(
            requests,
            static request => request.Contains(
                "/1.0.0/",
                StringComparison.Ordinal));
        Assert.Contains(
            requests,
            static request => request.Contains(
                "/8.0.0/",
                StringComparison.Ordinal));
        Assert.Contains(
            requests,
            static request => request.Contains(
                "/4.0.0/",
                StringComparison.Ordinal));

        using var content = JsonDocument.Parse(result.Output);
        JsonElement history = content.RootElement
            .GetProperty("document")
            .GetProperty("content");
        JsonElement plan = history.GetProperty("evaluation_plan");
        Assert.Equal(
            "representativeSurvey",
            plan.GetProperty("plan").GetString());
        Assert.Equal(50, plan.GetProperty("sample_percent").GetInt32());
        Assert.Equal(3, plan.GetProperty("maximum_probes").GetInt32());
        Assert.Equal(3, history.GetProperty("authorized_probe_count").GetInt32());
        Assert.Equal(3, history.GetProperty("evaluations").GetArrayLength());
        Assert.All(
            history.GetProperty("probes").EnumerateArray(),
            probe => Assert.Equal(
                "RepresentativeSample",
                probe.GetProperty("purpose").GetString()));
        Assert.Equal(
            "representativeSurveyCompleted",
            history.GetProperty("terminal_outcome")
                .GetProperty("outcome")
                .GetString());
    }

    [Fact]
    public async Task
        DiffHistoryRange_MajorVersionsUsesFirstStableApiRepresentatives()
    {
        const string Id = "range.diff-history.major-api";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(
                FirstFeed,
                Id,
                [
                    "1.0.0",
                    "1.0.1",
                    "2.0.0-preview.1",
                    "2.0.0",
                    "2.0.1",
                    "3.0.0-preview.1",
                    "3.0.0-preview.2",
                ],
                version => CreateApiPackage(Id, version),
                requests));

        var result = await RunCommandAsync(
            [
                "diff",
                "--history",
                "--major-versions",
                "--preview",
                "--package", $"{Id}@1.0.0..3.0.0",
                "--type", RangeType,
                "--finding", "api.type",
                "--source", FirstFeed,
                "--json",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal(
            3,
            requests.Count(static request =>
                request.EndsWith(".nupkg", StringComparison.Ordinal)));
        Assert.Contains(
            requests,
            static request => request.Contains(
                "/1.0.0/",
                StringComparison.Ordinal));
        Assert.Contains(
            requests,
            static request => request.Contains(
                "/2.0.0/",
                StringComparison.Ordinal));
        Assert.Contains(
            requests,
            static request => request.Contains(
                "/3.0.0-preview.2/",
                StringComparison.Ordinal));

        using var content = JsonDocument.Parse(result.Output);
        JsonElement history = content.RootElement
            .GetProperty("document")
            .GetProperty("content");
        JsonElement plan = history.GetProperty("evaluation_plan");
        Assert.Equal(
            "majorVersionRepresentatives",
            plan.GetProperty("plan").GetString());
        Assert.Equal(
            "FirstStable",
            plan.GetProperty("representative_policy").GetString());
        Assert.Equal(3, history.GetProperty("evaluations").GetArrayLength());
        Assert.All(
            history.GetProperty("probes").EnumerateArray(),
            probe => Assert.Equal(
                "MajorVersionRepresentative",
                probe.GetProperty("purpose").GetString()));
        Assert.Equal(
            "majorVersionRepresentativesCompleted",
            history.GetProperty("terminal_outcome")
                .GetProperty("outcome")
                .GetString());
    }

    [Fact]
    public async Task
        DiffHistoryRange_MajorVersionsUsesLatestAnalysisRepresentatives()
    {
        const string Id = "range.diff-history.major-analysis";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(
                FirstFeed,
                Id,
                ["1.0.0", "1.0.1", "2.0.0", "2.0.1"],
                version => CreateApiPackage(Id, version),
                requests));

        var result = await RunCommandAsync(
            [
                "diff",
                "--history",
                "--major-versions",
                "--package", $"{Id}@1.0.0..2.0.1",
                "--type", RangeType,
                "--member", "BodyState",
                "--finding", "analysis.unsafety",
                "--source", FirstFeed,
                "--json",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.DoesNotContain(
            requests,
            static request => request.Contains(
                "/1.0.0/",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            requests,
            static request => request.Contains(
                "/2.0.0/",
                StringComparison.Ordinal));
        Assert.Contains(
            requests,
            static request => request.Contains(
                "/1.0.1/",
                StringComparison.Ordinal));
        Assert.Contains(
            requests,
            static request => request.Contains(
                "/2.0.1/",
                StringComparison.Ordinal));

        using var content = JsonDocument.Parse(result.Output);
        JsonElement history = content.RootElement
            .GetProperty("document")
            .GetProperty("content");
        Assert.Equal(
            "Latest",
            history.GetProperty("evaluation_plan")
                .GetProperty("representative_policy")
                .GetString());
        Assert.Equal(
            "1.0.1",
            history.GetProperty("evaluations")[0]
                .GetProperty("address")
                .GetProperty("normalized_version")
                .GetString());
    }

    [Theory]
    [InlineData("1.0.0..4.0.0", "2.0.0")]
    [InlineData("4.0.0..1.0.0", "3.0.0")]
    public async Task DiffHistoryRange_EvenMidpointUsesCallerDirectedPosition(
        string range,
        string expected)
    {
        const string Id = "range.diff-history.reverse-midpoint";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(
                FirstFeed,
                Id,
                ["1.0.0", "2.0.0", "3.0.0", "4.0.0"],
                version => CreateApiPackage(Id, version),
                requests));

        var result = await RunCommandAsync(
            [
                "diff",
                "--history",
                "--package", $"{Id}@{range}",
                "--type", RangeType,
                "--finding", "api.type",
                "--source", FirstFeed,
                "--at", "midpoint",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        string request = Assert.Single(
            requests,
            static request => request.EndsWith(
                ".nupkg",
                StringComparison.Ordinal));
        Assert.Contains($"/{expected}/", request);
    }

    [Fact]
    public async Task DiffHistoryRange_BlockedOutcomeKeepsUnresolvedAndBlockingEvidence()
    {
        const string Id = "range.diff-history.blocked-outcome";
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(
                FirstFeed,
                Id,
                [
                    "1.0.0",
                    "2.0.0",
                    "3.0.0",
                    "4.0.0",
                    "5.0.0",
                    "6.0.0",
                    "7.0.0",
                ],
                version => version switch
                {
                    "1.0.0" => CreateApiPackage(
                        Id,
                        version,
                        FixtureCatalog.DiffV1.AssemblyPath()),
                    "2.0.0" => [1, 2, 3],
                    "4.0.0" => CreateApiPackage(
                        Id,
                        version,
                        FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
                    "7.0.0" => CreateApiPackage(
                        Id,
                        version,
                        FixtureCatalog.DiffV2.AssemblyPath()),
                    _ => throw new InvalidOperationException(
                        $"Unexpected History probe at {version}."),
                },
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
            [
                "diff",
                "--history",
                "--package", $"{Id}@1.0.0..7.0.0",
                "--type", RangeType,
                "--finding", "api.type",
                "--source", FirstFeed,
                "--max-probes", "8",
                "--tips", "q",
            ]);

        Assert.Contains("Blocked by failure", result.Output);
        Assert.Contains("4.0.0..7.0.0", result.Output);
        Assert.Contains("#2 (2.0.0)", result.Output);
        Assert.Contains("Blocked", result.Output);
    }

    [Fact]
    public async Task DiffHistoryRange_JsonAndEnvelopeRetainTheSameCompleteContent()
    {
        const string Id = "range.diff-history.envelope";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(
                FirstFeed,
                Id,
                ["1.0.0", "2.0.0"],
                version => CreateApiPackage(Id, version),
                requests));
        string[] common =
        [
            "diff",
            "--history",
            "--package",
            $"{Id}@1.0.0..2.0.0",
            "--type",
            RangeType,
            "--finding",
            "api.type",
            "--source",
            FirstFeed,
            "--at",
            "all",
            "--tips",
            "q",
        ];

        // Warm the durable authority store first, so both compared runs
        // report the same acquisition origin.
        var warm = await RunCommandAsync([.. common, "--json"]);
        Assert.True(warm.Exit == 0, warm.Error);
        var contentResult = await RunCommandAsync([.. common, "--json"]);
        var envelopeResult = await RunCommandAsync(
            [.. common, "--envelope"]);

        Assert.True(contentResult.Exit == 0, contentResult.Error);
        Assert.True(envelopeResult.Exit == 0, envelopeResult.Error);
        Assert.Empty(contentResult.Error);
        Assert.Empty(envelopeResult.Error);

        using var content = JsonDocument.Parse(contentResult.Output);
        using var envelope = JsonDocument.Parse(envelopeResult.Output);
        Assert.True(JsonElement.DeepEquals(
            content.RootElement,
            envelope.RootElement.GetProperty("content")));
        Assert.Equal(
            "diff-history",
            envelope.RootElement.GetProperty("result_kind").GetString());
        JsonElement history = content.RootElement
            .GetProperty("document")
            .GetProperty("content");
        Assert.Equal(
            2,
            history.GetProperty("evaluations").GetArrayLength());
        Assert.Equal(
            2,
            history.GetProperty("probes").GetArrayLength());
        Assert.Equal(
            1,
            history.GetProperty("transitions").GetArrayLength());
        Assert.Equal(
            "complete",
            history.GetProperty("evaluations")[0]
                .GetProperty("inspection")
                .GetProperty("outcome")
                .GetString());
    }

    [Fact]
    public async Task DiffHistoryRange_CountEnvelopeRetainsCompleteContent()
    {
        const string Id = "range.diff-history.count-envelope";
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(
                FirstFeed,
                Id,
                ["1.0.0", "2.0.0", "3.0.0"],
                version => CreateApiPackage(
                    Id,
                    version,
                    version switch
                    {
                        "1.0.0" => FixtureCatalog.DiffV1.AssemblyPath(),
                        "2.0.0" => FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
                        _ => FixtureCatalog.DiffV2.AssemblyPath(),
                    }),
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
            [
                "diff",
                "--history",
                "--package", $"{Id}@1.0.0..3.0.0",
                "--type", RangeType,
                "--finding", "api.type",
                "--source", FirstFeed,
                "--at", "all",
                "--count",
                "--rows", "1..1",
                "--envelope",
                "--compact",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.DoesNotContain('\n', result.Output.TrimEnd());
        using var envelope = JsonDocument.Parse(result.Output);
        JsonElement content = envelope.RootElement.GetProperty("content");
        Assert.Equal("available", content.GetProperty("outcome").GetString());
        Assert.Equal(
            "completed",
            content.GetProperty("count").GetProperty("outcome").GetString());
        Assert.Equal(
            1,
            content.GetProperty("count")
                .GetProperty("counts")[0]
                .GetProperty("value")
                .GetInt32());
        Assert.Equal(
            3,
            content.GetProperty("document")
                .GetProperty("content")
                .GetProperty("evaluations")
                .GetArrayLength());
    }

    [Fact]
    public async Task DiffHistoryRange_AnalysisJsonRetainsFindingEvidence()
    {
        const string Id = "range.diff-history.analysis-json";
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(
                FirstFeed,
                Id,
                ["1.0.0", "2.0.0"],
                version => CreateApiPackage(
                    Id,
                    version,
                    version == "2.0.0"
                        ? FixtureCatalog.DiffV2.AssemblyPath()
                        : FixtureCatalog.DiffV1.AssemblyPath()),
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
            [
                "diff",
                "--history",
                "--package", $"{Id}@2.0.0..1.0.0",
                "--type", RangeType,
                "--member", "BodyState",
                "--finding", "analysis.unsafety",
                "--source", FirstFeed,
                "--at", "all",
                "--json",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        using var content = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "unsafety",
            content.RootElement
                .GetProperty("document")
                .GetProperty("document")
                .GetString());
        JsonElement history = content.RootElement
            .GetProperty("document")
            .GetProperty("content");
        Assert.Equal(2, history.GetProperty("evaluations").GetArrayLength());
        Assert.Equal(
            "complete",
            history.GetProperty("evaluations")[0]
                .GetProperty("inspection")
                .GetProperty("outcome")
                .GetString());
        Assert.Equal(
            JsonValueKind.Object,
            history.GetProperty("source_receipt").ValueKind);
    }

    [Fact]
    public async Task DiffHistoryRange_SemanticRowsComposeWithoutReducingExplicitAcquisition()
    {
        const string Id = "range.diff-history.semantic-rows";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(FirstFeed, Id, ["1.0.0", "2.0.0", "3.0.0"],
                version => CreateApiPackage(Id, version), requests));

        var result = await RunCommandAsync(
            [
                "diff",
                "--history",
                "--package", $"{Id}@1.0.0..3.0.0",
                "--type", RangeType,
                "--finding", "api.type",
                "--source", FirstFeed,
                "--at", "all",
                "-S", "Evaluations",
                "-n", "2",
                "--tail",
                "--tsv",
                "--tips", "q"
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal(
            3,
            requests.Count(request =>
                request.EndsWith(".nupkg", StringComparison.Ordinal)));
        Assert.DoesNotContain("1.0.0", result.Output);
        Assert.Contains("2.0.0", result.Output);
        Assert.Contains("3.0.0", result.Output);
    }

    [Fact]
    public async Task DiffHistoryRange_ExplicitLinesClipRenderedOutput()
    {
        const string Id = "range.diff-history.lines";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(
                FirstFeed,
                Id,
                ["1.0.0", "2.0.0", "3.0.0"],
                version => CreateApiPackage(Id, version),
                requests));

        var result = await RunCommandAsync(
            [
                "diff",
                "--history",
                "--package", $"{Id}@1.0.0..3.0.0",
                "--type", RangeType,
                "--finding", "api.type",
                "--source", FirstFeed,
                "--at", "all",
                "-S", "Evaluations",
                "-n", "2",
                "--lines",
                "--tsv",
                "--tips", "q"
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal(
            3,
            requests.Count(request =>
                request.EndsWith(".nupkg", StringComparison.Ordinal)));
        Assert.Equal(
            2,
            result.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task DiffHistoryRange_LinesRejectDocumentJsonBeforeAcquisition()
    {
        const string Id = "range.diff-history.lines-json";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(
                FirstFeed,
                Id,
                ["1.0.0", "2.0.0", "3.0.0"],
                version => CreateApiPackage(Id, version),
                requests));

        var result = await RunCommandAsync(
            [
                "diff",
                "--history",
                "--package", $"{Id}@1.0.0..3.0.0",
                "--type", RangeType,
                "--finding", "api.type",
                "--source", FirstFeed,
                "--at", "all",
                "-n", "2",
                "--lines",
                "--json",
                "--tips", "q"
            ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output",
            result.Error);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task DiffHistoryRange_ProbeReplayRetainsWorkingDirectoryAndSelectionPolicy()
    {
        const string Id = "range.diff-history.replay";
        string source = Path.Combine(_root, "diff-history-feed");
        WriteApiPackage(source, Id, "1.0.0");
        WriteApiPackage(source, Id, "2.0.0-preview.1");
        WriteApiPackage(
            source,
            Id,
            "3.0.0",
            FixtureCatalog.DiffV2.AssemblyPath());
        string originalDirectory = Directory.GetCurrentDirectory();
        string replayDirectory = Directory.CreateDirectory(Path.Combine(_root, "replay")).FullName;

        var result = await RunCommandAsync(
            ["diff", "--history", "--package", $"{Id}@1.0.0..3.0.0", "--type", RangeType,
                "--finding", "api.member", "--source", Path.GetRelativePath(originalDirectory, source),
                "--preview", "--all", "--tfm", "net10.0",
                "--at", "first", "--at", "last", "-S", "@History", "--tips", "q"]);

        Assert.True(result.Exit == 0, result.Error);
        string recommendation = result.Output.Split('\n').Single(
            line => line.Contains(
                "dotnet-inspect diff --history",
                StringComparison.Ordinal));
        Assert.Contains("2.0.0-preview.1", recommendation);
        Assert.Contains($"--source {ShellCommandText.Quote(source)}", recommendation);
        Assert.Contains($"--nugetconfig-directory {ShellCommandText.Quote(originalDirectory)}", recommendation);
        Assert.Contains("--preview", recommendation);
        Assert.Contains("--all", recommendation);
        Assert.Contains("--tfm 'net10.0'", recommendation);

        try
        {
            Directory.SetCurrentDirectory(replayDirectory);
            var replay = await RunCommandAsync(
                ["diff", "--history", "--package", $"{Id}@1.0.0..3.0.0", "--type", RangeType,
                    "--finding", "api.type", "--source", source,
                    "--nugetconfig-directory", originalDirectory,
                    "--preview", "--all", "--tfm", "net10.0", "--at", "#2", "--tips", "q"]);
            Assert.True(replay.Exit == 0, replay.Error);
            Assert.Contains("2.0.0-preview.1", replay.Output);
            Assert.Contains("Complete", replay.Output);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    [Fact]
    public async Task DiffHistoryRange_ProbeReplayRetainsExplicitPrereleasePolicyWithoutObservedPrerelease()
    {
        const string Id = "range.diff-history.stable-preview-replay";
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(
                FirstFeed,
                Id,
                ["1.0.0", "2.0.0", "3.0.0"],
                version => CreateApiPackage(
                    Id,
                    version,
                    version == "1.0.0"
                        ? FixtureCatalog.DiffV1.AssemblyPath()
                        : FixtureCatalog.DiffV2.AssemblyPath()),
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
            [
                "diff",
                "--history",
                "--package", $"{Id}@1.0.0..3.0.0",
                "--type", RangeType,
                "--finding", "api.member",
                "--source", FirstFeed,
                "--at", "first",
                "--at", "last",
                "-S", "@History",
                "--preview",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Contains(
            $"diff --history --package '{Id}@1.0.0..3.0.0'",
            result.Output);
        Assert.Contains(" --preview", result.Output);
    }

    [Fact]
    public async Task ApiPin_LocalSourceSupportsExactReplayWithoutDiscovery()
    {
        const string Id = "range.api.pin";
        string source = Path.Combine(_root, "api-pin");
        WriteApiPackage(source, Id, Version);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            throw new InvalidOperationException("Local API pin opened an HTTP transport."));

        var result = await RunCommandAsync(
            ["type", RangeType, "--package", $"{Id}@{Version}",
                "--source", source, "--source", Path.Combine(_root, "unreadable"),
                "--tips", "q"]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Contains($"{Id} {Version}", result.Output);
    }

    [Theory]
    [InlineData("", "2.0.0")]
    [InlineData("@latest", "2.0.0")]
    [InlineData("@1.*", "1.1.0-preview.1")]
    public async Task ApiSelection_LocalFeedUsesConfiguredAuthority(
        string selector, string expectedVersion)
    {
        const string Id = "selection.api.local";
        string source = Path.Combine(_root, "api-selection");
        foreach (string version in new[] { "1.0.0", "1.1.0-preview.1", "2.0.0" })
            WriteApiPackage(source, Id, version);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            throw new InvalidOperationException("Local API selection opened an HTTP transport."));

        var result = await RunCommandAsync(
            ["type", RangeType, "--package", $"{Id}{selector}",
                "--source", source, "--tips", "q"]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Contains(RangeType, result.Output);
        Assert.Contains($"{Id} {expectedVersion}", result.Output);
    }

    [Fact]
    public async Task ApiSelection_UnreadablePeerFailsBeforePayload()
    {
        const string Id = "selection.api.partial";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(FirstFeed, Id, ["1.0.0", "2.0.0"],
                _ => throw new InvalidOperationException("Partial discovery reached a payload."),
                requests));
        string missing = Path.Combine(_root, "missing-selection-peer");

        var result = await RunCommandAsync(
            ["type", RangeType, "--package", Id,
                "--source", FirstFeed, "--source", missing, "--tips", "q"]);

        Assert.Equal(1, result.Exit);
        Assert.Contains("could not be acquired", result.Error);
        Assert.Contains(missing, result.Error);
        Assert.DoesNotContain(requests,
            request => request.EndsWith(".nupkg", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiffHistoryRange_ConfigDirectoryErrorsPrecedeDiscovery(bool conflictingConfig)
    {
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            throw new InvalidOperationException("Invalid replay configuration reached discovery."));
        List<string> args =
        [
            "diff", "--history", "--package", "range.config.invalid@1.0.0..2.0.0",
            "--type", RangeType, "--source", FirstFeed,
            "--nugetconfig-directory", conflictingConfig ? _root : Path.Combine(_root, "missing"),
        ];
        if (conflictingConfig)
        {
            string config = Path.Combine(_root, "NuGet.Config");
            File.WriteAllText(config, $"""
                <configuration><packageSources><clear />
                <add key="feed" value="{FirstFeed}" />
                </packageSources></configuration>
                """);
            args.AddRange(["--nugetconfig", config]);
        }

        var result = await RunCommandAsync([.. args]);

        Assert.Equal(1, result.Exit);
        Assert.Contains(conflictingConfig
            ? "--nugetconfig and --nugetconfig-directory cannot be combined"
            : "NuGet config discovery directory not found", result.Error);
    }

    [Fact]
    public async Task ApiRange_ProjectionFailureCleansTransferredTemporaryDirectory()
    {
        const string Id = "range.api.cleanup";
        string source = Path.Combine(_root, "api-cleanup");
        WriteApiPackage(source, Id, Version);
        string temporary = Directory.CreateDirectory(Path.Combine(_root, "owned-temporary")).FullName;

        var result = await RunIsolatedCommandAsync(temporary,
            ["type", "--package", $"{Id}@{Version}..{Version}", "--at", "first",
                "--library", "Missing.dll", "--source", source, "--tips", "q"]);

        Assert.Equal(1, result.Exit);
        Assert.Contains("Library 'Missing.dll' not found", result.Error);
        Assert.Empty(Directory.EnumerateDirectories(temporary, "inspect-api*", SearchOption.TopDirectoryOnly));
    }

    private static void WriteApiPackage(
        string source,
        string id,
        string version,
        string? assemblyPath = null)
    {
        Directory.CreateDirectory(source);
        File.WriteAllBytes(
            Path.Combine(source, $"{id}.{version}.nupkg"),
            CreateApiPackage(id, version, assemblyPath));
    }

    private static byte[] CreateApiPackage(
        string id,
        string version,
        string? assemblyPath = null)
    {
        using var buffer = new MemoryStream();
        buffer.Write(CreatePackage(id, version, version: version));
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
            archive.CreateEntryFromFile(
                assemblyPath ?? FixtureCatalog.DiffV1.AssemblyPath(),
                "lib/net10.0/RangeFixture.dll");
        return buffer.ToArray();
    }
}
