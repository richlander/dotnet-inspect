using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.PackageQueries;
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

        internal static Task<BrowserPackageQueryEvent> PumpAsync(
            IAsyncEnumerable<PackageQueryEvent> events,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline = null) =>
            PumpAsync(events, Project, matchCredit, emit, cancellationToken, deadline);

        internal static Task<BrowserPackageQueryEvent> ExecuteAssemblyAsync(
            PackageAssemblyQueryPlan plan,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline = null) =>
            PumpAsync(
                PackageAssemblyQuery.ExecuteAsync(
                    BrowserPackageWorkspace.Gallery,
                    BrowserPackageWorkspace.GallerySourceIdentity,
                    plan,
                    cancellationToken),
                queryEvent => ProjectAssembly(plan, queryEvent),
                matchCredit,
                emit,
                cancellationToken,
                deadline);

        internal static BrowserPackageQueryEvent ProjectAssembly(
            PackageAssemblyQueryPlan plan,
            PackageAssemblyQueryEvent queryEvent) =>
            queryEvent switch
            {
                PackageAssemblyQueryEvent.Progress progress =>
                    new(BrowserPackageQueryEventKind.Progress, null, null, null,
                        new(BrowserPackageQueryProgressPhase.Assembly,
                            progress.CompletedCandidates, progress.Limit)),
                PackageAssemblyQueryEvent.AcquisitionFailed failed =>
                    new(BrowserPackageQueryEventKind.Failure, null,
                        new(failed.Value.Coordinate.PackageId, failed.Value.Coordinate.Version,
                            failed.Value.Producer.ToString(),
                            BrowserPackageQueryFailureKind.AssemblyAcquisition,
                            failed.Value.Message.ToString()), null, null),
                PackageAssemblyQueryEvent.Evaluated evaluated =>
                    ProjectAssemblyOutcome(evaluated.Value),
                PackageAssemblyQueryEvent.Completed completed =>
                    new(BrowserPackageQueryEventKind.Completed, null, null,
                        new(
                            plan.Pattern.Operand.DisplayText.ToString(),
                            BrowserPackageWorkspace.Gallery.Source.Producer.Display.ToString(),
                            plan.Coordinates.Length, plan.Coordinates.Length,
                            completed.Value.Candidates, completed.Value.Matches,
                            completed.Value.Failures,
                            BrowserPackageQueryCompletionKind.ExplicitCandidatesComplete,
                            SemanticMisses: completed.Value.SemanticMisses,
                            NotApplicable: completed.Value.NotApplicable,
                            Scope: "Selected primary implementation assemblies only; not all package assemblies."),
                        null),
                _ => throw new InvalidOperationException("Unknown assembly-query event."),
            };

        static BrowserPackageQueryEvent ProjectAssemblyOutcome(PackageAssemblyEvaluationOutcome outcome)
        {
            PackageAssemblyEvaluationSubject subject = outcome.Subject;
            string id = subject.Coordinate.PackageId;
            string version = subject.Coordinate.Version;
            string rootRequest = subject.RootRequest.Encode();
            return outcome switch
            {
                PackageAssemblyEvaluationOutcome.Matched { SelectedAsset: { } selected } matched =>
                    new(BrowserPackageQueryEventKind.Match,
                        new(id, version, BrowserPackageQueryFacetTier.Assembly,
                            [
                                new("selected-assembly",
                                    $"{selected.Asset.Path}: {matched.Evidence.Occurrences.Length} literal uses; "
                                    + $"{selected.UnevaluatedSiblings} sibling assemblies not evaluated."),
                                .. matched.Evidence.Occurrences.Take(3).Select(occurrence =>
                                    new BrowserPackageQueryEvidence("literal-use",
                                        $"Method 0x{occurrence.Address.MethodDefinitionToken:X8}, "
                                        + $"IL_{occurrence.Address.ILOffset:X4}: "
                                        + Excerpt(occurrence.LiteralText.ToString()))),
                            ],
                            null, null, subject.Coordinate.Producer,
                            RootRequest: rootRequest),
                        null, null, null),
                PackageAssemblyEvaluationOutcome.NoMatch { SelectedAsset: { } selected } =>
                    new(BrowserPackageQueryEventKind.Assessment, null, null, null, null,
                        new(id, version, BrowserPackageAssemblyAssessmentKind.NoMatch,
                            "The selected implementation assembly has no matching decoded ldstr use.",
                            selected.Asset.Path.ToString(), rootRequest)),
                PackageAssemblyEvaluationOutcome.NotApplicable notApplicable =>
                    new(BrowserPackageQueryEventKind.Assessment, null, null, null, null,
                        new(id, version, BrowserPackageAssemblyAssessmentKind.NotApplicable,
                            notApplicable.Reason switch
                            {
                                PackageAssemblyNotApplicableReason.NoCompileAssets => "No compile assembly is available.",
                                PackageAssemblyNotApplicableReason.NoMatchingTargetFramework => "No compile group matches the requested framework.",
                                PackageAssemblyNotApplicableReason.EmptyCompileGroup => "The selected compile group is explicitly empty.",
                                PackageAssemblyNotApplicableReason.NoImplementationCounterpart => "The primary compile assembly has no implementation counterpart.",
                                _ => throw new InvalidOperationException("Unknown assembly-query applicability outcome."),
                            },
                            notApplicable.SelectedAsset?.Asset.Path.ToString(), rootRequest)),
                PackageAssemblyEvaluationOutcome.Failure failure =>
                    new(BrowserPackageQueryEventKind.Failure, null,
                        new(id, version, subject.Coordinate.Producer,
                            BrowserPackageQueryFailureKind.AssemblyEvaluation,
                            $"Assembly evaluation failed: {failure.Reason.Stage}."
                            + (failure.Cleanup is null ? "" : " Candidate cleanup was incomplete.")),
                        null, null),
                _ => throw new InvalidOperationException("Unknown assembly-query evaluation outcome."),
            };
        }

        static string Excerpt(string value) =>
            value.Length <= 160 ? value : value[..160] + "...";

        internal static async Task<BrowserPackageQueryEvent> PumpAsync<TEvent>(
            IAsyncEnumerable<TEvent> events,
            Func<TEvent, BrowserPackageQueryEvent> project,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline = null)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(emit);
            BrowserPackageQueryEvent? completedEvent = null;
            await foreach (TEvent queryEvent in events
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                BrowserPackageQueryEvent projected = project(queryEvent);
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
    public static string ListPackageAssemblyQueryPatterns() =>
        JsonSerializer.Serialize(
            PackageAssemblyPatterns.Descriptors.Select(pattern =>
                new BrowserPackageAssemblyQueryPattern(
                    pattern.Id,
                    pattern.Label,
                    pattern.Summary,
                    pattern.MaximumOperandLength,
                    PackageAssemblyQuery.MaximumPackages)).ToArray(),
            BrowserPackageJsonContext.Default.BrowserPackageAssemblyQueryPatternArray);

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
    public static async Task<string> RunPackageAssemblyQuery(
        string patternId,
        string operand,
        string packageCoordinatesJson,
        string targetFramework,
        int initialMatchCredit,
        JSObject eventSink)
    {
        ArgumentNullException.ThrowIfNull(eventSink);
        string[] coordinates = JsonSerializer.Deserialize(
            packageCoordinatesJson, BrowserPackageJsonContext.Default.StringArray)
            ?? throw new ArgumentException("Exact package coordinates are required.", nameof(packageCoordinatesJson));
        PackageAssemblyQueryPlan plan = PackageAssemblyQuery.Plan(
            patternId, operand, coordinates, targetFramework);
        using BrowserPackageQueryOperationLease operation =
            await BrowserPackageQueryOperationCoordinator.BeginAsync(initialMatchCredit);
        BrowserPackageQueryEvent completed = await BrowserPackageWorkspace.RunPackageOperationAsync(
            deadline => BrowserPackageQueryOperations.ExecuteAssemblyAsync(
                plan, operation.MatchCredit,
                queryEvent => eventSink.SetProperty(
                    "event", BrowserPackageQueryOperations.Serialize(queryEvent)),
                deadline.Token, deadline),
            BrowserPackageWorkspace.PackageOperationTimeout,
            operation.CancellationToken);
        return JsonSerializer.Serialize(
            completed, BrowserPackageJsonContext.Default.BrowserPackageQueryEvent);
    }

    [JSExport]
    public static async Task<string> OpenPackageAssemblyQueryResult(string rootRequest)
    {
        if (!PackageRootReacquisitionRequest.TryDecode(rootRequest, out var request))
            throw new ArgumentException("Invalid package Root reopening request.", nameof(rootRequest));

        BrowserPackageSurface surface = await BrowserPackageWorkspace.RunPackageOperationAsync(
            async deadline =>
            {
                BrowserPackageCoordinate coordinate =
                    await BrowserPackageWorkspace.ReacquireAsync(request, deadline.Token);
                await using BrowserScopeLease<BrowserInspectionScope> lease =
                    await BrowserPackageWorkspace.OpenScopeAsync([coordinate], deadline.Token);
                return BrowserPackageWireProjection.Project(
                    BrowserPackageSurfaceProjection.ProjectSurface(lease.Scope, coordinate));
            },
            BrowserPackageWorkspace.PackageOperationTimeout);
        return JsonSerializer.Serialize(surface, BrowserPackageJsonContext.Default.BrowserPackageSurface);
    }

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
