namespace DotnetInspector.BuildEvents.Events;

public sealed record UnknownBuildEvent(
    int SchemaVersion,
    string Kind,
    long SequenceNumber,
    DateTimeOffset Timestamp,
    int ThreadId,
    BuildEventContextData Context,
    string RawJson,
    string? RawPayloadJson) : IBuildEvent
{
    object? IBuildEvent.Payload => RawPayloadJson;
}
