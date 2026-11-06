using System.Runtime.Versioning;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using InertText;

namespace DotnetInspect.Web.Interop.Package;

internal abstract record BrowserReferencePackageSupplierOutcome
{
    internal sealed record Available(
        PackageAssemblyReferenceSupplierCoordinate[] Suppliers)
        : BrowserReferencePackageSupplierOutcome;

    internal sealed record Failed(Exception Error)
        : BrowserReferencePackageSupplierOutcome
    {
        internal string Kind => Error switch
        {
            TimeoutException => "Timeout",
            OperationCanceledException => "Cancelled",
            _ => "Unexpected",
        };

        internal InertString Detail { get; } =
            new(TextPolicy.Field, Error.Message);
    }
}

[SupportedOSPlatform("browser")]
internal static class BrowserReferencePackageSupplierInspection
{
    static readonly InspectionShare Share =
        new InspectionShare.NonProjectable(
            "library-reference-package-suppliers/share",
            "Optional Package supplier annotations do not have a canonical Workspace Share projection.");

    internal static Task<InspectionEnvelope<BrowserReferencePackageSupplierOutcome>> ExecuteAsync(
        Func<Action<PackageHouseAcquisitionReceipt>?,
            Task<PackageAssemblyReferenceSupplierCoordinate[]>> operation) =>
        ExecuteAsync(operation, captureAcquisition: null);

    internal static async Task<EvidenceInspectionEnvelope<
        BrowserReferencePackageSupplierOutcome, PackageAcquisitionEvidence>> ExecuteWithEvidenceAsync(
        Func<Action<PackageHouseAcquisitionReceipt>?,
            Task<PackageAssemblyReferenceSupplierCoordinate[]>> operation)
    {
        var acquisitions = new List<PackageAcquisitionEvidenceEntry>();
        InspectionEnvelope<BrowserReferencePackageSupplierOutcome> inspection =
            await ExecuteAsync(
                operation,
                acquisition => acquisitions.Add(PackageAcquisitionEvidenceEntry.From(acquisition)))
                .ConfigureAwait(false);
        return new(inspection, new PackageAcquisitionEvidence([.. acquisitions]));
    }

    static async Task<InspectionEnvelope<BrowserReferencePackageSupplierOutcome>> ExecuteAsync(
        Func<Action<PackageHouseAcquisitionReceipt>?,
            Task<PackageAssemblyReferenceSupplierCoordinate[]>> operation,
        Action<PackageHouseAcquisitionReceipt>? captureAcquisition)
    {
        ArgumentNullException.ThrowIfNull(operation);
        BrowserReferencePackageSupplierOutcome outcome;
        try
        {
            outcome = new BrowserReferencePackageSupplierOutcome.Available(
                await operation(captureAcquisition).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            outcome = new BrowserReferencePackageSupplierOutcome.Failed(exception);
        }
        return new(outcome, Share);
    }
}
