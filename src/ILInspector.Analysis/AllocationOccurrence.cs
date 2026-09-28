using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Analysis;

public enum AllocationKind
{
    Box,
    Array,
    Object,
    Closure,
    StateMachine,
    Delegate,
    Enumerator,
}

public enum AllocationFrequency
{
    Always,
    CachedOnce,
    PerIteration,
}

public enum AllocationEscape
{
    Unknown,
    LocalOnly,
    Escapes,
    ThrowPath,
}

/// <summary>
/// Objective refinement of an <see cref="AllocationEscape.Escapes"/> verdict: WHERE
/// the produced value escapes. Additive on top of the binary verdict — a plain
/// <see cref="AllocationEscape.Escapes"/> with an undetermined sink stays
/// <see cref="None"/> (fail-honest). Feeds the static→dynamic promotion prior.
/// </summary>
public enum AllocationEscapeKind
{
    None,
    Return,
    Field,
    Static,
    Collection,
    Capture,
}

/// <summary>
/// A terminal use or sink accepted by the allocation lifetime proof.
/// </summary>
public enum AllocationLifetimeUseKind
{
    ElementRead,
    ElementWrite,
    LengthRead,
    Drop,
    Unbox,
    TrustedNonCapturingCall,
    Return,
    Throw,
    FieldStore,
    StaticStore,
    CollectionStore,
    Capture,
    ByReferenceTransfer,
}

/// <summary>
/// A typed reason why an allocation lifetime proof could not complete.
/// </summary>
public enum AllocationLifetimeLimitationKind
{
    ReachingDefinitionsUnavailable,
    ReachingDefinitionsIncomplete,
    DefinitionUnavailable,
    AliasCycle,
    UnsupportedInstruction,
    UnsupportedStackShape,
    UnsupportedCall,
    MetadataResolution,
    AnalysisFailure,
}

/// <summary>
/// An exact terminal-use coordinate for one allocation occurrence.
/// </summary>
public readonly record struct AllocationLifetimeUse(
    int ILOffset,
    AllocationLifetimeUseKind Kind);

/// <summary>
/// A proof-stopping limitation, with its exact instruction when available.
/// </summary>
public readonly record struct AllocationLifetimeLimitation(
    AllocationLifetimeLimitationKind Kind,
    int? ILOffset = null,
    ILOpCode? Operation = null);

/// <summary>
/// Owner-issued lifetime evidence for one exact allocation occurrence.
/// </summary>
public sealed class AllocationLifetimeEvidence
    : IEquatable<AllocationLifetimeEvidence>
{
    public static AllocationLifetimeEvidence Empty { get; } =
        new([], []);

    public AllocationLifetimeEvidence(
        ImmutableArray<AllocationLifetimeUse> uses,
        ImmutableArray<AllocationLifetimeLimitation> limitations)
    {
        Uses = NormalizeUses(uses);
        Limitations = NormalizeLimitations(limitations);
    }

    public ImmutableArray<AllocationLifetimeUse> Uses { get; }

    public ImmutableArray<AllocationLifetimeLimitation> Limitations { get; }

    public bool Equals(AllocationLifetimeEvidence? other) =>
        other is not null
        && Uses.SequenceEqual(other.Uses)
        && Limitations.SequenceEqual(other.Limitations);

    public override bool Equals(object? obj) =>
        obj is AllocationLifetimeEvidence other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (AllocationLifetimeUse use in Uses)
            hash.Add(use);
        foreach (AllocationLifetimeLimitation limitation in Limitations)
            hash.Add(limitation);
        return hash.ToHashCode();
    }

    static ImmutableArray<AllocationLifetimeUse> NormalizeUses(
        ImmutableArray<AllocationLifetimeUse> values) =>
        values.IsDefaultOrEmpty
            ? []
            : [.. values
                .Distinct()
                .OrderBy(value => value.ILOffset)
                .ThenBy(value => value.Kind)];

    static ImmutableArray<AllocationLifetimeLimitation> NormalizeLimitations(
        ImmutableArray<AllocationLifetimeLimitation> values) =>
        values.IsDefaultOrEmpty
            ? []
            : [.. values
                .Distinct()
                .OrderBy(value => value.ILOffset)
                .ThenBy(value => value.Kind)
                .ThenBy(value => value.Operation)];
}

public enum AllocationFactSource
{
    Newobj,
    Newarr,
    Box,
    GetEnumeratorCall,
}

public enum AllocationPathContext
{
    StraightLine,
    Branch,
    SwitchArm,
    LoopBody,
    ErrorPath,
}

public enum AllocationPathConfidence
{
    Unknown,
    DominatesReturn,
    BehindBranch,
}

public enum AllocationPostDominance
{
    Unknown,
    ReturnPostDominates,
}

public enum AllocationSizeTier
{
    Unknown,
    Exact,
    Approx,
}

/// <summary>
/// Objective per-invocation multiplicity tier: how many times a call to the
/// declaring method executes this allocation. A consolidation of the existing
/// path axes (loop membership, dominates-return, branch context) into one
/// "how often per call" signal. Fail-honest <see cref="Unknown"/> when it cannot
/// be proven. <see cref="Loop"/> is unbounded (the trip count N is not statically
/// resolved). Feeds the static pre-profile prioritization / dynamic validation.
/// </summary>
public enum AllocationMultiplicity
{
    Unknown,
    Once,
    Conditional,
    Loop,
}

/// <summary>
/// One static heap-allocation occurrence, located by IL offset within its method. The offset is
/// version-local provenance, not cross-version correspondence identity. Presentation layers project
/// this into Findings, hidden-fact annotations, method signals, and triage rows.
/// </summary>
public sealed record AllocationOccurrence(
    MethodIdentity Method,
    int ILOffset,
    int? OperandToken,
    AllocationKind Kind,
    TypeRef? AllocatedType,
    string? Detail,
    bool CountsAsHeapAllocation,
    AllocationFrequency Frequency,
    bool InLoop,
    AllocationEscape Escape,
    AllocationFactSource Source,
    string? RuntimeAllocationType = null,
    AllocationPathContext PathContext = AllocationPathContext.StraightLine,
    AllocationPathConfidence PathConfidence = AllocationPathConfidence.Unknown,
    int? EstimatedSizeBytes = null,
    AllocationSizeTier SizeTier = AllocationSizeTier.Unknown)
{
    public AllocationPostDominance PostDominance { get; init; }

    public AllocationEscapeKind EscapeKind { get; init; }

    /// <summary>
    /// Exact terminal uses and proof-stopping limitations for this occurrence.
    /// </summary>
    public AllocationLifetimeEvidence LifetimeEvidence { get; init; } =
        AllocationLifetimeEvidence.Empty;

    public AllocationMultiplicity Multiplicity { get; init; }

    /// <summary>
    /// The backing array a growable collection actually churns as it resizes
    /// (e.g. <c>List&lt;T&gt;</c> → <c>T[]</c>, <c>StringBuilder</c> → <c>char[]</c>),
    /// distinct from the allocated object type. Null when there is no single known
    /// backing store (fail-honest). Sharpens the static↔dynamic observed-type match.
    /// </summary>
    public string? ChurnedType { get; init; }

    public string AnnotationId => Kind switch
    {
        AllocationKind.Box => "alloc.box",
        AllocationKind.Array => "alloc.array",
        AllocationKind.Closure => "alloc.closure",
        AllocationKind.StateMachine => "alloc.statemachine",
        AllocationKind.Delegate => "alloc.delegate",
        AllocationKind.Enumerator => "alloc.enumerator",
        _ => "alloc.new",
    };
}
