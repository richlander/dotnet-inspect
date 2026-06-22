using System.Text.Json.Serialization;
using DotnetInspector.BuildEvents.Events;

namespace DotnetInspector.BuildEvents;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BuildEvent<BuildStartedPayload>))]
[JsonSerializable(typeof(BuildEvent<BuildFinishedPayload>))]
[JsonSerializable(typeof(BuildEvent<ProjectStartedPayload>))]
[JsonSerializable(typeof(BuildEvent<ProjectFinishedPayload>))]
[JsonSerializable(typeof(BuildEvent<TargetStartedPayload>))]
[JsonSerializable(typeof(BuildEvent<TargetFinishedPayload>))]
[JsonSerializable(typeof(BuildEvent<TaskStartedPayload>))]
[JsonSerializable(typeof(BuildEvent<TaskFinishedPayload>))]
[JsonSerializable(typeof(BuildEvent<DiagnosticPayload>))]
[JsonSerializable(typeof(BuildEvent<OutputPayload>))]
[JsonSerializable(typeof(BuildEvent<ArtifactPayload>))]
[JsonSerializable(typeof(BuildEvent<SummaryPayload>))]
[JsonSerializable(typeof(BuildEvent<ExtensionPayload>))]
internal sealed partial class BuildEventJsonContext : JsonSerializerContext;
