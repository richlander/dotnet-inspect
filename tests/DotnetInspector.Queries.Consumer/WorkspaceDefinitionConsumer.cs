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
}
