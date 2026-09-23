using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Returns one completed participant-scoped implementation-profile inspection.
/// </summary>
public static class ImplementationProfileInspectionOperation
{
    public static InspectionEnvelope<
        AssemblyContextEntry<AssemblyImplementationProfileInspection>> Execute(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);

        AssemblyContextEntry<AssemblyImplementationProfileInspection> content =
            AssemblyContextImplementationProfilesQuery.ExecuteParticipant(
                group,
                participant);
        return new(
            content,
            new InspectionShare.NonProjectable(
                "implementation-profiles/share",
                "Implementation profiles do not yet have a canonical "
                    + "Workspace Share projection."),
            ImplementationProfileInspectionDiagnostics.Create(
                content,
                inspection => inspection.Diagnostics,
                inspection => inspection.ApiSurfaceInspectionFailures));
    }
}
