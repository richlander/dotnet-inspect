using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using InspectWeb.Engine;

namespace InspectWeb.Engine
{
    [SupportedOSPlatform("browser")]
    internal static class BrowserPackageQueryOperations
    {
        internal static BrowserPackageQueryFacetCatalog Facets() =>
            new(
                [
                    .. PackageQuery.Facets.Select(facet =>
                        new BrowserPackageQueryFacetDescriptor(
                            facet.Id,
                            facet.Label,
                            facet.Summary,
                            facet.Weight,
                            facet.Tier switch
                            {
                                PackageQueryFacetTier.Nuspec =>
                                    BrowserPackageQueryFacetTier.Nuspec,
                                _ => throw new InvalidOperationException(
                                    "Unknown package-query facet tier."),
                            },
                            facet.SelectionGroupId)),
                ]);

        internal static async Task<BrowserPackageQueryEvent> ExecuteAsync(
            string prefix,
            string[] facetIds,
            int maximumCandidates,
            int maximumMatches,
            bool includePrerelease,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(facetIds);
            ArgumentNullException.ThrowIfNull(emit);

            PackageQueryPlanResult planResult = PackageQuery.Plan(
                new PackageQueryRequest(
                    prefix,
                    facetIds,
                    maximumCandidates,
                    maximumMatches,
                    includePrerelease));
            if (planResult is PackageQueryPlanResult.Rejected rejected)
                throw new InvalidOperationException(rejected.Failure.Message);

            PackageQueryPlan plan =
                ((PackageQueryPlanResult.Accepted)planResult).Plan;
            BrowserPackageQueryEvent? completedEvent = null;
            await foreach (PackageQueryEvent queryEvent in PackageQuery.ExecuteAsync(
                BrowserPackageWorkspace.Gallery,
                plan,
                cancellationToken).ConfigureAwait(false))
            {
                BrowserPackageQueryEvent projected = Project(queryEvent);
                emit(projected);
                if (projected.Kind == BrowserPackageQueryEventKind.Completed)
                    completedEvent = projected;
            }

            return completedEvent
                ?? throw new InvalidOperationException(
                    "The package-query stream ended without a completion event.");
        }

