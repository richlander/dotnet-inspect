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
            bool includeUnlisted = false,
            PackageVersionPopulationCountRequest? countRequest = null)
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
            Project(request, listing, countRequest),
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
        PackageHouseVersionListingResult listing,
        PackageVersionPopulationCountRequest? countRequest)
    {
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
                countRequest is null
                    ? null
                    : PackageVersionCountProjection.Count(
                        document.Versions,
                        document.SourceListings,
                        countRequest));
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
        return new PackageVersionListingOutcome.NotAvailable(
            new(
                request,
                kind,
                reason,
                listing.Evidence.HasOperationTimeout,
                failures));
    }

    private static InspectionEnvelope<PackageVersionListingOutcome> Envelope(
        PackageVersionListingOutcome outcome,
        IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                "package-version-listing/share",
                "Package version listing does not yet have a canonical Workspace Share projection."),
            diagnostics);

    private static InertString Field(string value) => new(TextPolicy.Field, value);
}
