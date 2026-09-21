using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using DotnetInspector.Packages;
using QuerySpace;
using QuerySpace.Rows;
using DotnetInspector.SourceSelection;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries;

internal abstract record PackageQueryInputEvent
{
    internal sealed record Acquired(int Count) : PackageQueryInputEvent;
    internal sealed record Match(PackageQueryPackage Value) : PackageQueryInputEvent;
    internal sealed record Failure(PackageQueryFailure Value) : PackageQueryInputEvent;
    internal sealed record Completed(int Candidates, PackageQueryCompletionKind Completion)
        : PackageQueryInputEvent;
}

public static partial class PackageQuery
{
    /// <summary>Plans an exact package ID or one explicit terminal-star prefix.</summary>
    public static PackageQueryPlanResult PlanInput(
        string text,
        IReadOnlyCollection<PortableQueryTerm>? terms = null,
        int maximumCandidates = DefaultMaximumCandidates,
        int? maximumMatches = DefaultMaximumMatches,
        bool includePrerelease = false,
        RowSelectionIntent<string>? rowSelection = null,
        string? targetFramework = null)
        => PlanInputCore(
            text,
            terms,
            maximumCandidates,
            maximumMatches,
            includePrerelease,
            rowSelection,
            targetFramework,
            ecosystemMemberships: null);

    public static PackageQueryPlanResult PlanInput(
        string text,
        PackageQueryEcosystemMembershipCatalog ecosystemMemberships,
        IReadOnlyCollection<PortableQueryTerm>? terms = null,
        int maximumCandidates = DefaultMaximumCandidates,
        int? maximumMatches = DefaultMaximumMatches,
        bool includePrerelease = false,
        RowSelectionIntent<string>? rowSelection = null,
        string? targetFramework = null)
    {
        ArgumentNullException.ThrowIfNull(ecosystemMemberships);
        return PlanInputCore(
            text,
            terms,
            maximumCandidates,
            maximumMatches,
            includePrerelease,
            rowSelection,
            targetFramework,
            ecosystemMemberships);
    }

