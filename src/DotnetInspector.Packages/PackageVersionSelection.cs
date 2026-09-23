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

    /// <summary>
    /// A retained prior settlement was served after a refresh could not
    /// complete. Only <see cref="PackageVersionResolutionReceipt.Prior"/> may
    /// carry this value; discovery-backed arms reject it.
    /// </summary>
    ServedPrior,
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
        PackageVersionDiscoveryFreshness freshness)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(freshness))
            throw new ArgumentOutOfRangeException(nameof(freshness));
        Request = request;
        Freshness = freshness;
    }

    public PackageVersionSelectionRequest Request { get; }

    public PackageVersionDiscoveryFreshness Freshness { get; }

    /// <summary>
    /// The discovery-backed arms. Every one retains the complete discovery
    /// result its outcome was computed from; <see cref="Prior"/> is the one
    /// arm that retains none, so consumers needing discovery evidence match
    /// on this type rather than on the receipt base.
    /// </summary>
    public abstract class Discovered : PackageVersionResolutionReceipt
    {
        private protected Discovered(
            PackageVersionSelectionRequest request,
            PackageVersionDiscoveryResult discovery,
            PackageVersionDiscoveryFreshness freshness)
            : base(request, freshness)
        {
            ArgumentNullException.ThrowIfNull(discovery);
            Discovery = discovery;
        }

        public PackageVersionDiscoveryResult Discovery { get; }
    }

    /// <summary>
    /// A retained prior settlement served as an exact coordinate without
    /// discovery. The candidate is the current source generation's pinned
    /// candidate for that coordinate. Freshness is <c>Current</c> when the
    /// entry was inside its window, or <c>ServedPrior</c> with the entry's
    /// age when a refresh could not complete.
    /// </summary>
    public sealed class Prior : PackageVersionResolutionReceipt
    {
        internal Prior(
            PackageVersionSelectionRequest request,
            PackageAcquisitionCandidate candidate,
            PackageVersionDiscoveryFreshness freshness,
            TimeSpan? age)
            : base(request, freshness)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (freshness is not (PackageVersionDiscoveryFreshness.Current
                or PackageVersionDiscoveryFreshness.ServedPrior))
            {
                throw new ArgumentException(
                    "A prior settlement is Current or ServedPrior; it is never refreshed for the request.",
                    nameof(freshness));
            }
            if ((freshness == PackageVersionDiscoveryFreshness.ServedPrior)
                != age.HasValue)
            {
                throw new ArgumentException(
                    "ServedPrior carries the entry's age; Current carries none.",
                    nameof(age));
            }
            if (age is { } value && value < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(age));
            if (candidate.Kind != PackageAcquisitionCandidateKind.CallerPinned)
            {
                throw new ArgumentException(
                    "A prior settlement is served through the pinned-candidate path.",
                    nameof(candidate));
            }
            if (!candidate.Coordinate.PackageId.Equals(
                    request.PackageId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The prior candidate belongs to another package request.",
                    nameof(candidate));
            }

            Candidate = candidate;
            Age = age;
        }

        public PackageAcquisitionCandidate Candidate { get; }

        public PackageSourceCoordinate Coordinate => Candidate.Coordinate;

        /// <summary>The entry's age when served as <c>ServedPrior</c>.</summary>
        public TimeSpan? Age { get; }
    }

    public sealed class Resolved : Discovered
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

    public sealed class NotFound : Discovered
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

    public sealed class NoMatch : Discovered
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

    public sealed class Ambiguous : Discovered
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

    public sealed class Incomplete : Discovered
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

    public sealed class Rejected : Discovered
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

    public sealed class Unavailable : Discovered
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

    public sealed class Failed : Discovered
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

/// <summary>
/// Resolves one typed package version-selection request from retained
/// configured-authority discovery evidence.
/// </summary>
public static class PackageVersionSelectionResolver
{
    /// <summary>
    /// Resolves one request from the supplied discovery evidence without
    /// performing source I/O.
    /// </summary>
    public static PackageVersionResolutionReceipt Resolve(
        PackageVersionSelectionRequest request,
        PackageVersionDiscoveryResult discovery,
        PackageVersionDiscoveryFreshness freshness)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(discovery);
        if (!Enum.IsDefined(freshness))
            throw new ArgumentOutOfRangeException(nameof(freshness));

        if (PackageVersionSelectionContract
                .GetPackageIdentityIncompatibility(
                    request,
                    discovery) is { } identityIncompatibility)
        {
            return new PackageVersionResolutionReceipt.Rejected(
                request,
                discovery,
                freshness,
                identityIncompatibility);
        }

        if (discovery.State
            == PackageVersionDiscoveryState.Authoritative)
        {
            if (PackageVersionSelectionContract
                    .GetDiscoveryIncompatibility(
                        request,
                        discovery,
                        freshness) is { } incompatibility)
            {
                return new PackageVersionResolutionReceipt.Rejected(
                    request,
                    discovery,
                    freshness,
                    incompatibility);
            }

            if (!discovery.HasAnyCandidate)
            {
                return new PackageVersionResolutionReceipt.NotFound(
                    request,
                    discovery,
                    freshness,
                    Reason(
                        "No configured authority reported the package."));
            }

            return PackageVersionSelectionContract.SelectVersion(
                    request,
                    discovery) is null
                ? new PackageVersionResolutionReceipt.NoMatch(
                    request,
                    discovery,
                    freshness,
                    Reason(
                        "No listed version satisfies the version-selection request."))
                : new PackageVersionResolutionReceipt.Resolved(
                    request,
                    discovery,
                    freshness);
        }

