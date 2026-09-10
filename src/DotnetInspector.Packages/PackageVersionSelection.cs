using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// Whether version discovery may reuse current authoritative evidence or must
/// refresh every configured authority for this request.
/// </summary>
public enum PackageVersionDiscoveryFreshness
{
    NotEstablished,
    Current,
    RefreshedForRequest,
}

/// <summary>
/// The complete discovery policy required by one version-selection request.
/// </summary>
public sealed class PackageVersionDiscoveryRequirement
{
    internal PackageVersionDiscoveryRequirement(
        bool includePrerelease,
        PackageVersionDiscoveryFreshness freshness)
    {
        IncludePrerelease = includePrerelease;
        Freshness = freshness;
    }

    public bool IncludePrerelease { get; }

    public bool RequiresListedVersions => true;

    public bool RequiresEveryConfiguredAuthority => true;

    public bool RequiresCompleteCandidateSet => true;

    public PackageVersionDiscoveryFreshness Freshness { get; }
}

/// <summary>One typed address in an inclusive package-version range.</summary>
public abstract class PackageVersionRangeSelection
{
    private PackageVersionRangeSelection()
    {
    }

    public sealed class First : PackageVersionRangeSelection
    {
        public First()
        {
        }
    }

    public sealed class Last : PackageVersionRangeSelection
    {
        public Last()
        {
        }
    }

    public sealed class Ordinal : PackageVersionRangeSelection
    {
        public Ordinal(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            Value = value;
        }

        public int Value { get; }
    }

    public sealed class Exact : PackageVersionRangeSelection
    {
        public Exact(string version)
        {
            Version = PackageVersionSelectionContract.NormalizeVersion(
                version,
                nameof(version));
        }

        public string Version { get; }
    }
}

/// <summary>
/// One resource-free request for resolving a canonical package ID to an exact
/// coordinate.
/// </summary>
public abstract class PackageVersionSelectionRequest
{
    private PackageVersionSelectionRequest(
        string packageId,
        bool includePrerelease,
        PackageVersionDiscoveryFreshness freshness)
    {
        if (!PackageCoordinateResolver.IsCanonicalPackageId(packageId))
        {
            throw new ArgumentException(
                "A version-selection request requires a valid package ID.",
                nameof(packageId));
        }
        if (!Enum.IsDefined(freshness))
            throw new ArgumentOutOfRangeException(nameof(freshness));

        PackageId = packageId.ToLowerInvariant();
        Discovery = new PackageVersionDiscoveryRequirement(
            includePrerelease,
            freshness);
    }

    public string PackageId { get; }

    public PackageVersionDiscoveryRequirement Discovery { get; }

    public sealed class LatestStable : PackageVersionSelectionRequest
    {
        public LatestStable(string packageId)
            : base(
                packageId,
                includePrerelease: false,
                PackageVersionDiscoveryFreshness.Current)
        {
        }
    }

    public sealed class LatestPrerelease : PackageVersionSelectionRequest
    {
        public LatestPrerelease(string packageId)
            : base(
                packageId,
                includePrerelease: true,
                PackageVersionDiscoveryFreshness.Current)
        {
        }
    }

    public sealed class AlwaysLatest : PackageVersionSelectionRequest
    {
        public AlwaysLatest(
            string packageId,
            bool includePrerelease = false)
            : base(
                packageId,
                includePrerelease,
                PackageVersionDiscoveryFreshness.RefreshedForRequest)
        {
        }
    }

    public sealed class Wildcard : PackageVersionSelectionRequest
    {
        public Wildcard(string packageId, string versionPrefix)
            : base(
                packageId,
                includePrerelease: true,
                PackageVersionDiscoveryFreshness.Current)
        {
            VersionPrefix = PackageVersionSelectionContract
                .NormalizeVersionPrefix(
                    versionPrefix,
                    nameof(versionPrefix));
        }

        public string VersionPrefix { get; }
    }

