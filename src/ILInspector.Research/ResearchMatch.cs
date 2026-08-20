using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis;

namespace ILInspector.Research;

/// <summary>
/// The discrete, verified outcome of matching two methods by structural clone
/// equivalence rather than by declared identity. This is the "Match" half of
/// <c>ResearchDiff</c>/<c>ResearchMatch</c>: <see cref="ResearchDiff"/> already
/// owns the identity-matched half (declared identity given, A vs A'); this
/// outcome model is the identity-agnostic half — recovering or confirming
/// correspondence from verified clone equivalence rather than assuming it.
/// </summary>
/// <remarks>
/// This is a small ordered set of verified, discrete states, not a continuous
/// similarity score. A scalar confidence value, if one is ever needed, is
/// reserved for genuinely approximate fringe cases only (mirroring
/// <c>FindingEdge.Confidence</c>) and must never replace this discrete
/// relation. <see cref="Near"/> already carries a fully verified, bounded
/// one-edit alignment; it is not an estimate.
/// </remarks>
public enum ResearchMatchOutcome
{
    /// <summary>Verified structurally different; no clone relationship.</summary>
    Unrelated,

    /// <summary>Exact clone at a different declared identity (a different MethodDef).</summary>
    RenamedOrMoved,

    /// <summary>Verified, bounded one-edit structural alignment.</summary>
    Near,

    /// <summary>Exact clone at the same declared identity (the same MethodDef).</summary>
    Unchanged,
}

/// <summary>
/// Research-owned projection of a <see cref="StructuralCloneComparisonDocument"/> onto the
/// <see cref="ResearchMatchOutcome"/> model.
/// </summary>
/// <remarks>
/// <para>
/// This type composes, but never recreates, the Analysis-owned comparison: the disposition,
/// relation, correspondence, and blockers all come from <see cref="Document"/> unchanged.
/// <see cref="Outcome"/> exists only when <see cref="StructuralCloneComparisonDocument.Disposition"/>
/// is <see cref="StructuralCloneDisposition.Completed"/> — an unsupported, limit-reached, or
/// failed comparison stays visible through <see cref="Document"/> rather than becoming an
/// empty or guessed outcome.
/// </para>
/// <para>
/// This slice is scoped to the same A-vs-A boundary as
/// <see cref="StructuralCloneComparisonDocument"/> itself: both methods come from one retained
/// module. Within that boundary, <see cref="ResearchMatchOutcome.Unchanged"/> only arises for
/// the degenerate case of comparing a method to itself (equal MethodDef tokens); comparing the
/// same declared identity across two different module snapshots (A vs A') is a distinct,
/// not-yet-built cross-module capability.
/// </para>
/// </remarks>
public sealed record ResearchMatchResult
{
    public ResearchMatchResult(StructuralCloneComparisonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Document = document;
        Outcome = document.Disposition == StructuralCloneDisposition.Completed
            ? Classify(document)
            : null;
    }

    /// <summary>The Analysis-owned comparison this result projects.</summary>
    public StructuralCloneComparisonDocument Document { get; }

    /// <summary>
    /// The discrete match outcome, present only when <see cref="Document"/> completed.
    /// </summary>
    public ResearchMatchOutcome? Outcome { get; }

    static ResearchMatchOutcome Classify(StructuralCloneComparisonDocument document)
    {
        bool sameDeclaredIdentity = document.LeftToken == document.RightToken;
        return document.Relation switch
        {
            StructuralCloneRelation.Different => ResearchMatchOutcome.Unrelated,
            StructuralCloneRelation.Near => ResearchMatchOutcome.Near,
            StructuralCloneRelation.Exact when sameDeclaredIdentity => ResearchMatchOutcome.Unchanged,
            StructuralCloneRelation.Exact => ResearchMatchOutcome.RenamedOrMoved,
            _ => throw new InvalidOperationException(
                "A completed structural clone comparison document must carry a relation."),
        };
    }
}

/// <summary>
/// Entry points for the Research-owned "Match" capability: establishing or confirming pairwise
/// correspondence from verified structural clone equivalence, independent of declared identity.
/// </summary>
public static class ResearchMatch
{
    /// <summary>
    /// Compares two methods from one retained module by structural clone equivalence and
    /// projects the result onto the discrete <see cref="ResearchMatchOutcome"/> model.
    /// </summary>
    public static ResearchMatchResult Compare(
        PEReader image,
        MethodDefinitionHandle left,
        MethodDefinitionHandle right,
        StructuralCloneModuleIdentity moduleIdentity,
        StructuralCloneComparisonLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(moduleIdentity);

        StructuralCloneComparison comparison = StructuralCloneAnalysis.Compare(image, left, right, limits);
        StructuralCloneComparisonDocument document =
            StructuralCloneComparisonDocument.Create(comparison, moduleIdentity, moduleIdentity);
        return new ResearchMatchResult(document);
    }

    /// <summary>
    /// Projects an already-produced comparison document onto the discrete
    /// <see cref="ResearchMatchOutcome"/> model, without recomputing or re-verifying it.
    /// </summary>
    public static ResearchMatchResult FromDocument(StructuralCloneComparisonDocument document)
        => new(document);
}
