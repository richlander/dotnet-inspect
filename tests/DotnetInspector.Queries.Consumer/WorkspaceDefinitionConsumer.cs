using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.QueriesConsumer;

public static class WorkspaceDefinitionConsumer
{
    public static WorkspacePlan GetPlan(ResolvedScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return scenario.WorkspacePlan
            ?? throw new InvalidOperationException(
                "The resolved scenario is workspace-free.");
    }

    public static InspectionWorkspace CreateWorkspace(
        ResolvedScenario scenario) =>
        new(GetPlan(scenario));

    public static CommittedScenarioDefinitionSet GetCommittedDefinitions(
        InspectionDefinitionRegistry registry,
        string scenarioId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.PrepareScenario(scenarioId) switch
        {
            InspectionDefinitionScenarioPreparationResult.Version2 prepared =>
                prepared.Definitions,
            InspectionDefinitionScenarioPreparationResult
                .LegacyCompatibilityRequired =>
                throw new InvalidOperationException(
                    "The scenario requires schema-version-1 compatibility execution."),
            InspectionDefinitionScenarioPreparationResult.Version1 =>
                throw new InvalidOperationException(
                    "The scenario is a schema-version-1 composition."),
            _ => throw new InvalidOperationException(
                "The scenario preparation result is unknown."),
        };
    }

    public static CommittedScenarioSelectorResolutionResult ResolveSelectors(
        CommittedScenarioDefinitionSet definitions,
        InspectionWorkspaceIdentity workspace,
        WorkspaceScopeSnapshot scope,
        IReadOnlyList<CommittedPackageStateResolutionFacts> packageFacts) =>
        CommittedScenarioSelectorResolver.Resolve(
            definitions,
            workspace,
            scope,
            packageFacts);

    public static CompleteRestorationPreparationResult PrepareRestoration(
        InspectionDefinitionRegistry registry,
        string scenarioId,
        ICompleteRestorationIntentAuthority authority) =>
        CompleteRestorationPreparation.FromDefinition(
            registry,
            scenarioId,
            authority);

    public static CompleteRestorationPreparationResult PrepareRestoration(
        string packet,
        ICompleteRestorationIntentAuthority authority) =>
        CompleteRestorationPreparation.FromPacket(packet, authority);

    public static ValueTask<CompleteRestorationResult<TActivation>>
        RestoreAsync<TActivation>(
            CompleteRestorationPreparationResult preparation,
            ICompleteRestorationIntentAuthority authority,
            ICompleteRestorationHost<TActivation> host,
            CompleteRestorationExecutionOptions options,
            CancellationToken cancellationToken = default) =>
        CompleteRestorationCoordinator.RestoreAsync(
            preparation,
            authority,
            host,
            options,
            cancellationToken);
}
