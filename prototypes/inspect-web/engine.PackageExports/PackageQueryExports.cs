using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using InspectWeb.Engine;
using InspectWeb.Engine.PackageFacade;
using NuGetFetch;

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

        internal static BrowserGalleryDiscoveryCatalog GalleryCatalog() =>
            new(
                new BrowserGalleryPackageTypeFacet(
                    NuGetGalleryDiscoveryCatalog.PackageType.Id,
                    NuGetGalleryDiscoveryCatalog.PackageType.Label,
                    NuGetGalleryDiscoveryCatalog.PackageType.Summary,
                    [
                        .. NuGetGalleryDiscoveryCatalog.PackageType.Suggestions
                            .Select(suggestion => new BrowserGalleryPackageTypeSuggestion(
                                suggestion.Value.Name,
                                suggestion.Label)),
                    ]),
                [
                    .. NuGetGalleryDiscoveryCatalog.Orders.Select(order =>
                        new BrowserGalleryDiscoveryOrder(
                            order.Id,
                            order.Label,
                            order.Summary)),
                ]);

        internal static PackageQueryPlanResult Plan(
            string text,
            string[] facetIds,
            int maximumCandidates,
            int maximumMatches,
            bool includePrerelease,
            string? packageType = null,
            string? sourceOrderId = null) =>
            PackageQuery.PlanGallery(
                new NuGetGalleryDiscoveryRequest(
                    PackageSourceDescriptor.NuGetGallery,
                    maximumCandidates,
                    text,
                    packageType is null
                        ? null
                        : NuGetGalleryDiscoveryCatalog.PackageType.Select(packageType),
                    sourceOrderId is null
                        ? null
                        : NuGetGalleryDiscoveryCatalog.GetOrder(sourceOrderId).Order,
                    includePrerelease),
                facetIds,
                maximumMatches);

        internal static async Task<BrowserPackageQueryEvent> ExecuteAsync(
            string prefix,
            string[] facetIds,
            int maximumCandidates,
            int maximumMatches,
            bool includePrerelease,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline = null,
            string? packageType = null,
            string? sourceOrderId = null)
            => await ExecuteAsync(
                prefix,
                facetIds,
                maximumCandidates,
                maximumMatches,
                includePrerelease,
                contentProvider: null,
                matchCredit,
                emit,
                cancellationToken,
                deadline,
                packageType,
                sourceOrderId).ConfigureAwait(false);

        internal static async Task<BrowserPackageQueryEvent> ExecuteAsync(
            string prefix,
            string[] facetIds,
            int maximumCandidates,
            int maximumMatches,
            bool includePrerelease,
            IPackageQueryContentProvider? contentProvider,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline = null,
            string? packageType = null,
            string? sourceOrderId = null)
        {
            ArgumentNullException.ThrowIfNull(facetIds);
            ArgumentNullException.ThrowIfNull(emit);

            PackageQueryPlanResult planResult = Plan(
                prefix,
                facetIds,
                maximumCandidates,
                maximumMatches,
                includePrerelease,
                packageType,
                sourceOrderId);
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
                cancellationToken,
                deadline).ConfigureAwait(false);
        }

        internal static async Task<BrowserPackageQueryEvent> PumpAsync(
            IAsyncEnumerable<PackageQueryEvent> events,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline = null)
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
                        if (deadline is null)
                        {
                            await matchCredit.WaitAsync(cancellationToken)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            await deadline.WaitForConsumerAsync(matchCredit.WaitAsync)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested
                            && (deadline is null
                                || deadline.CallerCancellation.IsCancellationRequested))
                    {
                        // Only caller cancellation revokes Browser publication;
                        // a timeout must not hand off this uncredited match.
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
                            PackageQueryFacetTier.SearchMetadata =>
                                BrowserPackageQueryFacetTier.SearchMetadata,
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
                        match.Value.Package.Source.Producer.Display.ToString(),
                        match.Value.Package.Description),
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
                            PackageQueryCompletionKind.GalleryResponseComplete =>
                                BrowserPackageQueryCompletionKind.GalleryResponseComplete,
                            _ => throw new InvalidOperationException(
                                "Unknown package-query completion kind."),
                        },
                        completed.Value.SourceCandidates,
                        completed.Value.EstimatedTotalHits)),
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
    public static string ListGalleryDiscoveryCatalog() =>
        JsonSerializer.Serialize(
            BrowserPackageQueryOperations.GalleryCatalog(),
            BrowserPackageJsonContext.Default.BrowserGalleryDiscoveryCatalog);

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
        JSObject eventSink,
        string? packageType,
        string? sourceOrderId)
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
                    deadline.Token,
                    deadline,
                    packageType,
                    sourceOrderId);
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
            PackageQueryPackage package,
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
