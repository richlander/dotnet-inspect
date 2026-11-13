using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

internal static class PackageRootTestHouseExecution
{
    internal static PackageRootHouseExecution Create(
        WorkspaceContextLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            new Executor(options));
    }

    private sealed class Executor(WorkspaceContextLoadOptions options)
        : IPackageRootHouseExecutor
    {
        public async Task<PackageHouseSettlement> ExecuteAsync(
            PackageHouseRequest request,
            string? requiredProducerKey,
            CancellationToken cancellationToken = default)
        {
            string packageId = request.Demand switch
            {
                PackageHouseDemand.Exact exact =>
                    exact.Coordinate.PackageId,
                _ => throw new ArgumentException(
                    "Package Root test execution requires an exact demand.",
                    nameof(request)),
            };
            PackageSourceAuthorization authorization =
                options.SourceAuthorization.AuthorizeSourcesFor(
                    packageId);
            var sourceOptions = new NuGetSourceOptions
            {
                Sources =
                [
                    .. authorization.Sources.Select(
                        static source => source.Url),
                ],
            };
            await using var composition =
                new DesktopPackageSourceComposition(
                    request.Operation.RequestTimeout);
            PackageStoreProvider getStore =
                (_, _) => new EntryOpaquePackageStore(options.PackageStore);
            PackagePayloadAcquisitionPlan payloadAcquisition =
                request.ContentQuery is not null
                    ? PackagePayloadAcquisitionPlan.ForContentQueries(
                        getStore,
                        options.PayloadLimits,
                        options.PackageTransferPolicy,
                        options.Log)
                    : new PackagePayloadAcquisitionPlan(
                        getStore,
                        options.PayloadLimits,
                        options.PackageTransferPolicy,
                        options.Log,
                        access: PackagePayloadAccess.Ranged);
            PackageHouse house = authorization.Sources.Count == 0
                && requiredProducerKey is null
                ? new PackageHouse(
                    options.SourceAuthorization,
                    payloadAcquisition,
                    options.Log)
                : composition.CreateRealizationHouse(
                    payloadAcquisition,
                    authorization.Sources.Count == 0
                        ? new NuGetSourceOptions
                        {
                            Sources =
                            [
                                "https://unauthorized.test/v3/index.json",
                            ],
                        }
                        : sourceOptions,
                    requiredProducerKey: requiredProducerKey);
            PackageSourceOperationLease? operation =
                composition.IssueSettlementOperation(cancellationToken);
            try
            {
                Task<PackageHouseSettlement> pending =
                    house.ExecuteAsync(request, operation);
                operation = null;
                return await pending.ConfigureAwait(false);
            }
            finally
            {
                operation?.Dispose();
            }
        }

        private sealed class EntryOpaquePackageStore(
            IPackageStore inner)
            : IPackageStore
        {
            public IPackageContent? TryGetCached(
                string packageName,
                string version,
                IReadOnlyList<string>? allowedSourceKeys,
                Action<string>? log = null) =>
                inner.TryGetCached(
                    packageName,
                    version,
                    allowedSourceKeys,
                    log);

            public IEnumerable<IPackageContent> EnumerateCached(
                string packageName,
                string version,
                IReadOnlyList<string>? allowedSourceKeys,
                Action<string>? log = null) =>
                inner.EnumerateCached(
                    packageName,
                    version,
                    allowedSourceKeys,
                    log);

            public ValueTask<IPackageContent> CommitAsync(
                string packageName,
                string version,
                string sourceKey,
                Stream nupkg,
                CancellationToken cancellationToken = default) =>
                inner.CommitAsync(
                    packageName,
                    version,
                    sourceKey,
                    nupkg,
                    cancellationToken);
        }
    }
}
