using NuGetFetch;

namespace DotnetInspector.Packages;

public sealed partial class DesktopPackageSourceComposition
{
    /// <summary>
    /// Realizes exactly one Library from a caller-pinned package coordinate.
    /// </summary>
    public Task<PackageLibraryRealizationResult> RealizeLibraryAsync(
        PackageSourceCoordinate coordinate,
        string targetFramework,
        PackageLibrarySelector selector,
        PackageLibraryRealizationDepth depth,
        PackageStoreProvider createStore,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null,
        string? requiredProducerKey = null,
        PackageHouseLibraryCompanionDemand companionDemand =
            PackageHouseLibraryCompanionDemand.None,
        long rangedSizeCut = PackageRangedRead.DefaultSizeCut) =>
        RealizeLibraryAsync(
            new PackageHouseDemand.Exact(coordinate),
            coordinate.PackageId,
            targetFramework,
            selector,
            depth,
            createStore,
            sourceOptions,
            log,
            cancellationToken,
            limits,
            transferPolicy,
            requiredProducerKey,
            companionDemand,
            rangedSizeCut);

    /// <summary>
    /// Selects a package version through PackageHouse and realizes exactly one
    /// Library from the selected coordinate.
    /// </summary>
    public Task<PackageLibraryRealizationResult> RealizeSelectedLibraryAsync(
        string packageId,
        string? versionSelector,
        string targetFramework,
        PackageLibrarySelector selector,
        PackageLibraryRealizationDepth depth,
        PackageStoreProvider createStore,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null,
        bool includePrerelease = false,
        string? rangeAddress = null,
        CancellationToken cancellationToken = default,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null,
        PackageHouseLibraryCompanionDemand companionDemand =
            PackageHouseLibraryCompanionDemand.None,
        long rangedSizeCut = PackageRangedRead.DefaultSizeCut)
    {
        if (sourceOptions?.AuthorizedSourceKeys is not null
            || sourceOptions?.ResolvedSources is not null)
        {
            throw new ArgumentException(
                "Selected Library realization requires configured sources, not legacy producer or resolved-source restrictions.",
                nameof(sourceOptions));
        }
        if (!TryCreateSelectionRequest(
                packageId,
                versionSelector,
                includePrerelease,
                rangeAddress,
                out PackageVersionSelectionRequest? selection,
                out ConfiguredPackagePayloadResult? failure))
        {
            throw new ArgumentException(
                failure!.Failures.FirstOrDefault()?.Message
                    ?? "The package version selection is invalid.",
                nameof(versionSelector));
        }

        return RealizeLibraryAsync(
            new PackageHouseDemand.Selecting(selection!),
            packageId,
            targetFramework,
            selector,
            depth,
            createStore,
            sourceOptions,
            log,
            cancellationToken,
            limits,
            transferPolicy,
            requiredProducerKey: null,
            companionDemand,
            rangedSizeCut);
    }

    private Task<PackageLibraryRealizationResult> RealizeLibraryAsync(
        PackageHouseDemand package,
        string packageId,
        string targetFramework,
        PackageLibrarySelector selector,
        PackageLibraryRealizationDepth depth,
        PackageStoreProvider createStore,
        NuGetSourceOptions? sourceOptions,
        Action<string>? log,
        CancellationToken cancellationToken,
        PackagePayloadLimits? limits,
        IPackagePayloadTransferPolicy? transferPolicy,
        string? requiredProducerKey,
        PackageHouseLibraryCompanionDemand companionDemand,
        long rangedSizeCut)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(createStore);
        var request = new PackageLibraryRealizationRequest(
            package,
            targetFramework,
            selector,
            depth,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize,
                _options.RequestTimeout,
                _options.OperationTimeout),
            companionDemand);
        PackageSourceOperationLease? sourceOperation =
            IssueHouseOperation(cancellationToken);
        try
        {
            IPackageSourceAuthorization authorization =
                AuthorizeHouseSources(
                    packageId,
                    sourceOptions,
                    requiredProducerKey);
            Task<PackageLibraryRealizationResult> execution =
                PackageLibraryRealization.ExecuteAsync(
                    request,
                    authorization,
                    new PackageLibraryRealizationPlan(
                        createStore,
                        limits,
                        transferPolicy,
                        log,
                        rangedSizeCut),
                    sourceOperation);
            sourceOperation = null;
            return execution;
        }
        finally
        {
            sourceOperation?.Dispose();
        }
    }
}
