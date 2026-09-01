using System.Collections.Immutable;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>The compiler state-machine contract claimed by one kickoff method.</summary>
public enum StateMachineClaimKind
{
    ClassicAsync,
    AsyncIterator,
    Iterator,
}

/// <summary>An exact interface role implemented by a state-machine MethodDef.</summary>
public enum StateMachineMethodRole
{
    MoveNext,
    SetStateMachine,
    MoveNextAsync,
    Dispose,
    DisposeAsync,
}

/// <summary>One exact MethodDef implementing a required state-machine role.</summary>
public sealed record StateMachineMethodRelationship(
    StateMachineMethodRole Role,
    MetadataMethodAddress Method);

/// <summary>
/// A structurally authenticated same-module state-machine relationship.
/// </summary>
public sealed record StateMachineRelationship
{
    internal StateMachineRelationship(
        MetadataMethodAddress kickoff,
        MetadataTypeDefinitionAddress stateMachineType,
        MetadataTypeDefinitionName stateMachineName,
        StateMachineClaimKind kind,
        ImmutableArray<StateMachineMethodRelationship> methods)
    {
        if (methods.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A state-machine relationship must contain an execution method.",
                nameof(methods));
        }

        Kickoff = kickoff;
        StateMachineType = stateMachineType;
        StateMachineName = stateMachineName;
        Kind = kind;
        Methods = methods;
    }

    public MetadataMethodAddress Kickoff { get; }
    public MetadataTypeDefinitionAddress StateMachineType { get; }
    public MetadataTypeDefinitionName StateMachineName { get; }
    public StateMachineClaimKind Kind { get; }
    public ImmutableArray<StateMachineMethodRelationship> Methods { get; }

    public bool TryGetMethod(
        StateMachineMethodRole role,
        out MetadataMethodAddress method)
    {
        foreach (StateMachineMethodRelationship candidate in Methods)
        {
            if (candidate.Role == role)
            {
                method = candidate.Method;
                return true;
            }
        }

        method = default;
        return false;
    }
}

/// <summary>Why a state-machine relationship could not be authenticated.</summary>
public enum StateMachineRelationshipFailureKind
{
    Unresolved,
    Malformed,
    Duplicate,
    CrossKind,
    BudgetExceeded,
    Ambiguous,
}

/// <summary>Inspectable structural evidence for a rejected relationship.</summary>
public sealed record StateMachineRelationshipFailure
{
    internal StateMachineRelationshipFailure(
        StateMachineRelationshipFailureKind kind,
        string detail,
        ImmutableArray<MetadataMethodAddress> kickoffCandidates,
        ImmutableArray<MetadataTypeDefinitionAddress> stateMachineCandidates,
        ImmutableArray<MetadataTypeDefinitionName> claimedTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        Kind = kind;
        Detail = detail;
        KickoffCandidates = kickoffCandidates.IsDefault
            ? []
            : kickoffCandidates;
        StateMachineCandidates = stateMachineCandidates.IsDefault
            ? []
            : stateMachineCandidates;
        ClaimedTypes = claimedTypes.IsDefault
            ? []
            : claimedTypes;
    }

    public StateMachineRelationshipFailureKind Kind { get; }
    public string Detail { get; }
    public ImmutableArray<MetadataMethodAddress> KickoffCandidates { get; }
    public ImmutableArray<MetadataTypeDefinitionAddress>
        StateMachineCandidates { get; }
    public ImmutableArray<MetadataTypeDefinitionName> ClaimedTypes { get; }
}

/// <summary>
/// Total result of enumerating state-machine relationships from one module.
/// </summary>
public abstract record StateMachineRelationshipsResult
{
    private protected StateMachineRelationshipsResult()
    {
    }

    /// <summary>
    /// Successful module-wide construction. The complete relationship set may
    /// be empty.
    /// </summary>
    public sealed record Available : StateMachineRelationshipsResult
    {
        internal Available(
            ImmutableArray<StateMachineRelationship> relationships) =>
            Relationships = relationships.IsDefault
                ? []
                : relationships;

        public ImmutableArray<StateMachineRelationship> Relationships
        {
            get;
        }
    }

    /// <summary>Module-wide construction failed.</summary>
    public sealed record Rejected : StateMachineRelationshipsResult
    {
        internal Rejected(StateMachineRelationshipFailure failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public StateMachineRelationshipFailure Failure { get; }
    }
}

/// <summary>
/// Total result of one state-machine relationship query. Absence and rejection
/// are distinct so malformed metadata cannot become an empty success.
/// </summary>
public abstract record StateMachineRelationshipResult
{
    private protected StateMachineRelationshipResult()
    {
    }

    public sealed record Resolved : StateMachineRelationshipResult
    {
        internal Resolved(StateMachineRelationship relationship)
        {
            ArgumentNullException.ThrowIfNull(relationship);
            Relationship = relationship;
        }

        public StateMachineRelationship Relationship { get; }
    }

    public sealed record Absent : StateMachineRelationshipResult;

    public sealed record Rejected : StateMachineRelationshipResult
    {
        internal Rejected(StateMachineRelationshipFailure failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public StateMachineRelationshipFailure Failure { get; }
    }
}
