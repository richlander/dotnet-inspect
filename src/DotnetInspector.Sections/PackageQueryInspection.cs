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
        InspectionEnvelope<PackageQueryDocument>>
        ExecuteAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            source,
            plan,
            contentProvider: null,
            dependencyTraversalServices: null,
            nonterminalSink: null,
            cancellationToken).ConfigureAwait(false);

    public static async ValueTask<
        InspectionEnvelope<PackageQueryDocument>>
        ExecuteAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            IPackageQueryContentProvider? contentProvider,
            CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            source,
            plan,
            contentProvider,
            dependencyTraversalServices: null,
            nonterminalSink: null,
            cancellationToken).ConfigureAwait(false);

    public static async ValueTask<
        InspectionEnvelope<PackageQueryDocument>>
        ExecuteAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            IPackageQueryContentProvider? contentProvider,
            IPackageQueryNonterminalSink? nonterminalSink,
            CancellationToken cancellationToken = default)
            => await ExecuteAsync(
                source,
                plan,
                contentProvider,
                dependencyTraversalServices: null,
                nonterminalSink,
                cancellationToken).ConfigureAwait(false);

    public static async ValueTask<
            InspectionEnvelope<PackageQueryDocument>>
            ExecuteAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            IPackageQueryContentProvider? contentProvider,
            PackageQueryDependencyTraversalServices?
                dependencyTraversalServices,
            IPackageQueryNonterminalSink? nonterminalSink,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);

        var results = ImmutableArray.CreateBuilder<PackageQueryMatch>();
        var failures = ImmutableArray.CreateBuilder<PackageQueryFailure>();
        PackageQuerySummary? summary = null;
        await foreach (PackageQueryEvent queryEvent in PackageQuery.ExecuteAsync(
            source,
            plan,
            contentProvider,
            dependencyTraversalServices,
            cancellationToken).ConfigureAwait(false))
        {
            if (summary is not null)
            {
                throw new InvalidOperationException(
                    "Package Query produced an event after completion.");
            }

            switch (queryEvent)
            {
                case PackageQueryEvent.Match match:
                    results.Add(match.Value);
                    break;
                case PackageQueryEvent.Failure failure:
                    failures.Add(failure.Value);
                    break;
                case PackageQueryEvent.Completed completed:
                    summary = completed.Value;
                    continue;
            }

            if (queryEvent is PackageQueryEvent.Nonterminal nonterminal
                && nonterminalSink is not null)
            {
                await nonterminalSink.ReportAsync(
                    nonterminal,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (summary is null)
        {
            throw new InvalidOperationException(
                "Package Query ended without completion.");
        }

        var content = new PackageQueryDocument(
            results.ToImmutable(),
            failures.ToImmutable(),
            summary);
        ValidateContent(plan, content);

        return new(
            new ResourcePath("package-query"),
            InspectionContentKind.Document,
            content,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
    }

    private static void ValidateContent(
        PackageQueryPlan plan,
        PackageQueryDocument content)
    {
        if (content.Results.IsDefault
            || content.Failures.IsDefault)
        {
            throw new ArgumentException(
                "Package Query content must be initialized.",
                nameof(content));
        }
        PackageQuerySummary summary = content.Summary;
        if (summary.Matches != content.Results.Length
            || summary.Failures != content.Failures.Length)
        {
            throw new ArgumentException(
                "Package Query content does not match its terminal accounting.",
                nameof(content));
        }

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

/// <summary>
/// Receives Package Query events established before terminal settlement.
/// Completion remains authoritative in the returned Document.
/// </summary>
public interface IPackageQueryNonterminalSink
{
    ValueTask ReportAsync(
        PackageQueryEvent.Nonterminal queryEvent,
        CancellationToken cancellationToken);
}
