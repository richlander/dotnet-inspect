using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Returns one completed participant-scoped implementation-profile family
/// inspection.
/// </summary>
public static class ImplementationProfileFamilyInspectionOperation
{
    public static InspectionEnvelope<
        AssemblyContextEntry<AssemblyImplementationProfileFamilyInspection>>
        Execute(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            ImplementationProfileFamilySelection selection)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(selection);

        AssemblyContextEntry<AssemblyImplementationProfileFamilyInspection>
            content =
                AssemblyContextImplementationProfileFamilyQuery
                    .ExecuteParticipant(
                        group,
                        participant,
                        selection);
        return new(
            content,
            new InspectionShare.NonProjectable(
                "implementation-profile-family/share",
                "Implementation-profile families do not yet have a canonical "
                    + "Workspace Share projection."),
            ImplementationProfileInspectionDiagnostics.Create(
                content,
                inspection => inspection.Diagnostics,
                inspection => inspection.ApiSurfaceInspectionFailures));
    }
}
