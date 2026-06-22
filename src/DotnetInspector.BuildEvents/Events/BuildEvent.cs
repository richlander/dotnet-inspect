namespace DotnetInspector.BuildEvents.Events;

/// <summary>
/// A normalized build event envelope.
/// </summary>
public interface IBuildEvent
{
    int SchemaVersion { get; }

    string Kind { get; }

    long SequenceNumber { get; }

    DateTimeOffset Timestamp { get; }

    int ThreadId { get; }

    BuildEventContextData Context { get; }

    object? Payload { get; }
}

/// <summary>
/// A normalized build event envelope with a typed payload.
/// </summary>
public interface IBuildEvent<out TPayload> : IBuildEvent
{
    new TPayload Payload { get; }
}

/// <summary>
/// Default implementation of a normalized build event envelope.
/// </summary>
public sealed record BuildEvent<TPayload>(
    int SchemaVersion,
    string Kind,
    long SequenceNumber,
    DateTimeOffset Timestamp,
    int ThreadId,
    BuildEventContextData Context,
    TPayload Payload) : IBuildEvent<TPayload>
{
    object? IBuildEvent.Payload => Payload;
}
