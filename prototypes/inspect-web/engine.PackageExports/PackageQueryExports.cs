using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using InspectWeb.Engine;
using InspectWeb.Engine.PackageFacade;

namespace InspectWeb.Engine.PackageFacade
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
                                PackageQueryFacetTier.PackageContent =>
                                    BrowserPackageQueryFacetTier.PackageContent,
                                _ => throw new InvalidOperationException(
                                    "Unknown package-query facet tier."),
                            },
                            facet.SelectionGroupId,
                            facet.CombinesWithinSelectionGroup,
                            facet.DisplayGroupId,
                            facet.DisplayGroupLabel)),
                ]);

        internal static async Task<BrowserPackageQueryEvent> ExecuteAsync(
            string prefix,
            string[] facetIds,
            int maximumCandidates,
            int maximumMatches,
            bool includePrerelease,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken)
            => await ExecuteAsync(
                prefix,
                facetIds,
                maximumCandidates,
                maximumMatches,
                includePrerelease,
                contentProvider: null,
                matchCredit,
                emit,
                cancellationToken).ConfigureAwait(false);

        internal static async Task<BrowserPackageQueryEvent> ExecuteAsync(
            string prefix,
            string[] facetIds,
            int maximumCandidates,
            int maximumMatches,
            bool includePrerelease,
            IPackageQueryContentProvider? contentProvider,
            BrowserPackageQueryMatchCredit? matchCredit,
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
            return await PumpAsync(
                PackageQuery.ExecuteAsync(
                    BrowserPackageWorkspace.Gallery,
                    plan,
                    contentProvider,
                    cancellationToken),
                matchCredit,
                emit,
                cancellationToken).ConfigureAwait(false);
        }

        internal static async Task<BrowserPackageQueryEvent> PumpAsync(
            IAsyncEnumerable<PackageQueryEvent> events,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(emit);
            BrowserPackageQueryEvent? completedEvent = null;
            await foreach (PackageQueryEvent queryEvent in events
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                BrowserPackageQueryEvent projected = Project(queryEvent);
                if (completedEvent is not null)
                {
                    throw new InvalidOperationException(
                        "The package-query stream produced an event after completion.");
                }

                if (projected.Kind == BrowserPackageQueryEventKind.Completed)
                {
                    completedEvent = projected;
                    continue;
                }

                if (projected.Kind == BrowserPackageQueryEventKind.Match
                    && matchCredit is not null)
                {
                    try
                    {
                        await matchCredit.WaitAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        // The match was already established by MoveNextAsync.
                        // Hand it to the generation guard before physical
                        // cancellation settles, even though logical
                        // cancellation has revoked view publication.
                        emit(projected);
                        throw;
                    }
                }

                emit(projected);
            }

            return completedEvent
                ?? throw new InvalidOperationException(
                    "The package-query stream ended without a completion event.");
        }

        internal static BrowserPackageQueryEvent Project(
            PackageQueryEvent queryEvent) =>
            queryEvent switch
            {
            PackageQueryEvent.Progress progress =>
                new BrowserPackageQueryEvent(
                    BrowserPackageQueryEventKind.Progress,
                    Row: null,
                    Failure: null,
                    Completion: null,
                    Progress: new BrowserPackageQueryProgress(
                        progress.Value.Phase switch
                        {
                            PackageQueryProgressPhase.Search =>
                                BrowserPackageQueryProgressPhase.Search,
                            PackageQueryProgressPhase.Manifest =>
                                BrowserPackageQueryProgressPhase.Manifest,
                            PackageQueryProgressPhase.PackageContent =>
                                BrowserPackageQueryProgressPhase.PackageContent,
                            _ => throw new InvalidOperationException(
                                "Unknown package-query progress phase."),
                        },
                        progress.Value.Completed,
                        progress.Value.Limit)),
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
                            PackageQueryFacetTier.PackageContent =>
                                BrowserPackageQueryFacetTier.PackageContent,
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
                            PackageQueryFailureKind.Search =>
                                BrowserPackageQueryFailureKind.Search,
                            PackageQueryFailureKind.SearchContract =>
                                BrowserPackageQueryFailureKind.SearchContract,
                            PackageQueryFailureKind.ManifestAcquisition =>
                                BrowserPackageQueryFailureKind.ManifestAcquisition,
                            PackageQueryFailureKind.ManifestContract =>
                                BrowserPackageQueryFailureKind.ManifestContract,
                            PackageQueryFailureKind.InvalidManifest =>
                                BrowserPackageQueryFailureKind.InvalidManifest,
                            PackageQueryFailureKind.PackageContentAcquisition =>
                                BrowserPackageQueryFailureKind.PackageContentAcquisition,
                            PackageQueryFailureKind.PackageContentEvaluation =>
                                BrowserPackageQueryFailureKind.PackageContentEvaluation,
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
                BrowserPackageJsonContext.Default.BrowserPackageQueryEvent);
    }
}

[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    [JSExport]
    public static string ListPackageQueryFacets() =>
        JsonSerializer.Serialize(
            BrowserPackageQueryOperations.Facets(),
            BrowserPackageJsonContext.Default.BrowserPackageQueryFacetCatalog);

    [JSExport]
    public static void CancelPackageQuery() =>
        BrowserPackageQueryOperationCoordinator.CancelCurrent();

    [JSExport]
    public static bool RequestPackageQueryMatches(int additionalMatchCredit) =>
        BrowserPackageQueryOperationCoordinator.RequestCurrentMatches(
            additionalMatchCredit);

    [JSExport]
    public static async Task<string> RunPackageQuery(
        string prefix,
        string facetIdsJson,
        int maximumCandidates,
        int maximumMatches,
        bool includePrerelease,
        int initialMatchCredit,
        JSObject eventSink)
    {
        ArgumentNullException.ThrowIfNull(eventSink);
        string[] facetIds = JsonSerializer.Deserialize(
            facetIdsJson,
            BrowserPackageJsonContext.Default.StringArray) ?? [];

        using BrowserPackageQueryOperationLease operation =
            await BrowserPackageQueryOperationCoordinator.BeginAsync(
                initialMatchCredit);
        BrowserPackageQueryEvent completed =
            await BrowserPackageWorkspace.RunPackageOperationAsync(
            async deadline =>
            {
                var contentProvider =
                    new BrowserPackageQueryContentProvider(deadline);
                return await BrowserPackageQueryOperations.ExecuteAsync(
                    prefix,
                    facetIds,
                    maximumCandidates,
                    maximumMatches,
                    includePrerelease,
                    contentProvider,
                    operation.MatchCredit,
                    queryEvent => eventSink.SetProperty(
                        "event",
                        BrowserPackageQueryOperations.Serialize(queryEvent)),
                    deadline.Token);
            },
            BrowserPackageWorkspace.PackageOperationTimeout,
            operation.CancellationToken);
        return JsonSerializer.Serialize(
            completed,
            BrowserPackageJsonContext.Default.BrowserPackageQueryEvent);
    }
}

namespace InspectWeb.Engine.PackageFacade
{
    [SupportedOSPlatform("browser")]
    internal sealed class BrowserPackageQueryContentProvider(
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
        : IPackageQueryContentProvider
    {
        public ValueTask<PackageQueryContentResult> GetContentAsync(
            PackageProfileMatch package,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BrowserPackageWorkspace.AcquirePackageQueryContentAsync(
                package,
                BrowserPackageWorkspace.Gallery,
                deadline);
        }
    }
}
