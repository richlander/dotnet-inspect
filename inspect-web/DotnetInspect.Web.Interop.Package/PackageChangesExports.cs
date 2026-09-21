using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web;
using DotnetInspector.Ecosystems;
using DotnetInspector.Networking;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using NuGetFetch;

namespace DotnetInspect.Web.Interop.Package;

[SupportedOSPlatform("browser")]
internal static class BrowserPackageChangesOperations
{
    internal const int PackageSetCatalogVersion = 1;

    internal static BrowserPackageChangesPackageSetCatalog PackageSets() =>
        new(
            PackageSetCatalogVersion,
            [
                .. PackageSetCatalog.Discover().Select(packageSet =>
                    new BrowserPackageChangesPackageSetDescriptor(
                        packageSet.Id.Value,
                        packageSet.Title,
                        packageSet.Summary,
                        packageSet.Order)),
            ]);

    internal static EcosystemChangeReportPlan ResolvePlan(
        BrowserPackageChangesRequest request,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!PackageSetId.TryCreate(request.PackageSetId, out PackageSetId? id))
        {
            throw new ArgumentException(
                "A canonical package-set identity is required.",
                nameof(request));
        }

        PackageSetDescriptor descriptor =
            PackageSetCatalog.Lookup(id) switch
            {
                PackageSetLookupResult.Known known => known.Descriptor,
                PackageSetLookupResult.Unknown =>
                    throw new ArgumentException(
                        "The package-set identity is not registered.",
                        nameof(request)),
                _ => throw new InvalidOperationException(
                    "Package-set lookup returned an unknown result."),
            };
        if ((request.FromExclusive is null) !=
            (request.ThroughInclusive is null))
        {
            throw new ArgumentException(
                "A custom interval requires both endpoints.",
                nameof(request));
        }

        NuGetCatalogRequest? interval = request.FromExclusive is null
            ? null
            : new NuGetCatalogRequest(
                ParseTimestamp(request.FromExclusive, nameof(request)),
                ParseTimestamp(request.ThroughInclusive!, nameof(request)));
        var queryRequest = new EcosystemChangeReportRequest(
            new EcosystemChangePackageSelection.PackageSet(
                descriptor.Id.Value,
                descriptor.Members),
            interval,
            request.SecurityOnly
                ? EcosystemChangeSecuritySelection.SecurityRelevant
                : EcosystemChangeSecuritySelection.AllActivity,
            request.MaximumRows);
        return EcosystemChangeReportPlan.Resolve(queryRequest, timeProvider);
    }

    internal static async Task<BrowserPackageChangesInspection> ExecuteAsync(
        BrowserPackageChangesRequest request,
        INuGetCatalogPackageSourceClient source,
        GitHubNuGetAdvisoryService advisoryService,
        TimeProvider timeProvider,
        Action<BrowserPackageChangesEvent> emit,
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext = null,
        CancellationToken advisoryDeadlineCancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(advisoryService);
        ArgumentNullException.ThrowIfNull(timeProvider);
        EcosystemChangeReportPlan plan = ResolvePlan(request, timeProvider);
        IAsyncEnumerable<EcosystemChangeReportEvent> events =
            EcosystemChangeReportQuery.ExecuteAsync(
                source,
                advisoryService,
                plan,
                cancellationToken,
                operationContext,
                advisoryDeadlineCancellation);
        return await MaterializeAsync(
            events,
            emit,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<BrowserPackageChangesInspection> MaterializeAsync(
        IAsyncEnumerable<EcosystemChangeReportEvent> events,
        Action<BrowserPackageChangesEvent> emit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(emit);
        InspectionEnvelope<EcosystemChangeReportDocument> inspection =
            await EcosystemChangeReportInspection.ExecuteAsync(
                events,
                new EventSink(emit),
                cancellationToken).ConfigureAwait(false);
        return BrowserPackageChangesWireProjection.Project(inspection);
    }

    static DateTimeOffset ParseTimestamp(string value, string parameterName)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed))
        {
            throw new ArgumentException(
                "Package Activity interval endpoints must use round-trip timestamps.",
                parameterName);
        }

        return parsed.ToUniversalTime();
    }

    sealed class EventSink(Action<BrowserPackageChangesEvent> emit)
        : IEcosystemChangeReportNonterminalSink
    {
        public ValueTask ReportAsync(
            EcosystemChangeReportNonterminalEvent reportEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            emit(BrowserPackageChangesWireProjection.Project(reportEvent));
            return ValueTask.CompletedTask;
        }
    }
}

