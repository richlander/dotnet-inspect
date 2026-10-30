using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web;
using DotnetInspector.PackageQueries;

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
        BrowserReferencePackageSupplier[] suppliers =
            await BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => ReferencePackageSuppliersAsync(
                    packageId,
                    version,
                    targetFramework,
                    assemblyId,
                    deadline),
                BrowserPackageWorkspace.PackageOperationTimeout)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            suppliers,
            BrowserPackageJsonContext.Default.BrowserReferencePackageSupplierArray);
    }

    static async Task<BrowserReferencePackageSupplier[]>
        ReferencePackageSuppliersAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyId,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
    {
        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                packageId,
                version,
                targetFramework,
                deadline.Token).ConfigureAwait(false);
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                deadline.Token).ConfigureAwait(false);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate retained = scope.Coordinate(coordinate);
        if (!retained.Selection.IsSelected)
            return [];

        PackageAssemblyReferenceSupplierCoordinate[] selected =
            await BrowserReferencePackageSuppliers.ResolveAsync(
                scope,
                retained,
                assemblyId,
                deadline,
                PackageDependencyEvidenceAsync).ConfigureAwait(false);
        var suppliers = new BrowserReferencePackageSupplier[selected.Length];
        for (int index = 0; index < selected.Length; index++)
        {
            PackageAssemblyReferenceSupplierCoordinate supplier =
                selected[index];
            suppliers[index] = new(
                supplier.Name,
                supplier.Version,
                supplier.Culture,
                supplier.PublicKeyToken,
                supplier.Supplier?.PackageId,
                supplier.Supplier?.Version);
        }
        return suppliers;
    }
}
