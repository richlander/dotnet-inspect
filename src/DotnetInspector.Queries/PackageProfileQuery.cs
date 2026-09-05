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

        PackageSourceOperationResult<PackageSearchResult> search =
            await source.SearchByPrefixAsync(
                request.Prefix,
                request.MaximumPackages,
                request.IncludePrerelease,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        if (search.Failure is { } searchFailure)
        {
            yield return new PackageProfileEvent.Failure(
                new PackageProfileFailure(
                    PackageId: null,
                    Version: null,
                    searchFailure.Source,
                    PackageProfileFailureKind.Search,
                    searchFailure.Message));
            yield return new PackageProfileEvent.Completed(
                new PackageProfileSummary(
                    request.Prefix,
                    source.Source,
                    Candidates: 0,
                    Matches: 0,
                    Failures: 1,
                    PackageSearchTruncationReason.None));
            yield break;
        }

        PackageSearchResult searchResult =
            search.Value
            ?? throw new InvalidOperationException(
                "The package source search completed without a value or failure.");
        PackageSearchMatch[] searchMatches =
        [
            .. searchResult.Matches.Take(request.MaximumPackages + 1),
        ];
        if (searchResult.Matches.Count > request.MaximumPackages
            || searchMatches.Length > request.MaximumPackages
            || searchMatches.Length != searchResult.Matches.Count)
        {
            yield return new PackageProfileEvent.Failure(
                new PackageProfileFailure(
                    PackageId: null,
                    Version: null,
                    source.Source,
                    PackageProfileFailureKind.SearchContract,
                    "The package source returned more matches than requested."));
            yield return new PackageProfileEvent.Completed(
                new PackageProfileSummary(
                    request.Prefix,
                    source.Source,
                    Candidates: 0,
                    Matches: 0,
                    Failures: 1,
                    PackageSearchTruncationReason.None));
            yield break;
        }

        int candidates = 0;
        int matches = 0;
        int failures = 0;
        foreach (PackageSearchMatch candidate in searchMatches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidates++;
            PackageSourceCoordinate? expectedCoordinate = null;
            bool metadataIsValid = true;
            try
            {
                expectedCoordinate = PackageSourceCoordinate.Create(
                    candidate.Metadata.Id,
                    candidate.Metadata.Version);
            }
            catch (ArgumentException)
            {
                metadataIsValid = false;
            }

            if (!metadataIsValid
                || expectedCoordinate is null
                || candidate.Candidate.Coordinate != expectedCoordinate
                || !ReferenceEquals(
                    candidate.Candidate.Source,
                    source.Source)
                || candidate.Candidate.DiscoveryContract
                    != PackageDiscoveryContract.KeywordSearch
                || candidate.Candidate.ListingState
                    != PackageListingState.Listed)
            {
                failures++;
                yield return Failure(
                    candidate,
                    PackageProfileFailureKind.SearchContract,
                    "The package source returned inconsistent search metadata or provenance.");
                continue;
            }

            if (!candidate.Metadata.Id.StartsWith(
                    request.Prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                failures++;
                yield return Failure(
                    candidate,
                    PackageProfileFailureKind.SearchContract,
                    "The package source returned an item outside the requested prefix.");
                continue;
            }

            PackageSourceCoordinate coordinate = expectedCoordinate;
            PackageSourceOperationResult<PackageSourceManifest> manifestResult =
                await source.GetManifestAsync(
                    coordinate.PackageId,
                    coordinate.Version,
                    cancellationToken,
                    operationContext).ConfigureAwait(false);
            if (manifestResult.Failure is { } manifestFailure)
            {
                failures++;
                yield return new PackageProfileEvent.Failure(
                    new PackageProfileFailure(
                        candidate.Metadata.Id,
                        candidate.Metadata.Version,
                        manifestFailure.Source,
                        PackageProfileFailureKind.ManifestAcquisition,
                        manifestFailure.Message));
                continue;
            }

            PackageSourceManifest manifest =
                manifestResult.Value
                ?? throw new InvalidOperationException(
                    "The package source manifest operation completed without a value or failure.");
            if (manifest.Coordinate != coordinate
                || !ReferenceEquals(
                    manifest.Source,
                    candidate.Candidate.Source)
                || !ReferenceEquals(manifest.Source, source.Source))
            {
                failures++;
                yield return Failure(
                    candidate,
                    PackageProfileFailureKind.ManifestContract,
                    "The package source returned a manifest with mismatched coordinate or provenance.");
                continue;
            }

            PackageManifestFactsResult manifestFacts =
                PackageManifestFactsQuery.Execute(
                    manifest.Content.ToArray(),
                    coordinate);
            if (manifestFacts is PackageManifestFactsResult.Failed
                manifestFailureResult)
            {
                failures++;
                yield return Failure(
                    candidate,
                    PackageProfileFailureKind.InvalidManifest,
                    manifestFailureResult.Failure.Message,
                    manifestFailureResult.Failure.Reason);
                continue;
            }

            matches++;
            yield return new PackageProfileEvent.Match(
                new PackageProfileMatch(
                    candidate.Metadata.Id,
                    candidate.Metadata.Version,
                    [
                        .. (candidate.Metadata.Owners ?? [])
                            .Where(owner =>
                                !string.IsNullOrWhiteSpace(owner)),
                    ],
                    candidate.Metadata.TotalDownloads,
                    candidate.Metadata.Verified,
                    candidate.Candidate.Source,
                    ((PackageManifestFactsResult.Available)manifestFacts)
                        .Value));
        }

        yield return new PackageProfileEvent.Completed(
            new PackageProfileSummary(
                request.Prefix,
                source.Source,
                candidates,
                matches,
                failures,
                searchResult.TruncationReason));
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
