using System.Collections.Immutable;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>Settles raw package version listing through the shared House boundary.</summary>
public static class PackageVersionListingInspection
{
    /// <summary>
    /// Consumes the source operation and returns only detached listing content.
    /// </summary>
    public static async Task<InspectionEnvelope<PackageVersionListingOutcome>>
        ExecuteAsync(
            string packageId,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation,
            bool includePrerelease = false,
            bool includeUnlisted = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(sourceOperation);
        var request = new PackageVersionListingRequest(
            packageId.ToLowerInvariant(),
            includePrerelease,
            includeUnlisted);
        if (PackageCoordinateResolver.Validate(
                new PackageCoordinate(packageId))
            is { } invalid)
        {
            using (sourceOperation)
            {
                return Envelope(
                    new PackageVersionListingOutcome.NotAvailable(
                        new(
                            request,
                            PackageVersionListingFailureKind.Rejected,
                            Field(invalid.Message),
                            OperationTimedOut: false,
                            [
                                new(
                                    InertString.Empty,
                                    PackageAuthorityFailureKind.Input,
                                    Field(invalid.Message),
                                    null),
                            ])));
            }
        }

        var operation = PackageHouseOperation.Create(
            PackageHouseOperationProfile.Settle,
            sourceOperation.RequestTimeout,
            sourceOperation.OperationTimeout);
        PackageHouseVersionListingResult listing =
            await house.SettleVersionListingAsync(
                new(
                    packageId,
                    operation,
                    includePrerelease,
                    includeUnlisted),
                sourceOperation).ConfigureAwait(false);
        return Envelope(
            Project(request, listing),
            listing is PackageHouseVersionListingResult.Available
                ? listing.Evidence.Failures
                    .OfType<PackageHouseFailure.Authority>()
                    .Select(value => new InspectionDiagnostic(
                        "package-version-listing.source-failure",
                        InspectionDiagnosticSeverity.Warning,
                        value.Failure.Message))
                : []);
    }

    private static PackageVersionListingOutcome Project(
        PackageVersionListingRequest request,
        PackageHouseVersionListingResult listing)
    {
        ImmutableArray<PackageVersionListingAuthorityFailure> failures =
        [
            .. listing.Evidence.Failures
                .OfType<PackageHouseFailure.Authority>()
                .Select(value => new PackageVersionListingAuthorityFailure(
                    value.Failure.Authority,
                    value.Failure.Kind,
                    Field(value.Failure.Message),
                    value.Failure.Timeout?.Kind)),
        ];
        if (listing is PackageHouseVersionListingResult.Available available)
        {
            PackageVersionDiscoveryResult discovery =
                available.Evidence.Discovery
                ?? throw new InvalidOperationException(
                    "Available version listing did not retain discovery.");
            var document = new PackageVersionListingDocument(
                request,
                available.IsAuthoritative
                    ? PackageVersionListingCompleteness.Authoritative
                    : PackageVersionListingCompleteness.Partial,
                [.. discovery.Listings],
                [.. discovery.SourceListings]);
            return new PackageVersionListingOutcome.Listed(
                document,
                failures);
        }

        (PackageVersionListingFailureKind kind, InertString reason) =
            listing switch
            {
                PackageHouseVersionListingResult.NotFound value =>
                    (PackageVersionListingFailureKind.NotFound, value.Reason),
                PackageHouseVersionListingResult.Incomplete value =>
                    (PackageVersionListingFailureKind.Incomplete, value.Reason),
                PackageHouseVersionListingResult.Rejected value =>
                    (PackageVersionListingFailureKind.Rejected, value.Reason),
                PackageHouseVersionListingResult.Unavailable value =>
                    (PackageVersionListingFailureKind.Unavailable, value.Reason),
                PackageHouseVersionListingResult.Failed value =>
                    (PackageVersionListingFailureKind.Failed, value.Reason),
                _ => throw new InvalidOperationException(
                    "Version-listing settlement returned an unsupported outcome."),
            };
        return new PackageVersionListingOutcome.NotAvailable(
            new(
                request,
                kind,
                reason,
                listing.Evidence.HasOperationTimeout,
                failures));
    }

    /// <summary>Reduces one available listing to its requested Count result.</summary>
    public static PackageVersionPopulationCountOutcome Count(
        PackageVersionListingDocument document,
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
    /// Projects a completed listing Count as scalar Content while preserving
    /// diagnostics from the same settled listing.
    /// </summary>
    public static InspectionEnvelope<int> ProjectCountEnvelope(
        InspectionEnvelope<PackageVersionListingOutcome> listing,
        PackageVersionPopulationCountOutcome.Completed count)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(count);
        if (listing.Content is not PackageVersionListingOutcome.Listed)
        {
            throw new ArgumentException(
                "A Count envelope requires an available package version listing.",
                nameof(listing));
        }

        return PackageVersionCountProjection.ProjectEnvelope(
            listing,
            count);
    }

    private static InspectionEnvelope<PackageVersionListingOutcome> Envelope(
        PackageVersionListingOutcome outcome,
        IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(
            InspectionContentKind.Outcome,
            outcome,
            new InspectionPortableProjection.NonProjectable(
                "package-version-listing/share",
                InspectionPortableProjectionFailureReason.NotSupported),
            diagnostics);

    private static InertString Field(string value) => new(TextPolicy.Field, value);
}
