using System.Runtime.CompilerServices;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using NuGetFetch;

namespace DotnetInspector.Queries;

public static partial class PackageQuery
{
    /// <summary>Plans an exact package ID or one explicit terminal-star prefix.</summary>
    public static PackageQueryPlanResult PlanInput(
        string text,
        IReadOnlyCollection<string>? facetIds = null,
        int maximumCandidates = DefaultMaximumCandidates,
        int maximumMatches = DefaultMaximumMatches,
        bool includePrerelease = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maximumCandidates is <= 0 or > PackageProfileQuery.MaximumPackageLimit)
        {
            return Rejected(
                PackageQueryRequestFailureReason.InvalidCandidateLimit,
                value: maximumCandidates);
        }

        string spelling = text.Trim();
        SourceSelector input;
        string scope;
        string explanation;
        try
        {
            if (spelling.EndsWith('*'))
            {
                var prefix = new PackagePrefixRequest(
                    spelling[..^1], maximumCandidates, includePrerelease);
                input = new SourceSelector.PackagePrefix(prefix);
                scope = prefix.Prefix;
                explanation = $"Package ID matches prefix \"{scope}\".";
            }
            else
            {
                input = new SourceSelector.Package(new PackageCoordinate(spelling));
                scope = spelling;
                explanation = $"Package ID is \"{scope}\".";
                maximumCandidates = 1;
            }
        }
        catch (ArgumentException)
        {
            return Rejected(PackageQueryRequestFailureReason.InvalidPackageInput);
        }

        return PlanCore(
            Evidence(scope), Evidence(explanation), facetIds,
            maximumCandidates, maximumMatches, includePrerelease,
            packageInput: input);
    }

    static async IAsyncEnumerable<PackageQueryInputEvent> AcquireExactInputAsync(
        IPackageSourceClient source,
        PackageQueryPlan plan,
        SourceSelector.Package input,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        PackageSourceCoordinateResolution resolution =
            await PackageSourceCoordinateResolver.ResolveLatestListedAsync(
                source, input.Coordinate.PackageId, plan.IncludePrerelease,
                cancellationToken).ConfigureAwait(false);
        if (resolution is PackageSourceCoordinateResolution.NoEligibleVersion)
        {
            yield return new PackageQueryInputEvent.Acquired(0, null);
            yield return new PackageQueryInputEvent.Completed(
                0, PackageQueryCompletionKind.ExactPackageComplete);
            yield break;
        }
        if (resolution is not PackageSourceCoordinateResolution.Resolved resolved)
        {
            string message = resolution switch
            {
                PackageSourceCoordinateResolution.Failed failed => failed.Failure.Message,
                PackageSourceCoordinateResolution.Unavailable unavailable => unavailable.Message,
                PackageSourceCoordinateResolution.Invalid invalid => invalid.Message,
                _ => throw new InvalidOperationException("Unknown package resolution result."),
            };
            yield return new PackageQueryInputEvent.Failure(new PackageQueryFailure(
                null, null, source.Source, PackageQueryFailureKind.Search, message));
            yield return new PackageQueryInputEvent.Completed(0, PackageQueryCompletionKind.Failed);
            yield break;
        }

        PackageCandidateObservation candidate = resolved.Candidate
            ?? throw new InvalidOperationException(
                "Listed package resolution returned no source observation.");
        yield return new PackageQueryInputEvent.Acquired(1, null);
        PackageManifestFacts? manifest = null;
        if (!plan.Definitions.IsEmpty)
        {
            var (facts, failure) = await PackageProfileQuery.AcquireManifestAsync(
                source, candidate, input.Coordinate.PackageId, candidate.Coordinate.Version,
                cancellationToken).ConfigureAwait(false);
            if (failure is not null)
            {
                yield return new PackageQueryInputEvent.Failure(FromProfileFailure(failure));
                yield return new PackageQueryInputEvent.Completed(
                    1, PackageQueryCompletionKind.ExactPackageComplete);
                yield break;
            }
            manifest = facts ?? throw new InvalidOperationException(
                "Manifest acquisition returned no facts or failure.");
        }

        yield return new PackageQueryInputEvent.Match(new PackageQueryPackage(
            input.Coordinate.PackageId, candidate.Coordinate.Version,
            [], null, null, candidate.Source, manifest, manifest?.Description?.ToString()));
        yield return new PackageQueryInputEvent.Completed(
            1, PackageQueryCompletionKind.ExactPackageComplete);
    }

    static async IAsyncEnumerable<PackageQueryInputEvent> AcquirePrefixMetadataAsync(
        IPackageSourceClient source,
        PackagePrefixRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int candidates = 0;
        PackageSearchTruncationReason truncation = PackageSearchTruncationReason.None;
        await foreach (PackageSourceOperationResult<PackageSearchResult> operation
            in source.SearchByPrefixPagesAsync(
                request.Prefix, request.MaxPackages, request.IncludePrerelease,
                cancellationToken).ConfigureAwait(false))
        {
            if (operation.Failure is { } failure)
            {
                yield return new PackageQueryInputEvent.Failure(new PackageQueryFailure(
                    null, null, failure.Source, PackageQueryFailureKind.Search, failure.Message));
                yield return new PackageQueryInputEvent.Completed(
                    candidates, PackageQueryCompletionKind.Failed);
                yield break;
            }

            PackageSearchResult page = operation.Value
                ?? throw new InvalidOperationException("Prefix search returned no value or failure.");
            int remaining = request.MaxPackages - candidates;
            PackageSearchMatch[] matches = [.. page.Matches.Take(remaining + 1)];
            if (page.Matches.Count > remaining
                || matches.Length > remaining
                || matches.Length != page.Matches.Count)
            {
                yield return new PackageQueryInputEvent.Failure(new PackageQueryFailure(
                    null, null, source.Source, PackageQueryFailureKind.SearchContract,
                    "The package source returned more matches than requested."));
                yield return new PackageQueryInputEvent.Completed(
                    candidates, PackageQueryCompletionKind.Failed);
                yield break;
            }

            truncation = page.TruncationReason;
            foreach (PackageSearchMatch match in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates++;
                if (PackageProfileQuery.ValidateSearchCandidate(
                    source, request.Prefix, match) is { } invalid)
                {
                    yield return new PackageQueryInputEvent.Failure(FromProfileFailure(invalid));
                    continue;
                }

                yield return new PackageQueryInputEvent.Match(new PackageQueryPackage(
                    match.Metadata.Id, match.Metadata.Version,
                    [.. (match.Metadata.Owners ?? []).Where(owner => !string.IsNullOrWhiteSpace(owner))],
                    match.Metadata.TotalDownloads, match.Metadata.Verified,
                    match.Candidate.Source, Description: match.Metadata.Description));
            }
            if (truncation != PackageSearchTruncationReason.None
                || candidates == request.MaxPackages)
                break;
        }
        cancellationToken.ThrowIfCancellationRequested();
        yield return new PackageQueryInputEvent.Completed(candidates, MapCompletion(truncation));
    }
}
