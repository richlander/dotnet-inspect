namespace DotnetInspector.Packages;

public sealed partial class DesktopPackageSourceComposition
{
    /// <summary>
    /// Settles one complete configured-source package version population
    /// through PackageHouse.
    /// </summary>
    public async Task<PackageHouseVersionPopulationResult>
        SettleVersionPopulationAsync(
            PackageHouseVersionPopulationRequest request,
            NuGetSourceOptions? sourceOptions = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PackageSourceOperationLease? sourceOperation =
            _sourceLease.IssueOperationLease(
                cancellationToken,
                request.Operation.RequestTimeout,
                request.Operation.OperationTimeout);
        try
        {
            var failures = new List<PackageAuthorityFailure>();
            PackageSourceAuthorization authorization =
                AuthorizeSourcesForCore(
                    request.Range.PackageId,
                    sourceOptions,
                    static () => { },
                    failures);
            var house = new PackageHouse(
                new SinglePackageAuthorization(
                    request.Range.PackageId,
                    authorization));
            Task<PackageHouseVersionPopulationResult> execution =
                house.SettleVersionPopulationAsync(
                    request,
                    sourceOperation);
            sourceOperation = null;
            return await execution.ConfigureAwait(false);
        }
        finally
        {
            sourceOperation?.Dispose();
        }
    }

    /// <summary>
    /// Executes one owner-issued version-population cell as an ordinary
    /// candidate-bound PackageHouse request.
    /// </summary>
    public Task<PackageHouseSettlement>
        ExecuteVersionPopulationCellAsync(
            PackageHouseVersionPopulationCell cell,
            PackageHouseOperation operation,
            PackagePayloadAcquisitionPlan? payloadAcquisition = null,
            PackageHouseTargetContext? targetContext = null,
            PackageHouseAssetSelectionKind? assetSelection = null,
            PackageHouseLibraryHandoffMode libraryHandoff =
                PackageHouseLibraryHandoffMode.PackageOnly,
            NuGetSourceOptions? sourceOptions = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(operation);
        PackageHouseRequest request = cell.CreateRequest(
            operation,
            targetContext,
            assetSelection,
            libraryHandoff);
        PackageSourceOperationLease sourceOperation =
            _sourceLease.IssueOperationLease(
                cancellationToken,
                operation.RequestTimeout,
                operation.OperationTimeout);
        return ExecuteHouseCoreAsync(
            request,
            cell.Candidate.Coordinate.PackageId,
            sourceOptions,
            payloadAcquisition,
            sourceOperation,
            requiredProducerKey: null);
    }
}
