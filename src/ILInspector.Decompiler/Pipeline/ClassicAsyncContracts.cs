using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>The role of the requested method in an owner-issued classic state-machine relationship.</summary>
public enum ClassicAsyncHostRole
{
    DeclaredKickoff,
    Execution,
    Support,
    Ordinary,
}

/// <summary>How a classic-async decision resolves the declaration's async modifier.</summary>
public enum ClassicAsyncDeclarationDisposition
{
    NoOpinion,
    IncludeAsync,
    OmitAsync,
}

/// <summary>Why a healthy classic state machine was not reconstructed.</summary>
public enum ClassicAsyncDeclineReason
{
    KickoffMachineMismatch,
    NonNarrowKickoffHandoff,
    UnsupportedBuilder,
    UnrecognizedAwaiterProtocol,
}

/// <summary>How a declined kickoff body preserves its physical handoff.</summary>
public enum ClassicAsyncKickoffDisposition
{
    ReplacedNarrowHandoff,
    PreservedOriginal,
}

/// <summary>The typed terminal outcome of a healthy classic-async decision.</summary>
public abstract record ClassicAsyncOutcome
{
    private ClassicAsyncOutcome()
    {
    }

    public sealed record Reconstructed : ClassicAsyncOutcome;

    public sealed record Declined(
        ClassicAsyncDeclineReason Reason,
        ClassicAsyncKickoffDisposition KickoffDisposition)
        : ClassicAsyncOutcome;
}

/// <summary>
/// Metadata-owned relationship evidence stamped onto one imported method while
/// its acquisition source is live.
/// </summary>
internal sealed record ClassicAsyncRelationshipEvidence(
    MetadataMethodAddress RequestedHost,
    ClassicAsyncHostRole HostRole,
    MethodClassification? Classification,
    StateMachineRelationshipResult Relationship,
    object AcquisitionGuard);