internal static class BrowserPackageChangesWireProjection
{
    internal static BrowserPackageChangesEvent Project(
        EcosystemChangeReportNonterminalEvent reportEvent) =>
        reportEvent switch
        {
            EcosystemChangeReportNonterminalEvent.Progress progress =>
                new(
                    BrowserPackageChangesEventKind.Progress,
                    Project(progress.Value),
                    null,
                    null),
            EcosystemChangeReportNonterminalEvent.Row row =>
                new(
                    BrowserPackageChangesEventKind.Row,
                    null,
                    Project(row.Value),
                    null),
            EcosystemChangeReportNonterminalEvent.Failure failure =>
                new(
                    BrowserPackageChangesEventKind.Failure,
                    null,
                    null,
                    Project(failure.Value)),
            _ => throw new InvalidOperationException(
                "Unknown Package Activity nonterminal event."),
        };

    internal static BrowserPackageChangesInspection Project(
        InspectionEnvelope<EcosystemChangeReportDocument> inspection) =>
        new(
            inspection.ResourcePath.Value,
            BrowserInspectionWireProjection.Project(inspection.ContentKind),
            Project(inspection.Content),
            inspection.PortableProjection switch
            {
                InspectionPortableProjection.Available available =>
                    new(
                        BrowserInspectionPortableProjectionKind.Available,
                        available.FullUrl,
                        available.Packet,
                        Location: null,
                        null,
                        null),
                InspectionPortableProjection.NonProjectable nonProjectable =>
                    new(
                        BrowserInspectionPortableProjectionKind.NonProjectable,
                        null,
                        null,
                        nonProjectable.Location,
                        BrowserInspectionWireProjection.Project(
                            nonProjectable.Reason),
                        nonProjectable.Explanation),
                _ => throw new InvalidOperationException(
                    "Unknown inspection portable projection."),
            },
            [
                .. inspection.Diagnostics.Select(diagnostic =>
                    new BrowserInspectionDiagnostic(
                        diagnostic.Code,
                        diagnostic.Severity.ToString(),
                        diagnostic.Summary.ToString(),
                        diagnostic.Correspondence?.ToString())),
            ]);

    static BrowserPackageChangesDocument Project(
        EcosystemChangeReportDocument value) =>
        new(
            value.SchemaVersion,
            new(
                value.Request.ReferenceTime.ToUniversalTime(),
                value.Request.FromExclusive.ToUniversalTime(),
                value.Request.ThroughInclusive.ToUniversalTime(),
                value.Request.UsedDefaultInterval,
                new(
                    value.Request.PackageScope.Kind.ToString(),
                    value.Request.PackageScope.SelectionId,
                    value.Request.PackageScope.Prefix,
                    [.. value.Request.PackageScope.PackageIds]),
                value.Request.SecuritySelection.ToString(),
                value.Request.MaximumRows,
                value.Request.MaximumCandidateEvents,
                value.Request.MaximumReceiptRequests),
            new(
                value.Source.ProducerKey,
                value.Source.Producer,
                value.Source.TransportKind.ToString()),
            [.. value.Progress.Select(Project)],
            [.. value.Rows.Select(Project)],
            [.. value.Failures.Select(Project)],
            Project(value.Summary));

    static BrowserPackageChangesCatalogActivity Project(
        EcosystemChangeCatalogActivityPresentation value) =>
        new(
            value.PackageId,
            value.Version,
            value.NormalizedPackageId,
            value.NormalizedVersion,
            value.LeafUrl,
            value.CommitId,
            value.CommitTimestamp.ToUniversalTime(),
            value.CatalogKind.ToString(),
            value.Activity.ToString());

