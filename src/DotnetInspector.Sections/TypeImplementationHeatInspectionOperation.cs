using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Returns one completed participant-scoped member-list heat inspection for
/// every eligible overload family on one Type.
/// </summary>
public static class TypeImplementationHeatInspectionOperation
{
    public static InspectionEnvelope<
        AssemblyContextEntry<AssemblyTypeImplementationHeatInspection>>
        Execute(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            string typeDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeDefinitionId);

        AssemblyContextEntry<AssemblyTypeImplementationHeatInspection>
            content =
                AssemblyContextTypeImplementationHeatQuery.ExecuteParticipant(
                    group,
                    participant,
                    typeDefinitionId);
        return new(
            content,
            new InspectionShare.NonProjectable(
                "type-implementation-heat/share",
                "Type implementation heat does not yet have a canonical "
                    + "Workspace Share projection."),
            ImplementationProfileInspectionDiagnostics.Create(
                content,
                inspection => inspection.Diagnostics,
                inspection => inspection.ApiSurfaceInspectionFailures));
    }
}