    private static PackageQueryPlanResult PlanInputCore(
        string text,
        IReadOnlyCollection<PortableQueryTerm>? terms,
        int maximumCandidates,
        int? maximumMatches,
        bool includePrerelease,
        RowSelectionIntent<string>? rowSelection,
        string? targetFramework,
        PackageQueryEcosystemMembershipCatalog? ecosystemMemberships)
    {
        ArgumentNullException.ThrowIfNull(text);

        string spelling = text.Trim();
        string populationKey;
        string populationValue;
        if (spelling.EndsWith('*'))
        {
            string prefix = spelling[..^1];
            if (prefix.Length == 0
                || prefix.Contains('*')
                || !PackageProfileQuery.IsValidPrefix(prefix)
                || !InertString.IsPermitted(TextPolicy.Field, prefix))
            {
                return Rejected(
                    PackageQueryRequestFailureReason.InvalidPackageInput);
            }

            populationKey = PrefixTermKey;
            populationValue = prefix;
        }
        else
        {
            if (spelling.Contains('*')
                || !DotnetInspector.Packages.PackageExtractor
                    .IsValidPackageId(spelling)
                || !InertString.IsPermitted(TextPolicy.Field, spelling))
            {
                return Rejected(
                    PackageQueryRequestFailureReason.InvalidPackageInput);
            }

            populationKey = PackageTermKey;
            populationValue = spelling;
        }

        if (maximumCandidates is <= 0 or > MaximumCandidates)
            return Rejected(PackageQueryRequestFailureReason.InvalidCandidateLimit);
        if (maximumMatches is <= 0 or > MaximumCandidates)
            return Rejected(PackageQueryRequestFailureReason.InvalidMatchLimit);
        bool hasLibraryLiteral = terms?.Any(term =>
            term.Key == LibraryLiteralTermKey) == true;
        int contextualTermCount = hasLibraryLiteral ? 1 : 0;
        if (terms is { } suppliedTerms
            && suppliedTerms.Count + contextualTermCount
                > MaximumInspectionTerms)
        {
            return Rejected(
                PackageQueryRequestFailureReason.TooManyTerms,
                value: suppliedTerms.Count);
        }
        if (hasLibraryLiteral && string.IsNullOrWhiteSpace(targetFramework))
        {
            return Rejected(
                PackageQueryRequestFailureReason.LibraryLiteralRequiresTarget,
                [LibraryLiteralTermKey, LibraryTargetTermKey]);
        }
        if (!hasLibraryLiteral && targetFramework is not null)
        {
            return Rejected(
                PackageQueryRequestFailureReason.LibraryTargetRequiresLiteral,
                [LibraryTargetTermKey, LibraryLiteralTermKey]);
        }
        if (populationKey == PackageTermKey)
            maximumCandidates = 1;

        var intentTerms = new List<PortableQueryTerm>
        {
            new(
                populationKey,
                PortableQueryOperator.Equal,
                populationValue),
            new(
                PrereleaseTermKey,
                PortableQueryOperator.Equal,
                includePrerelease ? "include" : "stable"),
        };
        if (terms is not null)
            intentTerms.AddRange(terms);
        if (hasLibraryLiteral)
        {
            try
            {
                PackageHouseTargetContext target =
                    PackageHouseTargetContext.Exact(targetFramework!);
                intentTerms.Add(new(
                    LibraryTargetTermKey,
                    PortableQueryOperator.Equal,
                    target.RequestedFramework!));
            }
            catch (ArgumentException)
            {
                return Rejected(
                    PackageQueryRequestFailureReason.InvalidTermValue,
                    [LibraryTargetTermKey]);
            }
        }

        var bounds = new List<PortableQueryBound>
        {
            new(PackageQueryVocabulary.CandidatesDimension, maximumCandidates),
        };
        if (maximumMatches is int matches)
        {
            bounds.Add(new(
                PackageQueryVocabulary.MatchesDimension,
                matches));
        }

        PortableQueryIntent intent = PortableQueryIntent.Create(
            intentTerms,
            bounds,
            ToPortableStages(rowSelection),
            []);
        return ecosystemMemberships is null
            ? ResolveIntent(intent)
            : ResolveIntent(intent, ecosystemMemberships);
    }

    private static IReadOnlyList<PortableQueryStage> ToPortableStages(
        RowSelectionIntent<string>? rowSelection) =>
        rowSelection is null
            ? []
            :
            [
                .. rowSelection.Operations.Select(operation =>
                    operation.Kind switch
                    {
                        RowSelectionStageKind.Head =>
                            PortableQueryStage.Head(operation.Count),
                        RowSelectionStageKind.Tail =>
                            PortableQueryStage.Tail(operation.Count),
                        RowSelectionStageKind.Window =>
                            PortableQueryStage.Window(
                                operation.Start,
                                operation.End),
                        RowSelectionStageKind.Top =>
                            PortableQueryStage.Top(operation.Count),
                        _ => throw new InvalidOperationException(
                            "Unknown row-selection stage."),
                    }),
            ];

    static void AddScopeEvidence(
        PackageQueryPlan plan,
        ImmutableArray<PackageQueryEvidence>.Builder evidence) =>
        evidence.Add(new PackageQueryEvidence(
            plan.PackageInput is SourceSelector.Package
                ? ExactPackageEvidenceId
                : PrefixEvidenceId)
        {
            Scope = PackageQueryEvidenceScope.Query,
            Properties =
            [
                Property(
                    plan.PackageInput is SourceSelector.Package
                        ? "package"
                        : "prefix",
                    plan.Prefix.ToString()),
            ],
        });

    static PackageQueryEvidenceProperty Property(
        string name,
        string value) =>
        new(name, new InertString(TextPolicy.Field, value));

