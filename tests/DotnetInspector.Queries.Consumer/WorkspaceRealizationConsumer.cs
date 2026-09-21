using DotnetInspector.Queries;

namespace DotnetInspector.QueriesConsumer;

public static class WorkspaceRealizationConsumer
{
    public static async ValueTask<WorkspaceRealizationCandidate> BeginAsync(
        WorkspaceReplacementCoordinator coordinator,
        WorkspacePlan plan)
    {
        WorkspaceRealizationCandidateStartResult result =
            await coordinator.BeginCandidateAsync(plan);
        return AssertPrepared(result);
    }

    public static WorkspaceRealizationConstructionLease EnterConstruction(
        WorkspaceRealizationCandidate candidate) =>
        candidate.EnterConstruction();

    public static async ValueTask<WorkspaceRealization> ActivateAsync(
        WorkspaceReplacementCoordinator coordinator,
        WorkspaceRealizationCandidate candidate)
    {
        WorkspaceRealizationCandidateCompletionResult completion =
            await coordinator.CompleteCandidateAsync(candidate);
        if (completion
            is not WorkspaceRealizationCandidateCompletionResult.Ready)
        {
            throw new InvalidOperationException(
                "The candidate did not become ready.");
        }

        return coordinator.CutOver(candidate)
            is WorkspaceRealizationCutoverResult.Activated activated
                ? activated.Realization
                : throw new InvalidOperationException(
                    "The candidate did not activate.");
    }

    public static async ValueTask<WorkspaceRealizationOperationLease>
        EnterAsync(WorkspaceReplacementCoordinator coordinator)
    {
        WorkspaceRealizationOperationAdmission result =
            await coordinator.EnterOperationAsync();
        return result
            is WorkspaceRealizationOperationAdmission.Admitted admitted
                ? admitted.Lease
                : throw new InvalidOperationException(
                    "The realization did not admit the operation.");
    }

    static WorkspaceRealizationCandidate AssertPrepared(
        WorkspaceRealizationCandidateStartResult result) =>
        result is WorkspaceRealizationCandidateStartResult.Prepared prepared
            ? prepared.Candidate
            : throw new InvalidOperationException(
                "The replacement candidate was not prepared.");
}
