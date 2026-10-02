using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Returns one complete participant-scoped Top Leverage designation for every
/// method declared by one selected Type.
/// </summary>
public static class TypeMethodLeverageInspectionOperation
{
    public static InspectionEnvelope<
        AssemblyContextEntry<AssemblyTypeMethodLeverageInspection>>
        Execute(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            string typeDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeDefinitionId);

        AssemblyContextEntry<AssemblyTypeMethodLeverageInspection>
            content =
                AssemblyContextTypeMethodLeverageQuery.ExecuteParticipant(
                    group,
                    participant,
                    typeDefinitionId);
        return new(
            content,
            new InspectionShare.NonProjectable(
                "type-method-leverage/share",
                "Type method leverage does not yet have a canonical "
                    + "Workspace Share projection."),
            ImplementationProfileInspectionDiagnostics.Create(
                content,
                inspection => inspection.Diagnostics,
                inspection => inspection.ApiSurfaceInspectionFailures,
                "method-leverage"));
    }
}