        internal static BrowserPackageQueryEvent Project(
            PackageQueryEvent queryEvent) =>
            queryEvent switch
            {
            PackageQueryEvent.Match match =>
                new BrowserPackageQueryEvent(
                    BrowserPackageQueryEventKind.Match,
                    Row: new BrowserPackageQueryRow(
                        match.Value.Package.PackageId,
                        match.Value.Package.Version,
                        match.Value.Tier switch
                        {
                            PackageQueryFacetTier.Nuspec =>
                                BrowserPackageQueryFacetTier.Nuspec,
                            _ => throw new InvalidOperationException(
                                "Unknown package-query match tier."),
                        },
                        [
                            .. match.Value.Evidence.Select(evidence =>
                                new BrowserPackageQueryEvidence(
                                    evidence.Id,
                                    evidence.Value)),
                        ],
                        match.Value.Package.TotalDownloads,
                        match.Value.Package.Verified,
                        match.Value.Package.Source.Producer.Display.ToString()),
                    Failure: null,
                    Completion: null),
            PackageQueryEvent.Failure failure =>
                new BrowserPackageQueryEvent(
                    BrowserPackageQueryEventKind.Failure,
                    Row: null,
                    Failure: new BrowserPackageQueryFailure(
                        failure.Value.PackageId,
                        failure.Value.Version,
                        failure.Value.Source.Producer.Display.ToString(),
                        failure.Value.Kind switch
                        {
                            PackageProfileFailureKind.Search =>
                                BrowserPackageQueryFailureKind.Search,
                            PackageProfileFailureKind.SearchContract =>
                                BrowserPackageQueryFailureKind.SearchContract,
                            PackageProfileFailureKind.ManifestAcquisition =>
                                BrowserPackageQueryFailureKind.ManifestAcquisition,
                            PackageProfileFailureKind.ManifestContract =>
                                BrowserPackageQueryFailureKind.ManifestContract,
                            PackageProfileFailureKind.InvalidManifest =>
                                BrowserPackageQueryFailureKind.InvalidManifest,
                            _ => throw new InvalidOperationException(
                                "Unknown package-query failure kind."),
                        },
                        failure.Value.Message),
                    Completion: null),
            PackageQueryEvent.Completed completed =>
                new BrowserPackageQueryEvent(
                    BrowserPackageQueryEventKind.Completed,
                    Row: null,
                    Failure: null,
                    Completion: new BrowserPackageQueryCompletion(
                        completed.Value.Prefix.ToString(),
                        completed.Value.Source.Producer.Display.ToString(),
                        completed.Value.CandidateLimit,
                        completed.Value.MatchLimit,
                        completed.Value.Candidates,
                        completed.Value.Matches,
                        completed.Value.Failures,
                        completed.Value.Completion switch
                        {
                            PackageQueryCompletionKind.Exhausted =>
                                BrowserPackageQueryCompletionKind.Exhausted,
                            PackageQueryCompletionKind.MatchLimitReached =>
                                BrowserPackageQueryCompletionKind.MatchLimitReached,
                            PackageQueryCompletionKind.CandidateLimitReached =>
                                BrowserPackageQueryCompletionKind.CandidateLimitReached,
                            PackageQueryCompletionKind.SourcePageLimitReached =>
                                BrowserPackageQueryCompletionKind.SourcePageLimitReached,
                            PackageQueryCompletionKind.ClientPageLimitReached =>
                                BrowserPackageQueryCompletionKind.ClientPageLimitReached,
                            PackageQueryCompletionKind.Failed =>
                                BrowserPackageQueryCompletionKind.Failed,
                            _ => throw new InvalidOperationException(
                                "Unknown package-query completion kind."),
                        })),
                _ => throw new InvalidOperationException(
                    "Unknown package-query event."),
            };

        internal static string Serialize(BrowserPackageQueryEvent queryEvent) =>
            JsonSerializer.Serialize(
                queryEvent,
                BrowserJsonContext.Default.BrowserPackageQueryEvent);
    }
}

[SupportedOSPlatform("browser")]
public static partial class InspectionEngine
{
    [JSExport]
    public static string ListPackageQueryFacets() =>
        JsonSerializer.Serialize(
            BrowserPackageQueryOperations.Facets(),
            BrowserJsonContext.Default.BrowserPackageQueryFacetCatalog);

    [JSExport]
    public static void CancelPackageQuery() =>
        BrowserPackageQueryOperationCoordinator.CancelCurrent();

    [JSExport]
    public static async Task<string> RunPackageQuery(
        string prefix,
        string facetIdsJson,
        int maximumCandidates,
        int maximumMatches,
        bool includePrerelease,
        JSObject eventSink)
    {
        ArgumentNullException.ThrowIfNull(eventSink);
        string[] facetIds = JsonSerializer.Deserialize(
            facetIdsJson,
            BrowserJsonContext.Default.StringArray) ?? [];

        using BrowserPackageQueryOperationLease operation =
            await BrowserPackageQueryOperationCoordinator.BeginAsync();
        BrowserPackageQueryEvent completed =
            await BrowserPackageWorkspace.RunPackageOperationAsync(
            async deadline =>
            {
                return await BrowserPackageQueryOperations.ExecuteAsync(
                    prefix,
                    facetIds,
                    maximumCandidates,
                    maximumMatches,
                    includePrerelease,
                    queryEvent => eventSink.SetProperty(
                        "event",
                        BrowserPackageQueryOperations.Serialize(queryEvent)),
                    deadline.Token);
            },
            BrowserPackageWorkspace.PackageOperationTimeout,
            operation.CancellationToken);
        return JsonSerializer.Serialize(
            completed,
            BrowserJsonContext.Default.BrowserPackageQueryEvent);
    }
}
