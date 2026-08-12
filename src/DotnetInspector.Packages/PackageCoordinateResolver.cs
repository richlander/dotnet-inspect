using System.Collections.ObjectModel;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// A package acquisition coordinate. An omitted version floats to the latest
/// acceptable stable version; a present version is an exact pin. CLI selectors
/// such as <c>latest</c>, wildcards, and ranges are not coordinate versions.
/// </summary>
public sealed record PackageCoordinate(
    string PackageId,
    string? Version = null,
    string? Framework = null,
    string? RuntimeIdentifier = null);

/// <summary>
/// An exact package coordinate together with the sources authorized to provide
/// its payload.
/// </summary>
public sealed record ResolvedPackageCoordinate
{
    internal ResolvedPackageCoordinate(
        string packageId,
        string version,
        string? framework,
        string? runtimeIdentifier,
        IEnumerable<PackageSource> sources,
        bool wasFloating)
    {
        PackageId = packageId;
        Version = version;
        Framework = framework;
        RuntimeIdentifier = runtimeIdentifier;
        Sources = new ReadOnlyCollection<PackageSource>([.. sources]);
        WasFloating = wasFloating;
    }

    public string PackageId { get; }
    public string Version { get; }
    public string? Framework { get; }
    public string? RuntimeIdentifier { get; }
    public IReadOnlyList<PackageSource> Sources { get; }
    public bool WasFloating { get; }
}

/// <summary>The result of resolving one package coordinate.</summary>
public abstract record PackageCoordinateResolution
{
    private protected PackageCoordinateResolution()
    {
    }

    public sealed record Resolved : PackageCoordinateResolution
    {
        internal Resolved(ResolvedPackageCoordinate coordinate) =>
            Coordinate = coordinate;

        public ResolvedPackageCoordinate Coordinate { get; }
    }

    public sealed record Invalid : PackageCoordinateResolution
    {
        internal Invalid(string message) => Message = message;

        public string Message { get; }
    }

    public sealed record Unavailable : PackageCoordinateResolution
    {
        internal Unavailable(string message) => Message = message;

        public string Message { get; }
    }
}

/// <summary>
/// Resolves floating and exact package coordinates through the product's
/// source, mapping, candidate-listing, and version-normalization policy.
/// Payload storage remains a separate host-owned concern.
/// </summary>
/// <remarks>
/// Gated by <c>PackageCoordinateResolverTests</c>:
/// <c>FloatingCoordinate_SelectsLatestListedStableVersion</c> for the
/// listing-aware floating path,
/// <c>ExactCoordinate_PreservesUnlistedVersionWithoutDiscovery</c> for exact
/// pins bypassing discovery,
/// <c>Coordinate_WithNoAuthorizedSource_IsUnavailable</c> for the rule that
/// this overload reads no ambient configuration, and
/// <c>Coordinate_RejectsAPackageIdOutsideTheGrammar</c> plus
/// <c>FloatingCoordinate_OutsideTheGrammar_IsRejectedWithoutNetworkWork</c>
/// for the id grammar preceding every source, cache, and network step.
/// </remarks>
public static class PackageCoordinateResolver
{
    /// <summary>
    /// The largest package id NuGet accepts. An id longer than this cannot name
    /// a real package, so it is rejected before any source is consulted.
    /// </summary>
    public const int MaxPackageIdLength = 100;

    /// <summary>
    /// Resolves a coordinate against an already-authorized source set. This
    /// overload performs no source-configuration discovery. Candidate caching
    /// is opt-in so Browser/Wasm callers have no implicit filesystem dependency.
    /// </summary>
    /// <remarks>
    /// A floating coordinate goes through the product's shared listing-aware
    /// version policy rather than a second implementation of it, so an unlisted
    /// head is excluded here exactly as it is for the CLI. With
    /// <paramref name="useVersionCache"/> off, that path neither reads nor
    /// writes the on-disk candidate cache.
    /// </remarks>
    public static async Task<PackageCoordinateResolution> ResolveAsync(
        HttpClient client,
        PackageCoordinate coordinate,
        IReadOnlyList<PackageSource> authorizedSources,
        Action<string>? log = null,
        bool includePrerelease = false,
        bool useVersionCache = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(authorizedSources);
        cancellationToken.ThrowIfCancellationRequested();

        if (ValidateCoordinate(coordinate, out NuGetVersion? exactVersion)
            is { } invalid)
        {
            return invalid;
        }

        if (exactVersion is not null)
        {
            if (authorizedSources.Count == 0)
            {
                return NoAuthorizedSource(coordinate.PackageId);
            }

            return new PackageCoordinateResolution.Resolved(
                new ResolvedPackageCoordinate(
                    coordinate.PackageId.ToLowerInvariant(),
                    CanonicalVersion(exactVersion),
                    coordinate.Framework,
                    coordinate.RuntimeIdentifier,
                    authorizedSources,
                    wasFloating: false));
        }

        if (authorizedSources.Count == 0)
        {
            return NoAuthorizedSource(coordinate.PackageId);
        }

        PackageVersionResolution? resolution =
            await PackageExtractor.ResolveLatestVersionAsync(
                client,
                coordinate.PackageId,
                [.. authorizedSources],
                log,
                skipCache: !useVersionCache,
                includePrerelease,
                cancellationToken).ConfigureAwait(false);
        if (resolution is null)
        {
            return new PackageCoordinateResolution.Unavailable(
                $"No acceptable version of package '{coordinate.PackageId}' is available.");
        }

        return new PackageCoordinateResolution.Resolved(
            new ResolvedPackageCoordinate(
                coordinate.PackageId.ToLowerInvariant(),
                CanonicalVersion(
                    NuGetVersion.Parse(resolution.Version)),
                coordinate.Framework,
                coordinate.RuntimeIdentifier,
                resolution.ReportingSources,
                wasFloating: true));
    }

