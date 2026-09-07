using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// A bounded NuGet.org package-ID prefix profile.
/// </summary>
public sealed record PackagePrefixProfileRequest(
    string Prefix,
    int MaximumPackages = 100,
    bool IncludePrerelease = false);

/// <summary>
/// Host-supplied input for one package-profile query execution.
/// </summary>
public sealed record PackageProfileQueryContext(
    IPackageSourceClient Source,
    PackagePrefixProfileRequest Request,
    NuGetOperationContext? OperationContext = null);

/// <summary>
/// One package whose latest listed manifest was projected by a package profile.
/// </summary>
public sealed record PackageProfileMatch(
    string PackageId,
    string Version,
    ImmutableArray<string> Owners,
    long TotalDownloads,
    bool Verified,
    PackageSourceResultIdentity Source,
    PackageManifestFacts Manifest);

/// <summary>The stage at which one package-profile item failed.</summary>
public enum PackageProfileFailureKind
{
    Search,
    SearchContract,
    ManifestAcquisition,
    ManifestContract,
    InvalidManifest,
}

/// <summary>
/// One visible package-profile failure.
/// </summary>
public sealed record PackageProfileFailure(
    string? PackageId,
    string? Version,
    PackageSourceResultIdentity Source,
    PackageProfileFailureKind Kind,
    string Message,
    PackageManifestFailureReason? ManifestFailureReason = null);

/// <summary>
/// Terminal accounting for a completed package-profile stream.
/// </summary>
public sealed record PackageProfileSummary(
    string Prefix,
    PackageSourceResultIdentity Source,
    int Candidates,
    int Matches,
    int Failures,
    PackageSearchTruncationReason TruncationReason)
{
    public bool Truncated =>
        TruncationReason != PackageSearchTruncationReason.None;
}

/// <summary>
/// One event from a package-profile stream.
/// </summary>
public abstract record PackageProfileEvent
{
    private PackageProfileEvent()
    {
    }

    public sealed record Match(PackageProfileMatch Value) : PackageProfileEvent;

    public sealed record Failure(PackageProfileFailure Value) : PackageProfileEvent;

    public sealed record Completed(PackageProfileSummary Value) : PackageProfileEvent;
}

/// <summary>
/// Streams a bounded package profile from source search metadata and exact
/// package manifests without acquiring package archives or assemblies.
/// </summary>
public static class PackageProfileQuery
{
    public const int MaximumPackageLimit = 10_000;

    public static bool IsValidPrefix(string? prefix) =>
        !string.IsNullOrWhiteSpace(prefix)
        && prefix.Length <= 100
        && prefix.AsSpan().Trim().Length == prefix.Length
        && !prefix.Any(char.IsControl);

    public static InspectionQuery<ImmutableArray<PackageProfileEvent>>
        Definition { get; } =
            new("Package profile", InspectionCost.Unbounded);

    /// <summary>
    /// Executes and materializes one profile for registry consumers.
    /// </summary>
    public static async ValueTask<ImmutableArray<PackageProfileEvent>>
        ExecuteToArrayAsync(
            IPackageSourceClient source,
            PackagePrefixProfileRequest request,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        var events = ImmutableArray.CreateBuilder<PackageProfileEvent>();
        await foreach (PackageProfileEvent profileEvent in ExecuteAsync(
            source,
            request,
            cancellationToken,
            operationContext).ConfigureAwait(false))
        {
            events.Add(profileEvent);
        }

        return events.ToImmutable();
    }

    public static async IAsyncEnumerable<PackageProfileEvent> ExecuteAsync(
        IPackageSourceClient source,
        PackagePrefixProfileRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        int candidates = 0;
        int matches = 0;
        int failures = 0;
        PackageSearchTruncationReason truncationReason =
            PackageSearchTruncationReason.None;
        await foreach (PackageSourceOperationResult<PackageSearchResult> search
            in source.SearchByPrefixPagesAsync(
                request.Prefix,
                request.MaximumPackages,
                request.IncludePrerelease,
                cancellationToken,
                operationContext).ConfigureAwait(false))
        {
            if (search.Failure is { } searchFailure)
            {
                failures++;
                yield return new PackageProfileEvent.Failure(
                    new PackageProfileFailure(
                        PackageId: null,
                        Version: null,
                        searchFailure.Source,
                        PackageProfileFailureKind.Search,
                        searchFailure.Message));
                break;
            }

            PackageSearchResult searchResult =
                search.Value
                ?? throw new InvalidOperationException(
                    "The package source search completed without a value or failure.");
            int remaining = request.MaximumPackages - candidates;
            PackageSearchMatch[] searchMatches =
            [
                .. searchResult.Matches.Take(remaining + 1),
            ];
            if (searchResult.Matches.Count > remaining
                || searchMatches.Length > remaining
                || searchMatches.Length != searchResult.Matches.Count)
            {
                failures++;
                yield return new PackageProfileEvent.Failure(
                    new PackageProfileFailure(
                        PackageId: null,
                        Version: null,
                        source.Source,
                        PackageProfileFailureKind.SearchContract,
                        "The package source returned more matches than requested."));
                break;
            }

            truncationReason = searchResult.TruncationReason;
            foreach (PackageSearchMatch candidate in searchMatches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates++;
                PackageProfileEvent profileEvent = await EvaluateCandidateAsync(
                    source,
                    request.Prefix,
                    candidate,
                    cancellationToken,
                    operationContext).ConfigureAwait(false);
                if (profileEvent is PackageProfileEvent.Match)
                    matches++;
                else
                    failures++;
                yield return profileEvent;
            }

            if (truncationReason != PackageSearchTruncationReason.None
                || candidates == request.MaximumPackages)
                break;
        }

        cancellationToken.ThrowIfCancellationRequested();
        yield return new PackageProfileEvent.Completed(
            new PackageProfileSummary(
                request.Prefix,
                source.Source,
                candidates,
                matches,
                failures,
                truncationReason));
    }

