using System.Collections.Concurrent;
using System.Text.Json;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Transfer receipt design gate 8, host delivery: the Debug
/// <c>--evidence-envelope</c> of Diff History and of pairwise package diff
/// lists each House acquisition with its transfer receipt, and the ordinary
/// output is unchanged. Delivery exists only in
/// Debug hosts; the configuration-neutral evidence and serializer are gated in
/// Release by the Services contract suite.
/// </summary>
public sealed partial class ConfiguredPayloadAcquisitionTests
{
#if DEBUG
    [Fact]
    public async Task DiffHistoryEvidenceEnvelope_ListsEachAcquisitionWithItsReceipt()
    {
        const string Id = "range.diff-history.evidence";
        var requests = new ConcurrentQueue<string>();
        var sizes = new ConcurrentDictionary<string, int>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(FirstFeed, Id, ["1.0.0", "2.0.0", "3.0.0"],
                version =>
                {
                    byte[] package = CreateApiPackage(Id, version);
                    sizes[version] = package.Length;
                    return package;
                },
                requests));
        string sidecar = Path.Combine(_root, "history-evidence.json");
        string warmSidecar = Path.Combine(_root, "history-evidence-warm.json");
        string[] ordinaryArguments =
        [
            "diff", "--history", "--package", $"{Id}@1.0.0..3.0.0",
            "--type", RangeType, "--finding", "api.type", "--at", "all",
            "--source", FirstFeed,
        ];

        var withEvidence = await RunCommandAsync(
            [.. ordinaryArguments, "--evidence-envelope", sidecar]);
        var ordinary = await RunCommandAsync(ordinaryArguments);
        var warmEvidence = await RunCommandAsync(
            [.. ordinaryArguments, "--evidence-envelope", warmSidecar]);

        Assert.True(withEvidence.Exit == 0, withEvidence.Error);
        Assert.Equal(0, ordinary.Exit);
        Assert.Equal(0, warmEvidence.Exit);
        Assert.Equal(ordinary.Output, withEvidence.Output);
        Assert.Equal(ordinary.Output, warmEvidence.Output);
        Assert.Equal(
            $"Evidence envelope: {sidecar}{Environment.NewLine}",
            withEvidence.Error);

        // These tiny archives fit in the directory-tail response.
        foreach (JsonElement entry in await ReadHistoryAcquisitionsAsync(sidecar, Id))
        {
            Assert.Equal("Download", entry.GetProperty("origin").GetString());
            JsonElement transfer = entry.GetProperty("transfer");
            Assert.Equal("CompletedByRanges", transfer.GetProperty("path").GetString());
            JsonElement request = Assert.Single(
                transfer.GetProperty("requests").EnumerateArray());
            Assert.Equal("DirectoryTail", request.GetProperty("purpose").GetString());
            Assert.Equal("Completed", request.GetProperty("outcome").GetString());
            int size = sizes[entry.GetProperty("version").GetString()!];
            Assert.Equal(size, request.GetProperty("bytes_received").GetInt32());
            Assert.Equal(size, transfer.GetProperty("bytes_received").GetInt32());
        }

