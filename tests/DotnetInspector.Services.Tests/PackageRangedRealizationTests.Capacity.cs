using System.Net;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageRangedRealizationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RangedCapacity_AwaitsBeforeColdWarmAndPartialEntryBodies(int cacheState)
    {
        byte[] archive = ReadPclStorage();
        var store = new InMemoryPackageStore();
        if (cacheState > 0)
        {
            await using RangedEnvironment prime = RangedEnvironment.Create(
                new RangeFeed(PclStorage, PclStorageVersion, archive));
            await prime.RealizeAsync(store, PackagePayloadAccess.Ranged, "net45");
            if (cacheState == 2)
                store.RemoveEntryForTesting(PclStorage, PclStorageVersion, "lib/net45/PCLStorage.xml");
        }
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);
        var policy = new RangedCapacityPolicy();
        Task<PackageHouseSettlement> acquisition = environment.RealizeAsync(
            store, PackagePayloadAccess.Ranged, "net45", 0, PclStorage, PclStorageVersion,
            transferPolicy: policy);
        await policy.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(acquisition.IsCompleted);
        Assert.Equal(cacheState == 0 ? 1 : 0, server.RangedRequests);
        policy.Release.SetResult();
        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(await acquisition);
        var content = Assert.IsType<RangedPackageContent>(acquired.Payload.Content);
        Assert.Equal(content.MaterializedBytes, policy.ExpandedBytes);
        Assert.Same(content, policy.Published);
        Assert.True(policy.Disposed);
        if (cacheState == 1)
            Assert.Equal(0, server.FullRequests + server.RangedRequests);
    }

    [Fact]
    public async Task RangedCapacity_FallbackReleasesSelectedChargeBeforeCompleteTransfer()
    {
        var server = new RangeFeed(PclStorage, PclStorageVersion, ReadPclStorage())
        {
            EntryRangeStatus = HttpStatusCode.BadGateway,
        };
        await using RangedEnvironment environment = RangedEnvironment.Create(server);
        var policy = new RangedCapacityPolicy();
        policy.Release.SetResult();
        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged, "net45", 0, PclStorage, PclStorageVersion,
                transferPolicy: policy));
        Assert.Equal(PackagePayloadOrigin.Download, acquired.Payload.Origin);
        Assert.Null(policy.Published);
        Assert.True(policy.DisposedBeforeCompleteTransfer);
    }

    sealed class RangedCapacityPolicy : IPackagePayloadTransferPolicy,
        IPackageRangedContentPolicy, IPackageRangedContentReservation
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal long ExpandedBytes { get; private set; }
        internal RangedPackageContent? Published { get; private set; }
        internal bool Disposed { get; private set; }
        internal bool DisposedBeforeCompleteTransfer { get; private set; }

        public ValueTask<IPackagePayloadReservation> ReserveAsync(
            PackagePayloadTransfer transfer, CancellationToken cancellationToken = default)
        {
            if (Entered.Task.IsCompleted)
                DisposedBeforeCompleteTransfer = Disposed;
            return ValueTask.FromResult<IPackagePayloadReservation>(new CompleteReservation());
        }

        public async ValueTask<IPackageRangedContentReservation> ReserveRangedAsync(
            PackageSourceCoordinate coordinate, long expandedBytes, CancellationToken cancellationToken)
        {
            ExpandedBytes = expandedBytes;
            Entered.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return this;
        }

        public void Complete(RangedPackageContent content) => Published = content;
        public void Dispose() => Disposed = true;

        sealed class CompleteReservation : IPackagePayloadReservation
        {
            public void Complete() { }
            public void Dispose() { }
        }
    }
}