    private static async Task<PackageProfileEvent> EvaluateCandidateAsync(
        IPackageSourceClient source,
        string prefix,
        PackageSearchMatch candidate,
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext)
    {
        PackageSourceCoordinate expectedCoordinate;
        try
        {
            expectedCoordinate = PackageSourceCoordinate.Create(
                candidate.Metadata.Id,
                candidate.Metadata.Version);
        }
        catch (ArgumentException)
        {
            return Failure(
                candidate,
                PackageProfileFailureKind.SearchContract,
                "The package source returned inconsistent search metadata or provenance.");
        }

        if (candidate.Candidate.Coordinate != expectedCoordinate
            || !ReferenceEquals(candidate.Candidate.Source, source.Source)
            || candidate.Candidate.DiscoveryContract
                != PackageDiscoveryContract.KeywordSearch
            || candidate.Candidate.ListingState != PackageListingState.Listed)
        {
            return Failure(
                candidate,
                PackageProfileFailureKind.SearchContract,
                "The package source returned inconsistent search metadata or provenance.");
        }

        if (!candidate.Metadata.Id.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                candidate,
                PackageProfileFailureKind.SearchContract,
                "The package source returned an item outside the requested prefix.");
        }

        var (manifestFacts, manifestFailure) = await AcquireManifestAsync(
            source,
            candidate.Candidate,
            candidate.Metadata.Id,
            candidate.Metadata.Version,
            cancellationToken,
            operationContext).ConfigureAwait(false);
        if (manifestFailure is not null)
        {
            return new PackageProfileEvent.Failure(manifestFailure);
        }

        return new PackageProfileEvent.Match(
            new PackageProfileMatch(
                candidate.Metadata.Id,
                candidate.Metadata.Version,
                [
                    .. (candidate.Metadata.Owners ?? [])
                        .Where(owner => !string.IsNullOrWhiteSpace(owner)),
                ],
                candidate.Metadata.TotalDownloads,
                candidate.Metadata.Verified,
                candidate.Candidate.Source,
                manifestFacts
                    ?? throw new InvalidOperationException(
                        "Manifest acquisition returned no facts or failure.")));
    }

    internal static async ValueTask<(
        PackageManifestFacts? Facts,
        PackageProfileFailure? Failure)> AcquireManifestAsync(
            IPackageSourceClient source,
            PackageCandidateObservation candidate,
            string packageId,
            string version,
            CancellationToken cancellationToken,
            NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate = candidate.Coordinate;
        PackageSourceOperationResult<PackageSourceManifest> result =
            await source.GetManifestAsync(
                coordinate.PackageId,
                coordinate.Version,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        if (result.Failure is { } failure)
        {
            return (null, new PackageProfileFailure(
                packageId, version, failure.Source,
                PackageProfileFailureKind.ManifestAcquisition,
                failure.Message));
        }

        PackageSourceManifest manifest = result.Value
            ?? throw new InvalidOperationException(
                "The package source manifest operation completed without a value or failure.");
        if (manifest.Coordinate != coordinate
            || !ReferenceEquals(manifest.Source, candidate.Source)
            || !ReferenceEquals(manifest.Source, source.Source))
        {
            return (null, new PackageProfileFailure(
                packageId, version, candidate.Source,
                PackageProfileFailureKind.ManifestContract,
                "The package source returned a manifest with mismatched coordinate or provenance."));
        }

        PackageManifestFactsResult facts = PackageManifestFactsQuery.Execute(
            manifest.Content.ToArray(), coordinate);
        return facts switch
        {
            PackageManifestFactsResult.Available available => (available.Value, null),
            PackageManifestFactsResult.Failed failed => (null,
                new PackageProfileFailure(
                    packageId, version, candidate.Source,
                    PackageProfileFailureKind.InvalidManifest,
                    failed.Failure.Message,
                    failed.Failure.Reason)),
            _ => throw new InvalidOperationException("Unknown manifest facts result."),
        };
    }

    private static PackageProfileEvent.Failure Failure(
        PackageSearchMatch candidate,
        PackageProfileFailureKind kind,
        string message,
        PackageManifestFailureReason? manifestFailureReason = null) =>
        new(
            new PackageProfileFailure(
                candidate.Metadata.Id,
                candidate.Metadata.Version,
                candidate.Candidate.Source,
                kind,
                message,
                manifestFailureReason));

    private static void Validate(PackagePrefixProfileRequest request)
    {
        if (!IsValidPrefix(request.Prefix))
        {
            throw new ArgumentException(
                "A package prefix must be a non-empty package-ID prefix without surrounding whitespace.",
                nameof(request));
        }

        if (request.MaximumPackages is <= 0 or > MaximumPackageLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PackagePrefixProfileRequest.MaximumPackages),
                request.MaximumPackages,
                $"The package limit must be between 1 and {MaximumPackageLimit}.");
        }
    }
}
