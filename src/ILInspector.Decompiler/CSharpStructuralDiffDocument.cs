using System.Collections.Immutable;

namespace ILInspector.Decompiler;

/// <summary>
/// Portable, product-issued structural C# diff over two exact annotated-source
/// document revisions.
/// </summary>
/// <remarks>
/// The serialized artifact retains both correspondence evidence and generated
/// structural rows. Construction reissues correspondence from the embedded
/// documents and requires exact agreement with those rows before
/// <see cref="ToComparison"/> exposes them.
/// <c>CSharpStructuralComparisonTests.StructuralDiffDocument_RejectsTamperedCorrespondence</c>
/// <c>StructuralDiffDocument_RejectsTamperedProjection</c>, and
/// <c>StructuralDiffDocument_RejectsTamperedRows</c> are the non-vacuity gates
/// for that replay check.
/// <c>StructuralDiffDocument_ProjectsInterleavedIlWithoutInferringFromText</c>
/// gates that the top-level projections own the serialized row coordinates.
/// </remarks>
public sealed record CSharpStructuralDiffDocument
{
    /// <summary>Current JSON shape version.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Current correspondence and structural-comparison methodology.</summary>
    /// <remarks>
    /// Version 2 introduced
    /// <see cref="CSharpUnmatchedNodeReason.InferredDeclaration"/>. Version 3
    /// preserves retained facts and targets in the top-level C# projections.
    /// Version 4 adds group-level cardinality changes for ambiguous nodes that
    /// share exact IL-origin evidence and stable kind. Replaying any earlier
    /// methodology can derive different correspondence, projection, or
    /// multiplicity content from the same embedded documents, so the version
    /// gate rejects it before the replay-equality check can report apparent
    /// artifact tampering.
    /// </remarks>
    public const int CurrentMethodologyVersion = 4;

