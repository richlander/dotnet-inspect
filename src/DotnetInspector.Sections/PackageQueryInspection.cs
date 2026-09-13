using System.Collections.Immutable;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>
/// Materializes a completed Package Query operation into the shared
/// host-neutral inspection envelope.
/// </summary>
public static class PackageQueryInspection
{
    public static async ValueTask<
        InspectionEnvelope<ImmutableArray<PackageQueryEvent>>>
        ExecuteAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            source,
            plan,
            contentProvider: null,
            observer: null,
            cancellationToken).ConfigureAwait(false);

    public static async ValueTask<
        InspectionEnvelope<ImmutableArray<PackageQueryEvent>>>
        ExecuteAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            IPackageQueryContentProvider? contentProvider,
            CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            source,
            plan,
            contentProvider,
            observer: null,
            cancellationToken).ConfigureAwait(false);

    public static async ValueTask<
        InspectionEnvelope<ImmutableArray<PackageQueryEvent>>>
        ExecuteAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            IPackageQueryContentProvider? contentProvider,
            IPackageQueryEventObserver? observer,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        ImmutableArray<PackageQueryEvent> content =
            await PackageQuery.ExecuteToArrayAsync(
                source,
                plan,
                contentProvider,
                observer,
                cancellationToken).ConfigureAwait(false);
        ValidateContent(plan, content);

        return new(
            content,
            new InspectionShare.NonProjectable(
                "package-query/share",
                "Package Query plans do not yet have a canonical Workspace Share projection."));
    }

    private static void ValidateContent(
        PackageQueryPlan plan,
        ImmutableArray<PackageQueryEvent> content)
    {
        if (content.IsDefault)
        {
            throw new ArgumentException(
                "Package Query content must be initialized.",
                nameof(content));
        }
        if (content.Length == 0
            || content[^1] is not PackageQueryEvent.Completed completed
            || content.Count(static queryEvent =>
                queryEvent is PackageQueryEvent.Completed) != 1)
        {
            throw new ArgumentException(
                "Package Query content must end with exactly one completion event.",
                nameof(content));
        }

        PackageQuerySummary summary = completed.Value;
        if (!summary.Prefix.ToString().Equals(
                plan.Prefix.ToString(),
                StringComparison.Ordinal)
            || summary.CandidateLimit != plan.MaximumCandidates
            || summary.MatchLimit != plan.MaximumMatches)
        {
            throw new ArgumentException(
                "Package Query content belongs to another query plan.",
                nameof(content));
        }
    }
}