    public sealed class Range : PackageVersionSelectionRequest
    {
        public Range(
            PackageVersionRange versionRange,
            PackageVersionRangeSelection selection,
            bool includePrerelease = false)
            : base(
                VersionRangePackageId(versionRange),
                includePrerelease || versionRange.IncludesPrerelease,
                PackageVersionDiscoveryFreshness.Current)
        {
            ArgumentNullException.ThrowIfNull(selection);
            VersionRange = versionRange;
            Selection = selection;
        }

        public PackageVersionRange VersionRange { get; }

        public PackageVersionRangeSelection Selection { get; }

        private static string VersionRangePackageId(
            PackageVersionRange versionRange)
        {
            ArgumentNullException.ThrowIfNull(versionRange);
            return versionRange.PackageId;
        }
    }
}

/// <summary>
/// Owner-issued resource-free evidence that one selection request reached one
/// exact coordinate or typed terminal outcome.
/// </summary>
public abstract class PackageVersionResolutionReceipt
{
    private PackageVersionResolutionReceipt(
        PackageVersionSelectionRequest request,
        PackageVersionDiscoveryResult discovery,
        PackageVersionDiscoveryFreshness freshness)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(discovery);
        if (!Enum.IsDefined(freshness))
            throw new ArgumentOutOfRangeException(nameof(freshness));
        Request = request;
        Discovery = discovery;
        Freshness = freshness;
    }

    public PackageVersionSelectionRequest Request { get; }

    public PackageVersionDiscoveryResult Discovery { get; }

    public PackageVersionDiscoveryFreshness Freshness { get; }

    public sealed class Resolved : PackageVersionResolutionReceipt
    {
        internal Resolved(
            PackageVersionSelectionRequest request,
            PackageVersionDiscoveryResult discovery,
            PackageVersionDiscoveryFreshness freshness)
            : base(request, discovery, freshness)
        {
            PackageVersionSelectionContract
                .RequireAuthoritativeCompatibleDiscovery(
                    request,
                    discovery,
                    freshness);
            string version =
                PackageVersionSelectionContract.SelectVersion(
                    request,
                    discovery)
                ?? throw new ArgumentException(
                    "The authoritative discovery does not contain a version satisfying the request.",
                    nameof(discovery));
            Candidate = discovery.SelectCandidate(version);
            if (!Candidate.Coordinate.PackageId.Equals(
                    request.PackageId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The selected candidate belongs to another package request.",
                    nameof(discovery));
            }
        }

        public PackageAcquisitionCandidate Candidate { get; }

        public PackageSourceCoordinate Coordinate => Candidate.Coordinate;
    }

    public sealed class NotFound : PackageVersionResolutionReceipt
    {
        internal NotFound(
            PackageVersionSelectionRequest request,
            PackageVersionDiscoveryResult discovery,
            PackageVersionDiscoveryFreshness freshness,
            InertString reason)
            : base(request, discovery, freshness)
        {
            PackageVersionSelectionContract
                .RequireAuthoritativeCompatibleDiscovery(
                    request,
                    discovery,
                    freshness);
            if (discovery.HasAnyCandidate)
            {
                throw new ArgumentException(
                    "NotFound requires authoritative evidence that no package version exists.",
                    nameof(discovery));
            }

            Reason = PackageVersionSelectionContract.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class NoMatch : PackageVersionResolutionReceipt
    {
        internal NoMatch(
            PackageVersionSelectionRequest request,
            PackageVersionDiscoveryResult discovery,
            PackageVersionDiscoveryFreshness freshness,
            InertString reason)
            : base(request, discovery, freshness)
        {
            PackageVersionSelectionContract
                .RequireAuthoritativeCompatibleDiscovery(
                    request,
                    discovery,
                    freshness);
            if (!discovery.HasAnyCandidate
                || PackageVersionSelectionContract.SelectVersion(
                    request,
                    discovery) is not null)
            {
                throw new ArgumentException(
                    "NoMatch requires authoritative package evidence with no version satisfying the request.",
                    nameof(discovery));
            }

            Reason = PackageVersionSelectionContract.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Ambiguous : PackageVersionResolutionReceipt
    {
        internal Ambiguous(
            PackageVersionSelectionRequest request,
            PackageVersionDiscoveryResult discovery,
            PackageVersionDiscoveryFreshness freshness,
            InertString reason)
            : base(request, discovery, freshness)
        {
            PackageVersionSelectionContract
                .RequireAuthoritativeCompatibleDiscovery(
                    request,
                    discovery,
                    freshness);
            Reason = PackageVersionSelectionContract.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Incomplete : PackageVersionResolutionReceipt
    {
        internal Incomplete(
            PackageVersionSelectionRequest request,
            PackageVersionDiscoveryResult discovery,
            PackageVersionDiscoveryFreshness freshness,
            InertString reason)
            : base(request, discovery, freshness)
        {
            if (discovery.State
                == PackageVersionDiscoveryState.Authoritative)
            {
                throw new ArgumentException(
                    "Incomplete requires non-authoritative discovery evidence.",
                    nameof(discovery));
            }

            Reason = PackageVersionSelectionContract.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Rejected : PackageVersionResolutionReceipt
    {
        internal Rejected(
            PackageVersionSelectionRequest request,
            PackageVersionDiscoveryResult discovery,
            PackageVersionDiscoveryFreshness freshness,
            InertString reason)
            : base(request, discovery, freshness) =>
            Reason = PackageVersionSelectionContract.RequireReason(reason);

        public InertString Reason { get; }
    }

    public sealed class Unavailable : PackageVersionResolutionReceipt
    {
        internal Unavailable(
            PackageVersionSelectionRequest request,
            PackageVersionDiscoveryResult discovery,
            PackageVersionDiscoveryFreshness freshness,
            InertString reason)
            : base(request, discovery, freshness)
        {
            PackageVersionSelectionContract
                .RequireFailedDiscovery(discovery);
            Reason = PackageVersionSelectionContract.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Failed : PackageVersionResolutionReceipt
    {
        internal Failed(
            PackageVersionSelectionRequest request,
            PackageVersionDiscoveryResult discovery,
            PackageVersionDiscoveryFreshness freshness,
            InertString reason)
            : base(request, discovery, freshness)
        {
            PackageVersionSelectionContract
                .RequireFailedDiscovery(discovery);
            Reason = PackageVersionSelectionContract.RequireReason(reason);
        }

        public InertString Reason { get; }
    }
}

internal static class PackageVersionSelectionContract
{
    private static readonly IVersionComparer VersionComparer =
        NuGet.Versioning.VersionComparer.VersionReleaseMetadata;

    internal static string NormalizeVersion(
        string version,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(version)
            || !string.Equals(
                version,
                version.Trim(),
                StringComparison.Ordinal)
            || version.Contains('+', StringComparison.Ordinal)
            || !NuGetVersion.TryParse(version, out NuGetVersion? parsed))
        {
            throw new ArgumentException(
                "A range address version must be one exact NuGet version without build metadata or surrounding whitespace.",
                parameterName);
        }

        return parsed.ToNormalizedString().ToLowerInvariant();
    }

    internal static string NormalizeVersionPrefix(
        string prefix,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length > 256
            || prefix.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '-')))
        {
            throw new ArgumentException(
                "A version prefix must contain only ASCII letters, digits, '.' and '-'.",
                parameterName);
        }

        return prefix.ToLowerInvariant();
    }

    internal static void RequireAuthoritativeCompatibleDiscovery(
        PackageVersionSelectionRequest request,
        PackageVersionDiscoveryResult discovery,
        PackageVersionDiscoveryFreshness freshness)
    {
        if (discovery.State
            != PackageVersionDiscoveryState.Authoritative)
        {
            throw new ArgumentException(
                "Version selection requires authoritative discovery from every configured authority.",
                nameof(discovery));
        }
        if (freshness
                == PackageVersionDiscoveryFreshness.NotEstablished
            || request.Discovery.Freshness
                == PackageVersionDiscoveryFreshness.RefreshedForRequest
                && freshness
                    != PackageVersionDiscoveryFreshness.RefreshedForRequest)
        {
            throw new ArgumentException(
                "The discovery freshness does not satisfy the version-selection request.",
                nameof(freshness));
        }

        PackageVersionDiscoveryContract contract = discovery.Contract;
        if (contract.IncludeUnlisted
            || contract.Limit is not null
            || request.Discovery.IncludePrerelease
                && !contract.IncludePrerelease)
        {
            throw new ArgumentException(
                "The discovery contract does not satisfy the version-selection request.",
                nameof(discovery));
        }
    }

    internal static void RequireFailedDiscovery(
        PackageVersionDiscoveryResult discovery)
    {
        if (discovery.State != PackageVersionDiscoveryState.Failed
            || discovery.Failures.Count == 0)
        {
            throw new ArgumentException(
                "This terminal outcome requires failed discovery with typed failure evidence.",
                nameof(discovery));
        }
    }

    internal static string? SelectVersion(
        PackageVersionSelectionRequest request,
        PackageVersionDiscoveryResult discovery)
    {
        IEnumerable<NuGetVersion> versions = discovery.Versions
            .Select(NuGetVersion.Parse);

        return request switch
        {
            PackageVersionSelectionRequest.LatestStable =>
                SelectLatest(
                    versions.Where(version => !version.IsPrerelease)),
            PackageVersionSelectionRequest.LatestPrerelease =>
                SelectLatest(versions),
            PackageVersionSelectionRequest.AlwaysLatest always =>
                SelectLatest(always.Discovery.IncludePrerelease
                    ? versions
                    : versions.Where(version => !version.IsPrerelease)),
            PackageVersionSelectionRequest.Wildcard wildcard =>
                SelectLatest(versions.Where(version =>
                    version.ToNormalizedString().StartsWith(
                        wildcard.VersionPrefix,
                        StringComparison.OrdinalIgnoreCase))),
            PackageVersionSelectionRequest.Range range =>
                SelectRange(range, discovery.Versions),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
    }

    internal static InertString RequireReason(InertString reason)
    {
        if (reason.IsEmpty)
        {
            throw new ArgumentException(
                "A version-resolution non-success requires a visible reason.",
                nameof(reason));
        }

        return reason;
    }

    private static string? SelectLatest(
        IEnumerable<NuGetVersion> versions) =>
        versions
            .OrderByDescending(version => version, VersionComparer)
            .FirstOrDefault()
            ?.ToNormalizedString()
            .ToLowerInvariant();

    private static string? SelectRange(
        PackageVersionSelectionRequest.Range request,
        IReadOnlyList<string> versions)
    {
        var parsedVersions = new List<NuGetVersion>(versions.Count);
        foreach (string version in versions)
        {
            if (!NuGetVersion.TryParse(version, out NuGetVersion? parsed))
            {
                throw new ArgumentException(
                    $"Discovered version '{version}' is not a valid NuGet version.",
                    nameof(versions));
            }
            parsedVersions.Add(parsed);
        }

        if (!ContainsVersion(
                parsedVersions,
                request.VersionRange.Start)
            || !ContainsVersion(
                parsedVersions,
                request.VersionRange.End))
        {
            return null;
        }

        PackageVersionVector vector = PackageVersionVector.Create(
            request.VersionRange,
            versions,
            request.Discovery.IncludePrerelease);
        PackageVersionAddress? address = request.Selection switch
        {
            PackageVersionRangeSelection.First =>
                vector.Addresses.FirstOrDefault(),
            PackageVersionRangeSelection.Last =>
                vector.Addresses.LastOrDefault(),
            PackageVersionRangeSelection.Ordinal ordinal
                when ordinal.Value <= vector.Addresses.Length =>
                vector.Addresses[ordinal.Value - 1],
            PackageVersionRangeSelection.Exact exact =>
                vector.Addresses.FirstOrDefault(candidate =>
                    candidate.Version.ToNormalizedString().Equals(
                        exact.Version,
                        StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };

        return address?.Version.ToNormalizedString().ToLowerInvariant();

        static bool ContainsVersion(
            IEnumerable<NuGetVersion> candidates,
            NuGetVersion expected) =>
            candidates.Any(candidate =>
                VersionComparer.Equals(candidate, expected));
    }
}
