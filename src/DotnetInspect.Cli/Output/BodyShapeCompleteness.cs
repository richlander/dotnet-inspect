using ILInspector.Decompiler;

namespace DotnetInspect.Cli.Output;

/// <summary>
/// Body Shapes search completeness under <c>docs/design/body-shape-views.md</c>:
/// a body the search could not inspect at Full fidelity contributes no rows but
/// leaves the search incomplete, because nothing proves it lacks the selected
/// Kind. Rows and Count report what was observed; the incompleteness is
/// disclosed on stderr and the command exits nonzero.
/// </summary>
internal static class BodyShapeCompleteness
{
    public static bool IsIncomplete(BodyShapeSearchResult? search) =>
        search is { Failures.Count: > 0 };

    /// <summary>
    /// The default stderr disclosure: the number of bodies not searched and,
    /// when nothing matched, that no match was observed rather than that none
    /// exists.
    /// </summary>
    public static string Warning(BodyShapeSearchResult search)
    {
        int skipped = search.Failures.Count;
        string bodies = skipped == 1
            ? "1 body could not be searched"
            : $"{skipped} bodies could not be searched";
        string observed = search.Matches.Count == 0
            ? "no matches were observed, but "
            : string.Empty;
        return $"Body Shapes inspection incomplete: {observed}{bodies}; "
            + "rerun with --verbose for details.";
    }
}
