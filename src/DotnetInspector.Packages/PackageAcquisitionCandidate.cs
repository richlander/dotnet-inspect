using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>How one exact package acquisition candidate was established.</summary>
public enum PackageAcquisitionCandidateKind
{
    CallerPinned,
    Discovered,
}

/// <summary>The completion state of exact candidate authorization.</summary>
public enum PackageAcquisitionCandidateResultState
{
    Resolved,
    Denied,
    Incomplete,
}

/// <summary>
/// The complete version-discovery options that determined one candidate set.
/// </summary>
public sealed record PackageVersionDiscoveryContract
{
    private PackageVersionDiscoveryContract(
        int contractVersion,
        bool includePrerelease,
        bool includeUnlisted,
        int? limit)
    {
        ContractVersion = contractVersion;
        IncludePrerelease = includePrerelease;
        IncludeUnlisted = includeUnlisted;
        Limit = limit;
    }

    public int ContractVersion { get; }
    public bool IncludePrerelease { get; }
    public bool IncludeUnlisted { get; }
    public int? Limit { get; }

    /// <summary>
    /// The complete candidate set required before applying one NuGet
    /// dependency version constraint.
    /// </summary>
    public static PackageVersionDiscoveryContract DependencyRangeResolution
        { get; } = new(
            contractVersion: 1,
            includePrerelease: true,
            includeUnlisted: false,
            limit: null);

    public bool SupportsDependencyRangeResolution =>
        Equals(DependencyRangeResolution);

    internal static PackageVersionDiscoveryContract Create(
        bool includePrerelease,
        bool includeUnlisted,
        int? limit) =>
        new(
            contractVersion: 1,
            includePrerelease,
            includeUnlisted,
            limit);

    internal static PackageVersionDiscoveryContract Unspecified { get; } =
        new(
            contractVersion: 0,
            includePrerelease: false,
            includeUnlisted: false,
            limit: 0);
}

/// <summary>
/// One configured authority admitted to acquire an exact candidate.
/// </summary>
public sealed class PackageAcquisitionAuthorityEvidence
{
    internal PackageAcquisitionAuthorityEvidence(
        ConfiguredPackageAuthority authority,
        PackageCandidateObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (observation is not null
            && !ReferenceEquals(
                observation.Source.Association,
                authority.Association))
        {
            throw new InvalidOperationException(
                "The package candidate observation belongs to another configured authority.");
        }

        Authority = authority;
        Observation = observation;
    }

    public ConfiguredPackageAuthority Authority { get; }

    /// <summary>
    /// The source observation that reported a discovered coordinate, or
    /// <see langword="null"/> for a caller-pinned coordinate.
    /// </summary>
    public PackageCandidateObservation? Observation { get; }
}

/// <summary>
/// Opaque package-owned equality for exact candidate correspondence.
/// </summary>
public sealed class PackageAcquisitionCandidateCorrespondence :
    IEquatable<PackageAcquisitionCandidateCorrespondence>
{
    private readonly object _issuer;
    private readonly ConfiguredPackageAuthority[] _authorities;

    internal PackageAcquisitionCandidateCorrespondence(
        object issuer,
        PackageSourceCoordinate coordinate,
        PackageAcquisitionCandidateKind kind,
        PackageVersionDiscoveryContract? discoveryContract,
        IEnumerable<ConfiguredPackageAuthority> authorities)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(authorities);
        _issuer = issuer;
        Coordinate = coordinate;
        Kind = kind;
        DiscoveryContract = discoveryContract;
        _authorities = [.. authorities];
    }

    private PackageSourceCoordinate Coordinate { get; }
    private PackageAcquisitionCandidateKind Kind { get; }
    private PackageVersionDiscoveryContract? DiscoveryContract { get; }

    public bool Equals(PackageAcquisitionCandidateCorrespondence? other)
    {
        if (other is null
            || !ReferenceEquals(_issuer, other._issuer)
            || Coordinate != other.Coordinate
            || Kind != other.Kind
            || DiscoveryContract != other.DiscoveryContract
            || _authorities.Length != other._authorities.Length)
        {
            return false;
        }

        var expected = new HashSet<ConfiguredPackageAuthority>(
            _authorities,
            ReferenceEqualityComparer.Instance);
        return expected.SetEquals(other._authorities);
    }

    public override bool Equals(object? obj) =>
        obj is PackageAcquisitionCandidateCorrespondence other
        && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RuntimeHelpers.GetHashCode(_issuer));
        hash.Add(Coordinate);
        hash.Add(Kind);
        hash.Add(DiscoveryContract);
        int authorityHash = 0;
        foreach (ConfiguredPackageAuthority authority in _authorities)
            authorityHash ^= RuntimeHelpers.GetHashCode(authority);
        hash.Add(authorityHash);
        return hash.ToHashCode();
    }

    public static bool operator ==(
        PackageAcquisitionCandidateCorrespondence? left,
        PackageAcquisitionCandidateCorrespondence? right) =>
        EqualityComparer<PackageAcquisitionCandidateCorrespondence>.Default
            .Equals(left, right);

    public static bool operator !=(
        PackageAcquisitionCandidateCorrespondence? left,
        PackageAcquisitionCandidateCorrespondence? right) =>
        !(left == right);

    public override string ToString() =>
        nameof(PackageAcquisitionCandidateCorrespondence);
}

