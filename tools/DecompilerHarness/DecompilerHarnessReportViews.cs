using DotnetInspector.HarnessReports;

namespace ILInspector.DecompilerHarness;

internal static class DecompilerHarnessReportViews
{
    public static IReadOnlyList<(string Metric, string Value)> Metadata(IDecompilerHarnessReport report)
        =>
        [
            ("Kind", report.Descriptor.Id),
            ("Schema", report.Descriptor.SchemaVersion.ToString()),
            ("Disposition", report.Disposition.ToString()),
            ("Blockers", report.Blockers.Count.ToString()),
            ("Artifacts", report.Artifacts.Count.ToString()),
        ];
}
