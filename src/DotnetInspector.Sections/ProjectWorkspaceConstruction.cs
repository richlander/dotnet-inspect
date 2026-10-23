using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public abstract record ProjectWorkspacePlanOutcome
{
    private ProjectWorkspacePlanOutcome() { }

    public sealed record Available(WorkspacePlan Plan) :
        ProjectWorkspacePlanOutcome;

    public sealed record Rejected(ProjectInputDocument Input) :
        ProjectWorkspacePlanOutcome;
}

/// <summary>Prepares inert Workspace construction intent from an inspected Project.</summary>
public static class ProjectWorkspaceConstruction
{
    public static ProjectWorkspacePlanOutcome CreatePlan(
        ProjectInputDocument input,
        TraversalTargetFrameworkPolicy traversalTargetPolicy)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(traversalTargetPolicy);
        WorkspaceProjectInput project;
        switch (input.Kind)
        {
            case ProjectInputKind.AuthoredProject:
                if (input.AuthoredProject
                    is AuthoredProjectDependencyFactsResult.Failed)
                {
                    return new ProjectWorkspacePlanOutcome.Rejected(input);
                }
                project = new WorkspaceProjectInput.Authored(
                    input.DeclaredName,
                    input.AuthoredProject
                        ?? throw new InvalidOperationException(
                            "Authored Project input is missing its provider outcome."));
                break;
            case ProjectInputKind.RestoredProject:
                if (input.RestoredTraversal
                    is RestoredProjectDependencyTraversalResult.Failed
                    {
                        Failure: RestoredProjectDependencyTraversalFailure.Document,
                    })
                {
                    return new ProjectWorkspacePlanOutcome.Rejected(input);
                }
                project = new WorkspaceProjectInput.Restored(
                    input.DeclaredName,
                    input.RestoredTraversal
                        ?? throw new InvalidOperationException(
                            "Restored Project input is missing its provider outcome."));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
        }

        return new ProjectWorkspacePlanOutcome.Available(
            new WorkspacePlan(traversalTargetPolicy, [], [], project));
    }
}
