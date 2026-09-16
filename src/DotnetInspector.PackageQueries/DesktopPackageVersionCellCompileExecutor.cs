using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Desktop adapter for compile-only execution of one PackageHouse
/// version-population cell.
/// </summary>
public sealed class DesktopPackageVersionCellCompileExecutor(
    DesktopPackageSourceComposition composition,
    PackagePayloadAcquisitionPlan payloadAcquisition,
    NuGetSourceOptions? sourceOptions = null)
    : IPackageVersionCellCompileExecutor
{
    private readonly DesktopPackageSourceComposition _composition =
        composition ?? throw new ArgumentNullException(nameof(composition));

    private readonly PackagePayloadAcquisitionPlan _payloadAcquisition =
        payloadAcquisition
        ?? throw new ArgumentNullException(nameof(payloadAcquisition));

    public Task<PackageHouseSettlement> ExecuteAsync(
        PackageHouseVersionPopulationCell cell,
        PackageHouseOperation operation,
        PackageHouseTargetContext? targetContext,
        CancellationToken cancellationToken = default) =>
        _composition.ExecuteVersionPopulationCellAsync(
            cell,
            operation,
            _payloadAcquisition,
            targetContext,
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.PackageOnly,
            sourceOptions,
            cancellationToken);
}

