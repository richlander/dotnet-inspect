namespace DotnetInspector.BuildEvents.Events;

/// <summary>
/// Well-known build event stream kinds.
/// </summary>
public static class BuildEventKind
{
    public const string BuildStarted = "build.started";
    public const string BuildFinished = "build.finished";
    public const string ProjectStarted = "project.started";
    public const string ProjectFinished = "project.finished";
    public const string TargetStarted = "target.started";
    public const string TargetFinished = "target.finished";
    public const string TaskStarted = "task.started";
    public const string TaskFinished = "task.finished";
    public const string Diagnostic = "diagnostic";
    public const string Artifact = "artifact";
    public const string Output = "output";
    public const string Summary = "summary";
    public const string Extension = "extension";
    public const string Unknown = "unknown";
}
