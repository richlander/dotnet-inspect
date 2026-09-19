using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes Library Query through the shared host-neutral envelope boundary.
/// </summary>
public static class LibraryQueryInspection
{
    public static InspectionEnvelope<LibraryQueryDocument> Execute(
        LibraryQueryPopulation population,
        LibraryQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(plan);

        LibraryQueryDocument content = LibraryQuery.Execute(population, plan);
        return new(
            content,
            new InspectionShare.NonProjectable(
                "library-query/share",
                "Library Query plans do not yet have a canonical Workspace Share projection."));
    }
}
