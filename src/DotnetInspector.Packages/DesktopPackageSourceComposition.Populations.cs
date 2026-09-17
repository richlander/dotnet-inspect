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
            CancellationToken cancellationToken = default,
            Action<string>? log = null)
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
                    authorization),
                log: log);
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
        PackageHouseVersionPopulationCellExecution execution =
            cell.PrepareExecution(
                operation,
                targetContext,
                assetSelection,
                libraryHandoff);
        return ExecuteVersionPopulationCellAsync(
            execution,
            payloadAcquisition,
            sourceOptions,
            cancellationToken);
    }

    /// <summary>
    /// Executes one PackageHouse-issued exact version-population cell request.
    /// </summary>
    public Task<PackageHouseSettlement>
        ExecuteVersionPopulationCellAsync(
            PackageHouseVersionPopulationCellExecution execution,
            PackagePayloadAcquisitionPlan? payloadAcquisition = null,
            NuGetSourceOptions? sourceOptions = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        PackageHouseRequest request = execution.Request;
        var demand = (PackageHouseDemand.Candidate)request.Demand;
        PackageSourceOperationLease sourceOperation =
            _sourceLease.IssueOperationLease(
                cancellationToken,
                request.Operation.RequestTimeout,
                request.Operation.OperationTimeout);
        return ExecuteHouseCoreAsync(
            request,
            demand.Value.Coordinate.PackageId,
            sourceOptions,
            payloadAcquisition,
            sourceOperation,
            requiredProducerKey: null);
    }
}