        return ClassifyNonAuthoritativeDiscovery(discovery) switch
        {
            PackageVersionDiscoveryTerminalKind.Incomplete =>
                new PackageVersionResolutionReceipt.Incomplete(
                    request,
                    discovery,
                    freshness,
                    Reason(
                        "Required configured-authority discovery is incomplete.")),
            PackageVersionDiscoveryTerminalKind.Failed =>
                new PackageVersionResolutionReceipt.Failed(
                    request,
                    discovery,
                    freshness,
                    Reason(
                        "Version discovery failed before the request could be resolved.")),
            PackageVersionDiscoveryTerminalKind.Rejected =>
                new PackageVersionResolutionReceipt.Rejected(
                    request,
                    discovery,
                    freshness,
                    Reason(
                        "Configured-authority evidence is unusable for version selection.")),
            PackageVersionDiscoveryTerminalKind.Unavailable =>
                new PackageVersionResolutionReceipt.Unavailable(
                    request,
                    discovery,
                    freshness,
                    Reason(
                        "Required version-selection source capability is unavailable.")),
            _ => throw new InvalidOperationException(
                "Version discovery returned an unknown terminal classification."),
        };
    }

    internal static PackageVersionDiscoveryTerminalKind
        ClassifyNonAuthoritativeDiscovery(
            PackageVersionDiscoveryResult discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        if (discovery.State == PackageVersionDiscoveryState.Authoritative)
        {
            throw new ArgumentException(
                "Only non-authoritative discovery has a terminal failure classification.",
                nameof(discovery));
        }
        if (discovery.State == PackageVersionDiscoveryState.Partial)
        {
            return PackageVersionDiscoveryTerminalKind.Incomplete;
        }
        if (discovery.Failures.Any(failure =>
                failure.Kind is PackageAuthorityFailureKind.Timeout
                    or PackageAuthorityFailureKind.Transport))
        {
            return PackageVersionDiscoveryTerminalKind.Failed;
        }
        if (discovery.Failures.Any(failure =>
                failure.Kind
                    == PackageAuthorityFailureKind.IncompleteMetadata))
        {
            return PackageVersionDiscoveryTerminalKind.Incomplete;
        }
        if (discovery.Failures.Count == 0
            || discovery.Failures.Any(failure =>
                failure.Kind is PackageAuthorityFailureKind.Input
                    or PackageAuthorityFailureKind.InvalidResponse
                    or PackageAuthorityFailureKind.ResponseRejected))
        {
            return PackageVersionDiscoveryTerminalKind.Rejected;
        }
        return PackageVersionDiscoveryTerminalKind.Unavailable;
    }

    private static InertString Reason(string text) =>
        new(TextPolicy.Field, text);
}

internal enum PackageVersionDiscoveryTerminalKind
{
    Incomplete,
    Failed,
    Rejected,
    Unavailable,
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
        if (GetDiscoveryIncompatibility(
                request,
                discovery,
                freshness) is { } incompatibility)
            throw new ArgumentException(
                incompatibility.ToString(),
                nameof(discovery));
    }

    internal static InertString? GetDiscoveryIncompatibility(
        PackageVersionSelectionRequest request,
        PackageVersionDiscoveryResult discovery,
        PackageVersionDiscoveryFreshness freshness)
    {
        if (GetPackageIdentityIncompatibility(
                request,
                discovery) is { } identityIncompatibility)
        {
            return identityIncompatibility;
        }
        if (discovery.State
            != PackageVersionDiscoveryState.Authoritative)
        {
            return new(
                TextPolicy.Field,
                "Version selection requires authoritative discovery from every configured authority.");
        }
        if (freshness
            == PackageVersionDiscoveryFreshness.NotEstablished)
        {
            return new(
                TextPolicy.Field,
                "Version-selection discovery freshness was not established.");
        }
        if (freshness
            == PackageVersionDiscoveryFreshness.ServedPrior)
        {
            return new(
                TextPolicy.Field,
                "ServedPrior is reserved for prior settlements; discovery-backed outcomes are Current or RefreshedForRequest.");
        }
        if (request.Discovery.Freshness
                == PackageVersionDiscoveryFreshness.RefreshedForRequest
            && freshness
                != PackageVersionDiscoveryFreshness.RefreshedForRequest)
        {
            return new(
                TextPolicy.Field,
                "The request requires discovery refreshed for this exact version selection.");
        }

        PackageVersionDiscoveryContract contract = discovery.Contract;
        if (contract.IncludeUnlisted)
        {
            return new(
                TextPolicy.Field,
                "Automatic version selection requires listed-only discovery.");
        }
        if (contract.Limit is not null)
        {
            return new(
                TextPolicy.Field,
                "Automatic version selection requires the complete candidate set.");
        }
        if (request.Discovery.IncludePrerelease
            && !contract.IncludePrerelease)
        {
            return new(
                TextPolicy.Field,
                "The discovery evidence omitted prerelease candidates required by the request.");
        }

        return null;
    }

    internal static InertString? GetPackageIdentityIncompatibility(
        PackageVersionSelectionRequest request,
        PackageVersionDiscoveryResult discovery)
    {
        if (discovery.PackageId is null
            || !discovery.PackageId.Equals(
                request.PackageId,
                StringComparison.Ordinal))
        {
            return new(
                TextPolicy.Field,
                "Version-discovery evidence does not identify the requested package.");
        }

        return null;
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
