namespace DotnetInspector.Sections;

/// <summary>
/// Graph text names an edge's relationship only where it distinguishes the
/// edge: in a graph that mixes relationships, and never for the mainline
/// package dependency (docs/design/dependency-inspection-command.md). Rows
/// and JSON always carry the relationship.
/// </summary>
public static class DependencyGraphEdgeText
{
    private const string PackageDependency = "package-dependency";

    public static bool MixesRelationships(DependencyGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Edges.Select(static edge => edge.Relationship)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any();
    }

    public static string? Label(
        bool mixesRelationships,
        string relationship) =>
        mixesRelationships
        && !string.Equals(
            relationship,
            PackageDependency,
            StringComparison.Ordinal)
            ? relationship
            : null;
}
