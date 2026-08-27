using System.Collections.Concurrent;
using System.Collections.Immutable;
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

internal enum ClassicAsyncStage
{
    Raised,
    Lowered,
}

/// <summary>Why a healthy classic state machine was not reconstructed.</summary>
public enum ClassicAsyncDeclineReason
{
    NoExecutionMethod,
    KickoffMachineMismatch,
    NonNarrowKickoffHandoff,
    UnsupportedBuilder,
    UnconsumedExecutionRegion,
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

internal sealed record ClassicAsyncFailure(
    string DiagnosticId,
    string Message);

internal abstract record ClassicAsyncPreparationResult
{
    private ClassicAsyncPreparationResult()
    {
    }

    internal sealed record NotApplicable(
        ClassicAsyncHostRole HostRole,
        MethodClassification? Classification)
        : ClassicAsyncPreparationResult;

    internal sealed record InputUnavailable(
        StateMachineRelationshipFailure Failure)
        : ClassicAsyncPreparationResult;

    internal sealed record ImportFailed(
        ClassicAsyncHostRole Role,
        ClassicAsyncFailure Failure)
        : ClassicAsyncPreparationResult;

    internal sealed record PlanningFailed(
        ClassicAsyncFailure Failure)
        : ClassicAsyncPreparationResult;

    internal sealed record Decided(
        ClassicAsyncDecision Decision)
        : ClassicAsyncPreparationResult;
}

internal abstract record ClassicAsyncDecision
{
    private ClassicAsyncDecision()
    {
    }

    internal sealed record Reconstruct(
        ClassicAsyncPlan Plan)
        : ClassicAsyncDecision;

    internal sealed record Decline(
        ClassicAsyncDeclineReason Reason,
        ClassicAsyncKickoffDisposition KickoffDisposition)
        : ClassicAsyncDecision;
}

internal abstract record ClassicAsyncStageResult
{
    private ClassicAsyncStageResult()
    {
    }

    internal sealed record Applied(
        ClassicAsyncStage Stage,
        ClassicAsyncOutcome Outcome,
        ClassicAsyncDeclarationDisposition DeclarationDisposition)
        : ClassicAsyncStageResult;

    internal sealed record NotApplicable(
        ClassicAsyncStage Stage)
        : ClassicAsyncStageResult;

    internal sealed record Failed(
        ClassicAsyncStage Stage,
        ClassicAsyncFailure Failure)
        : ClassicAsyncStageResult;
}

internal sealed record ClassicAsyncMachine(
    MetadataMethodAddress Kickoff,
    MetadataMethodAddress Execution,
    MetadataTypeDefinitionAddress StateMachine,
    MetadataTypeDefinitionName StateMachineName,
    TypeRef StateMachineType,
    int StateMachineLocal,
    object AcquisitionGuard);

internal sealed record ClassicAsyncPlan(
    ClassicAsyncMachine Machine,
    ClassicAsyncBodyPlan Body,
    IrTypeFactsSnapshot TypeFacts);

/// <summary>
/// A detached body template owned by one immutable classic decision. The
/// captured tree is never exposed; every application receives a deep copy.
/// </summary>
internal sealed class ClassicAsyncBodyPlan
{
    readonly BlockContainer _body;

    ClassicAsyncBodyPlan(
        BlockContainer body,
        ImmutableArray<TypeRef> locals,
        ImmutableArray<string?> localNames)
    {
        _body = (BlockContainer)body.Clone();
        Locals = locals;
        LocalNames = localNames;
    }

    internal ImmutableArray<TypeRef> Locals { get; }

    internal ImmutableArray<string?> LocalNames { get; }

    internal static ClassicAsyncBodyPlan Capture(
        BlockContainer body,
        ImmutableArray<TypeRef> locals,
        ImmutableArray<string?> localNames)
        => new(body, locals, localNames);

    internal BlockContainer Materialize()
        => (BlockContainer)_body.Clone();
}

/// <summary>
/// Source-lifetime cache for classic decisions. Work is performed outside the
/// dictionary so nested or concurrent preparation never holds another
/// address's publication lock.
/// </summary>
internal interface IClassicAsyncPlanningSession
{
    ClassicAsyncPreparationResult Prepare(
        ClassicAsyncRelationshipEvidence evidence);
}

internal sealed class ClassicAsyncPlanningSession
    : IClassicAsyncPlanningSession
{
    static int s_nextId;

    [ThreadStatic]
    static HashSet<(int Session, MetadataMethodAddress Method)>? s_active;

    readonly ConcurrentDictionary<
        MetadataMethodAddress,
        ClassicAsyncPreparationResult> _preparations = new();
    readonly MetadataSource _source;
    readonly int _id = Interlocked.Increment(ref s_nextId);
    int _preparationCount;

    internal ClassicAsyncPlanningSession(MetadataSource source)
        => _source = source;

    internal int PreparationCount => Volatile.Read(ref _preparationCount);

    internal int PublishedPreparationCount => _preparations.Count;

    ClassicAsyncPreparationResult IClassicAsyncPlanningSession.Prepare(
        ClassicAsyncRelationshipEvidence evidence)
        => Prepare(evidence);

    internal ClassicAsyncPreparationResult Prepare(
        ClassicAsyncRelationshipEvidence evidence)
    {
        if (_preparations.TryGetValue(
                evidence.RequestedHost,
                out ClassicAsyncPreparationResult? prepared))
        {
            return prepared;
        }

        s_active ??= [];
        var key = (_id, evidence.RequestedHost);
        if (!s_active.Add(key))
        {
            return new ClassicAsyncPreparationResult.PlanningFailed(
                new(
                    DiagnosticIds.InternalError,
                    "classic async planning re-entered the same method"));
        }

        try
        {
            Interlocked.Increment(ref _preparationCount);
            ClassicAsyncPreparationResult candidate =
                ClassicAsyncReconstructionPass.Prepare(
                    _source,
                    evidence);
            return _preparations.GetOrAdd(
                evidence.RequestedHost,
                candidate);
        }
        finally
        {
            s_active.Remove(key);
        }
    }
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
    object AcquisitionGuard,
    IClassicAsyncPlanningSession PlanningSession);
