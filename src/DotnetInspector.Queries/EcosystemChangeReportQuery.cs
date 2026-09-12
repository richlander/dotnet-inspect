using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using DotnetInspector.RowSelection;
using DotnetInspector.Services;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>Composes bounded Catalog and advisory evidence into activity rows.</summary>
public static class EcosystemChangeReportQuery
{
    public static async ValueTask<ImmutableArray<EcosystemChangeReportEvent>>
        ExecuteToArrayAsync(
            INuGetCatalogPackageSourceClient source,
            GitHubNuGetAdvisoryService advisoryService,
            EcosystemChangeReportPlan plan,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        var events = ImmutableArray.CreateBuilder<
            EcosystemChangeReportEvent>();
        await foreach (EcosystemChangeReportEvent queryEvent
            in ExecuteAsync(
                source,
                advisoryService,
                plan,
                cancellationToken,
                operationContext).ConfigureAwait(false))
        {
            events.Add(queryEvent);
        }

        return events.ToImmutable();
    }

    public static async IAsyncEnumerable<EcosystemChangeReportEvent>
        ExecuteAsync(
            INuGetCatalogPackageSourceClient source,
            GitHubNuGetAdvisoryService advisoryService,
            EcosystemChangeReportPlan plan,
            [EnumeratorCancellation] CancellationToken cancellationToken =
                default,
            NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(advisoryService);
        ArgumentNullException.ThrowIfNull(plan);
        if (source.Source.Producer != PackageProducerIdentity.NuGetOrg)
        {
            throw new ArgumentException(
                "The ecosystem security report requires a NuGet.org Catalog source.",
                nameof(source));
        }

        cancellationToken.ThrowIfCancellationRequested();
        yield return new EcosystemChangeReportEvent.Progress(
            new EcosystemChangeReportProgress(
                EcosystemChangeReportProgressPhase.Catalog,
                Completed: 0,
                Total: null));

        EcosystemChangeReportRequest request = plan.Request;
        var candidates = new PriorityQueue<
            NuGetCatalogEvent,
            NuGetCatalogEvent>(
                CatalogEventOldestComparer.Instance);
        long matchingEventCount = 0;
        DateTimeOffset? capturedHorizon = null;
        NuGetCatalogCompletion? catalogCompletion = null;
        PackageSourceFailure? catalogFailure = null;
        int catalogPagesAcquired = 0;
        int catalogHttpAttempts = 0;
        long catalogDecodedBytes = 0;
        long catalogInWindowEventCount = 0;

        await foreach (PackageSourceOperationResult<NuGetCatalogPage> result
            in source.AcquireCatalogAsync(
                plan.Interval,
                cancellationToken,
                operationContext).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Failure is { } failure)
            {
                catalogFailure = failure;
                break;
            }

            NuGetCatalogPage page = result.Value!;
            capturedHorizon = page.CapturedHorizon;
            catalogCompletion = page.Completion;
            catalogPagesAcquired = page.PagesAcquired;
            catalogHttpAttempts = page.HttpAttempts;
            catalogDecodedBytes = page.DecodedBytes;
            catalogInWindowEventCount = page.InWindowEventCount;
            foreach (NuGetCatalogEvent catalogEvent in page.Events)
            {
                if (!Matches(request.PackageSelection, catalogEvent.PackageId))
                    continue;

                matchingEventCount++;
                candidates.Enqueue(catalogEvent, catalogEvent);
                if (candidates.Count > request.MaximumCandidateEvents)
                    candidates.Dequeue();
            }

            yield return new EcosystemChangeReportEvent.Progress(
                new EcosystemChangeReportProgress(
                    EcosystemChangeReportProgressPhase.Catalog,
                    page.InWindowEventCount,
                    Total: null,
                    page.CapturedHorizon,
                    page.PagesAcquired,
                    page.HttpAttempts,
                    page.DecodedBytes));
        }
        if (catalogFailure is null && catalogCompletion is null)
        {
            throw new InvalidOperationException(
                "Catalog acquisition ended without a terminal page or failure.");
        }

