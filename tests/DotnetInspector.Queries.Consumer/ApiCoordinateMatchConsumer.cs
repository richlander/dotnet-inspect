using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspector.QueriesConsumer;

public static class ApiCoordinateMatchConsumer
{
    public static Task<InspectionEnvelope<ApiCoordinateMatchContent>> MatchAsync(
        ApiCoordinateMatchRequest request,
        ApiCoordinateMatchHouseExecution execution,
        CancellationToken cancellationToken = default) =>
        ApiCoordinateMatchInspection.ExecuteAsync(
            request,
            execution,
            cancellationToken);
}
