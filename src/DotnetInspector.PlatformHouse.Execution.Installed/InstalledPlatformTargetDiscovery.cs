using DotnetInspector.Platforms.Installed;

namespace DotnetInspector.PlatformHouse.Installed;

/// <summary>
/// Projects installed discovery into source-neutral ephemeral attempts.
/// </summary>
public static class InstalledPlatformTargetDiscovery
{
    /// <summary>Creates one lazily invoked installed discovery source.</summary>
    public static PlatformTargetDiscoverySource CreateSource(
        InstalledPlatformHouseAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return new(
            adapter.Capabilities.TargetDiscovery,
            request => ValueTask.FromResult(
                PrepareAttempt(adapter.DiscoverTargets(request))));
    }

    public static PlatformTargetDiscoveryAttempt PrepareAttempt(
        InstalledPlatformHouseResult<
            InstalledReferenceTargetInventory> discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        return discovery switch
        {
            InstalledPlatformHouseResult<
                InstalledReferenceTargetInventory>.Succeeded success =>
                    PrepareAttempt(success),
            InstalledPlatformHouseResult<
                InstalledReferenceTargetInventory>.NotSucceeded terminal =>
                    PrepareAttempt(terminal),
            _ => throw new InvalidOperationException(
                "Unknown installed target-discovery result."),
        };
    }

    public static PlatformTargetDiscoveryAttempt PrepareAttempt(
        InstalledPlatformHouseResult<
            InstalledReferenceTargetInventory>.Succeeded discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        var contribution =
            (PlatformSourceContribution.TargetDiscovery)
                discovery.Contribution;
        return new PlatformTargetDiscoveryAttempt.Succeeded(
            contribution,
            discovery.Value.Targets.Select(
                (target, index) =>
                    new PlatformTargetDiscoveryCandidate<
                        InstalledReferenceTarget>(
                            contribution.Candidates[index],
                            target)));
    }

    public static PlatformTargetDiscoveryAttempt PrepareAttempt(
        InstalledPlatformHouseResult<
            InstalledReferenceTargetInventory>.NotSucceeded discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        return new PlatformTargetDiscoveryAttempt.NotSucceeded(
            discovery.Contribution,
            RejectionKind(discovery));
    }

    static PlatformHouseRejectionKind? RejectionKind(
        InstalledPlatformHouseResult<
            InstalledReferenceTargetInventory>.NotSucceeded terminal) =>
        terminal.Contribution is not PlatformSourceContribution.Rejected
            ? null
            : terminal.Diagnostic.Kind switch
            {
                InstalledPlatformSourceDiagnosticKind.InvalidRequest =>
                    PlatformHouseRejectionKind.InvalidRequest,
                InstalledPlatformSourceDiagnosticKind.InvalidCoordinate =>
                    PlatformHouseRejectionKind
                        .InvalidTargetCorrespondence,
                _ => PlatformHouseRejectionKind.InvalidOwnerResult,
            };
}
