using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Runs one bounded Type Member population inspection over an assembly-context
/// participant.
/// </summary>
public static class AssemblyContextTypeMemberPopulationQuery
{
    public static AssemblyContextEntry<
        MetadataTypeMemberPopulationOutcome> ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            MetadataTypeMemberPopulationRequest request,
            ApiSurfaceExtractionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bounds);
        return AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            session => MetadataTypeMemberPopulationInspection.Inspect(
                session,
                request,
                bounds));
    }
}
