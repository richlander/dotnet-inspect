using System.Net;
using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// The transfer receipt the acquisition step issues and the House
/// acquisition receipt carries: the gates of
/// <c>docs/design/package-transfer-receipt.md#pathological-cases-and-gates</c>.
/// </summary>
public sealed partial class PackageRangedRealizationTests
{
    /// <summary>The tail the archive reader requests to find the directory.</summary>
    private const int DirectoryTailLength = 22 + ushort.MaxValue;

    /// <summary>Gate 1: a complete download is one completed request carrying the whole archive.</summary>
    [Fact]
    public async Task TransferReceipt_CompleteDownload()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        PackageTransferReceipt receipt = Transfer(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Complete,
                "net45"),
            PackagePayloadOrigin.Download);

        Assert.Equal(PackageTransferPath.Download, receipt.Path);
        Assert.Null(receipt.FallbackReason);
        PackageTransferRequest request = Assert.Single(receipt.Requests);
        Assert.Equal(
            new PackageTransferRequest(
                PackageTransferRequestPurpose.Complete,
                null,
                PackageTransferRequestOutcome.Completed,
                archive.Length,
                archive.Length),
            request);
        AssertTotals(receipt, 1, archive.Length);
    }

    /// <summary>Gate 2: a cached payload carries a receipt with no requests.</summary>
    [Fact]
    public async Task TransferReceipt_CachedPayload()
    {
        byte[] archive = ReadPclStorage();
        var store = new InMemoryPackageStore();
        await using (RangedEnvironment first = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, archive)))
        {
            Transfer(
                await first.RealizeAsync(store, PackagePayloadAccess.Complete, "net45"),
                PackagePayloadOrigin.Download);
        }

        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);
        PackageTransferReceipt receipt = Transfer(
            await environment.RealizeAsync(store, PackagePayloadAccess.Ranged, "net45"),
            PackagePayloadOrigin.Cache);

        Assert.Same(PackageTransferReceipt.Cache, receipt);
        Assert.Equal(PackageTransferPath.Cache, receipt.Path);
        Assert.Empty(receipt.Requests);
        AssertTotals(receipt, 0, 0);
        Assert.Equal(0, server.RangedRequests + server.FullRequests);
    }

    /// <summary>
    /// Gate 3 (real asset PCLStorage 1.0.2): a ranged read is a directory
    /// tail, then one entry span, each with its requested range and the
    /// bytes it received.
    /// </summary>
    [Fact]
    public async Task TransferReceipt_RangedRead()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        PackageTransferReceipt receipt = Transfer(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net45"),
            PackagePayloadOrigin.Ranged);

        Assert.Equal(PackageTransferPath.Ranged, receipt.Path);
        Assert.Null(receipt.FallbackReason);
        // Size first: the complete request is abandoned at its advertised
        // length before the ranged read (docs/design/package-cache-policy.md).
        Assert.Equal(server.FullRequests + server.RangedRequests, receipt.RequestCount);
        Assert.Collection(
            receipt.Requests,
            probe => Assert.Equal(
                new PackageTransferRequest(
                    PackageTransferRequestPurpose.SizeProbe,
                    null,
                    PackageTransferRequestOutcome.Abandoned,
                    archive.Length,
                    0),
                probe),
            tail =>
            {
                long received = Math.Min(DirectoryTailLength, archive.Length);
                Assert.Equal(
                    new PackageTransferRequest(
                        PackageTransferRequestPurpose.DirectoryTail,
                        new PackageTransferRange(null, DirectoryTailLength),
                        PackageTransferRequestOutcome.Completed,
                        received,
                        received),
                    tail);
            },
            span =>
            {
                Assert.Equal(PackageTransferRequestPurpose.EntrySpan, span.Purpose);
                Assert.Equal(PackageTransferRequestOutcome.Completed, span.Outcome);
                PackageTransferRange range = Assert.IsType<PackageTransferRange>(span.Range);
                Assert.NotNull(range.Start);
                Assert.Equal(range.Length, span.BytesReceived);
                Assert.Equal(range.Length, span.AdvertisedLength);
                Assert.Contains(
                    server.Requests,
                    line => line.EndsWith(
                        $"bytes={range.Start}-{range.Start + range.Length - 1}",
                        StringComparison.Ordinal));
            });
        AssertTotals(
            receipt,
            3,
            receipt.Requests.Sum(request => request.BytesReceived));
        Assert.True(receipt.BytesReceived < archive.Length);
    }

    /// <summary>
    /// Gate 4: a server that ignores <c>Range</c> records the ignored
    /// request, then one complete request, with reason RangeIgnored.
    /// </summary>
    [Fact]
    public async Task TransferReceipt_RangeIgnored()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive)
        {
            IgnoreRange = true,
        };
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        PackageTransferReceipt receipt = Transfer(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net45"),
            PackagePayloadOrigin.Download);

        Assert.Equal(PackageTransferPath.RangedThenDownload, receipt.Path);
        Assert.Equal(PackageTransferFallbackReason.RangeIgnored, receipt.FallbackReason);
        Assert.Collection(
            receipt.Requests,
            probe => Assert.Equal(
                new PackageTransferRequest(
                    PackageTransferRequestPurpose.SizeProbe,
                    null,
                    PackageTransferRequestOutcome.Abandoned,
                    archive.Length,
                    0),
                probe),
            ignored =>
            {
                Assert.Equal(PackageTransferRequestPurpose.DirectoryTail, ignored.Purpose);
                Assert.Equal(new PackageTransferRange(null, DirectoryTailLength), ignored.Range);
                Assert.Equal(PackageTransferRequestOutcome.RangeIgnored, ignored.Outcome);
                Assert.Equal(archive.Length, ignored.AdvertisedLength);
                // The whole archive was offered; the range source read none of it.
                Assert.Equal(0, ignored.BytesReceived);
            },
            complete => Assert.Equal(
                new PackageTransferRequest(
                    PackageTransferRequestPurpose.Complete,
                    null,
                    PackageTransferRequestOutcome.Completed,
                    archive.Length,
                    archive.Length),
                complete));
        AssertTotals(receipt, 3, archive.Length);
    }

    /// <summary>
    /// Gate 5: a refused credential is recorded and no complete request
    /// follows it; the next authority then serves the ranged read.
    /// </summary>
    [Fact]
    public async Task TransferReceipt_RefusedCredential_HasNoFallbackRequest()
    {
        byte[] archive = ReadPclStorage();
        var refusing = new RangeFeed(PclStorage, PclStorageVersion, archive)
        {
            Host = "refusing.example",
            RangedStatus = HttpStatusCode.Unauthorized,
        };
        var serving = new RangeFeed(PclStorage, PclStorageVersion, archive)
        {
            Host = "serving.example",
        };
        await using RangedEnvironment environment =
            RangedEnvironment.Create(refusing, serving);

        PackageTransferReceipt receipt = Transfer(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net45"),
            PackagePayloadOrigin.Ranged);

        // The refusing source's one complete request is its size probe,
        // abandoned before the refused ranged read; no complete request
        // follows the refusal.
        Assert.Equal(1, refusing.FullRequests);
        Assert.Equal(1, refusing.RangedRequests);
        Assert.Equal(PackageTransferPath.Ranged, receipt.Path);
        Assert.Equal(
            [
                (PackageTransferRequestPurpose.SizeProbe, PackageTransferRequestOutcome.Abandoned),
                (PackageTransferRequestPurpose.DirectoryTail, PackageTransferRequestOutcome.Refused),
                (PackageTransferRequestPurpose.SizeProbe, PackageTransferRequestOutcome.Abandoned),
                (PackageTransferRequestPurpose.DirectoryTail, PackageTransferRequestOutcome.Completed),
                (PackageTransferRequestPurpose.EntrySpan, PackageTransferRequestOutcome.Completed),
            ],
            receipt.Requests.Select(request => (request.Purpose, request.Outcome)));
        Assert.Equal(0, receipt.Requests[1].BytesReceived);
        Assert.DoesNotContain(
            receipt.Requests,
            request => request.Purpose == PackageTransferRequestPurpose.Complete);
    }

    /// <summary>
    /// Gate 7: more requests than the bound keeps the first 256, marks the
    /// receipt truncated, and keeps complete totals.
    /// </summary>
    [Fact]
    public void TransferReceipt_BoundsItsRequests()
    {
        PackageTransferRequest[] requests =
        [
            .. Enumerable.Range(0, 300).Select(index => new PackageTransferRequest(
                PackageTransferRequestPurpose.EntrySpan,
                new PackageTransferRange(index * 10L, 10),
                PackageTransferRequestOutcome.Completed,
                10,
                10)),
        ];

        PackageTransferReceipt receipt = PackageTransferReceipt.Create(
            PackageTransferPath.Ranged,
            null,
            requests);

        Assert.Equal(PackageTransferReceipt.MaxRequests, receipt.Requests.Count);
        Assert.Equal(requests[..256], receipt.Requests);
        Assert.True(receipt.IsTruncated);
        AssertTotals(receipt, 300, 3000);

        PackageTransferReceipt bounded = PackageTransferReceipt.Create(
            PackageTransferPath.Ranged,
            null,
            requests[..256]);
        Assert.False(bounded.IsTruncated);
        Assert.Equal(256, bounded.RequestCount);
    }

    /// <summary>
    /// The receipt the acquisition step issued, which the House acquisition
    /// receipt carries (gate 6: its origin agrees with the path).
    /// </summary>
    private static PackageTransferReceipt Transfer(
        PackageHouseSettlement settlement,
        PackagePayloadOrigin origin)
    {
        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(settlement);
        Assert.Equal(origin, acquired.Payload.Origin);
        PackageHouseAcquisitionReceipt acquisition =
            Assert.IsType<PackageHouseAcquisitionReceipt>(
                acquired.Result.Evidence.Acquisition);
        Assert.Equal(origin, acquisition.Origin);
        Assert.True(acquisition.Transfer.AgreesWith(acquisition.Origin));
        Assert.Same(acquisition.Transfer, acquired.SourcePayloadResult!.Transfer);
        return acquisition.Transfer;
    }

    private static void AssertTotals(
        PackageTransferReceipt receipt,
        int requestCount,
        long bytesReceived)
    {
        Assert.Equal(requestCount, receipt.RequestCount);
        Assert.Equal(bytesReceived, receipt.BytesReceived);
    }
}
