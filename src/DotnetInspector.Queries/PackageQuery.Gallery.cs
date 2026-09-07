using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using DotnetInspector.RowSelection;
using DotnetInspector.SourceSelection;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries;

internal abstract record PackageQueryInputEvent
{
    internal sealed record Acquired(int Count, long? EstimatedTotalHits)
        : PackageQueryInputEvent;
    internal sealed record Match(PackageQueryPackage Value) : PackageQueryInputEvent;
    internal sealed record Failure(PackageQueryFailure Value) : PackageQueryInputEvent;
    internal sealed record Completed(int Candidates, PackageQueryCompletionKind Completion)
        : PackageQueryInputEvent;
}

public static partial class PackageQuery
{
    public const string GalleryScopeEvidenceId = "package.query.scope.gallery";
    public const string GalleryOrderEvidenceId = "package.query.source.gallery-order";
    public const string GalleryPackageTypeEvidenceId = "package.query.source.gallery-package-type";
    public const string GalleryPrereleaseEvidenceId = "package.query.source.gallery-prerelease";

    /// <summary>
    /// Plans local selection over one bounded Gallery response. Match selection
    /// does not change the capacity, text, type, or order of that source input.
    /// </summary>
    public static PackageQueryPlanResult PlanGallery(
        NuGetGalleryDiscoveryRequest request,
        IReadOnlyCollection<string>? facetIds = null,
        int maximumMatches = DefaultMaximumMatches)
    {
        ArgumentNullException.ThrowIfNull(request);
        string scopeEvidence = request.IsBrowse
            ? "Package belongs to the acquired Gallery browse response."
            : $"Package belongs to the Gallery response for \"{request.Text}\".";
        if (!InertString.IsPermitted(TextPolicy.Prose, request.Text ?? "")
            || !InertString.IsPermitted(TextPolicy.Prose, scopeEvidence))
        {
            return Rejected(PackageQueryRequestFailureReason.InvalidSearchText);
        }

        return PlanCore(
            Evidence(""),
            Evidence(scopeEvidence),
            facetIds,
            request.Capacity,
            maximumMatches,
            request.IncludePrerelease,
            request);
    }

    static void AddScopeEvidence(
        PackageQueryPlan plan,
        ImmutableArray<PackageQueryEvidence>.Builder evidence)
    {
        if (plan.GalleryRequest is not { } request)
        {
            evidence.Add(ScopeEvidence(
                plan.PackageInput is SourceSelector.Package
                    ? ExactPackageEvidenceId
                    : PrefixEvidenceId,
                plan.PrefixEvidence));
            return;
        }

        evidence.Add(ScopeEvidence(GalleryScopeEvidenceId, plan.PrefixEvidence));
        evidence.Add(ScopeEvidence(
            GalleryOrderEvidenceId,
            Evidence(request.Order == NuGetGalleryDiscoveryOrder.MostDownloaded
                ? "Gallery download-ranked response order; not a global top-N."
                : "Gallery relevance response order.")));
        evidence.Add(ScopeEvidence(
            GalleryPrereleaseEvidenceId,
            Evidence(request.IncludePrerelease
                ? "Gallery source selection permits prerelease versions."
                : "Gallery source selection permits stable versions only.")));
        if (request.PackageType is { } packageType)
        {
            evidence.Add(ScopeEvidence(
                GalleryPackageTypeEvidenceId,
                Evidence($"Gallery applied package type \"{packageType.Name}\"; this is index evidence.")));
        }
    }

    static PackageQueryEvidence ScopeEvidence(string id, InertString text) =>
        new(id, text) { Scope = PackageQueryEvidenceScope.Query };

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
            && plan.Definitions.IsEmpty)
        {
            await foreach (PackageQueryInputEvent item in AcquirePrefixMetadataAsync(
                source, prefix.Request, cancellationToken).ConfigureAwait(false))
                yield return item;
            yield break;
        }

        if (plan.GalleryRequest is not null)
        {
            await foreach (PackageQueryInputEvent item in AcquireGalleryInputAsync(
                source, plan, cancellationToken).ConfigureAwait(false))
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

    static async IAsyncEnumerable<PackageQueryInputEvent> AcquireGalleryInputAsync(
        IPackageSourceClient source,
        PackageQueryPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        NuGetGalleryDiscoveryRequest request = plan.GalleryRequest
            ?? throw new InvalidOperationException("A Gallery input requires a Gallery request.");
        if (source is not INuGetGalleryPackageSourceClient gallery)
        {
            yield return SearchFailure(
                "The package source does not support Gallery discovery.",
                PackageQueryFailureKind.Search);
            yield return new PackageQueryInputEvent.Completed(0, PackageQueryCompletionKind.Failed);
            yield break;
        }

        PackageSourceOperationResult<NuGetGalleryDiscoveryResult> operation =
            await gallery.DiscoverAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Failure is { } failure)
        {
            yield return SearchFailure(failure.Message, PackageQueryFailureKind.Search);
            yield return new PackageQueryInputEvent.Completed(0, PackageQueryCompletionKind.Failed);
            yield break;
        }

        NuGetGalleryDiscoveryResult response = operation.Value
            ?? throw new InvalidOperationException("Gallery discovery returned no value or failure.");
        if (response.Request != request || !ReferenceEquals(response.Source, source.Source))
        {
            yield return SearchFailure(
                "Gallery discovery returned a different source input.",
                PackageQueryFailureKind.SearchContract);
            yield return new PackageQueryInputEvent.Completed(0, PackageQueryCompletionKind.Failed);
            yield break;
        }

        yield return new PackageQueryInputEvent.Acquired(
            response.Matches.Length, response.EstimatedTotalHits);
        bool needsManifest = !plan.Definitions.IsEmpty;
        IReadOnlyList<NuGetGalleryDiscoveryMatch> input = response.Matches;
        if (!needsManifest)
        {
            // Selection applies only after acquisition of the exact K-sized input.
            input = RowSelectionExecutor.Apply(
                input,
                RowSelectionPlan<string>.Create(
                    [RowSelectionStage<string>.Head(plan.MaximumMatches)])).Values;
        }

        int candidates = 0;
        foreach (NuGetGalleryDiscoveryMatch match in input)
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidates++;
            PackageManifestFacts? manifest = null;
            if (needsManifest)
            {
                var (facts, manifestFailure) = await PackageProfileQuery.AcquireManifestAsync(
                    source, match.Candidate, match.PackageId, match.Version,
                    cancellationToken).ConfigureAwait(false);
                if (manifestFailure is not null)
                {
                    yield return new PackageQueryInputEvent.Failure(
                        FromProfileFailure(manifestFailure));
                    continue;
                }
                manifest = facts ?? throw new InvalidOperationException(
                    "Manifest acquisition returned no facts or failure.");
            }

            yield return new PackageQueryInputEvent.Match(new PackageQueryPackage(
                match.PackageId,
                match.Version,
                match.Owners,
                match.TotalDownloads,
                match.Verified,
                match.Candidate.Source,
                manifest,
                match.Description));
        }

        yield return new PackageQueryInputEvent.Completed(
            candidates, PackageQueryCompletionKind.GalleryResponseComplete);

        PackageQueryInputEvent.Failure SearchFailure(
            string message,
            PackageQueryFailureKind kind) =>
            new(new PackageQueryFailure(null, null, source.Source, kind, message));
    }
}