    /// <summary>Creates and validates one portable structural diff.</summary>
    public CSharpStructuralDiffDocument(
        int SchemaVersion,
        int MethodologyVersion,
        CSharpNodeCorrespondenceResult Correspondence,
        AnnotatedSourceDocument Before,
        AnnotatedSourceDocument After,
        ImmutableArray<CSharpStructuralDiffRow> Rows,
        CSharpStructuralFidelityEvidence? Fidelity = null,
        ImmutableArray<CSharpStructuralMultiplicityDelta> MultiplicityDeltas = default)
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SchemaVersion),
                "Structural diff schema version is unsupported.");
        }
        if (MethodologyVersion != CurrentMethodologyVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MethodologyVersion),
                "Structural diff methodology version is unsupported.");
        }
        ArgumentNullException.ThrowIfNull(Correspondence);
        ArgumentNullException.ThrowIfNull(Before);
        ArgumentNullException.ThrowIfNull(After);
        if (Rows.IsDefault)
            throw new ArgumentException("Structural diff rows must be initialized.", nameof(Rows));
        if (MultiplicityDeltas.IsDefault)
        {
            throw new ArgumentException(
                "Structural multiplicity deltas must be initialized.",
                nameof(MultiplicityDeltas));
        }
        if (Fidelity?.Note is { } note)
        {
            AnnotatedSourceText.ValidateWellFormedUtf16(
                note,
                nameof(Fidelity),
                "Structural fidelity note");
        }

        var comparison = CSharpBodyDiff.CompareStructure(Correspondence, Fidelity);
        if (Before != comparison.Before
            || After != comparison.After
            || !RowsEqual(Rows, comparison.Rows)
            || !MultiplicityDeltasEqual(MultiplicityDeltas, comparison.MultiplicityDeltas))
        {
            throw new ArgumentException(
                "Structural diff projection or rows do not match the product-issued comparison.");
        }

        this.SchemaVersion = SchemaVersion;
        this.MethodologyVersion = MethodologyVersion;
        this.Correspondence = Correspondence;
        this.Before = Before;
        this.After = After;
        this.Rows = Rows;
        this.Fidelity = Fidelity;
        this.MultiplicityDeltas = MultiplicityDeltas;
    }

    /// <summary>JSON shape version.</summary>
    public int SchemaVersion { get; }

    /// <summary>Correspondence and structural-comparison methodology version.</summary>
    public int MethodologyVersion { get; }

    /// <summary>Exact documents, revisions, matches, and unmatched nodes.</summary>
    public CSharpNodeCorrespondenceResult Correspondence { get; }

    /// <summary>C#-only Before projection that owns Before row ids and spans.</summary>
    public AnnotatedSourceDocument Before { get; }

    /// <summary>C#-only After projection that owns After row ids and spans.</summary>
    public AnnotatedSourceDocument After { get; }

    /// <summary>Product-generated structural rows over the exact document revisions.</summary>
    public ImmutableArray<CSharpStructuralDiffRow> Rows { get; }

    /// <summary>Optional independently measured compile-back evidence.</summary>
    public CSharpStructuralFidelityEvidence? Fidelity { get; }

    /// <summary>
    /// Product-generated group-level count changes that preserve ambiguity.
    /// </summary>
    public ImmutableArray<CSharpStructuralMultiplicityDelta> MultiplicityDeltas { get; }

    /// <summary>Issues a structural diff from two exact product documents.</summary>
    public static CSharpStructuralDiffDocument Create(
        AnnotatedSourceDocument before,
        AnnotatedSourceDocument after,
        CSharpStructuralFidelityEvidence? fidelity = null)
    {
        var correspondence = CSharpBodyDiff.IssueCorrespondence(before, after);
        var comparison = CSharpBodyDiff.CompareStructure(correspondence, fidelity);
        return new(
            CurrentSchemaVersion,
            CurrentMethodologyVersion,
            correspondence,
            comparison.Before,
            comparison.After,
            comparison.Rows,
            fidelity,
            comparison.MultiplicityDeltas);
    }

    /// <summary>
    /// Revalidates the embedded product correspondence and derives presentation
    /// rows over the exact document revisions.
    /// </summary>
    public CSharpStructuralComparison ToComparison()
        => CSharpBodyDiff.CompareStructure(Correspondence, Fidelity);

    static bool RowsEqual(
        ImmutableArray<CSharpStructuralDiffRow> left,
        ImmutableArray<CSharpStructuralDiffRow> right)
    {
        if (left.Length != right.Length)
            return false;

        for (int index = 0; index < left.Length; index++)
        {
            var leftRow = left[index];
            var rightRow = right[index];
            if (leftRow.Change != rightRow.Change
                || leftRow.BeforeNodeId != rightRow.BeforeNodeId
                || leftRow.AfterNodeId != rightRow.AfterNodeId
                || leftRow.BeforeKind != rightRow.BeforeKind
                || leftRow.AfterKind != rightRow.AfterKind
                || leftRow.BeforeLabel != rightRow.BeforeLabel
                || leftRow.AfterLabel != rightRow.AfterLabel
                || leftRow.BeforeRegion != rightRow.BeforeRegion
                || leftRow.AfterRegion != rightRow.AfterRegion
                || !leftRow.BeforeSpans.SequenceEqual(rightRow.BeforeSpans)
                || !leftRow.AfterSpans.SequenceEqual(rightRow.AfterSpans))
            {
                return false;
            }
        }

        return true;
    }

    static bool MultiplicityDeltasEqual(
        ImmutableArray<CSharpStructuralMultiplicityDelta> left,
        ImmutableArray<CSharpStructuralMultiplicityDelta> right)
    {
        if (left.Length != right.Length)
            return false;

        for (int index = 0; index < left.Length; index++)
        {
            var leftDelta = left[index];
            var rightDelta = right[index];
            if (!string.Equals(leftDelta.NodeKind, rightDelta.NodeKind, StringComparison.Ordinal)
                || leftDelta.BeforeCount != rightDelta.BeforeCount
                || leftDelta.AfterCount != rightDelta.AfterCount
                || !leftDelta.Evidence.IlOffsets.SequenceEqual(rightDelta.Evidence.IlOffsets))
            {
                return false;
            }
        }

        return true;
    }
}
