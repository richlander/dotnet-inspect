using System.Collections.Concurrent;
using System.Text.Json;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Transfer receipt design gate 8, host delivery: Diff History's Debug
/// <c>--evidence-envelope</c> lists each House acquisition with its transfer
/// receipt, and the ordinary output is unchanged. Delivery exists only in
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
            "--source", FirstFeed, "--tips", "q",
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

        // Version cells are read by range, size first: each archive here is
        // under the size cut, so the size probe's response is kept as the one
        // complete transfer (docs/design/package-cache-policy.md#size-first).
        foreach (JsonElement entry in await ReadHistoryAcquisitionsAsync(sidecar, Id))
        {
            Assert.Equal("Download", entry.GetProperty("origin").GetString());
            JsonElement transfer = entry.GetProperty("transfer");
            Assert.Equal("Download", transfer.GetProperty("path").GetString());
            JsonElement request = Assert.Single(
                transfer.GetProperty("requests").EnumerateArray());
            Assert.Equal("Complete", request.GetProperty("purpose").GetString());
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

    [Fact]
    public void DiffEvidenceEnvelope_RequiresHistoryAtParse()
    {
        string[] arguments =
        [
            "diff", "--package", "range.pairwise@1.0.0..2.0.0",
            "--evidence-envelope", Path.Combine(_root, "pairwise.json"),
        ];

        var parsed = CommandLineBuilder.CreateRootCommand().Parse(
            CommandLineBuilder.PreprocessArgs(arguments));

        Assert.Contains(
            parsed.Errors,
            error => error.Message
                == "--evidence-envelope is supported only by diff --history.");
    }
#endif
}
