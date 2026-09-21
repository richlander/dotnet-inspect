using System.Collections.Immutable;
using DotnetInspector.Packages;
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
        bool includeUnlisted = false)
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
                Project(request, population),
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
        PackageHouseVersionPopulationResult population)
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
            return new PackageVersionPopulationOutcome.Populated(document);
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

    /// <summary>Reduces one available population to its requested Count result.</summary>
    public static PackageVersionPopulationCountOutcome Count(
        PackageVersionPopulationDocument document,
        PackageVersionPopulationCountRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        return PackageVersionCountProjection.Count(
            document.Versions,
            document.SourceListings,
            request);
    }

    /// <summary>
    /// Projects a completed population Count as scalar Content while preserving
    /// diagnostics from the same settled population.
    /// </summary>
    public static InspectionEnvelope<int> ProjectCountEnvelope(
        InspectionEnvelope<PackageVersionPopulationOutcome> population,
        PackageVersionPopulationCountOutcome.Completed count)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(count);
        if (population.Content is not PackageVersionPopulationOutcome.Populated)
        {
            throw new ArgumentException(
                "A Count envelope requires an available package version population.",
                nameof(population));
        }

        return PackageVersionCountProjection.ProjectEnvelope(
            population,
            count);
    }

    private static InspectionEnvelope<PackageVersionPopulationOutcome> Envelope(
        PackageVersionPopulationOutcome outcome,
        IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(
            new ResourcePath("package-version-population"),
            InspectionContentKind.Outcome,
            outcome,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported),
            diagnostics);

    private static InertString Field(string value) => new(TextPolicy.Field, value);
}
