using DotnetInspector.Models;
using ILInspector.Findings;
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
    public static void Apply(this LibraryInspection inspection, ResourceTriageScan scan)
    {
        inspection.ResourceLifecycleInspection = scan.Inspection;
        inspection.ResourceTriage = scan.Triage;
    }
}
