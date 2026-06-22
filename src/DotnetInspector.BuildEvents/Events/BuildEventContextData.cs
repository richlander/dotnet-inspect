namespace DotnetInspector.BuildEvents.Events;

public readonly record struct BuildEventContextData(
    int SubmissionId,
    int NodeId,
    int EvaluationId,
    int ProjectInstanceId,
    int ProjectContextId,
    int TargetId,
    int TaskId)
{
    public static BuildEventContextData Empty { get; } = new(-1, -2, -1, -1, -2, -1, -1);

    public BuildContextKey ToKey() => new(SubmissionId, ProjectInstanceId, ProjectContextId);

    public BuildContextKey ProjectKey => new(SubmissionId, ProjectInstanceId, ProjectContextId);
}

public readonly record struct BuildContextKey(int SubmissionId, int ProjectInstanceId, int ProjectContextId);
