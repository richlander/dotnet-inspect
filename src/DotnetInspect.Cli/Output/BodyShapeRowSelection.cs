using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Sections;
using ILInspector.Decompiler;
using QuerySpace.Rows;

namespace DotnetInspect.Cli.Output;

/// <summary>
/// The rows one semantic Body Shapes selection retained: occurrences for
/// <c>Body Shapes</c>, or summary groups with their complete Counts for
/// <c>Body Shape Summary</c>. The other view's rows stay unselected.
/// </summary>
internal sealed record BodyShapeRowSelection(
    IReadOnlyList<BodyShapeMatch>? Matches,
    IReadOnlyList<BodyShapeSummary>? Summary)
{
    /// <summary>
    /// Applies <paramref name="intent"/> to the selected view's rows in search
    /// order. Summary groups form before selection, so a selected group keeps
    /// its complete occurrence Count. Writes the error and returns false when a
    /// strict Window is unavailable.
    /// </summary>
    public static bool TrySelect(
        RowSelectionIntent<string>? intent,
        IReadOnlyCollection<string>? sections,
        IReadOnlyList<BodyShapeMatch> matches,
        out BodyShapeRowSelection? selection)
    {
        selection = null;
        if (intent is null)
            return true;

        if (SelectsSummary(sections))
        {
            if (!TrySelectRows(
                    intent,
                    SectionNames.BodyShapeSummary,
                    BodyShapeSummary.FromMatches(matches),
                    out IReadOnlyList<BodyShapeSummary> groups))
            {
                return false;
            }

            selection = new(null, groups);
            return true;
        }

        if (!TrySelectRows(
                intent,
                SectionNames.BodyShapes,
                matches,
                out IReadOnlyList<BodyShapeMatch> selected))
        {
            return false;
        }

        selection = new(selected, null);
        return true;
    }

    /// <summary>
    /// The match count after which the search may stop: the selection's
    /// required prefix of occurrences. Summary groups need every occurrence for
    /// their Counts, and Tail needs the complete search, so both search
    /// everything.
    /// </summary>
    public static int? SearchLimit(
        RowSelectionIntent<string>? intent,
        IReadOnlyCollection<string>? sections) =>
        intent is null || SelectsSummary(sections)
            ? null
            : intent.RequiredPrefix();

    /// <summary>
    /// True when <paramref name="sections"/> selects <c>Body Shape Summary</c>,
    /// whose rows are groups rather than occurrences.
    /// </summary>
    public static bool SelectsSummary(IReadOnlyCollection<string>? sections) =>
        sections?.Contains(
            SectionNames.BodyShapeSummary,
            StringComparer.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Selects one Body Shapes view's rows, already in search order: one
    /// occurrence per <c>Body Shapes</c> row, or one group per
    /// <c>Body Shape Summary</c> row.
    /// </summary>
    public static bool TrySelectRows<T>(
        RowSelectionIntent<string> intent,
        string section,
        IReadOnlyList<T> rows,
        out IReadOnlyList<T> selected)
    {
        string unit = section.Equals(
                SectionNames.BodyShapeSummary,
                StringComparison.OrdinalIgnoreCase)
            ? "group"
            : "occurrence";
        return CliSemanticRowSelection.TrySelect(
            intent,
            rows,
            section,
            failure => Failure(section, failure.Failure, unit),
            out selected);
    }

    private static string Failure(
        string section,
        RowWindowFailure failure,
        string unit) =>
        $"{section} row selection stage {failure.StageNumber} requires row "
        + $"{failure.RequiredPosition}, but only {failure.AvailableCount} "
        + $"{unit}{(failure.AvailableCount == 1 ? " is" : "s are")} available.";
}
