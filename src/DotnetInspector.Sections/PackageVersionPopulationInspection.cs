using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.RowSelection;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>Settles a package version range through the shared House and envelope boundary.</summary>
public static class PackageVersionPopulationInspection
{
    /// <summary>
    /// Consumes the source operation and returns only detached population content.
    /// </summary>
    public static async Task<InspectionEnvelope<PackageVersionPopulationOutcome>> ExecuteAsync(
        PackageVersionRange range,
        PackageHouse house,
        PackageSourceOperationLease sourceOperation,
        bool includePrerelease = false,
        bool includeUnlisted = false,
        PackageVersionPopulationCountRequest? countRequest = null)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(sourceOperation);
        using (sourceOperation)
        {
            var request = new PackageVersionPopulationRequest(
                range.PackageId.ToLowerInvariant(),
                range.StartVersion,
                range.EndVersion,
                includePrerelease,
                includeUnlisted);
            var operation = PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle,
                sourceOperation.RequestTimeout,
                sourceOperation.OperationTimeout);
            var populationRequest = new PackageHouseVersionPopulationRequest(
                range,
                operation,
                includePrerelease,
                includeUnlisted: includeUnlisted);
            PackageHouseVersionPopulationResult population =
                await house.SettleVersionPopulationAsync(
                    populationRequest,
                    sourceOperation).ConfigureAwait(false);
            return Envelope(
                Project(request, population, countRequest),
                population is PackageHouseVersionPopulationResult.Available
                    ? population.Evidence.Failures
                        .OfType<PackageHouseFailure.Authority>()
                        .Select(value => new InspectionDiagnostic(
                            "package-version-population.source-failure",
                            InspectionDiagnosticSeverity.Warning,
                            value.Failure.Message))
                    : []);
        }
    }

    private static PackageVersionPopulationOutcome Project(
        PackageVersionPopulationRequest request,
        PackageHouseVersionPopulationResult population,
        PackageVersionPopulationCountRequest? countRequest)
    {
        if (population is PackageHouseVersionPopulationResult.Available available)
        {
            PackageVersionDiscoveryResult discovery =
                available.Evidence.Discovery
                ?? throw new InvalidOperationException(
                    "Available population settlement did not retain discovery.");
            var listingByVersion = discovery.Listings.ToDictionary(
                row => row.Version,
                row => row.Listed,
                StringComparer.OrdinalIgnoreCase);
            ImmutableArray<PackageVersionPopulationVersion> versions =
            [
                .. available.Vector.Addresses.Select(address =>
                {
                    string version = address.NormalizedVersion;
                    return new PackageVersionPopulationVersion(
                        address.Ordinal,
                        address.Selector,
                        version,
                        listingByVersion.TryGetValue(version, out bool listed)
                            ? listed
                            : true);
                }),
            ];
            var sourceRowsByVersion = discovery.SourceListings.ToLookup(
                row => row.Version,
                StringComparer.OrdinalIgnoreCase);
            ImmutableArray<PackageVersionSourceInfo> sourceListings =
            [
                .. versions.SelectMany(version =>
                    sourceRowsByVersion[version.Version]),
            ];
            var document = new PackageVersionPopulationDocument(
                request,
                versions,
                sourceListings);
            return new PackageVersionPopulationOutcome.Populated(
                document,
                countRequest is null
                    ? null
                    : Count(document, countRequest));
        }

        (PackageVersionPopulationFailureKind kind, InertString reason) =
            population switch
            {
                PackageHouseVersionPopulationResult.NotFound value =>
                    (PackageVersionPopulationFailureKind.NotFound, value.Reason),
                PackageHouseVersionPopulationResult.NoMatch value =>
                    (PackageVersionPopulationFailureKind.NoMatch,
                        DescribeNoMatch(value)),
                PackageHouseVersionPopulationResult.Rejected value =>
                    (PackageVersionPopulationFailureKind.Rejected, value.Reason),
                PackageHouseVersionPopulationResult.Unavailable value =>
                    (PackageVersionPopulationFailureKind.Unavailable, value.Reason),
                PackageHouseVersionPopulationResult.Incomplete value =>
                    (PackageVersionPopulationFailureKind.Incomplete, value.Reason),
                PackageHouseVersionPopulationResult.Failed value =>
                    (PackageVersionPopulationFailureKind.Failed, value.Reason),
                _ => throw new InvalidOperationException(
                    "Version population settlement returned an unsupported outcome."),
            };
        ImmutableArray<PackageVersionPopulationAuthorityFailure> failures =
        [
            .. population.Evidence.Failures
                .OfType<PackageHouseFailure.Authority>()
                .Select(value => new PackageVersionPopulationAuthorityFailure(
                    value.Failure.Authority,
                    value.Failure.Kind,
                    Field(value.Failure.Message),
                    value.Failure.Timeout?.Kind)),
        ];
        return new PackageVersionPopulationOutcome.NotAvailable(
            new(
                request,
                kind,
                reason,
                population.Evidence.HasOperationTimeout,
                failures));
    }

    private static PackageVersionPopulationCountOutcome Count(
        PackageVersionPopulationDocument document,
        PackageVersionPopulationCountRequest request) =>
        request.Cohort switch
        {
            PackageVersionPopulationCountCohort.Versions =>
                CountRows(document.Versions, request),
            PackageVersionPopulationCountCohort.SourceListings =>
                CountRows(document.SourceListings, request),
            _ => throw new InvalidOperationException(
                $"Unknown package version population Count cohort '{request.Cohort}'."),
        };

    private static PackageVersionPopulationCountOutcome CountRows<T>(
        IReadOnlyList<T> rows,
        PackageVersionPopulationCountRequest request)
    {
        if (request.RowSelection is not { Operations.Count: > 0 })
        {
            return new PackageVersionPopulationCountOutcome.Completed(
                new(request.Cohort, rows.Count));
        }

        RowsCohortResult<string, T> selected =
            RowsCohortExecutor.ApplyUnordered(
                [
                    RowsCohortSequence<string, T>.Create(
                        "Package versions",
                        rows),
                ],
                request.RowSelection);
        if (selected.IsSuccess)
        {
            return new PackageVersionPopulationCountOutcome.Completed(
                new(request.Cohort, selected.RowSets[0].Values.Count));
        }

        RowsCohortSemanticFailure<string> failure = selected.Failure!;
        return new PackageVersionPopulationCountOutcome.Rejected(
            new(
                request.Cohort,
                failure.Failure.StageNumber,
                failure.Failure.RequiredPosition,
                failure.Failure.AvailableCount));
    }

    private static InertString DescribeNoMatch(
        PackageHouseVersionPopulationResult.NoMatch population)
    {
        try
        {
            _ = PackageVersionVector.CreateListingAware(
                population.Request.Range,
                population.Evidence.Discovery!.Listings,
                population.Request.IncludePrerelease);
        }
        catch (ArgumentException exception)
        {
            return Field(exception.Message);
        }

        return population.Reason;
    }

    private static InspectionEnvelope<PackageVersionPopulationOutcome> Envelope(
        PackageVersionPopulationOutcome outcome,
        IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                "package-version-population/share",
                "Package version population does not yet have a canonical Workspace Share projection."),
            diagnostics);

    private static InertString Field(string value) => new(TextPolicy.Field, value);
}
