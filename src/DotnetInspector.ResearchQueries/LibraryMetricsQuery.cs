using ILInspector.Analysis;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>Typed result of composing a whole-library metrics report.</summary>
public abstract record LibraryMetricsResult
{
    private LibraryMetricsResult()
    {
    }

    /// <summary>The Research-owned document for one exact library.</summary>
    public sealed record Available(
        LibraryStructuralReportDocument Document)
        : LibraryMetricsResult;

    /// <summary>
    /// Research could not issue a whole-library report from the supplied
    /// Analysis evidence, while preserving the Research unavailable outcome.
    /// </summary>
    public sealed record Unavailable(
        LibraryStructuralReportResult.Unavailable Outcome)
        : LibraryMetricsResult;

    /// <summary>The image contains no managed metadata and therefore has no method bodies.</summary>
    public sealed record NoMetadata : LibraryMetricsResult;

    /// <summary>The query failed while acquiring or composing the report.</summary>
    public sealed record Failed(Exception Error) : LibraryMetricsResult;
}

/// <summary>
/// Carries the completed Research-owned Library Metrics document from already
/// produced Analysis implementation-profile evidence.
/// </summary>
public static class LibraryMetricsQuery
{
    public static InspectionQuery<LibraryMetricsResult> Definition { get; } =
        new("Library metrics", InspectionCost.Unbounded);

    public static LibraryMetricsResult Execute(
        LibraryBodyAnalysisExecution analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return Execute(
            analysis.ImplementationProfiles,
            analysis.CallGraph);
    }

    public static LibraryMetricsResult Execute(
        LibraryImplementationProfileAnalysisResult analysis,
        LibraryCallGraphAnalysisResult? callGraph = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        try
        {
            return LibraryStructuralReport.Execute(
                analysis,
                callGraph) switch
            {
                LibraryStructuralReportResult.Available available =>
                    new LibraryMetricsResult.Available(available.Document),
                LibraryStructuralReportResult.Unavailable unavailable =>
                    new LibraryMetricsResult.Unavailable(unavailable),
                var unknown => throw new InvalidOperationException(
                    "Unknown Library Metrics report result "
                    + $"'{unknown.GetType().Name}'."),
            };
        }
        catch (Exception ex)
        {
            return new LibraryMetricsResult.Failed(ex);
        }
    }
}
