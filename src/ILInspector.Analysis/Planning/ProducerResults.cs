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
}

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
    string? FailedPrerequisite = null)
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
    public ProducerParticipation For(ProducerDeclaration producer) =>
        Producers.Single(participation =>
            string.Equals(
                participation.Producer,
                producer.Identity,
                StringComparison.Ordinal));
}

/// <summary>
/// Thrown when a producer reads a layer, fact, or result it did not declare.
/// It is a producer contract violation, never a recoverable unit failure.
/// </summary>
public sealed class ProducerContractException(string message)
    : Exception(message);
