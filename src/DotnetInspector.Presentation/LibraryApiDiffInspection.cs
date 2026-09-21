using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Presentation;

/// <summary>Completes one selected-Library API comparison for either host.</summary>
public static class LibraryApiDiffInspection
{
    public static InspectionEnvelope<LibraryApiDiffOutcome> Execute(
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
            InspectionContentKind.Outcome,
            LibraryApiDiffPresentationAdapter.Create(comparison),
            new InspectionPortableProjection.NonProjectable(
                "comparison/endpoints",
                InspectionPortableProjectionFailureReason.NotSupported,
                "Workspace sharing does not yet represent a selected-Library "
                    + "comparison's ordered endpoints and API scope."));
    }
}