        ImmutableArray<NuGetCatalogEvent> orderedEvents =
        [
            .. candidates.UnorderedItems
                .Select(static item => item.Element)
                .Order(CatalogEventNewestComparer.Instance),
        ];
        var coordinates = new List<PackageSourceCoordinate>(
            orderedEvents.Length);
        var coordinateKeys = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (NuGetCatalogEvent catalogEvent in orderedEvents)
        {
            string key = CoordinateKey(catalogEvent.Coordinate);
            if (coordinateKeys.Add(key))
                coordinates.Add(catalogEvent.Coordinate);
        }

        cancellationToken.ThrowIfCancellationRequested();
        yield return new EcosystemChangeReportEvent.Progress(
            new EcosystemChangeReportProgress(
                EcosystemChangeReportProgressPhase.Advisory,
                Completed: 0,
                Total: coordinates.Count));
        GitHubNuGetAdvisoryAcquisition advisoryEvidence =
            await advisoryService.AcquireAsync(
                new GitHubNuGetAdvisoryRequest(
                    source.Source.Producer,
                    coordinates),
                cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        yield return new EcosystemChangeReportEvent.Progress(
            new EcosystemChangeReportProgress(
                EcosystemChangeReportProgressPhase.Advisory,
                coordinates.Count,
                coordinates.Count));

        Dictionary<string, GitHubNuGetAdvisoryPackageEvidence> evidenceByKey =
            advisoryEvidence.Packages.ToDictionary(
                static item => CoordinateKey(item.Coordinate),
                StringComparer.OrdinalIgnoreCase);
        NuGetCatalogEvent[] receiptCandidates =
        [
            .. orderedEvents.Where(catalogEvent =>
                catalogEvent.Kind == NuGetCatalogEventKind.Details
                && evidenceByKey[CoordinateKey(catalogEvent.Coordinate)]
                    .FixedVersionAdvisories.Count > 0),
        ];
        var receipts = new Dictionary<
            NuGetCatalogEvent,
            NuGetCatalogPackageReceipt>();
        var receiptFailures =
            ImmutableArray.CreateBuilder<EcosystemChangeReceiptFailure>();
        var receiptLimited = new HashSet<NuGetCatalogEvent>();
        int receiptRequests = 0;
        int receiptSuccesses = 0;

        if (receiptCandidates.Length > 0)
        {
            yield return new EcosystemChangeReportEvent.Progress(
                new EcosystemChangeReportProgress(
                    EcosystemChangeReportProgressPhase.PackageReceipt,
                    Completed: 0,
                    Total: receiptCandidates.Length));
        }
        for (int index = 0; index < receiptCandidates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NuGetCatalogEvent catalogEvent = receiptCandidates[index];
            if (receiptRequests == request.MaximumReceiptRequests)
            {
                receiptLimited.Add(catalogEvent);
            }
            else
            {
                receiptRequests++;
                PackageSourceOperationResult<NuGetCatalogPackageReceipt>
                    receiptResult = await source.GetPackageReceiptAsync(
                        catalogEvent,
                        cancellationToken,
                        operationContext).ConfigureAwait(false);
                if (receiptResult.Value is { } receipt)
                {
                    receipts.Add(catalogEvent, receipt);
                    receiptSuccesses++;
                }
                else
                {
                    receiptFailures.Add(new(
                        catalogEvent,
                        receiptResult.Failure!));
                }
            }

            yield return new EcosystemChangeReportEvent.Progress(
                new EcosystemChangeReportProgress(
                    EcosystemChangeReportProgressPhase.PackageReceipt,
                    index + 1,
                    receiptCandidates.Length));
        }

        var rows = new List<EcosystemChangeReportRow>(orderedEvents.Length);
        int currentContextUnevaluableRows = 0;
        int securityReleaseUnevaluableRows = 0;
        foreach (NuGetCatalogEvent catalogEvent in orderedEvents)
        {
            GitHubNuGetAdvisoryPackageEvidence advisory =
                evidenceByKey[CoordinateKey(catalogEvent.Coordinate)];
            EcosystemChangeAdvisoryEvidence current =
                new(
                    advisory.CurrentContextAvailability,
                    advisory.CurrentAdvisories);
            EcosystemChangeAdvisoryEvidence fixedVersion =
                new(
                    advisory.FixedVersionAvailability,
                    advisory.FixedVersionAdvisories);
            if (current.Advisories.Count == 0
                && current.Availability
                    != GitHubNuGetAdvisoryAvailability.Complete)
            {
                currentContextUnevaluableRows++;
            }

            NuGetCatalogPackageReceipt? receipt = null;
            EcosystemSecurityReleaseEvidence? securityRelease = null;
            EcosystemSecurityReleaseStatus releaseStatus;
            if (fixedVersion.Advisories.Count == 0)
            {
                releaseStatus = fixedVersion.Availability
                    == GitHubNuGetAdvisoryAvailability.Complete
                    ? EcosystemSecurityReleaseStatus
                        .CheckedNoFixedVersionAssociation
                    : EcosystemSecurityReleaseStatus
                        .FixedVersionEvidenceUnavailable;
                if (releaseStatus == EcosystemSecurityReleaseStatus
                    .FixedVersionEvidenceUnavailable)
                {
                    securityReleaseUnevaluableRows++;
                }
            }
            else if (catalogEvent.Kind == NuGetCatalogEventKind.Delete)
            {
                releaseStatus =
                    EcosystemSecurityReleaseStatus.DeleteActivityUnevaluable;
                securityReleaseUnevaluableRows++;
            }
            else if (receiptLimited.Contains(catalogEvent))
            {
                releaseStatus =
                    EcosystemSecurityReleaseStatus.ReceiptLimitReached;
                securityReleaseUnevaluableRows++;
            }
            else if (!receipts.TryGetValue(catalogEvent, out receipt))
            {
                releaseStatus =
                    EcosystemSecurityReleaseStatus.ReceiptFailure;
                securityReleaseUnevaluableRows++;
            }
            else if (receipt.ReceivedAt <= plan.Interval.FromExclusive
                || receipt.ReceivedAt > plan.Interval.ThroughInclusive)
            {
                releaseStatus =
                    EcosystemSecurityReleaseStatus.ReceiptOutsideInterval;
            }
            else
            {
                releaseStatus =
                    EcosystemSecurityReleaseStatus.EvidencedInInterval;
                securityRelease = new(
                    receipt,
                    fixedVersion.Advisories);
            }

            rows.Add(new EcosystemChangeReportRow(
                source.Source,
                catalogEvent,
                current,
                fixedVersion,
                receipt,
                releaseStatus,
                securityRelease));
        }

        List<EcosystemChangeReportRow> eligibleRows = request.SecuritySelection
            == EcosystemChangeSecuritySelection.SecurityRelevant
            ? [.. rows.Where(static row => row.IsSecurityRelevant)]
            : rows;
        RowSelectionPlan<EcosystemChangeReportOrder> rowPlan =
            RowSelectionPlan<EcosystemChangeReportOrder>.Empty.Append(
                RowSelectionStage<EcosystemChangeReportOrder>.Head(
                    request.MaximumRows));
        RowSelectionResult<EcosystemChangeReportRow> selected =
            RowSelectionExecutor.Apply(eligibleRows, rowPlan);
        IReadOnlyList<EcosystemChangeReportRow> returnedRows =
            selected.Values;
        bool resultLimitReached =
            eligibleRows.Count > returnedRows.Count;

        foreach (EcosystemChangeReportRow row in returnedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new EcosystemChangeReportEvent.Row(row);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (catalogFailure is not null)
        {
            yield return new EcosystemChangeReportEvent.Failure.Catalog(
                catalogFailure);
        }
        foreach (GitHubNuGetAdvisoryFailureKind failure
            in advisoryEvidence.Failures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new EcosystemChangeReportEvent.Failure.Advisory(
                failure);
        }
        foreach (EcosystemChangeReceiptFailure failure in receiptFailures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new EcosystemChangeReportEvent.Failure.PackageReceipt(
                failure);
        }

        bool candidateLimitReached =
            matchingEventCount > orderedEvents.Length;
        bool receiptLimitReached = receiptLimited.Count > 0;
        EcosystemChangeReportCompletionKind completion =
            catalogFailure is not null
                ? EcosystemChangeReportCompletionKind.Failed
                : catalogCompletion != NuGetCatalogCompletion.WindowExhausted
                    || candidateLimitReached
                    || !advisoryEvidence.Complete
                    || receiptFailures.Count > 0
                    || receiptLimitReached
                    ? EcosystemChangeReportCompletionKind.Partial
                    : resultLimitReached
                        ? EcosystemChangeReportCompletionKind.ResultLimitReached
                        : EcosystemChangeReportCompletionKind.Complete;
        cancellationToken.ThrowIfCancellationRequested();
        yield return new EcosystemChangeReportEvent.Completed(
            new EcosystemChangeReportSummary(
                plan,
                source.Source,
                capturedHorizon,
                catalogCompletion,
                catalogFailure,
                catalogPagesAcquired,
                catalogHttpAttempts,
                catalogDecodedBytes,
                catalogInWindowEventCount,
                matchingEventCount,
                orderedEvents.Length,
                advisoryEvidence,
                receiptCandidates.Length,
                receiptRequests,
                receiptSuccesses,
                receiptFailures.ToImmutable(),
                receiptLimitReached,
                currentContextUnevaluableRows,
                securityReleaseUnevaluableRows,
                eligibleRows.Count,
                returnedRows.Count,
                resultLimitReached,
                completion));
    }

    private enum EcosystemChangeReportOrder
    {
        NewestFirst,
    }

    private static bool Matches(
        EcosystemChangePackageSelection selection,
        string packageId) =>
        selection switch
        {
            EcosystemChangePackageSelection.PackageSet packageSet =>
                packageSet.Contains(packageId),
            EcosystemChangePackageSelection.PackagePrefix prefix =>
                packageId.StartsWith(
                    prefix.Prefix.Prefix,
                    StringComparison.OrdinalIgnoreCase),
            _ => throw new InvalidOperationException(
                $"Unsupported package selection {selection.GetType().Name}."),
        };

    private static string CoordinateKey(PackageSourceCoordinate coordinate) =>
        $"{coordinate.PackageId}\0{coordinate.Version}";

    private sealed class CatalogEventOldestComparer :
        IComparer<NuGetCatalogEvent>
    {
        internal static CatalogEventOldestComparer Instance { get; } = new();

        public int Compare(NuGetCatalogEvent? left, NuGetCatalogEvent? right) =>
            CompareEvents(left!, right!, newestFirst: false);
    }

    private sealed class CatalogEventNewestComparer :
        IComparer<NuGetCatalogEvent>
    {
        internal static CatalogEventNewestComparer Instance { get; } = new();

        public int Compare(NuGetCatalogEvent? left, NuGetCatalogEvent? right) =>
            CompareEvents(left!, right!, newestFirst: true);
    }

    private static int CompareEvents(
        NuGetCatalogEvent left,
        NuGetCatalogEvent right,
        bool newestFirst)
    {
        int comparison = left.CommitTimestamp.CompareTo(right.CommitTimestamp);
        if (newestFirst)
            comparison = -comparison;
        if (comparison != 0)
            return comparison;

        comparison = StringComparer.Ordinal.Compare(
            left.CommitId,
            right.CommitId);
        if (!newestFirst)
            comparison = -comparison;
        if (comparison != 0)
            return comparison;
        comparison = StringComparer.Ordinal.Compare(
            left.LeafUrl,
            right.LeafUrl);
        if (!newestFirst)
            comparison = -comparison;
        if (comparison != 0)
            return comparison;
        comparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.Coordinate.PackageId,
            right.Coordinate.PackageId);
        if (!newestFirst)
            comparison = -comparison;
        if (comparison != 0)
            return comparison;
        comparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.Coordinate.Version,
            right.Coordinate.Version);
        if (!newestFirst)
            comparison = -comparison;
        if (comparison != 0)
            return comparison;
        comparison = left.Kind.CompareTo(right.Kind);
        return newestFirst ? comparison : -comparison;
    }
}
