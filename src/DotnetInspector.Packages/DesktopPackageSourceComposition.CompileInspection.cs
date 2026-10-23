using NuGetFetch;

namespace DotnetInspector.Packages;

public sealed partial class DesktopPackageSourceComposition
{
    /// <summary>
    /// Acquires package-wide directory evidence and the selected compile
    /// assets for one exact coordinate.
    /// </summary>
    public Task<ConfiguredPackagePayloadResult>
        AcquirePinnedCompileInspectionAsync(
            string packageId,
            string version,
            PackageHouseTargetContext target,
            PackageStoreProvider createStore,
            NuGetSourceOptions? sourceOptions = null,
            Action<string>? log = null,
            CancellationToken cancellationToken = default,
            PackagePayloadLimits? limits = null,
            IPackagePayloadTransferPolicy? transferPolicy = null,
            long rangedSizeCut = PackageRangedRead.DefaultSizeCut)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(createStore);

        PackageSourceOperationLease? sourceOperation =
            IssueHouseOperation(cancellationToken);
        try
        {
            if (!PackageExtractor.IsValidPackageId(packageId)
                || !PackageExtractor.TryNormalizePackageVersion(
                    version,
                    out string normalizedVersion))
            {
                return Task.FromResult(
                    InvalidSelection(
                        "Compile-inventory acquisition requires a valid package ID and an exact version."));
            }

            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(
                    packageId,
                    normalizedVersion);
            PackageHouseRequest request =
                PackageHouseRequest.CompileInspection(
                    new PackageHouseDemand.Exact(coordinate),
                    CreateHouseOperation(
                        PackageHouseOperationProfile.Realize),
                    target);
            Task<ConfiguredPackagePayloadResult> execution =
                ExecuteAndProjectPayloadAsync(
                    request,
                    packageId,
                    sourceOptions,
                    PackagePayloadAcquisitionPlan.ForContentQueries(
                        createStore,
                        limits,
                        transferPolicy,
                        log,
                        rangedSizeCut),
                    sourceOperation,
                    requiredProducerKey: null);
            sourceOperation = null;
            return execution;
        }
        finally
        {
            sourceOperation?.Dispose();
        }
    }

    /// <summary>
    /// Selects a package coordinate and acquires package-wide directory
    /// evidence plus the selected compile assets.
    /// </summary>
    public Task<ConfiguredPackagePayloadResult>
        AcquireSelectedCompileInspectionAsync(
            string packageId,
            string? versionSelector,
            PackageHouseTargetContext target,
            PackageStoreProvider createStore,
            NuGetSourceOptions? sourceOptions = null,
            Action<string>? log = null,
            bool includePrerelease = false,
            string? rangeAddress = null,
            CancellationToken cancellationToken = default,
            PackagePayloadLimits? limits = null,
            IPackagePayloadTransferPolicy? transferPolicy = null,
            long rangedSizeCut = PackageRangedRead.DefaultSizeCut)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(createStore);
        if (sourceOptions?.AuthorizedSourceKeys is not null
            || sourceOptions?.ResolvedSources is not null)
        {
            return Task.FromResult(
                InvalidSelection(
                    "Selected compile-inventory acquisition requires configured sources, not legacy producer or resolved-source restrictions."));
        }
        if (!TryCreateSelectionRequest(
                packageId,
                versionSelector,
                includePrerelease,
                rangeAddress,
                out PackageVersionSelectionRequest? selection,
                out ConfiguredPackagePayloadResult? failure))
        {
            return Task.FromResult(failure!);
        }

        PackageSourceOperationLease? sourceOperation =
            IssueHouseOperation(cancellationToken);
        try
        {
            PackageHouseRequest request =
                PackageHouseRequest.CompileInspection(
                    new PackageHouseDemand.Selecting(selection!),
                    CreateHouseOperation(
                        PackageHouseOperationProfile.Realize),
                    target);
            Task<ConfiguredPackagePayloadResult> execution =
                ExecuteAndProjectPayloadAsync(
                    request,
                    packageId,
                    sourceOptions,
                    PackagePayloadAcquisitionPlan.ForContentQueries(
                        createStore,
                        limits,
                        transferPolicy,
                        log,
                        rangedSizeCut),
                    sourceOperation,
                    requiredProducerKey: null);
            sourceOperation = null;
            return execution;
        }
        finally
        {
            sourceOperation?.Dispose();
        }
    }
}
