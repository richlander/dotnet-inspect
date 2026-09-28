using System.Collections.Immutable;

namespace ILInspector.Analysis.Planning;

/// <summary>How one producer's work ended.</summary>
public enum ProducerOutcome
{
    /// <summary>Every unit in scope was visited and the producer completed.</summary>
    Complete,

    /// <summary>
    /// The producer stopped because the request it served was satisfied, such
    /// as a settled Exists terminal.
    /// </summary>
    Stopped,

    /// <summary>The producer failed; <see cref="ProducerResult{T}.Failure"/> says where.</summary>
    Failed,

    /// <summary>A declared dependency failed, so this producer did not run to completion.</summary>
    PrerequisiteFailed,

    /// <summary>
    /// A containment budget was exhausted, so the whole execution stopped and
    /// published no result; <see cref="ProducerResult{T}.Critical"/> says which.
    /// </summary>
    Aborted,
}

/// <summary>
/// The one critical failure that aborted an execution: the owner of the
/// exhausted budget (the source gate or a producer), the budget, and the unit
/// being visited. Every requested producer carries the same value.
/// </summary>
public sealed record CriticalFailure(
    string Owner,
    string Budget,
    int UnitToken,
    string Unit,
    string Message);

/// <summary>Where and why a producer failed.</summary>
public sealed record ProducerFailure(
    int UnitToken,
    string Unit,
    string Message);

/// <summary>One producer's typed result and outcome.</summary>
public sealed record ProducerResult<T>(
    ProducerOutcome Outcome,
    T? Value,
    ProducerFailure? Failure = null,
    string? FailedPrerequisite = null,
    CriticalFailure? Critical = null)
{
    public bool HasValue =>
        Outcome is ProducerOutcome.Complete or ProducerOutcome.Stopped;
}

/// <summary>How often one layer of a unit was acquired for a producer.</summary>
public sealed record ProducerLayerParticipation(
    string Layer,
    int Acquired);

/// <summary>
/// What actually happened for one producer, recorded by the executor where
/// work started and ended.
/// </summary>
public sealed record ProducerParticipation(
    string Producer,
    ProducerOutcome Outcome,
    int UnitsAttempted,
    int UnitsCompleted,
    int UnitsFailed,
    ImmutableArray<ProducerLayerParticipation> Layers);

/// <summary>The participation receipt for one execution of a work description.</summary>
public sealed record WorkReceipt(
    int UnitsVisited,
    ImmutableArray<ProducerParticipation> Producers)
{
    /// <summary>The critical failure that aborted the execution, if any.</summary>
    public CriticalFailure? Critical { get; init; }

    /// <summary>Whether the plan declared identity text, which arms the gate's identity budget.</summary>
    public bool IdentityBudgetArmed { get; init; }

    /// <summary>Identity work charged against the gate's identity budget.</summary>
    public long IdentityWorkCharged { get; init; }

    /// <summary>Signature-shape nodes the gate walked across the execution, memoized walks counted once.</summary>
    public long SignatureShapeNodesWalked { get; init; }

    public ProducerParticipation For(ProducerDeclaration producer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        foreach (ProducerParticipation participation in Producers)
        {
            if (string.Equals(
                    participation.Producer,
                    producer.Identity,
                    StringComparison.Ordinal))
            {
                return participation;
            }
        }

        throw new InvalidOperationException(
            $"Producer '{producer.Identity}' has no participation.");
    }
}

/// <summary>
/// Thrown when a producer reads a layer, fact, or result it did not declare.
/// It is a producer contract violation, never a recoverable unit failure.
/// </summary>
public sealed class ProducerContractException(string message)
    : Exception(message);
