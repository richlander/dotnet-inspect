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

/// <summary>
/// The closed physical disposition of one state-machine interface role.
/// </summary>
public abstract record StateMachineRoleDisposition
{
    private protected StateMachineRoleDisposition(
        StateMachineMethodRole role) =>
        Role = role;

    public StateMachineMethodRole Role { get; }

    /// <summary>An exact MethodDef implements the role.</summary>
    public sealed record Present : StateMachineRoleDisposition
    {
        internal Present(
            StateMachineMethodRole role,
            MetadataMethodAddress method)
            : base(role) =>
            Method = method;

        public MetadataMethodAddress Method { get; }
    }

    /// <summary>
    /// A bounded scan found no candidate for an explicitly optional role.
    /// </summary>
    public sealed record AbsentFromArtifact : StateMachineRoleDisposition
    {
        internal AbsentFromArtifact(StateMachineMethodRole role)
            : base(role)
        {
        }
    }
}

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
        ImmutableArray<StateMachineRoleDisposition> roles)
    {
        ReadOnlySpan<StateMachineMethodRole> expected = RolesFor(kind);
        if (roles.IsDefault || roles.Length != expected.Length)
        {
            throw new ArgumentException(
                "A state-machine relationship must account for every role.",
                nameof(roles));
        }

        foreach (StateMachineMethodRole expectedRole in expected)
        {
            StateMachineRoleDisposition? disposition = null;
            foreach (StateMachineRoleDisposition candidate in roles)
            {
                if (candidate.Role != expectedRole)
                    continue;
                if (disposition is not null)
                {
                    throw new ArgumentException(
                        "A state-machine relationship cannot contain duplicate roles.",
                        nameof(roles));
                }

                disposition = candidate;
            }

            if (disposition is null
                || disposition is
                    StateMachineRoleDisposition.AbsentFromArtifact
                    && !CanBeAbsent(kind, expectedRole))
            {
                throw new ArgumentException(
                    "A state-machine relationship contains an invalid role disposition.",
                    nameof(roles));
            }
        }

        Kickoff = kickoff;
        StateMachineType = stateMachineType;
        StateMachineName = stateMachineName;
        Kind = kind;
        Roles = roles;
    }

    public MetadataMethodAddress Kickoff { get; }
    public MetadataTypeDefinitionAddress StateMachineType { get; }
    public MetadataTypeDefinitionName StateMachineName { get; }
    public StateMachineClaimKind Kind { get; }
    public ImmutableArray<StateMachineRoleDisposition> Roles { get; }

    public StateMachineRoleDisposition GetRole(
        StateMachineMethodRole role)
    {
        foreach (StateMachineRoleDisposition candidate in Roles)
        {
            if (candidate.Role == role)
                return candidate;
        }

        throw new ArgumentOutOfRangeException(
            nameof(role),
            role,
            "The role does not belong to this state-machine claim kind.");
    }

    public bool TryGetMethod(
        StateMachineMethodRole role,
        out MetadataMethodAddress method)
    {
        foreach (StateMachineRoleDisposition candidate in Roles)
        {
            if (candidate is StateMachineRoleDisposition.Present present
                && candidate.Role == role)
            {
                method = present.Method;
                return true;
            }
        }

        method = default;
        return false;
    }

    internal static ReadOnlySpan<StateMachineMethodRole> RolesFor(
        StateMachineClaimKind kind) =>
        kind switch
        {
            StateMachineClaimKind.ClassicAsync =>
            [
                StateMachineMethodRole.MoveNext,
                StateMachineMethodRole.SetStateMachine,
            ],
            StateMachineClaimKind.AsyncIterator =>
            [
                StateMachineMethodRole.MoveNext,
                StateMachineMethodRole.SetStateMachine,
                StateMachineMethodRole.MoveNextAsync,
                StateMachineMethodRole.DisposeAsync,
            ],
            StateMachineClaimKind.Iterator =>
            [
                StateMachineMethodRole.MoveNext,
                StateMachineMethodRole.Dispose,
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    internal static bool CanBeAbsent(
        StateMachineClaimKind kind,
        StateMachineMethodRole role) =>
        kind == StateMachineClaimKind.ClassicAsync
        && role == StateMachineMethodRole.SetStateMachine;
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
