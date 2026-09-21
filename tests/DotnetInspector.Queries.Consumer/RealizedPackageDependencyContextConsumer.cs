using DotnetInspector.Packages;
using DotnetInspector.Queries;

namespace DotnetInspector.QueriesConsumer;

public sealed record RealizedPackageDependencyObservation(
    string Status,
    PackageRootReacquisitionRequest RootRequest,
    PackageRootSelectionIdentity Selection,
    PackageContentGenerationIdentity ContentGeneration,
    PackageDependencyEvidenceRoot? Evidence);

public static class RealizedPackageDependencyContextConsumer
{
    public static PackageDependencyTraversalRootOccurrence CreateTraversalRoot(
        RealizedPackageDependencyContext context) =>
        new(
            context,
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

    public static async ValueTask<RealizedPackageDependencyObservation>
        ObserveAsync(
            PackageRootBinding binding,
            CancellationToken cancellationToken = default)
    {
        RealizedPackageDependencyContextResult result =
            await RealizedPackageDependencyContextQuery.ExecuteAsync(
                binding,
                cancellationToken);
        return result switch
        {
            RealizedPackageDependencyContextResult.Available available =>
                new(
                    "available",
                    available.Subject.RootRequest,
                    available.Subject.Selection,
                    available.Subject.ContentGeneration,
                    available.Context.Evidence),
            RealizedPackageDependencyContextResult.Unavailable unavailable =>
                new(
                    $"unavailable:{unavailable.Reason}",
                    unavailable.Subject.RootRequest,
                    unavailable.Subject.Selection,
                    unavailable.Subject.ContentGeneration,
                    null),
            RealizedPackageDependencyContextResult.Failed failed =>
                new(
                    "failed",
                    failed.Subject.RootRequest,
                    failed.Subject.Selection,
                    failed.Subject.ContentGeneration,
                    null),
            _ => throw new InvalidOperationException(
                "Unknown realized package dependency context result."),
        };
    }
}
