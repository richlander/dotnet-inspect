using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>Detached Resource Triage content with its explicit Share outcome.</summary>
public static class AssemblyResourceTriageInspection
{
    public static async Task<InspectionEnvelope<AssemblyContextEntry<AssemblyResourceTriageResult>>> ExecuteWithRuntimeAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextGroup runtimeGroup,
        AssemblyContextParticipant runtimeParticipant) =>
        new(await AssemblyContextResourceTriageQuery.ExecuteParticipantWithRuntimeAsync(
                group, participant, runtimeGroup, runtimeParticipant),
            new InspectionShare.NonProjectable("resource-triage/share",
                "Resource Triage does not yet have a canonical Workspace Share projection."));

    public static InspectionEnvelope<AssemblyContextEntry<AssemblyResourceTriageResult>> Execute(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant) =>
        new(AssemblyContextResourceTriageQuery.ExecuteParticipant(group, participant),
            new InspectionShare.NonProjectable("resource-triage/share",
                "Resource Triage does not yet have a canonical Workspace Share projection."));
}
