using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// The compiled role that admits a method to an unsafe-member inventory.
/// </summary>
public enum UnsafeMemberUseKind
{
    ExplicitContract,
    PointerDereference,
    IndirectCall,
    StackAllocation,
    ExplicitContractCall,
}

/// <summary>
/// Positive compiled evidence for one unsafe-member role.
/// </summary>
public sealed record UnsafeMemberUseEvidence(
    UnsafeMemberUseKind Kind,
    int? ILOffset,
    string Detail);

/// <summary>
/// One physical method with positive compiled unsafe-member evidence.
/// </summary>
public sealed record UnsafeMemberUse(
    MethodIdentity Method,
    bool HasExplicitUnsafeContract,
    ImmutableArray<UnsafeMemberUseEvidence> Evidence);
