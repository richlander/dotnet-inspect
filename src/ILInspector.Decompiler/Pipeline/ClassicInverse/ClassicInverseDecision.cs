using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Why one healthy classic request fell outside the proven recipe domain. A
/// decline is never a planning failure: the request and its bodies were
/// well-formed, and the inverse simply could not discharge one of the three
/// proof obligations named by
/// <c>docs/design/classic-async-reconstruction.md</c>.
/// </summary>
internal enum ClassicInverseDeclineReason
{
    /// <summary>No closed recipe recognized the request's lowering shell.</summary>
    NoRecipeMatched,

    /// <summary>More than one recipe matched and the matches are not provably equivalent.</summary>
    AmbiguousRecipeMatch,

    /// <summary>A physical region has no explicit disposition, or two dispositions overlap.</summary>
    UnclassifiedPhysicalRegion,

    /// <summary>An input semantic effect reached no output realization, or reached more than one.</summary>
    UnrealizedSemanticEffect,

    /// <summary>An output effect cites no input effect or authenticated protocol fact.</summary>
    InventedOutputEffect,

    /// <summary>A consumed semantic node has an unknown or incomplete structured-ancestor path.</summary>
    UnmodeledStructuredAncestor,

    /// <summary>A consumed node's output context does not lie under its reproduced ancestor's output context.</summary>
    EscapedControlContext,

    /// <summary>A receipt has missing or ambiguous correspondence to the unmodified import snapshot.</summary>
    MissingImportCorrespondence,

    /// <summary>The proposed body contains a node form outside the closed output blueprint.</summary>
    UnsupportedOutputNode,

    /// <summary>The proposed await would require an enclosing unsafe context.</summary>
    UnsafeAwaitContext,
}

/// <summary>
/// Why planning could not produce a trustworthy decision at all. A failure
/// never degrades into a decline, a plan, or an empty success.
/// </summary>
internal enum ClassicInverseFailureKind
{
    /// <summary>The request's identities and bodies do not correlate.</summary>
    InvalidCorrelation,

    /// <summary>A core-owned traversal, node, or receipt budget was exhausted.</summary>
    BudgetExhausted,

    /// <summary>Planning reached a state its own contract says is unreachable.</summary>
    InternalPlanningFailure,
}

/// <summary>A visible planning failure, carrying the kind and a stable detail.</summary>
internal sealed record ClassicInverseFailure(
    ClassicInverseFailureKind Kind,
    string Detail)
{
    public override string ToString() => $"{Kind}: {Detail}";
}

/// <summary>
/// The terminal result of the classic inverse core. Exactly three arms, as the
/// owning design requires: an immutable detached plan, a healthy decline, or a
/// visible failure.
/// </summary>
internal abstract record ClassicInverseDecision
{
    private protected ClassicInverseDecision()
    {
    }

    /// <summary>A licensed reconstruction with its three proof ledgers.</summary>
    internal sealed record Reconstruct(ClassicInversePlan Plan)
        : ClassicInverseDecision
    {
        internal override string Signature => $"Reconstruct({Plan.Signature})";
    }

    /// <summary>A healthy request outside the proven recipe domain.</summary>
    internal sealed record Decline(
        ClassicInverseDeclineReason Reason,
        string Detail)
        : ClassicInverseDecision
    {
        /// <summary>
        /// True when no recipe claimed the request at all. The pass preserves the
        /// original kickoff silently for this arm; every other decline reason means
        /// a recipe claimed the shell and then failed a proof obligation, which the
        /// pass reports visibly.
        /// </summary>
        internal bool IsRecipeDomainMiss =>
            Reason == ClassicInverseDeclineReason.NoRecipeMatched;

        internal override string Signature => $"Decline({Reason}:{Detail})";
    }

    /// <summary>Planning could not produce a trustworthy decision.</summary>
    internal sealed record Failed(ClassicInverseFailure Failure)
        : ClassicInverseDecision
    {
        internal override string Signature =>
            $"Failed({Failure.Kind}:{Failure.Detail})";
    }

    /// <summary>
    /// A canonical, order-stable rendering of the whole decision. Decisions are
    /// compared through this value so a plan that differs only by object
    /// identity, request order, or receipt construction order is still equal.
    /// </summary>
    internal abstract string Signature { get; }

    internal static ClassicInverseDecision DeclineWith(
        ClassicInverseDeclineReason reason,
        string detail)
        => new Decline(reason, detail);

    internal static ClassicInverseDecision FailWith(
        ClassicInverseFailureKind kind,
        string detail)
        => new Failed(new ClassicInverseFailure(kind, detail));
}

/// <summary>
/// The core-owned planning budget. Exhausting it is
/// <see cref="ClassicInverseFailureKind.BudgetExhausted"/>, never a decline and
/// never a partial proof.
/// </summary>
internal sealed class ClassicInverseBudget
{
    internal const int DefaultNodeBudget = 200_000;

    int _remaining;

    internal ClassicInverseBudget(int nodeBudget = DefaultNodeBudget)
    {
        if (nodeBudget <= 0)
            throw new ArgumentOutOfRangeException(nameof(nodeBudget));
        _remaining = nodeBudget;
        Total = nodeBudget;
    }

    internal int Total { get; }

    /// <summary>
    /// Units charged so far. Every proof phase charges for each node it
    /// touches, so this measures the planning work a body actually bought.
    /// </summary>
    internal int Consumed => Total - _remaining;

    internal bool Exhausted { get; private set; }

    /// <summary>Charges one unit of traversal; returns false once exhausted.</summary>
    internal bool Charge()
    {
        if (Exhausted)
            return false;
        if (--_remaining < 0)
        {
            Exhausted = true;
            return false;
        }
        return true;
    }
}

/// <summary>
/// A stable, order-independent rendering helper shared by the plan and its
/// receipts so equal proofs render equal text.
/// </summary>
internal static class ClassicInverseSignature
{
    internal static string Join(IEnumerable<string> parts)
        => string.Join("|", parts.OrderBy(static p => p, StringComparer.Ordinal));

    internal static string Sequence(IEnumerable<string> parts)
        => string.Join(",", parts);

    internal static string Path(ImmutableArray<int> path)
        => path.IsDefaultOrEmpty ? "/" : "/" + string.Join("/", path);
}
