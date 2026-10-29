using System.Text;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    // PR-fast: fixture-controlled transfers; no API projection or external network.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageRealizationEvidence_RecordsContinuationsWithoutWarmReplay(bool hasCompileLibrary)
    {
        string id = $"evidence.runtime.{Guid.NewGuid():N}";
        byte[] assembly = File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        var entries = new List<(string, byte[])>
        {
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(Nuspec(id, "1.0.0"))),
            ($"runtimes/linux-x64/lib/net11.0/{id}.dll", assembly),
            ("content/padding.bin", new byte[2 * MiB]),
        };
        if (hasCompileLibrary)
            entries.Add(($"lib/net11.0/{id}.dll", assembly));
        var handler = new GalleryPackageHandler(id, "1.0.0", PackageEntries([.. entries]));
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageRealizationResult result = await BrowserPackageWorkspace.RealizeWithSettlementAsync(
            id, "1.0.0", "net11.0", source, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var realized = Assert.IsType<BrowserPackageRealizationResult.Realized>(result);
        var evidence = BrowserPackageSummaryEvidence.Capture(result);
        Assert.Same(realized.Realization.VersionSettlement, evidence.Inspection);
        Assert.Equal(2, evidence.Evidence.Acquisitions.Length);
        Assert.Same(result.Acquisition!.Transfer, evidence.Evidence.Acquisitions[0].Transfer);
        Assert.Same(result.Continuation!.Acquisition!.Transfer, evidence.Evidence.Acquisitions[1].Transfer);
        Assert.All(evidence.Evidence.Acquisitions, acquisition =>
        {
            Assert.Equal(id.ToLowerInvariant(), acquisition.PackageId);
            Assert.Equal("1.0.0", acquisition.Version);
        });
        Assert.Equal(handler.PackageBytesServed,
            evidence.Evidence.Acquisitions.Sum(acquisition => acquisition.Transfer.BytesReceived));
        Assert.Equal(handler.RangedPackageResponses + handler.OrdinaryPackageResponses,
            evidence.Evidence.Acquisitions.Sum(acquisition => acquisition.Transfer.RequestCount));
        BrowserPackageVersionSettlementEvidenceInspection wire = BrowserPackageWireProjection.Project(evidence);
        wire = Assert.IsType<BrowserPackageVersionSettlementEvidenceInspection>(
            JsonSerializer.Deserialize(
                JsonSerializer.Serialize(wire,
                    BrowserPackageJsonContext.Default.BrowserPackageVersionSettlementEvidenceInspection),
                BrowserPackageJsonContext.Default.BrowserPackageVersionSettlementEvidenceInspection));
        Assert.Equal(handler.PackageBytesServed,
            wire.Evidence.Acquisitions.Sum(acquisition => acquisition.Transfer.BytesReceived));
        Assert.Contains(wire.Evidence.Acquisitions.SelectMany(acquisition => acquisition.Transfer.Requests),
            request => request.Purpose == "DirectoryTail" && request.Range is { Start: null });
        Assert.Contains(wire.Evidence.Acquisitions.SelectMany(acquisition => acquisition.Transfer.Requests),
            request => request.Purpose == "EntrySpan" && request.Range is { Start: not null });

        int requests = handler.Requested.Count;
        BrowserPackageRealizationResult warm = await BrowserPackageWorkspace.RealizeWithSettlementAsync(
            id, "1.0.0", "net11.0", source, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Empty(BrowserPackageSummaryEvidence.Capture(warm).Evidence.Acquisitions);
        Assert.Equal(requests, handler.Requested.Count);
    }

    [Fact]
    public async Task PackageRealizationEvidence_FailedContinuationRetainsEarlierAcquisition()
    {
        string id = $"evidence.continuation.failure.{Guid.NewGuid():N}";
        var handler = new GalleryPackageHandler(id, "1.0.0", PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(Nuspec(id, "1.0.0"))),
            ($"runtimes/linux-x64/lib/net11.0/{id}.dll",
                File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location)),
            ("content/padding.bin", new byte[2 * MiB])));
        using IPackageSourceClient source = Gallery(new FailContinuationHandler(handler));
        BrowserPackageRealizationResult result = await BrowserPackageWorkspace.RealizeWithSettlementAsync(
            id, "1.0.0", "net11.0", source, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var unavailable = Assert.IsType<BrowserPackageRealizationResult.NotSettled>(result);
        Assert.IsType<BrowserPackageRealizationResult.NotSettled>(result.Continuation);
        var evidence = BrowserPackageSummaryEvidence.Capture(result);
        Assert.Same(unavailable.VersionSettlement, evidence.Inspection);
        Assert.Same(result.Acquisition!.Transfer, Assert.Single(evidence.Evidence.Acquisitions).Transfer);
    }

    [Fact]
    public async Task PackageInventoryEvidence_PreservesUnsettledBaseline()
    {
        string id = $"evidence.missing.{Guid.NewGuid():N}";
        var handler = new GalleryPackageHandler(
            id, "1.0.0", [], packageStatus: System.Net.HttpStatusCode.NotFound);
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageRealizationResult result = await BrowserPackageWorkspace.InventoryWithSettlementAsync(
            id, "1.0.0", "net11.0", source, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var unavailable = Assert.IsType<BrowserPackageRealizationResult.NotSettled>(result);
        var evidence = BrowserPackageSummaryEvidence.Capture(result);
        Assert.Same(unavailable.VersionSettlement, evidence.Inspection);
        Assert.Empty(evidence.Evidence.Acquisitions);
    }

    [Fact]
    public async Task PackageSummaryEvidence_PreservesOrdinaryResultAndProjectsCachedReceipt()
    {
        string id = $"evidence.summary.{Guid.NewGuid():N}";
        byte[] archive = PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(Nuspec(id, "1.0.0"))),
            ($"lib/net11.0/{id}.dll", new byte[4]));
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(new BrowserPackage(
            id, "1.0.0", archive, fromCache: false,
            producerKey: BrowserPackageWorkspace.Gallery.Source.Producer.Key));
