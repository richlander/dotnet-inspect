using System.Collections.Immutable;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>Settles an exact or latest package through the shared House and envelope boundary.</summary>
public static class PackageVersionSettlementInspection
{
    /// <summary>
    /// Consumes the source operation, leaving source clients and their root lifetime host-owned.
    /// A missing version requests fresh latest selection; exact pins perform no discovery.
    /// </summary>
    public static async Task<InspectionEnvelope<PackageVersionSettlementOutcome>> ExecuteAsync(
        PackageCoordinate request,
        PackageHouse house,
        PackageSourceOperationLease sourceOperation,
        bool includePrerelease = false)
    {
        ArgumentNullException.ThrowIfNull(sourceOperation);
        using (sourceOperation)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(house);
            if (PackageCoordinateResolver.Validate(request) is { } invalid)
            {
                return Envelope(new PackageVersionSettlementOutcome.NotSettled(
                    new(
                        request,
                        PackageVersionSettlementFailureKind.Rejected,
                        Field(invalid.Message),
                        OperationTimedOut: false,
                        [new(InertString.Empty, PackageAuthorityFailureKind.Input,
                            Field(invalid.Message), null)])));
            }

            PackageSourceCoordinate? exact = request.Version is { } version
                ? PackageSourceCoordinate.Create(request.PackageId, version)
                : null;
            request = request with
            {
                PackageId = request.PackageId.ToLowerInvariant(),
                Version = exact?.Version,
            };
            PackageHouseDemand demand = exact is not null
                ? new PackageHouseDemand.Exact(exact)
                : new PackageHouseDemand.Selecting(
                    new PackageVersionSelectionRequest.AlwaysLatest(
                        request.PackageId, includePrerelease));
            var operation = PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle,
                sourceOperation.RequestTimeout,
                sourceOperation.OperationTimeout);
            PackageHouseSettlement settlement = await house.ExecuteAsync(
                new PackageHouseRequest(demand, operation),
                sourceOperation).ConfigureAwait(false);
            return Envelope(
                Project(request, includePrerelease, settlement.Result),
                settlement.Result is PackageHouseResult.Settled
                    ? settlement.Result.Evidence.Failures.OfType<PackageHouseFailure.Authority>()
                        .Select(value => new InspectionDiagnostic(
                            "package-version-settlement.source-failure",
                            InspectionDiagnosticSeverity.Warning,
                            value.Failure.Message))
                    : []);
        }
    }

    private static PackageVersionSettlementOutcome Project(
        PackageCoordinate request,
        bool includePrerelease,
        PackageHouseResult settlement)
    {
        if (settlement is PackageHouseResult.Settled)
        {
            PackageSourceCoordinate coordinate = settlement.Decision?.Coordinate
                ?? throw new InvalidOperationException(
                    "PackageHouse settlement did not retain its selected coordinate.");
            PackageVersionResolutionReceipt? resolution =
                settlement.Decision.VersionResolution;
            if (request.Version is null
                && resolution is not PackageVersionResolutionReceipt.Resolved)
            {
                throw new InvalidOperationException(
                    "Latest PackageHouse settlement did not retain its version receipt.");
            }

            bool Matches(string version) =>
                string.Equals(
                    PackageSourceCoordinate.Create(request.PackageId, version).Version,
                    coordinate.Version,
                    StringComparison.OrdinalIgnoreCase);
            return new PackageVersionSettlementOutcome.Settled(
                new(
                    request,
                    coordinate,
                    includePrerelease,
                    resolution?.Freshness,
                    resolution is null ? [] :
                        [.. resolution.Discovery.Listings.Where(row => Matches(row.Version))],
                    resolution is null ? [] :
                        [.. resolution.Discovery.SourceListings.Where(row => Matches(row.Version))]));
        }

        (PackageVersionSettlementFailureKind kind, InertString reason) = settlement switch
        {
            PackageHouseResult.NotFound value =>
                (PackageVersionSettlementFailureKind.NotFound, value.Reason),
            PackageHouseResult.NoMatch value =>
                (PackageVersionSettlementFailureKind.NoMatch, value.Reason),
            PackageHouseResult.Ambiguous value =>
                (PackageVersionSettlementFailureKind.Ambiguous, value.Reason),
            PackageHouseResult.Rejected value =>
                (PackageVersionSettlementFailureKind.Rejected, value.Reason),
            PackageHouseResult.Unavailable value =>
                (PackageVersionSettlementFailureKind.Unavailable, value.Reason),
            PackageHouseResult.Incomplete value =>
                (PackageVersionSettlementFailureKind.Incomplete, value.Reason),
            PackageHouseResult.Failed value =>
                (PackageVersionSettlementFailureKind.Failed, value.Reason),
            _ => throw new InvalidOperationException(
                "Exact/latest settlement returned an unsupported PackageHouse outcome."),
        };
        ImmutableArray<PackageVersionSettlementAuthorityFailure> failures =
        [
            .. settlement.Evidence.Failures.OfType<PackageHouseFailure.Authority>()
                .Select(value => new PackageVersionSettlementAuthorityFailure(
                    value.Failure.Authority,
                    value.Failure.Kind,
                    Field(value.Failure.Message),
                    value.Failure.Timeout?.Kind)),
        ];
        return new PackageVersionSettlementOutcome.NotSettled(
            new(request, kind, reason, settlement.Evidence.HasOperationTimeout, failures));
    }

    private static InspectionEnvelope<PackageVersionSettlementOutcome> Envelope(
        PackageVersionSettlementOutcome outcome,
        IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(
            new ResourcePath("package-version-settlement"),
            InspectionContentKind.Outcome,
            outcome,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported),
            diagnostics);

    private static InertString Field(string value) => new(TextPolicy.Field, value);
}
