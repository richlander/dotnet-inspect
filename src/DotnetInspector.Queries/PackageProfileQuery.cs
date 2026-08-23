using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using DotnetInspector.Services;
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
/// One package whose latest listed manifest was projected by a package profile.
/// </summary>
public sealed record PackageProfileMatch(
    string PackageId,
    string Version,
    string? Authors,
    ImmutableArray<string> Owners,
    long TotalDownloads,
    bool Verified,
    PackageSourceIdentity Producer,
    PackageDependencyGroups DependencyGroups);

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
    PackageSourceIdentity Producer,
    PackageProfileFailureKind Kind,
    string Message);

/// <summary>
/// Terminal accounting for a completed package-profile stream.
/// </summary>
public sealed record PackageProfileSummary(
    string Prefix,
    PackageSourceIdentity Producer,
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

    public static InspectionQuery<IAsyncEnumerable<PackageProfileEvent>>
        Definition { get; } =
            new("Package profile", InspectionCost.Unbounded);

    public static async IAsyncEnumerable<PackageProfileEvent> ExecuteAsync(
        IPackageSourceClient source,
        PackagePrefixProfileRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        PackageSourceOperationResult<PackageSearchResult> search =
            await source.SearchByPrefixAsync(
                request.Prefix,
                request.MaximumPackages,
                request.IncludePrerelease,
                cancellationToken).ConfigureAwait(false);
        if (search is PackageSourceOperationResult<PackageSearchResult>.Failed
            searchFailure)
        {
            yield return new PackageProfileEvent.Failure(
                new PackageProfileFailure(
                    PackageId: null,
                    Version: null,
                    searchFailure.Failure.Producer,
                    PackageProfileFailureKind.Search,
                    searchFailure.Failure.Message));
            yield return new PackageProfileEvent.Completed(
                new PackageProfileSummary(
                    request.Prefix,
                    source.Identity,
                    Candidates: 0,
                    Matches: 0,
                    Failures: 1,
                    PackageSearchTruncationReason.None));
            yield break;
        }

        PackageSearchResult searchResult =
            ((PackageSourceOperationResult<PackageSearchResult>.Succeeded)search)
            .Value;
        int candidates = 0;
        int matches = 0;
        int failures = 0;
        foreach (PackageSearchMatch candidate in searchResult.Matches)
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
                || candidate.Candidate.Producer != source.Identity
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
                    cancellationToken).ConfigureAwait(false);
            if (manifestResult
                is PackageSourceOperationResult<PackageSourceManifest>.Failed
                    manifestFailure)
            {
                failures++;
                yield return new PackageProfileEvent.Failure(
                    new PackageProfileFailure(
                        candidate.Metadata.Id,
                        candidate.Metadata.Version,
                        manifestFailure.Failure.Producer,
                        PackageProfileFailureKind.ManifestAcquisition,
                        manifestFailure.Failure.Message));
                continue;
            }

            PackageSourceManifest manifest =
                ((PackageSourceOperationResult<PackageSourceManifest>.Succeeded)
                    manifestResult).Value;
            if (manifest.Coordinate != coordinate
                || manifest.Producer != candidate.Candidate.Producer
                || manifest.Producer != source.Identity
                || manifest.TransportKind != source.Kind)
            {
                failures++;
                yield return Failure(
                    candidate,
                    PackageProfileFailureKind.ManifestContract,
                    "The package source returned a manifest with mismatched coordinate or provenance.");
                continue;
            }

            if (manifest.Content.Length
                > PackageDependencyGroupsQuery.MaxManifestBytes)
            {
                failures++;
                yield return Failure(
                    candidate,
                    PackageProfileFailureKind.InvalidManifest,
                    "The package manifest exceeded the package-profile byte limit.");
                continue;
            }

            PackageProfileEvent projected;
            try
            {
                NuspecData nuspec = PackageManifestProjection.ParseAndValidate(
                    manifest.Content,
                    candidate.Metadata.Id,
                    candidate.Metadata.Version);
                PackageDependencyGroups dependencyGroups =
                    PackageManifestProjection.ProjectDependencyGroups(
                        nuspec,
                        requestedTargetFramework: null);
                projected = new PackageProfileEvent.Match(
                    new PackageProfileMatch(
                        candidate.Metadata.Id,
                        candidate.Metadata.Version,
                        nuspec.Authors,
                        [
                            .. (candidate.Metadata.Owners ?? [])
                                .Where(owner =>
                                    !string.IsNullOrWhiteSpace(owner)),
                        ],
                        candidate.Metadata.TotalDownloads,
                        candidate.Metadata.Verified,
                        candidate.Candidate.Producer,
                        dependencyGroups));
            }
            catch (Exception exception) when (
                exception is InvalidDataException
                    or NuspecParseException)
            {
                projected = Failure(
                    candidate,
                    PackageProfileFailureKind.InvalidManifest,
                    exception.Message);
            }

            if (projected is PackageProfileEvent.Match)
                matches++;
            else
                failures++;
            yield return projected;
        }

        yield return new PackageProfileEvent.Completed(
            new PackageProfileSummary(
                request.Prefix,
                source.Identity,
                candidates,
                matches,
                failures,
                searchResult.TruncationReason));
    }

    private static PackageProfileEvent.Failure Failure(
        PackageSearchMatch candidate,
        PackageProfileFailureKind kind,
        string message) =>
        new(
            new PackageProfileFailure(
                candidate.Metadata.Id,
                candidate.Metadata.Version,
                candidate.Candidate.Producer,
                kind,
                message));

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