/// <summary>
/// One exact, source-authorized and resource-free package acquisition
/// candidate.
/// </summary>
public sealed class PackageAcquisitionCandidate
{
    private readonly object _issuer;

    private PackageAcquisitionCandidate(
        object issuer,
        PackageSourceCoordinate coordinate,
        PackageAcquisitionCandidateKind kind,
        PackageVersionDiscoveryContract? discoveryContract,
        IReadOnlyList<PackageAcquisitionAuthorityEvidence> authorities)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(authorities);
        if (authorities.Count == 0)
        {
            throw new ArgumentException(
                "An acquisition candidate requires at least one authority.",
                nameof(authorities));
        }

        bool expectsObservations =
            kind == PackageAcquisitionCandidateKind.Discovered;
        var seen = new HashSet<ConfiguredPackageAuthority>(
            ReferenceEqualityComparer.Instance);
        foreach (PackageAcquisitionAuthorityEvidence evidence in authorities)
        {
            if (!seen.Add(evidence.Authority))
            {
                throw new ArgumentException(
                    "An acquisition candidate cannot repeat an authority.",
                    nameof(authorities));
            }
            if ((evidence.Observation is not null) != expectsObservations)
            {
                throw new ArgumentException(
                    "Pinned candidates carry no observations and discovered candidates require one observation per authority.",
                    nameof(authorities));
            }
            if (evidence.Observation is { } observation
                && observation.Coordinate != coordinate)
            {
                throw new ArgumentException(
                    "Every reporting observation must name the candidate coordinate.",
                    nameof(authorities));
            }
        }

        if ((discoveryContract is not null) != expectsObservations)
        {
            throw new ArgumentException(
                "Only a discovered candidate carries a discovery contract.",
                nameof(discoveryContract));
        }

        _issuer = issuer;
        Coordinate = coordinate;
        Kind = kind;
        DiscoveryContract = discoveryContract;
        Authorities =
            new ReadOnlyCollection<PackageAcquisitionAuthorityEvidence>(
                [.. authorities]);
        Correspondence = new PackageAcquisitionCandidateCorrespondence(
            issuer,
            coordinate,
            kind,
            discoveryContract,
            authorities.Select(evidence => evidence.Authority));
    }

    public PackageSourceCoordinate Coordinate { get; }
    public PackageAcquisitionCandidateKind Kind { get; }
    public PackageVersionDiscoveryContract? DiscoveryContract { get; }
    public IReadOnlyList<PackageAcquisitionAuthorityEvidence> Authorities
        { get; }
    public PackageAcquisitionCandidateCorrespondence Correspondence { get; }

    internal static PackageAcquisitionCandidate CreatePinned(
        object issuer,
        PackageSourceCoordinate coordinate,
        IReadOnlyList<ConfiguredPackageAuthority> authorities) =>
        new(
            issuer,
            coordinate,
            PackageAcquisitionCandidateKind.CallerPinned,
            discoveryContract: null,
            [
                .. authorities.Select(authority =>
                    new PackageAcquisitionAuthorityEvidence(
                        authority,
                        observation: null)),
            ]);

    internal static PackageAcquisitionCandidate CreateDiscovered(
        object issuer,
        PackageSourceCoordinate coordinate,
        PackageVersionDiscoveryContract discoveryContract,
        IReadOnlyList<ConfiguredPackageCandidateObservation> observations) =>
        new(
            issuer,
            coordinate,
            PackageAcquisitionCandidateKind.Discovered,
            discoveryContract,
            [
                .. observations.Select(observation =>
                    new PackageAcquisitionAuthorityEvidence(
                        observation.Authority,
                        observation.Observation)),
            ]);

    internal bool HasIssuer(object issuer) =>
        ReferenceEquals(_issuer, issuer);
}

/// <summary>
/// One caller-pinned exact candidate or typed authority diagnostics.
/// </summary>
public sealed class PackageAcquisitionCandidateResult
{
    internal PackageAcquisitionCandidateResult(
        PackageAcquisitionCandidateResultState state,
        PackageAcquisitionCandidate? candidate,
        IReadOnlyList<PackageAuthorityFailure> failures)
    {
        if ((state == PackageAcquisitionCandidateResultState.Resolved)
            != (candidate is not null))
        {
            throw new ArgumentException(
                "Only a resolved candidate result carries a candidate.",
                nameof(candidate));
        }

        State = state;
        Candidate = candidate;
        Failures = new ReadOnlyCollection<PackageAuthorityFailure>(
            [.. failures]);
    }

    public PackageAcquisitionCandidateResultState State { get; }
    public PackageAcquisitionCandidate? Candidate { get; }
    public IReadOnlyList<PackageAuthorityFailure> Failures { get; }
}