        // The feed is a credential-free HTTP authority, so the payloads are
        // durable: the same history again transfers nothing, and across the
        // three runs each version was requested once.
        foreach (JsonElement entry in await ReadHistoryAcquisitionsAsync(warmSidecar, Id))
        {
            Assert.Equal("Cache", entry.GetProperty("origin").GetString());
            JsonElement transfer = entry.GetProperty("transfer");
            Assert.Equal("Cache", transfer.GetProperty("path").GetString());
            Assert.Empty(transfer.GetProperty("requests").EnumerateArray());
        }
        Assert.Equal(
            3,
            requests.Count(url => url.EndsWith(".nupkg", StringComparison.Ordinal)));
    }

    private static async Task<JsonElement[]> ReadHistoryAcquisitionsAsync(
        string sidecar,
        string id)
    {
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(sidecar, TestContext.Current.CancellationToken));
        JsonElement root = document.RootElement;
        Assert.Equal("diff-history", root.GetProperty("result_kind").GetString());
        Assert.True(root.TryGetProperty("content", out _));
        Assert.False(root.TryGetProperty("inspection", out _));
        JsonElement[] acquisitions =
        [
            .. root.GetProperty("evidence").GetProperty("acquisitions").EnumerateArray()
                .Select(static entry => entry.Clone()),
        ];
        Assert.Equal(
            ["1.0.0", "2.0.0", "3.0.0"],
            acquisitions
                .Select(entry => entry.GetProperty("version").GetString())
                .Order(StringComparer.Ordinal));
        foreach (JsonElement entry in acquisitions)
        {
            Assert.Equal(
                id,
                entry.GetProperty("package_id").GetString(),
                ignoreCase: true);
            Assert.False(string.IsNullOrEmpty(entry.GetProperty("authority").GetString()));
        }
        return acquisitions;
    }

    [Theory]
    [InlineData("--package", "range.pairwise@1.0.0..2.0.0", false)]
    [InlineData("--platform", "System.Text.Json@9.0.0..10.0.0", true)]
    [InlineData("--library", "a.dll..b.dll", true)]
    public void DiffEvidenceEnvelope_RequiresHistoryOrAPackageAtParse(
        string endpoints,
        string range,
        bool rejected)
    {
        string[] arguments =
        [
            "diff", endpoints, range,
            "--evidence-envelope", Path.Combine(_root, "pairwise.json"),
        ];

        var parsed = CommandLineBuilder.CreateRootCommand().Parse(
            CommandLineBuilder.PreprocessArgs(arguments));

        Assert.Equal(
            rejected,
            parsed.Errors.Any(static error => error.Message
                == "--evidence-envelope is supported only by diff --history and pairwise diff --package."));
    }

    /// <summary>
    /// Gate 8 for pairwise package diff: Library API Diff, an API analysis
    /// set, and an analysis set that reads method bodies, opened through the
    /// package endpoint scope, list each endpoint's House acquisition with
    /// its receipt, and the ordinary output is unchanged. A repeated request
    /// reads both endpoints from the entry cache.
    /// </summary>
    [Theory]
    [InlineData("library-api-diff", false)]
    [InlineData("diff-analysis", false)]
    [InlineData("diff-analysis-summary-table", true)]
    public async Task PairwiseDiffEvidenceEnvelope_ListsEachEndpointAcquisition(
        string resultKind,
        bool readsBodies)
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        string[] ordinaryArguments = PairwiseSystemTextJsonDiff(
            resultKind == "library-api-diff"
                ? []
                : readsBodies
                    ?
                    [
                        "--type", "System.Text.Json.JsonSerializer",
                        "--member", "SerializeAsync:1",
                        "--analysis", "allocation,il",
                    ]
                    : ["--type", "System.Text.Json.JsonSerializer", "--analysis", "api"]);
        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, SystemTextJsonId, packages);
        UseFeed(feed);
        string sidecar = Path.Combine(_root, $"{resultKind}-{readsBodies}-evidence.json");
        string warmSidecar = Path.Combine(_root, $"{resultKind}-{readsBodies}-evidence-warm.json");

        var withEvidence = await RunCommandAsync(
            [.. ordinaryArguments, "--evidence-envelope", sidecar]);
        var ordinary = await RunCommandAsync(ordinaryArguments);
        var warmEvidence = await RunCommandAsync(
            [.. ordinaryArguments, "--evidence-envelope", warmSidecar]);

        Assert.True(withEvidence.Exit == 0, withEvidence.Error);
        Assert.Equal(0, ordinary.Exit);
        Assert.Equal(0, warmEvidence.Exit);
        Assert.Equal(ordinary.Output, withEvidence.Output);
        Assert.Equal(ordinary.Output, warmEvidence.Output);
        Assert.Equal(
            $"Evidence envelope: {sidecar}{Environment.NewLine}",
            withEvidence.Error);

        foreach (JsonElement entry in await ReadPairwiseAcquisitionsAsync(sidecar, resultKind))
        {
            Assert.Equal("Ranged", entry.GetProperty("origin").GetString());
            JsonElement transfer = entry.GetProperty("transfer");
            Assert.Equal("Ranged", transfer.GetProperty("path").GetString());
            Assert.Equal(
                ["DirectoryTail", "EntrySpan"],
                transfer.GetProperty("requests").EnumerateArray()
                    .Select(static request => request.GetProperty("purpose").GetString()));
        }
        foreach (JsonElement entry in await ReadPairwiseAcquisitionsAsync(warmSidecar, resultKind))
        {
            JsonElement transfer = entry.GetProperty("transfer");
            Assert.Equal("EntryCache", transfer.GetProperty("path").GetString());
            Assert.Empty(transfer.GetProperty("requests").EnumerateArray());
        }
    }

    /// <summary>
    /// The merged API changes view has no baseline envelope, so a member
    /// target is refused before any package read.
    /// </summary>
    [Fact]
    public async Task PairwiseDiffEvidenceEnvelope_RefusesViewsWithoutABaselineEnvelope()
    {
        UseFeed(new RejectNetworkHandler(new HttpClientHandler()));

        var result = await RunCommandAsync(
        [
            .. PairwiseSystemTextJsonDiff(["--member", "System.Text.Json.JsonSerializer.SerializeAsync:1"]),
            "--evidence-envelope", Path.Combine(_root, "member-evidence.json"),
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Contains(
            "--evidence-envelope requires diff --history, or a pairwise diff --package "
                + "Library API Diff or analysis set.",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_root, "member-evidence.json")));
    }

    /// <summary>
    /// A Library API Diff request whose endpoints are two Libraries each,
    /// as the real NUnit 4.1.0 and 4.2.2 archives are, has the merged API
    /// changes view, which has no baseline envelope: the option is refused
    /// once the endpoints are acquired, with no output and no sidecar.
    /// </summary>
    [Fact]
    public async Task PairwiseDiffEvidenceEnvelope_RefusesMultiLibraryEndpointsBeforeOutput()
    {
        Dictionary<string, byte[]> packages = await ReadNUnitPackagesAsync();
        UseFeed(new RangeHonoringHistoryFeedHandler(FirstFeed, NUnitId, packages));
        string sidecar = Path.Combine(_root, "nunit-evidence.json");

        var result = await RunCommandAsync(
        [
            "diff", "--package", $"{NUnitId}@4.1.0..4.2.2",
            "--tfm", "net6.0", "--source", FirstFeed,
            "--evidence-envelope", sidecar,
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Equal("", result.Output);
        Assert.Contains(
            "--evidence-envelope requires Library API Diff, which needs exactly one Library "
                + "at each endpoint; resolved 2 before and 2 after. Use --analysis api.",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(sidecar));
    }

    private static async Task<JsonElement[]> ReadPairwiseAcquisitionsAsync(
        string sidecar,
        string resultKind)
    {
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(sidecar, TestContext.Current.CancellationToken));
        JsonElement root = document.RootElement;
        Assert.Equal(resultKind, root.GetProperty("result_kind").GetString());
        Assert.True(root.TryGetProperty("content", out _));
        JsonElement[] acquisitions =
        [
            .. root.GetProperty("evidence").GetProperty("acquisitions").EnumerateArray()
                .Select(static entry => entry.Clone()),
        ];
        // Before, then after.
        Assert.Equal(
            SystemTextJsonVersions,
            acquisitions.Select(static entry => entry.GetProperty("version").GetString()));
        return acquisitions;
    }
#endif
}