    static BrowserPackageChangesAdvisoryReference Project(
        EcosystemChangeAdvisoryReferencePresentation value) =>
        new(
            value.GhsaId,
            value.CveId,
            value.Severity.ToString(),
            value.AdvisoryUrl,
            value.PublishedAt.ToUniversalTime(),
            value.UpdatedAt.ToUniversalTime());

    static BrowserPackageChangesAdvisoryEvidence Project(
        EcosystemChangeAdvisoryEvidencePresentation value) =>
        new(
            value.Availability.ToString(),
            [.. value.Advisories.Select(Project)]);

    static BrowserPackageChangesPackageReceipt Project(
        EcosystemChangePackageReceiptPresentation value) =>
        new(value.ReceivedAt.ToUniversalTime(), value.Basis.ToString());

    static BrowserPackageChangesSecurityRelease Project(
        EcosystemChangeSecurityReleasePresentation value) =>
        new(
            Project(value.Receipt),
            [.. value.Advisories.Select(Project)]);

    static BrowserPackageChangesRow Project(
        EcosystemChangeReportRowPresentation value) =>
        new(
            Project(value.CatalogActivity),
            Project(value.CurrentAdvisoryContext),
            Project(value.FixedVersionEvidence),
            value.PackageReceipt is null
                ? null
                : Project(value.PackageReceipt),
            value.SecurityReleaseStatus.ToString(),
            value.SecurityRelease is null
                ? null
                : Project(value.SecurityRelease),
            value.IsSecurityRelevant);

    static BrowserPackageChangesPackageSourceFailure Project(
        EcosystemChangePackageSourceFailurePresentation value) =>
        new(
            (int)value.Capability,
            value.PackageId,
            value.Version,
            value.Kind.ToString(),
            value.Detail);

    static BrowserPackageChangesReceiptFailure Project(
        EcosystemChangeReceiptFailurePresentation value) =>
        new(Project(value.CatalogActivity), Project(value.Failure));

    static BrowserPackageChangesAdvisoryPackage Project(
        EcosystemChangeAdvisoryPackagePresentation value) =>
        new(
            value.PackageId,
            value.Version,
            Project(value.CurrentAdvisoryContext),
            Project(value.FixedVersionEvidence));

    static BrowserPackageChangesAdvisoryAcquisition Project(
        EcosystemChangeAdvisoryAcquisitionPresentation value) =>
        new(
            value.PackageProducerKey,
            value.AdvisoryProducer,
            value.ObservedAt.ToUniversalTime(),
            value.ApiRequests,
            value.ResponseBytes,
            value.Complete,
            [.. value.Failures.Select(static item => item.ToString())],
            [.. value.Packages.Select(Project)]);

    static BrowserPackageChangesProgress Project(
        EcosystemChangeReportProgressPresentation value) =>
        new(
            value.Phase.ToString(),
            value.Completed,
            value.Total,
            value.CapturedHorizon is { } horizon
                ? horizon.ToUniversalTime()
                : null,
            value.CatalogPagesAcquired,
            value.CatalogHttpAttempts,
            value.CatalogDecodedBytes);

    static BrowserPackageChangesFailure Project(
        EcosystemChangeReportFailurePresentation value) =>
        new(
            value.Provider.ToString(),
            value.CatalogFailure is null
                ? null
                : Project(value.CatalogFailure),
            value.AdvisoryFailure?.ToString(),
            value.PackageReceiptFailure is null
                ? null
                : Project(value.PackageReceiptFailure));

    static BrowserPackageChangesSummary Project(
        EcosystemChangeReportSummaryPresentation value) =>
        new(
            value.CapturedHorizon is { } horizon
                ? horizon.ToUniversalTime()
                : null,
            value.CatalogCompletion?.ToString(),
            value.CatalogFailure is null
                ? null
                : Project(value.CatalogFailure),
            value.CatalogPagesAcquired,
            value.CatalogHttpAttempts,
            value.CatalogDecodedBytes,
            value.CatalogInWindowEventCount,
            value.MatchingEventCount,
            value.RetainedEventCount,
            value.CandidateLimitReached,
            Project(value.AdvisoryEvidence),
            value.ReceiptCandidates,
            value.ReceiptRequests,
            value.ReceiptSuccesses,
            [.. value.ReceiptFailures.Select(Project)],
            value.ReceiptLimitReached,
            value.CurrentContextUnevaluableRows,
            value.SecurityReleaseUnevaluableRows,
            value.EligibleRowCount,
            value.ReturnedRowCount,
            value.ResultLimitReached,
            value.Completion.ToString());

}

