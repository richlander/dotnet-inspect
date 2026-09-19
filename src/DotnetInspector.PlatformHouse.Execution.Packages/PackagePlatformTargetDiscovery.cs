using DotnetInspector.Packages;
using DotnetInspector.Platforms.Packages;

namespace DotnetInspector.PlatformHouse.Packages;

/// <summary>
/// Package-issued inventory and candidate retained only through selected-target
/// continuation.
/// </summary>
public sealed class PackagePlatformTargetDiscoveryAssociation
{
    internal PackagePlatformTargetDiscoveryAssociation(
        PackagePlatformHouseResult<
            PackagePlatformTargetInventory>.Succeeded discovery,
        PackagePlatformTargetSelection selection)
    {
        Discovery = discovery;
        Selection = selection;
    }

    internal PackagePlatformHouseResult<
        PackagePlatformTargetInventory>.Succeeded Discovery { get; }
    internal PackagePlatformTargetSelection Selection { get; }
}

/// <summary>
/// Projects package-backed discovery into source-neutral ephemeral attempts.
/// </summary>
public static class PackagePlatformTargetDiscovery
{
    /// <summary>
    /// Creates one lazy package-backed source whose operation lease is issued
    /// only when the typed fallback stage is invoked.
    /// </summary>
    public static PlatformTargetDiscoverySource CreateSource(
        PackagePlatformHouseAdapter adapter,
        Func<PlatformHouseRequest, PackageSourceOperationLease>
            issueOperation)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(issueOperation);
        return new(
            adapter.TargetDiscovery,
            async request =>
                PrepareAttempt(
                    await adapter.DiscoverTargetsAsync(
                        request,
                        issueOperation(request))
                    .ConfigureAwait(false)));
    }

    public static PlatformTargetDiscoveryAttempt PrepareAttempt(
        PackagePlatformHouseResult<
            PackagePlatformTargetInventory> discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        return discovery switch
        {
            PackagePlatformHouseResult<
                PackagePlatformTargetInventory>.Succeeded success =>
                    PrepareAttempt(success),
            PackagePlatformHouseResult<
                PackagePlatformTargetInventory>.NotSucceeded terminal =>
                    PrepareAttempt(terminal),
            _ => throw new InvalidOperationException(
                "Unknown package-backed target-discovery result."),
        };
    }

    public static PlatformTargetDiscoveryAttempt PrepareAttempt(
        PackagePlatformHouseResult<
            PackagePlatformTargetInventory>.Succeeded discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        var contribution =
            (PlatformSourceContribution.TargetDiscovery)
                discovery.Contribution;
        return new PlatformTargetDiscoveryAttempt.Succeeded(
            contribution,
            discovery.Value.Targets.Select(
                selection =>
                    new PlatformTargetDiscoveryCandidate<
                        PackagePlatformTargetDiscoveryAssociation>(
                            selection.Target,
                            new(discovery, selection))));
    }

    public static PlatformTargetDiscoveryAttempt PrepareAttempt(
        PackagePlatformHouseResult<
            PackagePlatformTargetInventory>.NotSucceeded discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        return new PlatformTargetDiscoveryAttempt.NotSucceeded(
            discovery.Contribution,
            RejectionKind(discovery));
    }

    static PlatformHouseRejectionKind? RejectionKind(
        PackagePlatformHouseResult<
            PackagePlatformTargetInventory>.NotSucceeded terminal) =>
        terminal.Contribution is not PlatformSourceContribution.Rejected
            ? null
            : terminal.Diagnostic.Kind switch
            {
                PackagePlatformSourceDiagnosticKind.InvalidSelection =>
                    PlatformHouseRejectionKind.InvalidRequest,
                PackagePlatformSourceDiagnosticKind.InvalidCoordinate
                    or PackagePlatformSourceDiagnosticKind.UnsupportedTarget =>
                        PlatformHouseRejectionKind
                            .InvalidTargetCorrespondence,
                _ => PlatformHouseRejectionKind.InvalidOwnerResult,
            };
}
