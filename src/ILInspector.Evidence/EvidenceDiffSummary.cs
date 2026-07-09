namespace ILInspector.Evidence;

/// <summary>A coarse, stream-agnostic classification of a diff read from the skeleton alone.</summary>
public enum EvidenceFinding
{
    /// <summary>No differences at all.</summary>
    Identical,

    /// <summary>The only differences are relocations (a reorder); the content set is unchanged.</summary>
    ReorderOnly,

    /// <summary>Content was added, removed, or changed.</summary>
    Structural,
}

/// <summary>
/// A minimal, stream-agnostic consumer of evidence rows. It reads only the skeleton
/// (<see cref="EvidenceRowKind"/> and <see cref="EvidenceDifferenceKind"/>) — never the opaque
/// payload — so the same code summarizes an IL diff, a C# diff, or a semantic-fact stream. This
/// is the shape a cross-stream consumer (e.g. Performance Triage) is built from: uniform queries
/// over one row envelope, regardless of which producer emitted the rows.
/// </summary>
public sealed record EvidenceDiffSummary(
    int Total,
    int Present,
    int Added,
    int Removed,
    int Changed,
    int Moved,
    EvidenceFinding Finding)
{
    public static EvidenceDiffSummary Summarize(IReadOnlyList<EvidenceRow> rows)
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
                case EvidenceRowKind.Present:
                    present++;
                    break;
                case EvidenceRowKind.Added:
                    added++;
                    break;
                case EvidenceRowKind.Removed:
                    removed++;
                    break;
                case EvidenceRowKind.Changed:
                    changed++;
                    break;
            }

            if (row.DifferenceKind == EvidenceDifferenceKind.Moved)
                moved++;
        }

        bool structural = added > 0 || removed > 0 || changed > 0;
        var verdict = structural
            ? EvidenceFinding.Structural
            : moved > 0 ? EvidenceFinding.ReorderOnly : EvidenceFinding.Identical;

        return new EvidenceDiffSummary(rows.Count, present, added, removed, changed, moved, verdict);
    }
}
