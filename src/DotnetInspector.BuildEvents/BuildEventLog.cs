using DotnetInspector.BuildEvents.Events;

namespace DotnetInspector.BuildEvents;

public sealed class BuildEventLog
{
    private BuildEventLog(string? eventLogId)
    {
        EventLogId = eventLogId;
    }

    public string? EventLogId { get; }

    public List<ProjectEvent> Projects { get; } = [];

    public List<ProjectResultEvent> ProjectResults { get; } = [];

    public List<DiagnosticEvent> Diagnostics { get; } = [];

    public List<TargetEvent> Targets { get; } = [];

    public List<TaskEvent> Tasks { get; } = [];

    public List<UnknownBuildEvent> UnknownEvents { get; } = [];

    public Dictionary<BuildContextKey, ProjectEvent> ProjectByContext { get; } = [];

    public BuildFinishedEvent? BuildFinished { get; private set; }

    public static BuildEventLog Load(string path, string? eventLogId = null)
    {
        BuildEventLog log = new(eventLogId ?? Path.GetFileNameWithoutExtension(path));

        foreach (IBuildEvent buildEvent in BuildEventLogReader.Read(path))
        {
            switch (buildEvent)
            {
                case IBuildEvent<ProjectStartedPayload> projectStarted:
                    ProjectEvent project = new(
                        projectStarted.SequenceNumber,
                        projectStarted.Context,
                        projectStarted.Payload.ProjectFile,
                        projectStarted.Payload.Dimensions,
                        projectStarted.Payload.HasParentContext
                            ? projectStarted.Payload.ParentContext.ToKey()
                            : default);
                    log.Projects.Add(project);
                    log.ProjectByContext[projectStarted.Context.ToKey()] = project;
                    break;

                case IBuildEvent<ProjectFinishedPayload> projectFinished:
                    log.ProjectResults.Add(new ProjectResultEvent(
                        projectFinished.SequenceNumber,
                        projectFinished.Context,
                        projectFinished.Payload.ProjectFile,
                        projectFinished.Payload.Dimensions,
                        projectFinished.Payload.Succeeded,
                        projectFinished.Payload.HasParentContext
                            ? projectFinished.Payload.ParentContext.ToKey()
                            : default));
                    break;

                case IBuildEvent<DiagnosticPayload> diagnostic:
                    log.Diagnostics.Add(new DiagnosticEvent(
                        diagnostic.SequenceNumber,
                        diagnostic.Context,
                        diagnostic.Payload.Severity,
                        diagnostic.Payload.Code,
                        diagnostic.Payload.File,
                        diagnostic.Payload.ProjectFile,
                        diagnostic.Payload.LineNumber,
                        diagnostic.Payload.ColumnNumber,
                        diagnostic.Payload.EndLineNumber,
                        diagnostic.Payload.EndColumnNumber,
                        diagnostic.Payload.Message));
                    break;

                case IBuildEvent<TargetStartedPayload> target:
                    log.Targets.Add(new TargetEvent(target.SequenceNumber, target.Context, target.Payload.TargetName));
                    break;

                case IBuildEvent<TaskStartedPayload> task:
                    log.Tasks.Add(new TaskEvent(task.SequenceNumber, task.Context, task.Payload.ProjectFile, task.Payload.TaskName));
                    break;

                case IBuildEvent<BuildFinishedPayload> buildFinished:
                    log.BuildFinished = new BuildFinishedEvent(
                        buildFinished.SequenceNumber,
                        buildFinished.Context,
                        buildFinished.Payload.Succeeded,
                        buildFinished.Payload.Message);
                    break;

                case UnknownBuildEvent unknown:
                    log.UnknownEvents.Add(unknown);
                    break;
            }
        }

        return log;
    }
}

public sealed record ProjectEvent(
    long SequenceNumber,
    BuildEventContextData Context,
    string? ProjectFile,
    ProjectDimensions Dimensions,
    BuildContextKey ParentContextKey);

public sealed record ProjectResultEvent(
    long SequenceNumber,
    BuildEventContextData Context,
    string? ProjectFile,
    ProjectDimensions Dimensions,
    bool Succeeded,
    BuildContextKey ParentContextKey)
{
    public BuildContextKey ProjectContextKey => Context.ProjectKey;
}

public sealed record DiagnosticEvent(
    long SequenceNumber,
    BuildEventContextData Context,
    string Severity,
    string? Code,
    string? File,
    string? ProjectFile,
    int LineNumber,
    int ColumnNumber,
    int EndLineNumber,
    int EndColumnNumber,
    string? Message);

public sealed record TargetEvent(long SequenceNumber, BuildEventContextData Context, string? TargetName)
{
    public BuildContextKey ProjectContextKey => Context.ProjectKey;
}

public sealed record TaskEvent(long SequenceNumber, BuildEventContextData Context, string? ProjectFile, string? TaskName)
{
    public BuildContextKey ProjectContextKey => Context.ProjectKey;
}

public sealed record BuildFinishedEvent(
    long SequenceNumber,
    BuildEventContextData Context,
    bool Succeeded,
    string? Message);
