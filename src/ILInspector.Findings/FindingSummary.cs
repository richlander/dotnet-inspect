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
/// A minimal, stream-agnostic consumer of transition rows. It reads only the pair skeleton
/// (<see cref="IPairFinding.Kind"/> and <see cref="IPairFinding.Difference"/>) — never the opaque
/// payload — so the same code summarizes an IL diff, a C# diff, or an API diff. This is the shape
/// a cross-stream consumer (e.g. Performance Triage) is built from: uniform queries over one
/// transition envelope, regardless of which producer emitted the pairs.
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
    public static FindingSummary Summarize(IReadOnlyList<IPairFinding> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        int present = 0;
        int added = 0;
        int removed = 0;
        int changed = 0;
        int moved = 0;

        foreach (var pair in pairs)
        {
            switch (pair.Kind)
            {
                case PairKind.Present:
                    present++;
                    break;
                case PairKind.Added:
                    added++;
                    break;
                case PairKind.Removed:
                    removed++;
                    break;
                case PairKind.Changed:
                    changed++;
                    break;
            }

            if (pair.Difference == FindingDifferenceKind.Moved)
                moved++;
        }

        bool structural = added > 0 || removed > 0 || changed > 0;
        var verdict = structural
            ? DiffShape.Structural
            : moved > 0 ? DiffShape.ReorderOnly : DiffShape.Identical;

        return new FindingSummary(pairs.Count, present, added, removed, changed, moved, verdict);
    }
}
