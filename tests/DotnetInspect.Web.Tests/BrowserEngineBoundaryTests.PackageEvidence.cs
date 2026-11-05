using System.Text;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
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
        var captured = new List<PackageHouseAcquisitionReceipt>();
        BrowserPackageRealizationResult result = await BrowserPackageWorkspace.RealizeWithSettlementAsync(
            id, "1.0.0", "net11.0", source, TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken, captured.Add);
        var realized = Assert.IsType<BrowserPackageRealizationResult.Realized>(result);
        var evidence = BrowserPackageSummaryEvidence.Capture(result);
        Assert.Same(realized.Realization.VersionSettlement, evidence.Inspection);
        Assert.Equal(2, evidence.Evidence.Acquisitions.Length);
        Assert.Equal(2, captured.Count);
        Assert.Same(result.Acquisition, captured[0]);
        Assert.Same(result.Continuation!.Acquisition, captured[1]);
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
        captured.Clear();
        BrowserPackageRealizationResult warm = await BrowserPackageWorkspace.RealizeWithSettlementAsync(
            id, "1.0.0", "net11.0", source, TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken, captured.Add);
        Assert.Empty(BrowserPackageSummaryEvidence.Capture(warm).Evidence.Acquisitions);
        Assert.Empty(captured);
        Assert.Equal(requests, handler.Requested.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SupplierEvidence_ContinuationFailureKeepsSettledRoot(bool cancelled, bool sharedWaiter)
    {
        string id = $"evidence.supplier.continuation.{Guid.NewGuid():N}";
        byte[] assembly = File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        var packageHandler = new GalleryPackageHandler(id, "1.0.0", PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(Nuspec(id, "1.0.0"))),
            ($"lib/net11.0/{id}.dll", assembly),
            ($"runtimes/linux-x64/lib/net11.0/{id}.dll", assembly),
            ("content/padding.bin", new byte[2 * MiB])));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var handler = new StallContinuationHandler(
            packageHandler, cancelled ? cancellation.Cancel : null);
        using IPackageSourceClient source = Gallery(handler);
        Task<BrowserReferencePackageSupplierEvidenceInspection> first = InspectAsync();
        Task<BrowserReferencePackageSupplierEvidenceInspection>? second = null;
        if (sharedWaiter)
        {
            await handler.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            second = InspectAsync();
        }
        BrowserReferencePackageSupplierEvidenceInspection[] results = second is null
            ? [await first]
            : [await first, await second];
        await handler.Stopped.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(handler.ContinuationStarted);
        foreach (BrowserReferencePackageSupplierEvidenceInspection wire in results)
        {
            Assert.Equal("failed", wire.Content.Outcome);
            Assert.Equal(cancelled ? "Cancelled" : "Timeout", wire.Content.Failure!.Kind);
            Assert.Null(wire.Content.Suppliers);
            BrowserPackageAcquisitionEvidenceEntry root = Assert.Single(wire.Evidence.Acquisitions);
            Assert.Equal(id.ToLowerInvariant(), root.PackageId);
            Assert.InRange(root.Transfer.BytesReceived, 1, packageHandler.PackageBytesServed - 1);
            Assert.Equal(packageHandler.RangedPackageResponses - 1, root.Transfer.RequestCount);
        }

        async Task<BrowserReferencePackageSupplierEvidenceInspection> InspectAsync()
        {
            var enriched = await BrowserReferencePackageSupplierInspection.ExecuteWithEvidenceAsync(
                async capture =>
                {
                    await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                        id, "1.0.0", "net11.0", source, TimeSpan.FromSeconds(2),
                        cancellation.Token, capture);
                    return [];
                });
            return BrowserPackageWireProjection.Project(enriched);
        }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupplierEvidence_KeepsBaselineFailureAndEarlierAcquisition(bool cancelled)
    {
        string id = $"evidence.supplier.failure.{Guid.NewGuid():N}";
        var handler = new GalleryPackageHandler(id, "1.0.0", PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(Nuspec(id, "1.0.0"))),
            ($"lib/net11.0/{id}.dll", new byte[4])));
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageRealizationResult realization =
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                id, "1.0.0", "net11.0", source, TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
        var acquisition = Assert.IsType<BrowserPackageRealizationResult.Realized>(realization).Acquisition!;
        Exception failure = cancelled
            ? new OperationCanceledException("Fixture cancellation")
            : new TimeoutException("Fixture deadline");
        int executions = 0;
        var enriched = await BrowserReferencePackageSupplierInspection.ExecuteWithEvidenceAsync(capture =>
        {
            executions++;
            capture!(acquisition);
            return Task.FromException<PackageAssemblyReferenceSupplierCoordinate[]>(failure);
        });
        Assert.Equal(1, executions);
        Assert.Same(failure,
            Assert.IsType<BrowserReferencePackageSupplierOutcome.Failed>(enriched.Inspection.Content).Error);
        Assert.Same(acquisition.Transfer, Assert.Single(enriched.Evidence.Acquisitions).Transfer);

        BrowserReferencePackageSupplierEvidenceInspection wire = BrowserPackageWireProjection.Project(enriched);
        wire = Assert.IsType<BrowserReferencePackageSupplierEvidenceInspection>(
            JsonSerializer.Deserialize(
                JsonSerializer.Serialize(wire,
                    BrowserPackageJsonContext.Default.BrowserReferencePackageSupplierEvidenceInspection),
                BrowserPackageJsonContext.Default.BrowserReferencePackageSupplierEvidenceInspection));
        Assert.Equal("failed", wire.Content.Outcome);
        Assert.Null(wire.Content.Suppliers);
        Assert.Equal(cancelled ? "Cancelled" : "Timeout", wire.Content.Failure!.Kind);
        Assert.Equal(failure.Message, wire.Content.Failure.Detail);
        Assert.Equal(handler.PackageBytesServed,
            Assert.Single(wire.Evidence.Acquisitions).Transfer.BytesReceived);
    }

    [Fact]
    public async Task SupplierEvidence_FailureBeforeAcquisitionIsNotAnEmptySuccess()
    {
        var enriched = await BrowserReferencePackageSupplierInspection.ExecuteWithEvidenceAsync(
            _ => Task.FromException<PackageAssemblyReferenceSupplierCoordinate[]>(
                new InvalidOperationException("Fixture failure")));
        BrowserReferencePackageSupplierEvidenceInspection wire = BrowserPackageWireProjection.Project(enriched);
        Assert.Equal("failed", wire.Content.Outcome);
        Assert.Equal("Unexpected", wire.Content.Failure!.Kind);
        Assert.Null(wire.Content.Suppliers);
        Assert.Empty(wire.Evidence.Acquisitions);
    }

    [Fact]
    public async Task SupplierEvidence_ExportKeepsRootReceiptAndOrdinaryException()
    {
        string id = $"evidence.supplier.library.{Guid.NewGuid():N}";
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(new BrowserPackage(
            id, "1.0.0", PackageEntries(
                ($"{id}.nuspec", Encoding.UTF8.GetBytes(Nuspec(id, "1.0.0"))),
                ($"lib/net11.0/{id}.dll", File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location))),
            fromCache: false, producerKey: BrowserPackageWorkspace.Gallery.Source.Producer.Key));
        const string missingLibrary = "compile:lib/net11.0/absent.dll";
        BrowserReferencePackageSupplierEvidenceInspection enriched =
            await PackageExports.ReferencePackageSuppliersWithEvidenceAsync(
                id, "1.0.0", "net11.0", missingLibrary);
        Exception ordinary = await Assert.ThrowsAnyAsync<Exception>(() =>
            PackageExports.QueryReferencePackageSuppliers(id, "1.0.0", "net11.0", missingLibrary));
        Assert.Equal("failed", enriched.Content.Outcome);
        Assert.Null(enriched.Content.Suppliers);
        Assert.Equal("Unexpected", enriched.Content.Failure!.Kind);
        Assert.Equal(ordinary.Message, enriched.Content.Failure.Detail);
        BrowserPackageAcquisitionEvidenceEntry acquisition = Assert.Single(enriched.Evidence.Acquisitions);
        Assert.Equal(id.ToLowerInvariant(), acquisition.PackageId);
        Assert.Equal("Cache", acquisition.Transfer.Path);
        Assert.Empty(acquisition.Transfer.Requests);
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

    sealed class StallContinuationHandler(HttpMessageHandler inner, Action? cancel)
        : DelegatingHandler(inner)
    {
        int _entryRequests;
        public bool ContinuationStarted { get; private set; }
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Range?.Ranges.Any(range => range.From is not null) == true
                && ++_entryRequests > 1)
            {
                ContinuationStarted = true;
                Started.TrySetResult();
                cancel?.Invoke();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    Stopped.TrySetResult();
                }
            }
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
