using System.Text.Json;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Sections;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Transfer receipt design gate 8, configuration-neutral half: the evidence
/// an operation's House acquisitions produce, and its serialization. Host
/// delivery through the Debug <c>--evidence-envelope</c> is gated by the
/// CLI harness in Debug.
/// </summary>
public sealed partial class PackageRangedRealizationTests
{
    /// <summary>
    /// Each settled acquisition is one entry, in order, with its coordinate,
    /// authority, origin, and receipt; two acquisitions of one coordinate are
    /// two entries; a settlement without an acquisition adds none.
    /// </summary>
    [Fact]
    public async Task AcquisitionEvidence_ListsEachHouseAcquisitionInOrder()
    {
        byte[] archive = ReadPclStorage();
        var store = new InMemoryPackageStore();
        await using RangedEnvironment environment = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, archive));
        await using RangedEnvironment refusing = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, archive)
            {
                RangedStatus = System.Net.HttpStatusCode.Unauthorized,
            });
        var settlements = new Queue<PackageHouseSettlement>(
        [
            await environment.RealizeAsync(store, PackagePayloadAccess.Complete, "net45"),
            await environment.RealizeAsync(store, PackagePayloadAccess.Ranged, "net45"),
            await refusing.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net45"),
        ]);
        Assert.IsNotType<PackageHouseSettlement.Acquired>(settlements.Last());
        var recorder = new PackageAcquisitionEvidenceRecorder(
            new QueuedExecutor(settlements));

        for (int i = 0; i < 3; i++)
            await recorder.ExecuteAsync(null!, TestContext.Current.CancellationToken);
        PackageAcquisitionEvidence evidence = recorder.ToEvidence();

        Assert.Collection(
            evidence.Acquisitions,
            first =>
            {
                Assert.Equal(("pclstorage", "1.0.2"), (first.PackageId.ToLowerInvariant(), first.Version));
                Assert.Equal("ranged", first.Authority);
                Assert.Equal(PackagePayloadOrigin.Download, first.Origin);
                Assert.Equal(PackageTransferPath.Download, first.Transfer.Path);
                Assert.Equal(archive.Length, first.Transfer.BytesReceived);
            },
            second =>
            {
                Assert.Equal(evidence.Acquisitions[0].PackageId, second.PackageId);
                Assert.Equal(evidence.Acquisitions[0].Version, second.Version);
                Assert.Equal(PackagePayloadOrigin.Cache, second.Origin);
                Assert.Same(PackageTransferReceipt.Cache, second.Transfer);
            });
    }

    /// <summary>
    /// The evidence serializes with snake-case members and string enums, and
    /// carries no URL.
    /// </summary>
    [Fact]
    public async Task AcquisitionEvidence_SerializesTheReceipt()
    {
        byte[] archive = ReadPclStorage();
        await using RangedEnvironment environment = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, archive));
        var recorder = new PackageAcquisitionEvidenceRecorder(
            new QueuedExecutor(new Queue<PackageHouseSettlement>(
            [
                await environment.RealizeAsync(
                    new InMemoryPackageStore(),
                    PackagePayloadAccess.Ranged,
                    "net45"),
            ])));
        await recorder.ExecuteAsync(null!, TestContext.Current.CancellationToken);

        string json = JsonSerializer.Serialize(
            recorder.ToEvidence(),
            PackageAcquisitionEvidenceJsonContext.Default.PackageAcquisitionEvidence);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement entry = Assert.Single(
            document.RootElement.GetProperty("acquisitions").EnumerateArray());
        Assert.Equal("1.0.2", entry.GetProperty("version").GetString());
        Assert.Equal("ranged", entry.GetProperty("authority").GetString());
        Assert.Equal("Ranged", entry.GetProperty("origin").GetString());
        JsonElement transfer = entry.GetProperty("transfer");
        Assert.Equal("Ranged", transfer.GetProperty("path").GetString());
        Assert.False(transfer.TryGetProperty("fallback_reason", out _));
        Assert.Equal(2, transfer.GetProperty("request_count").GetInt32());
        Assert.False(transfer.GetProperty("is_truncated").GetBoolean());
        JsonElement[] requests = [.. transfer.GetProperty("requests").EnumerateArray()];
        Assert.Equal("DirectoryTail", requests[0].GetProperty("purpose").GetString());
        // A tail range names only its length; the absent start means "the final bytes".
        Assert.False(requests[0].GetProperty("range").TryGetProperty("start", out _));
        Assert.Equal(
            DirectoryTailLength,
            requests[0].GetProperty("range").GetProperty("length").GetInt32());
        Assert.Equal("EntrySpan", requests[1].GetProperty("purpose").GetString());
        Assert.Equal("Completed", requests[1].GetProperty("outcome").GetString());
        Assert.True(requests[1].GetProperty("range").GetProperty("start").GetInt64() >= 0);
        Assert.Equal(
            requests.Sum(request => request.GetProperty("bytes_received").GetInt64()),
            transfer.GetProperty("bytes_received").GetInt64());
        Assert.DoesNotContain("http", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ranged.example", json, StringComparison.Ordinal);
    }

    private sealed class QueuedExecutor(Queue<PackageHouseSettlement> settlements)
        : IPackageHouseVersionPopulationCellExecutor
    {
        public Task<PackageHouseSettlement> ExecuteAsync(
            PackageHouseVersionPopulationCellExecution execution,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(settlements.Dequeue());
    }
}
