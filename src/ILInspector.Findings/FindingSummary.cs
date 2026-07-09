namespace ILInspector.Findings;

/// <summary>A coarse, stream-agnostic classification of a diff read from the skeleton alone.</summary>
public enum DiffShape
{
    /// <summary>No differences at all.</summary>
    Identical,

    /// <summary>The only differences are relocations (a reorder); the content set is unchanged.</summary>
    ReorderOnly,

    /// <summary>Content was added, removed, or changed.</summary>
    Structural,
}

/// <summary>
/// A minimal, stream-agnostic consumer of finding rows. It reads only the skeleton
/// (<see cref="FindingKind"/> and <see cref="FindingDifferenceKind"/>) — never the opaque
/// payload — so the same code summarizes an IL diff, a C# diff, or a semantic-fact stream. This
/// is the shape a cross-stream consumer (e.g. Performance Triage) is built from: uniform queries
/// over one row envelope, regardless of which producer emitted the rows.
/// </summary>
public sealed record FindingSummary(
    int Total,
    int Present,
    int Added,
    int Removed,
    int Changed,
    int Moved,
    DiffShape Shape)
{
    public static FindingSummary Summarize(IReadOnlyList<Finding> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        int present = 0;
        int added = 0;
        int removed = 0;
        int changed = 0;
        int moved = 0;

        foreach (var row in rows)
        {
            switch (row.Kind)
            {
                case FindingKind.Present:
                    present++;
                    break;
                case FindingKind.Added:
                    added++;
                    break;
                case FindingKind.Removed:
                    removed++;
                    break;
                case FindingKind.Changed:
                    changed++;
                    break;
            }

            if (row.DifferenceKind == FindingDifferenceKind.Moved)
                moved++;
        }

        bool structural = added > 0 || removed > 0 || changed > 0;
        var verdict = structural
            ? DiffShape.Structural
            : moved > 0 ? DiffShape.ReorderOnly : DiffShape.Identical;

        return new FindingSummary(rows.Count, present, added, removed, changed, moved, verdict);
    }
}
