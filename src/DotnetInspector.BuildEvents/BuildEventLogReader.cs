using System.Text.Json;
using DotnetInspector.BuildEvents.Events;

namespace DotnetInspector.BuildEvents;

/// <summary>
/// Reads newline-delimited JSON build events.
/// </summary>
public static class BuildEventLogReader
{
    public static IEnumerable<IBuildEvent> Read(string path)
    {
        using StreamReader reader = File.OpenText(path);
        foreach (IBuildEvent buildEvent in Read(reader))
        {
            yield return buildEvent;
        }
    }

    public static IEnumerable<IBuildEvent> Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            yield return ReadLine(line);
        }
    }

    public static IBuildEvent ReadLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            throw new ArgumentException("The JSONL line must not be null or empty.", nameof(line));
        }

        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;

        string kind = TryGetString(root, "kind") ?? BuildEventKind.Unknown;
        int schemaVersion = TryGetInt32(root, "schemaVersion", 0);

        return kind switch
        {
            BuildEventKind.BuildStarted => Deserialize<BuildStartedPayload>(line),
            BuildEventKind.BuildFinished => Deserialize<BuildFinishedPayload>(line),
            BuildEventKind.ProjectStarted => Deserialize<ProjectStartedPayload>(line),
            BuildEventKind.ProjectFinished => Deserialize<ProjectFinishedPayload>(line),
            BuildEventKind.TargetStarted => Deserialize<TargetStartedPayload>(line),
            BuildEventKind.TargetFinished => Deserialize<TargetFinishedPayload>(line),
            BuildEventKind.TaskStarted => Deserialize<TaskStartedPayload>(line),
            BuildEventKind.TaskFinished => Deserialize<TaskFinishedPayload>(line),
            BuildEventKind.Diagnostic => Deserialize<DiagnosticPayload>(line),
            BuildEventKind.Output => Deserialize<OutputPayload>(line),
            BuildEventKind.Artifact => Deserialize<ArtifactPayload>(line),
            BuildEventKind.Summary => Deserialize<SummaryPayload>(line),
            BuildEventKind.Extension => Deserialize<ExtensionPayload>(line),
            _ => CreateUnknown(line, root, kind, schemaVersion),
        };
    }

    private static BuildEvent<TPayload> Deserialize<TPayload>(string line)
    {
        return (BuildEvent<TPayload>?)JsonSerializer.Deserialize(line, typeof(BuildEvent<TPayload>), BuildEventJsonContext.Default)
            ?? throw new JsonException("Build event line deserialized to null.");
    }

    private static UnknownBuildEvent CreateUnknown(string line, JsonElement root, string kind, int schemaVersion)
    {
        long sequenceNumber = TryGetInt64(root, "sequenceNumber", 0);
        DateTimeOffset timestamp = TryGetDateTimeOffset(root, "timestamp", default);
        int threadId = TryGetInt32(root, "threadId", 0);
        BuildEventContextData context = TryGetContext(root);
        string? rawPayloadJson = root.TryGetProperty("payload", out JsonElement payload)
            ? payload.GetRawText()
            : null;

        return new UnknownBuildEvent(
            schemaVersion,
            kind,
            sequenceNumber,
            timestamp,
            threadId,
            context,
            line,
            rawPayloadJson);
    }

    private static BuildEventContextData TryGetContext(JsonElement root)
    {
        if (!root.TryGetProperty("context", out JsonElement context) ||
            context.ValueKind != JsonValueKind.Object)
        {
            return BuildEventContextData.Empty;
        }

        return new BuildEventContextData(
            TryGetInt32(context, "submissionId", BuildEventContextData.Empty.SubmissionId),
            TryGetInt32(context, "nodeId", BuildEventContextData.Empty.NodeId),
            TryGetInt32(context, "evaluationId", BuildEventContextData.Empty.EvaluationId),
            TryGetInt32(context, "projectInstanceId", BuildEventContextData.Empty.ProjectInstanceId),
            TryGetInt32(context, "projectContextId", BuildEventContextData.Empty.ProjectContextId),
            TryGetInt32(context, "targetId", BuildEventContextData.Empty.TargetId),
            TryGetInt32(context, "taskId", BuildEventContextData.Empty.TaskId));
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int TryGetInt32(JsonElement root, string propertyName, int fallback)
    {
        return root.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt32(out int value)
            ? value
            : fallback;
    }

    private static long TryGetInt64(JsonElement root, string propertyName, long fallback)
    {
        return root.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt64(out long value)
            ? value
            : fallback;
    }

    private static DateTimeOffset TryGetDateTimeOffset(JsonElement root, string propertyName, DateTimeOffset fallback)
    {
        return root.TryGetProperty(propertyName, out JsonElement property) && property.TryGetDateTimeOffset(out DateTimeOffset value)
            ? value
            : fallback;
    }
}