    /// <summary>
    /// Resolves active package sources and source mapping through the desktop
    /// source policy, then delegates to the host-neutral resolver.
    /// </summary>
    public static async Task<PackageCoordinateResolution> ResolveUsingSourcePolicyAsync(
        HttpClient client,
        PackageCoordinate coordinate,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null,
        bool includePrerelease = false,
        bool useVersionCache = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(coordinate);
        cancellationToken.ThrowIfCancellationRequested();

        if (ValidateCoordinate(coordinate, out _) is { } invalid)
        {
            return invalid;
        }

        // One owner for the desktop composition of config validity, sources,
        // mapping, and credentials: the same adapter a host passes to the
        // workspace loader.
        PackageSourceAuthorization authorization =
            new SourcePolicyPackageSourceAuthorization(sourceOptions)
                .AuthorizeSourcesFor(coordinate.PackageId);
        if (authorization.DenialReason is { } denial)
        {
            return new PackageCoordinateResolution.Unavailable(denial);
        }

        return await ResolveAsync(
            client,
            coordinate,
            authorization.Sources,
            log,
            includePrerelease,
            useVersionCache,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates a coordinate's shape without contacting a source. Returns the
    /// typed invalid result, or <see langword="null"/> when the coordinate is
    /// well formed. A caller that validates several coordinates before doing
    /// any network work uses this rather than restating the grammar.
    /// </summary>
    public static PackageCoordinateResolution.Invalid? Validate(
        PackageCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return ValidateCoordinate(coordinate, out _);
    }

    /// <summary>
    /// True when <paramref name="value"/> is a package id NuGet could publish:
    /// at most <see cref="MaxPackageIdLength"/> characters of ASCII letters,
    /// digits, and underscore, separated by single <c>.</c> or <c>-</c>
    /// characters.
    /// </summary>
    /// <remarks>
    /// This is the single owner of the id grammar. It is a bounded allow list
    /// rather than a deny list of dangerous spellings, so URL syntax
    /// (<c>?</c>, <c>#</c>, <c>%</c>, <c>@</c>, <c>:</c>), path separators,
    /// traversal segments, non-ASCII text, and control characters are all
    /// outside it by construction rather than by enumeration. An id is
    /// substituted into feed URLs and cache paths, so a caller holding a
    /// validated id knows those substitutions cannot change the shape of what
    /// they build.
    /// </remarks>
    public static bool IsCanonicalPackageId(string? value)
    {
        if (value is not { Length: > 0 } id || id.Length > MaxPackageIdLength)
            return false;

        for (int index = 0; index < id.Length; index++)
        {
            char character = id[index];
            if (IsIdWordCharacter(character))
                continue;

            // A separator is legal only between two word characters, so no id
            // starts or ends with one and no two are adjacent.
            if (character is not ('.' or '-')
                || index == 0
                || index == id.Length - 1
                || !IsIdWordCharacter(id[index - 1])
                || !IsIdWordCharacter(id[index + 1]))
            {
                return false;
            }
        }

        return true;

        static bool IsIdWordCharacter(char value) =>
            char.IsAsciiLetterOrDigit(value) || value == '_';
    }

    /// <summary>
    /// True when <paramref name="value"/> can be an acquisition target — a
    /// framework or runtime identifier — in canonical form: non-empty, without
    /// surrounding whitespace, and carrying no control character.
    /// </summary>
    /// <remarks>
    /// Every layer that accepts a target text shares this predicate, so a value
    /// a front door admits is exactly a value the canonical realized coordinate
    /// will hold. The spelling itself stays open, because the framework and
    /// runtime-identifier vocabularies are owned elsewhere and grow.
    /// </remarks>
    public static bool IsAcquisitionTargetText(string? value) =>
        value is { Length: > 0 }
        && !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    static PackageCoordinateResolution.Invalid? ValidateCoordinate(
        PackageCoordinate coordinate,
        out NuGetVersion? exactVersion)
    {
        exactVersion = null;
        if (!IsCanonicalPackageId(coordinate.PackageId))
        {
            // The rejected spelling is caller-supplied, but it is also the
            // value that failed a URL- and path-substitution grammar, so it is
            // described rather than echoed.
            return new PackageCoordinateResolution.Invalid(
                "A package coordinate requires a package id of at most "
                + $"{MaxPackageIdLength} characters of ASCII letters, digits, and underscore, "
                + "separated by single '.' or '-' characters.");
        }

        if (InvalidOptionalTarget(coordinate.Framework))
        {
            return new PackageCoordinateResolution.Invalid(
                "A package coordinate framework cannot be empty, have surrounding whitespace, or carry a control character.");
        }

        if (InvalidOptionalTarget(coordinate.RuntimeIdentifier))
        {
            return new PackageCoordinateResolution.Invalid(
                "A package coordinate runtime identifier cannot be empty, have surrounding whitespace, or carry a control character.");
        }

        if (coordinate.Version is not { } version)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(version)
            || !string.Equals(
                version,
                version.Trim(),
                StringComparison.Ordinal)
            || version.Contains('+', StringComparison.Ordinal)
            || !NuGetVersion.TryParse(version, out exactVersion))
        {
            return new PackageCoordinateResolution.Invalid(
                $"Package version '{version}' is not an exact normalized version.");
        }

        return null;
    }

    static bool InvalidOptionalTarget(string? value) =>
        value is not null && !IsAcquisitionTargetText(value);

    static PackageCoordinateResolution.Unavailable NoAuthorizedSource(
        string packageId) =>
        new($"No source is authorized to provide package '{packageId}'.");

    static string CanonicalVersion(NuGetVersion version) =>
        version.ToNormalizedString().ToLowerInvariant();
}