    static async IAsyncEnumerable<PackageQueryInputEvent> AcquireInputAsync(
        IPackageSourceClient source,
        PackageQueryPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (plan.PackageInput is SourceSelector.Package exact)
        {
            await foreach (PackageQueryInputEvent item in AcquireExactInputAsync(
                source, plan, exact, cancellationToken).ConfigureAwait(false))
                yield return item;
            yield break;
        }

        if (plan.PackageInput is SourceSelector.PackagePrefix prefix
            && !plan.RequiresManifest)
        {
            await foreach (PackageQueryInputEvent item in AcquirePrefixMetadataAsync(
                source, prefix.Request, cancellationToken).ConfigureAwait(false))
                yield return item;
            yield break;
        }

        await foreach (PackageProfileEvent item in PackageProfileQuery.ExecuteAsync(
            source,
            new PackagePrefixProfileRequest(
                plan.Prefix.ToString(), plan.MaximumCandidates, plan.IncludePrerelease),
            cancellationToken).ConfigureAwait(false))
        {
            yield return item switch
            {
                PackageProfileEvent.Match match =>
                    new PackageQueryInputEvent.Match(new PackageQueryPackage(match.Value)),
                PackageProfileEvent.Failure failure =>
                    new PackageQueryInputEvent.Failure(FromProfileFailure(failure.Value)),
                PackageProfileEvent.Completed completed =>
                    new PackageQueryInputEvent.Completed(
                        completed.Value.Candidates,
                        completed.Value.Candidates == 0 && completed.Value.Failures > 0
                            ? PackageQueryCompletionKind.Failed
                            : MapCompletion(completed.Value.TruncationReason)),
                _ => throw new InvalidOperationException("Unknown package-profile event."),
            };
        }
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
            yield return new PackageQueryInputEvent.Acquired(0);
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
        yield return new PackageQueryInputEvent.Acquired(1);
        SearchResult? metadata = null;
        if (plan.RequiresSearchMetadata)
        {
            var (searchMetadata, searchFailure) =
                await AcquireExactSearchMetadataAsync(
                    source,
                    input.Coordinate.PackageId,
                    plan.IncludePrerelease,
                    cancellationToken).ConfigureAwait(false);
            if (searchFailure is not null)
            {
                yield return new PackageQueryInputEvent.Failure(searchFailure);
                yield return new PackageQueryInputEvent.Completed(
                    1, PackageQueryCompletionKind.Failed);
                yield break;
            }
            metadata = searchMetadata
                ?? throw new InvalidOperationException(
                    "Exact search metadata returned no value or failure.");
        }

        PackageManifestFacts? manifest = null;
        if (plan.RequiresManifest)
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
            [
                .. (metadata?.Owners ?? [])
                    .Where(owner => !string.IsNullOrWhiteSpace(owner)),
            ],
            metadata?.TotalDownloads,
            metadata?.Verified,
            candidate.Source,
            manifest,
            manifest?.Description?.ToString() ?? metadata?.Description));
        yield return new PackageQueryInputEvent.Completed(
            1, PackageQueryCompletionKind.ExactPackageComplete);
    }

    static async ValueTask<(SearchResult? Metadata, PackageQueryFailure? Failure)>
        AcquireExactSearchMetadataAsync(
            IPackageSourceClient source,
            string packageId,
            bool includePrerelease,
            CancellationToken cancellationToken)
    {
        PackageSourceOperationResult<PackageSearchResult> operation =
            await source.SearchAsync(
                packageId,
                take: 20,
                includePrerelease,
                cancellationToken).ConfigureAwait(false);
        if (operation.Failure is { } failure)
        {
            return (null, new PackageQueryFailure(
                packageId,
                null,
                failure.Source,
                PackageQueryFailureKind.Search,
                failure.Message));
        }

        PackageSearchResult result = operation.Value
            ?? throw new InvalidOperationException(
                "Exact package search returned no value or failure.");
        foreach (PackageSearchMatch match in result.Matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PackageProfileQuery.ValidateSearchCandidate(
                source, string.Empty, match) is { } invalid)
            {
                return (null, FromProfileFailure(invalid));
            }

            if (string.Equals(
                match.Metadata.Id,
                packageId,
                StringComparison.OrdinalIgnoreCase))
            {
                return (match.Metadata, null);
            }
        }

        return (null, new PackageQueryFailure(
            packageId,
            null,
            source.Source,
            PackageQueryFailureKind.Search,
            "The package source did not return search metadata for the resolved package."));
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
