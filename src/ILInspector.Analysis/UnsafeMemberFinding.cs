using System.Collections.Immutable;

using Inspector.Findings;

namespace ILInspector.Analysis;

/// <summary>
/// Whether a declared member is an exact public MethodDef root of its image.
/// </summary>
public enum UnsafeMemberExposure
{
    Public,
    NonPublic,
    Unknown,
}

/// <summary>
/// One positive unsafe-member role and the physical body that produced it.
/// </summary>
public sealed record UnsafeMemberFindingEvidence(
    UnsafeMemberUseKind Kind,
    MethodIdentity Body,
    int? ILOffset,
    string Detail);

/// <summary>Why an unsafe member census or finding is not complete.</summary>
public enum UnsafeMemberLimitationReason
{
    /// <summary>The body-analysis receipt lacks full method-evidence scope.</summary>
    ScopedReceipt,
    /// <summary>Acquisition, decode, or analysis of a body did not complete.</summary>
    BodyAnalysisFailed,
    /// <summary>A generated body's owner could not be authenticated.</summary>
    UnattributedGeneratedBody,
    /// <summary>The image is a reference assembly.</summary>
    ReferenceAssembly,
    /// <summary>The reference-assembly rule could not be decided.</summary>
    ReferenceAssemblyUndecidable,
    /// <summary>The public root inventory stopped at a bound.</summary>
    PublicRootInventoryBounded,
    /// <summary>The public root inventory could not be read.</summary>
    PublicRootInventoryFailed,
}

/// <summary>
/// A typed reason a census or finding is not complete, with the affected
/// physical body when it has one and its declared member when that body's
/// owner was authenticated.
/// </summary>
public sealed record UnsafeMemberLimitation(
    UnsafeMemberLimitationReason Reason,
    int? BodyToken,
    MethodIdentity? Body,
    MethodIdentity? DeclaredMember,
    ImmutableArray<UnsafeMemberFindingEvidence> Evidence,
    string? Detail = null)
{
    ImmutableArray<UnsafeMemberFindingEvidence> _evidence =
        ImmutableArrayValueEquality.EmptyIfDefault(Evidence, nameof(Evidence));

    public ImmutableArray<UnsafeMemberFindingEvidence> Evidence
    {
        get => _evidence;
        init => _evidence = ImmutableArrayValueEquality.EmptyIfDefault(value, nameof(Evidence));
    }

    public bool Equals(UnsafeMemberLimitation? other)
        => other is not null
            && Reason == other.Reason
            && BodyToken == other.BodyToken
            && Equals(Body, other.Body)
            && Equals(DeclaredMember, other.DeclaredMember)
            && ImmutableArrayValueEquality.SequenceEqual(Evidence, other.Evidence)
            && string.Equals(Detail, other.Detail, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Reason);
        hash.Add(BodyToken);
        hash.Add(Body);
        hash.Add(DeclaredMember);
        ImmutableArrayValueEquality.AddToHash(ref hash, Evidence);
        hash.Add(Detail, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One declared member with positive compiled unsafe-member evidence, folded
/// from its own body and the generated bodies it owns.
/// </summary>
public sealed record UnsafeMemberFinding(
    MethodIdentity Member,
    bool HasExplicitUnsafeContract,
    ImmutableArray<UnsafeMemberFindingEvidence> Evidence,
    ImmutableArray<UnsafeMemberLimitation> UninspectedBodies,
    UnsafeMemberExposure Exposure)
{
    ImmutableArray<UnsafeMemberFindingEvidence> _evidence =
        ImmutableArrayValueEquality.RequireInitialized(Evidence, nameof(Evidence));
    ImmutableArray<UnsafeMemberLimitation> _uninspectedBodies =
        ImmutableArrayValueEquality.EmptyIfDefault(UninspectedBodies, nameof(UninspectedBodies));

    public ImmutableArray<UnsafeMemberFindingEvidence> Evidence
    {
        get => _evidence;
        init => _evidence = ImmutableArrayValueEquality.RequireInitialized(value, nameof(Evidence));
    }

    public ImmutableArray<UnsafeMemberLimitation> UninspectedBodies
    {
        get => _uninspectedBodies;
        init => _uninspectedBodies = ImmutableArrayValueEquality.EmptyIfDefault(value, nameof(UninspectedBodies));
    }

    public bool Equals(UnsafeMemberFinding? other)
        => other is not null
            && Equals(Member, other.Member)
            && HasExplicitUnsafeContract == other.HasExplicitUnsafeContract
            && ImmutableArrayValueEquality.SequenceEqual(Evidence, other.Evidence)
            && ImmutableArrayValueEquality.SequenceEqual(UninspectedBodies, other.UninspectedBodies)
            && Exposure == other.Exposure;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Member);
        hash.Add(HasExplicitUnsafeContract);
        ImmutableArrayValueEquality.AddToHash(ref hash, Evidence);
        ImmutableArrayValueEquality.AddToHash(ref hash, UninspectedBodies);
        hash.Add(Exposure);
        return hash.ToHashCode();
    }

    /// <summary>Whether an attributed body could not be inspected.</summary>
    public bool HasPartialEvidence => !UninspectedBodies.IsEmpty;

    /// <summary>
    /// Whether the member propagates unsafe context: only an explicit
    /// updated-model contract on the member itself.
    /// </summary>
    public bool PropagatesUnsafe => HasExplicitUnsafeContract;
}

