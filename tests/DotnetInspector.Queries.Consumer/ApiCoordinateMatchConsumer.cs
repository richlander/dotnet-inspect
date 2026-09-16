using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspector.QueriesConsumer;

public static class ApiCoordinateMatchConsumer
{
    public static Task<InspectionEnvelope<ApiCoordinateMatchContent>> MatchAsync(
        ApiCoordinateMatchRequest request,
        IPackageRootPayloadProvider source,
        CancellationToken cancellationToken = default) =>
        ApiCoordinateMatchInspection.ExecuteAsync(request, source, cancellationToken);
}
