using InertText;

namespace DotnetInspector.Queries;

/// <summary>One admitted, resource-free Project input used to construct a Workspace.</summary>
public abstract class WorkspaceProjectInput
{
    private WorkspaceProjectInput(InertString declaredName)
    {
        DeclaredName = declaredName;
    }

    public InertString DeclaredName { get; }

    public sealed class Authored : WorkspaceProjectInput
    {
        public Authored(
            InertString declaredName,
            AuthoredProjectDependencyFactsResult result)
            : base(declaredName)
        {
            ArgumentNullException.ThrowIfNull(result);
            Facts = result switch
            {
                AuthoredProjectDependencyFactsResult.Available available =>
                    available.Value,
                AuthoredProjectDependencyFactsResult.Incomplete incomplete =>
                    incomplete.Value,
                _ => throw new ArgumentException(
                    "A Workspace requires an admitted authored Project document.",
                    nameof(result)),
            };
            Result = result;
        }

        public AuthoredProjectDependencyFactsResult Result { get; }
        public AuthoredProjectDependencyFacts Facts { get; }
    }

    public sealed class Restored : WorkspaceProjectInput
    {
        public Restored(
            InertString declaredName,
            RestoredProjectDependencyTraversalResult result)
            : base(declaredName)
        {
            ArgumentNullException.ThrowIfNull(result);
            Facts = result switch
            {
                RestoredProjectDependencyTraversalResult.Available available =>
                    available.Value.Facts,
                RestoredProjectDependencyTraversalResult.Unavailable unavailable =>
                    unavailable.Facts,
                RestoredProjectDependencyTraversalResult.Failed
                {
                    Failure: RestoredProjectDependencyTraversalFailure.Graph graph,
                } => graph.Facts,
                _ => throw new ArgumentException(
                    "A Workspace requires an admitted restored Project document.",
                    nameof(result)),
            };
            Result = result;
        }

        public RestoredProjectDependencyTraversalResult Result { get; }
        public RestoredProjectDependencyFacts Facts { get; }
    }
}

public sealed class WorkspaceProjectOccurrenceIdentity :
    InspectionWorkspaceOccurrenceIdentity
{
    internal WorkspaceProjectOccurrenceIdentity(
        InspectionWorkspaceIdentity workspace)
        : base(workspace)
    {
    }
}

/// <summary>One construction-bound Project input occurrence in one exact Workspace.</summary>
public sealed class WorkspaceProjectOccurrence
{
    internal WorkspaceProjectOccurrence(
        InspectionWorkspaceIdentity workspace,
        WorkspaceProjectInput input)
    {
        Identity = new(workspace);
        Input = input;
    }

    public WorkspaceProjectOccurrenceIdentity Identity { get; }
    public InspectionWorkspaceIdentity Workspace => Identity.WorkspaceIdentity;
    public WorkspaceProjectInput Input { get; }
}
