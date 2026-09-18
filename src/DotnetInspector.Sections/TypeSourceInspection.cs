using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>Shared completed type Source operation.</summary>
public static class TypeSourceInspection
{
    public static async Task<InspectionEnvelope<AssemblyTypeSourceEntry>> ExecuteAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyTypeSourceRequest request,
        AssemblyContextSourceQueryContext context,
        CancellationToken cancellationToken = default)
    {
        AssemblyTypeSourceEntry content = await AssemblyContextSourceQuery.ExecuteTypeAsync(
            group, participant, request, context, cancellationToken).ConfigureAwait(false);
        return new(content, new InspectionShare.NonProjectable(
            "type-source/share",
            "Type Source requests do not yet have a canonical Workspace Share projection."));
    }
}