#if DEBUG
        BrowserPackageSummaryEvidenceResult result = Assert.IsType<BrowserPackageSummaryEvidenceResult>(
            JsonSerializer.Deserialize(
                await PackageExports.QueryPackageSummaryWithEvidence(id, "1.0.0", "net11.0"),
                BrowserPackageJsonContext.Default.BrowserPackageSummaryEvidenceResult));
#else
        BrowserPackageSummaryEvidenceResult result =
            await PackageExports.PackageSummaryWithEvidenceAsync(id, "1.0.0", "net11.0");
#endif
        string ordinary = await PackageExports.QueryPackageSummary(id, "1.0.0", "net11.0");
        Assert.Equal(ordinary, JsonSerializer.Serialize(result.Result,
            BrowserPackageJsonContext.Default.BrowserPackageLoadResult));
        BrowserPackageAcquisitionEvidenceEntry acquisition = Assert.Single(result.EvidenceEnvelope.Evidence.Acquisitions);
        Assert.Equal(id.ToLowerInvariant(), acquisition.PackageId);
        Assert.Equal("Cache", acquisition.Transfer.Path);
        Assert.Empty(acquisition.Transfer.Requests);
        Assert.Equal(0, acquisition.Transfer.BytesReceived);
        Assert.Equal(
            JsonSerializer.Serialize(result.Result.VersionSettlement,
                BrowserPackageJsonContext.Default.BrowserPackageVersionSettlementInspection),
            JsonSerializer.Serialize(new BrowserPackageVersionSettlementInspection(
                result.EvidenceEnvelope.Content, result.EvidenceEnvelope.Share, result.EvidenceEnvelope.Diagnostics),
                BrowserPackageJsonContext.Default.BrowserPackageVersionSettlementInspection));
    }

    sealed class FailContinuationHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        int _entryRequests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Headers.Range?.Ranges.Any(range => range.From is not null) == true
                && ++_entryRequests > 1)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
