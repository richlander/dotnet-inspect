using DotnetInspector.PackageQueries;

namespace DotnetInspector.Sections;

/// <summary>
/// One available Diff History document completed with an exact section Count
/// outcome.
/// </summary>
public sealed record DiffHistorySectionAvailable :
    DiffHistoryOutcome.Available
{
    internal DiffHistorySectionAvailable(
        DiffHistoryDocument document,
        SectionCountOutcome<
            DiffHistoryCountCohort,
            DiffHistoryChangedVersionCountEvidence>? count)
        : base(document)
    {
        Count = count;
    }

    public SectionCountOutcome<
        DiffHistoryCountCohort,
        DiffHistoryChangedVersionCountEvidence>? Count { get; }
}