/// <summary>
/// The Analysis-issued unsafe member census for one execution, measured
/// against the inventory's declared scope.
/// </summary>
public sealed record UnsafeMemberCensus(
    ImmutableArray<UnsafeMemberFinding> Members,
    ImmutableArray<UnsafeMemberLimitation> Limitations)
{
    ImmutableArray<UnsafeMemberFinding> _members =
        ImmutableArrayValueEquality.EmptyIfDefault(Members, nameof(Members));
    ImmutableArray<UnsafeMemberLimitation> _limitations =
        ImmutableArrayValueEquality.EmptyIfDefault(Limitations, nameof(Limitations));

    public ImmutableArray<UnsafeMemberFinding> Members
    {
        get => _members;
        init => _members = ImmutableArrayValueEquality.EmptyIfDefault(value, nameof(Members));
    }

    public ImmutableArray<UnsafeMemberLimitation> Limitations
    {
        get => _limitations;
        init => _limitations = ImmutableArrayValueEquality.EmptyIfDefault(value, nameof(Limitations));
    }

    public bool IsComplete => Limitations.IsEmpty;

    public bool Equals(UnsafeMemberCensus? other)
        => other is not null
            && ImmutableArrayValueEquality.SequenceEqual(Members, other.Members)
            && ImmutableArrayValueEquality.SequenceEqual(Limitations, other.Limitations);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        ImmutableArrayValueEquality.AddToHash(ref hash, Members);
        ImmutableArrayValueEquality.AddToHash(ref hash, Limitations);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Complete, incomplete, or failed unsafe member Finding inspection.
/// </summary>
public abstract record UnsafeMemberFindingInspection
{
    public sealed record Complete(
        FindingInspection<UnsafeMemberFinding>.Complete Inspection)
        : UnsafeMemberFindingInspection;

    /// <summary>
    /// Sound findings produced so far, never presented as a complete census.
    /// </summary>
    public sealed record Incomplete : UnsafeMemberFindingInspection
    {
        public Incomplete(
            FindingInspection<UnsafeMemberFinding>.Complete inspection,
            ImmutableArray<UnsafeMemberLimitation> limitations)
        {
            ArgumentNullException.ThrowIfNull(inspection);
            if (limitations.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "An incomplete census requires at least one limitation.",
                    nameof(limitations));
            }
            Inspection = inspection;
            Limitations = limitations;
        }

        public FindingInspection<UnsafeMemberFinding>.Complete Inspection { get; }

        public ImmutableArray<UnsafeMemberLimitation> Limitations { get; }

        public bool Equals(Incomplete? other)
            => other is not null
                && Equals(Inspection, other.Inspection)
                && ImmutableArrayValueEquality.SequenceEqual(Limitations, other.Limitations);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Inspection);
            ImmutableArrayValueEquality.AddToHash(ref hash, Limitations);
            return hash.ToHashCode();
        }
    }

    public sealed record Failed(InspectionError Error)
        : UnsafeMemberFindingInspection;
}
