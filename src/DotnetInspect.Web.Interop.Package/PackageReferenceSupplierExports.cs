using System.Runtime.InteropServices.JavaScript;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Package;

[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    /// <summary>
    /// Associates direct references with terminal Package suppliers after the
    /// ordinary References query. This optional query never changes the direct
    /// AssemblyRef answer or opens a supplier as a Workspace Library.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryReferencePackageSuppliers(
        string packageId,
        string version,
        string targetFramework,
        string assemblyId)
    {
        InspectionEnvelope<BrowserReferencePackageSupplierOutcome> inspection =
            await BrowserReferencePackageSupplierInspection.ExecuteAsync(
                capture => ReferencePackageSupplierOperationAsync(
                    packageId, version, targetFramework, assemblyId, capture))
                .ConfigureAwait(false);
        if (inspection.Content is BrowserReferencePackageSupplierOutcome.Failed failed)
            ExceptionDispatchInfo.Capture(failed.Error).Throw();
        var available = (BrowserReferencePackageSupplierOutcome.Available)inspection.Content;
        return JsonSerializer.Serialize(
            BrowserPackageWireProjection.Project(available.Suppliers),
            BrowserPackageJsonContext.Default.BrowserReferencePackageSupplierArray);
    }

#if DEBUG
    [JSExport]
    public static async Task<string> QueryReferencePackageSuppliersWithEvidence(
        string packageId,
        string version,
        string targetFramework,
        string assemblyId)
    {
        BrowserReferencePackageSupplierEvidenceInspection inspection =
            await ReferencePackageSuppliersWithEvidenceAsync(
                packageId, version, targetFramework, assemblyId).ConfigureAwait(false);
        return JsonSerializer.Serialize(
            inspection,
            BrowserPackageJsonContext.Default.BrowserReferencePackageSupplierEvidenceInspection);
    }
#endif

    internal static async Task<BrowserReferencePackageSupplierEvidenceInspection>
        ReferencePackageSuppliersWithEvidenceAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyId) =>
        BrowserPackageWireProjection.Project(
            await BrowserReferencePackageSupplierInspection.ExecuteWithEvidenceAsync(
                capture => ReferencePackageSupplierOperationAsync(
                    packageId, version, targetFramework, assemblyId, capture))
                .ConfigureAwait(false));

    static Task<PackageAssemblyReferenceSupplierCoordinate[]>
        ReferencePackageSupplierOperationAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyId,
            Action<PackageHouseAcquisitionReceipt>? captureAcquisition) =>
        BrowserPackageWorkspace.RunPackageOperationAsync(
            deadline => ReferencePackageSuppliersAsync(
                packageId, version, targetFramework, assemblyId, deadline, captureAcquisition),
            BrowserPackageWorkspace.PackageOperationTimeout);

    static async Task<PackageAssemblyReferenceSupplierCoordinate[]>
        ReferencePackageSuppliersAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyId,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline,
            Action<PackageHouseAcquisitionReceipt>? captureAcquisition)
    {
        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                packageId,
                version,
                targetFramework,
                runtimeIdentifier: null,
                deadline.Token,
                capture: captureAcquisition is null ? null : result =>
                {
                    for (BrowserPackageRealizationResult? step = result;
                        step is not null; step = step.Continuation)
                    {
                        if (step.Acquisition is { } acquisition)
                            captureAcquisition(acquisition);
                    }
                }).ConfigureAwait(false);
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                deadline.Token).ConfigureAwait(false);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate retained = scope.Coordinate(coordinate);
        if (!retained.Selection.IsSelected)
            return [];

        return await BrowserReferencePackageSuppliers.ResolveAsync(
                scope,
                retained,
                assemblyId,
                deadline,
                PackageDependencyEvidenceAsync,
                captureAcquisition).ConfigureAwait(false);
    }
}
