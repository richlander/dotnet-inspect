using System.IO.Compression;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Semantic exact-entry acquisition gates for
/// <c>docs/design/package-house-semantic-content-demand.md#content-query</c>.
/// </summary>
public sealed partial class PackageRangedRealizationTests
{
    [Fact]
    public async Task SemanticFiles_RangedReadPublishesOnlyExactEntries()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        string[] entries =
        [
            "lib/net45/PCLStorage.xml",
            "lib/sl5/PCLStorage.dll",
        ];
        PackageHouseContentQuery query =
            PackageHouseContentQuery.PackageFiles(entries);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                query));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        Assert.Same(query, acquired.Result.Request.ContentQuery);
        Assert.Equal(
            entries.Order(StringComparer.Ordinal),
            acquired.Payload.Content
                .EnumerateEntries()
                .Order(StringComparer.Ordinal));
        Assert.Null(acquired.Payload.Content.RootPath);
        Assert.Null(acquired.Payload.Content.NupkgPath);
        Assert.False(acquired.Payload.Content.TryOpenArchive(out _));
        Assert.False(acquired.Payload.Content.TryOpenEntry(
            "lib/net45/PCLStorage.dll",
            out _));

        PackageTransferReceipt receipt = Transfer(
            acquired,
            PackagePayloadOrigin.Ranged);
        Assert.Equal(PackageTransferPath.Ranged, receipt.Path);
        Assert.True(receipt.BytesReceived < archive.Length);
    }

    [Fact]
    public async Task SemanticFiles_CompleteFallbackRetainsArchiveButPublishesOnlyExactEntries()
    {
        const string Path = "lib/net45/PCLStorage.xml";
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        var store = new InMemoryPackageStore();
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                store,
                PackageHouseContentQuery.PackageFiles([Path]),
                sizeCut: archive.Length));

        Assert.Equal(PackagePayloadOrigin.Download, acquired.Payload.Origin);
        Assert.Equal([Path], acquired.Payload.Content.EnumerateEntries());
        Assert.False(acquired.Payload.Content.TryOpenEntry(
            "PCLStorage.nuspec",
            out _));
        IPackageContent? cached = store.TryGetCached(
            PclStorage,
            PclStorageVersion,
            [acquired.Payload.ProducerKey]);
        Assert.NotNull(cached);
        Assert.Contains("PCLStorage.nuspec", cached.EnumerateEntries());
        PackageTransferReceipt receipt = Transfer(
            acquired,
            PackagePayloadOrigin.Download);
        Assert.Equal(PackageTransferPath.Download, receipt.Path);
        Assert.Equal(1, server.FullRequests);
        Assert.Equal(0, server.RangedRequests);
    }

    [Fact]
    public async Task SemanticFiles_MissingEntryFailsVisibly()
    {
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    PclStorage,
                    PclStorageVersion,
                    ReadPclStorage()));

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery.PackageFiles(["README.md"])));

        var noMatch = Assert.IsType<PackageHouseResult.NoMatch>(
            acquired.Result);
        Assert.Contains(
            "'README.md'",
            noMatch.Reason.ToString(),
            StringComparison.Ordinal);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
        PackageHouseFailure.Stage failure =
            Assert.IsType<PackageHouseFailure.Stage>(
                Assert.Single(noMatch.Evidence.Failures));
        Assert.Equal(
            PackageHouseFailureStage.Selection,
            failure.StageKind);
    }

    [Fact]
    public async Task SemanticFiles_CaseAmbiguousEntryIsReported()
    {
        const string PackageId = "Ambiguous.Files";
        const string Version = "1.0.0";
        byte[] archive = CreateCaseAmbiguousArchive();
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(PackageId, Version, archive));

        PackageFileAcquisitionResult result =
            await environment.AcquireFileAsync(
                new InMemoryPackageStore(),
                "README.md",
                packageId: PackageId,
                version: Version);

        Assert.IsType<PackageFileAcquisitionResult.Unavailable>(result);
        Assert.Equal(
            PackageFileAcquisitionStatus.Ambiguous,
            result.Status);
        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            result.Settlement);
        Assert.Equal(
            ["README.md", "readme.md"],
            acquired.Payload.Content.EnumerateEntries());
    }

    [Fact]
    public async Task SemanticFiles_RequiresHouseOwnedPayloadPlanning()
    {
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    PclStorage,
                    PclStorageVersion,
                    ReadPclStorage()));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PclStorage,
                    PclStorageVersion)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire),
            contentQuery:
                PackageHouseContentQuery.PackageFiles(["README.md"]));
        var callerPlanned = new PackageHouse(
            environment.Authorization,
            new PackagePayloadAcquisitionPlan(
                static (_, _) => new InMemoryPackageStore(),
                access: PackagePayloadAccess.Ranged));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => callerPlanned.ExecuteAsync(
                    request,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        request.Operation.RequestTimeout,
                        request.Operation.OperationTimeout)));
        Assert.Contains(
            "House-planned",
            failure.Message,
            StringComparison.Ordinal);
    }

    private static byte[] CreateCaseAmbiguousArchive()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            Write("README.md", "upper");
            Write("readme.md", "lower");

            void Write(string path, string text)
            {
                using StreamWriter writer =
                    new(archive.CreateEntry(path).Open());
                writer.Write(text);
            }
        }
        return output.ToArray();
    }
}
