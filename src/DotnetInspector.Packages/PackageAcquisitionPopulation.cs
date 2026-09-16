using System.Collections.Immutable;

namespace DotnetInspector.Packages;

/// <summary>How package-source population selection stopped.</summary>
public enum PackageAcquisitionPopulationCompletionKind
{
    ExactCoordinates,
    ExactPackageComplete,
    PrefixExhausted,
    CandidateLimitReached,
    SourcePageLimitReached,
    ClientPageLimitReached,
    SourceFailed,
}

/// <summary>
/// One population-scoped failure, correlated with the requested or selected
/// package when that identity is known.
/// </summary>
public sealed class PackageAcquisitionPopulationFailure
{
    internal PackageAcquisitionPopulationFailure(
        int? candidateOrdinal,
        string? packageId,
        NuGetFetch.PackageSourceCoordinate? coordinate,
        PackageAuthorityFailure failure)
    {
        if (candidateOrdinal <= 0)
            throw new ArgumentOutOfRangeException(nameof(candidateOrdinal));
        ArgumentNullException.ThrowIfNull(failure);
        if (candidateOrdinal is null
            && (packageId is not null || coordinate is not null))
        {
            throw new ArgumentException(
                "A source-wide population failure cannot name one candidate.",
                nameof(candidateOrdinal));
        }
        if (candidateOrdinal is not null
            && packageId is null
            && coordinate is null)
        {
            throw new ArgumentException(
                "A candidate population failure requires package identity.",
                nameof(packageId));
        }
        if (coordinate is not null
            && packageId is not null
            && !coordinate.PackageId.Equals(
                packageId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A population failure's coordinate and package ID must agree.",
                nameof(packageId));
        }

        CandidateOrdinal = candidateOrdinal;
        PackageId = coordinate?.PackageId ?? packageId;
        Coordinate = coordinate;
        Failure = failure;
    }

    internal static PackageAcquisitionPopulationFailure ForSource(
        PackageAuthorityFailure failure) =>
        new(
            candidateOrdinal: null,
            packageId: null,
            coordinate: null,
            failure);

    internal static PackageAcquisitionPopulationFailure ForCandidate(
        int candidateOrdinal,
        string packageId,
        NuGetFetch.PackageSourceCoordinate? coordinate,
        PackageAuthorityFailure failure) =>
        new(
            candidateOrdinal,
            packageId,
            coordinate,
            failure);

    /// <summary>
    /// One-based position in the exact input or selected prefix package-ID
    /// sequence, or <see langword="null"/> for a source-wide failure.
    /// </summary>
    public int? CandidateOrdinal { get; }

    public string? PackageId { get; }

    public NuGetFetch.PackageSourceCoordinate? Coordinate { get; }

    public PackageAuthorityFailure Failure { get; }
}

/// <summary>
/// One frozen, resource-free population of authority-bearing package
/// acquisition candidates.
/// </summary>
public sealed class PackageAcquisitionPopulation
{
    public const int MaximumCandidates = 5;

    internal PackageAcquisitionPopulation(
        int requestedCandidates,
        IEnumerable<PackageAcquisitionCandidate> candidates,
        IEnumerable<PackageAcquisitionPopulationFailure> failures,
        PackageAcquisitionPopulationCompletionKind completion)
    {
        if (requestedCandidates is < 1 or > MaximumCandidates)
            throw new ArgumentOutOfRangeException(nameof(requestedCandidates));
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(failures);
        if (!Enum.IsDefined(completion))
            throw new ArgumentOutOfRangeException(nameof(completion));

        Candidates = [.. candidates];
        Failures = [.. failures];
        if (Candidates.Length > requestedCandidates)
        {
            throw new ArgumentException(
                "A package population cannot exceed its requested candidate count.",
                nameof(candidates));
        }

        var coordinates = new HashSet<NuGetFetch.PackageSourceCoordinate>();
        foreach (PackageAcquisitionCandidate candidate in Candidates)
        {
            if (!coordinates.Add(candidate.Coordinate))
            {
                throw new ArgumentException(
                    "A package population cannot repeat a package coordinate.",
                    nameof(candidates));
            }
        }
        foreach (PackageAcquisitionPopulationFailure failure in Failures)
        {
            if (failure.CandidateOrdinal > requestedCandidates)
            {
                throw new ArgumentException(
                    "A population failure cannot name a position beyond the requested candidate count.",
                    nameof(failures));
            }
        }

        RequestedCandidates = requestedCandidates;
        Completion = completion;
    }

    public int RequestedCandidates { get; }
    public ImmutableArray<PackageAcquisitionCandidate> Candidates { get; }
    public ImmutableArray<PackageAcquisitionPopulationFailure> Failures
        { get; }
    public PackageAcquisitionPopulationCompletionKind Completion { get; }

    /// <summary>
    /// Whether the requested bounded population was formed without source or
    /// selection incompleteness. Candidate-limit completion does not imply
    /// that the wider prefix was exhausted.
    /// </summary>
    public bool IsRequestedPopulationComplete =>
        Failures.IsEmpty
        && Completion is (
            PackageAcquisitionPopulationCompletionKind.ExactCoordinates
            or PackageAcquisitionPopulationCompletionKind.ExactPackageComplete
            or PackageAcquisitionPopulationCompletionKind.PrefixExhausted
            or PackageAcquisitionPopulationCompletionKind.CandidateLimitReached)
        && (Completion is
                PackageAcquisitionPopulationCompletionKind.ExactPackageComplete
                or PackageAcquisitionPopulationCompletionKind.PrefixExhausted
            || Candidates.Length == RequestedCandidates);
}
