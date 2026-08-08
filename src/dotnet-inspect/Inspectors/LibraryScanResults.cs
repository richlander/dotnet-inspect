using DotnetInspector.Models;
using ILInspector.Findings;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Inspectors;

// Results returned by the library scanners that compute a self-contained answer.
//
// A scanner that produces a result returns it rather than writing into LibraryInspection, so the
// computation can run for a caller that has no aggregate to write into. Where a scanner produces
// several correlated values, they travel together in one record rather than as separate writes,
// because they are only meaningful as a set — a census and the display order that census was
// projected from, for instance, must not drift apart.
//
// These are the shapes a typed L1 inspection query would return; see
// docs/design/inspection-layers.md. The assignment into the aggregate stays at the call site,
// which is where it will be deleted once L1 owns the queries.

/// <summary>
/// Extension member census, plus the metadata order it was projected from. The order is retained
/// separately because the census is a finding set (unordered by contract) while the rendered and
/// serialized views need the assembly's own ordering.
/// </summary>
internal readonly record struct ExtensionMemberScan(
    FindingInspection<ExtensionMemberObservation> Inspection,
    IReadOnlyList<ExtensionMethodInfo>? DisplayOrder);

/// <summary>
/// Assembly attribute census, plus the order used for JSON serialization.
/// </summary>
internal readonly record struct AssemblyAttributeScan(
    FindingInspection<AssemblyAttributeInfo> Inspection,
    IReadOnlyList<AssemblyAttributeInfo>? JsonOrder);

/// <summary>
/// Method classification census, plus the three per-classification projections derived from it.
/// All four come from one pass over the same classified-method list, so they are produced together;
/// splitting them into separate scanners would re-walk the assembly three more times.
/// </summary>
internal readonly record struct ClassifiedMethodScan(
    FindingInspection<ClassifiedMethodObservation> Inspection,
    List<ClassifiedMethodSummary>? UnsafeMethods,
    List<ClassifiedMethodSummary>? PInvokeMethods,
    List<AsyncMethodSummary>? AsyncMethods)
{
    /// <summary>
    /// The census alone, with no projections. Used on the failure path, where there is nothing to
    /// project from.
    /// </summary>
    public static ClassifiedMethodScan FromInspectionOnly(
        FindingInspection<ClassifiedMethodObservation> inspection)
        => new(inspection, null, null, null);
}

/// <summary>
/// Resource lifecycle findings, plus the actionable triage rows projected from them. The triage
/// projection needs member drill coordinates, so it is computed here rather than by the view.
/// </summary>
internal readonly record struct ResourceTriageScan(
    FindingInspection<Analysis.ResourceLifecycleOccurrence> Inspection,
    List<ResourceTriageSummary>? Triage);

/// <summary>
/// Writes a scan result into the <see cref="LibraryInspection"/> aggregate. This is the CLI-side
/// adapter between a scanner's result and the aggregate the views read from: it exists so the
/// scanners themselves need not know the aggregate, and it is the code that goes away when L1 owns
/// the queries and the aggregate is decomposed.
/// </summary>
internal static class LibraryScanApply
{
    public static void Apply(this LibraryInspection inspection, ExtensionMemberScan scan)
        => inspection.SetExtensionMemberInspection(scan.Inspection, scan.DisplayOrder);

    public static void Apply(this LibraryInspection inspection, AssemblyAttributeScan scan)
        => inspection.SetAssemblyAttributeInspection(scan.Inspection, scan.JsonOrder);

    public static void Apply(this LibraryInspection inspection, ClassifiedMethodScan scan)
    {
        inspection.ClassifiedMethodInspection = scan.Inspection;
        inspection.UnsafeMethods = scan.UnsafeMethods;
        inspection.PInvokeMethods = scan.PInvokeMethods;
        inspection.AsyncMethods = scan.AsyncMethods;
    }

    public static void Apply(this LibraryInspection inspection, ResourceTriageScan scan)
    {
        inspection.ResourceLifecycleInspection = scan.Inspection;
        inspection.ResourceTriage = scan.Triage;
    }
}
