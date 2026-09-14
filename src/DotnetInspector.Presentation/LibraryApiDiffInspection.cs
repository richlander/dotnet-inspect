using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Presentation;

/// <summary>Completes one selected-Library API comparison for either host.</summary>
public static class LibraryApiDiffInspection
{
    public static InspectionEnvelope<LibraryApiDiffPresentationResult> Execute(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant after,
        ApiSurfaceScope scope,
        ApiSurfaceProjectionLimits perEndpointLimits)
    {
        AssemblyContextApiComparisonResult comparison =
            AssemblyContextApiComparisonQuery.Execute(
                beforeGroup,
                before,
                afterGroup,
                after,
                scope,
                perEndpointLimits);

        return new(
            LibraryApiDiffPresentationAdapter.Create(comparison),
            new InspectionShare.NonProjectable(
                "comparison/endpoints",
                "Workspace Share does not yet represent a selected-Library comparison's ordered endpoints and API scope."));
    }
}