[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    static readonly BrowserManagedOperationBridge PackageChangesOperations =
        new();

    [JSExport]
    public static string ListPackageActivityPackageSets() =>
        JsonSerializer.Serialize(
            BrowserPackageChangesOperations.PackageSets(),
            BrowserPackageJsonContext.Default
                .BrowserPackageChangesPackageSetCatalog);

    [JSExport]
    public static string CancelPackageActivity(
        string operationId,
        string reason)
    {
        BrowserPackageChangesCancellation result =
            BrowserPackageChangesCancellation.From(
                PackageChangesOperations.RequestCancellation(
                    BrowserManagedOperationId.From(operationId),
                    BrowserManagedOperationCancelReasons.Parse(reason)));
        return JsonSerializer.Serialize(
            result,
            BrowserPackageJsonContext.Default
                .BrowserPackageChangesCancellation);
    }

    [JSExport]
    public static async Task<string> RunPackageActivity(
        string operationId,
        string requestJson,
        JSObject eventSink)
    {
        ArgumentNullException.ThrowIfNull(eventSink);
        BrowserPackageChangesRequest request =
            JsonSerializer.Deserialize(
                requestJson,
                BrowserPackageJsonContext.Default.BrowserPackageChangesRequest)
            ?? throw new ArgumentException(
                    "A Package Activity request is required.",
                nameof(requestJson));

        BrowserManagedOperationResult<
            BrowserPackageChangesInspection,
            string,
            string> result =
            await PackageChangesOperations.RunAsync<
                BrowserPackageChangesInspection,
                string,
                string,
                BrowserPackageChangesEvent>(
                BrowserManagedOperationId.From(operationId),
                operationEvent => eventSink.SetProperty(
                    "event",
                    JsonSerializer.Serialize(
                        operationEvent,
                        BrowserPackageJsonContext.Default
                            .BrowserPackageChangesEvent)),
                async (token, events) =>
                {
                    BrowserPackageChangesInspection inspection =
                        await BrowserPackageWorkspace.RunPackageOperationAsync(
                            async deadline =>
                            {
                                using var operation =
                                    new NuGetOperationContext(
                                        BrowserPackageWorkspace
                                            .PackageChangesRequestTimeout,
                                        BrowserPackageWorkspace
                                            .PackageChangesSourceOperationTimeout,
                                        deadline.Token);
                                using var sourceDeadline =
                                    new CancellationTokenSource(
                                        BrowserPackageWorkspace
                                            .PackageChangesSourceOperationTimeout);
                                var advisoryService =
                                    new GitHubNuGetAdvisoryService(
                                        BrowserPackageWorkspace
                                            .PackageChangesAdvisoryClient,
                                        new GitHubNuGetAdvisoryOptions
                                        {
                                            OperationTimeout =
                                                BrowserPackageWorkspace
                                                    .PackageChangesSourceOperationTimeout,
                                        });
                                using IDisposable advisoryAccess =
                                    NetworkTelemetry.Allow(
                                        NetworkTrafficKind.VulnerabilityData);
                                return await BrowserPackageChangesOperations
                                    .ExecuteAsync(
                                        request,
                                        BrowserPackageWorkspace.Catalog,
                                        advisoryService,
                                        TimeProvider.System,
                                        events.Report,
                                        deadline.Token,
                                        operation,
                                        sourceDeadline.Token)
                                    .ConfigureAwait(false);
                            },
                            BrowserPackageWorkspace
                                .PackageChangesOperationTimeout,
                            token).ConfigureAwait(false);
                    return new BrowserManagedOperationBodyResult<
                        BrowserPackageChangesInspection,
                        string,
                        string>.Succeeded(inspection);
                },
                exception => new(exception.Message, exception.ToString()));
        return JsonSerializer.Serialize(
            BrowserPackageChangesResult.From(result),
            BrowserPackageJsonContext.Default.BrowserPackageChangesResult);
    }
}
