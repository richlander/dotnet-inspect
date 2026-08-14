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

/// <summary>The result of listing the selectable versions of one package coordinate.</summary>
public abstract record PackageVersionListingResult
{
    private protected PackageVersionListingResult()
    {
    }

    /// <summary>An authoritative listed-version set, in ascending semantic-version order.</summary>
    public sealed record Available : PackageVersionListingResult
    {
        internal Available(IEnumerable<string> versions) =>
            Versions = new ReadOnlyCollection<string>([.. versions]);

        public IReadOnlyList<string> Versions { get; }
    }

    /// <summary>The package id is outside the coordinate grammar.</summary>
    public sealed record Invalid(string Message) : PackageVersionListingResult;

    /// <summary>The authorized sources did not produce an authoritative listing.</summary>
    public sealed record Unavailable(string Message) : PackageVersionListingResult;
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
    /// <para>
    /// A floating coordinate goes through the product's shared listing-aware
    /// version policy rather than a second implementation of it, so an unlisted
    /// head is excluded here exactly as it is for the CLI. With
    /// <paramref name="useVersionCache"/> off, that path neither reads nor
    /// writes the on-disk candidate cache.
    /// </para>
    /// <para>
    /// <paramref name="requireStableFloating"/> is the workspace's stricter
    /// contract, and it is opt-in for a reason. The shared policy prefers a
    /// stable release and falls back to a prerelease when a feed publishes
    /// nothing else; that is right for a CLI which must still inspect a
    /// preview-only package — <c>Aspire.OpenAI</c> publishes no stable version
    /// at all — and wrong for a workspace member whose context stated which
    /// versions it will bind. Enforcing it unconditionally here would silently
    /// re-impose it on the CLI, which reaches this same overload.
    /// </para>
    /// <para>
    /// The rule is applied to the resolved answer rather than to the discovery
    /// path, so a caller that opts in cannot be routed around it — not by a
    /// network listing, and not by a version-cache entry a legacy caller wrote
    /// after taking that fallback.
    /// </para>
    /// <para>
    /// The same opt-in requires an authoritative answer from every authorized
    /// source. A partial candidate set cannot prove a floating coordinate is
    /// latest, so a failed or malformed source makes the workspace result
    /// unavailable while legacy aggregation retains source fall-through.
    /// Gated by
    /// <c>PackageCoordinateResolverTests.FloatingCoordinate_RequiresEveryAuthorizedSourceToAnswer</c>.
    /// </para>
    /// </remarks>
    public static async Task<PackageCoordinateResolution> ResolveAsync(
        HttpClient client,
        PackageCoordinate coordinate,
        IReadOnlyList<PackageSource> authorizedSources,
        Action<string>? log = null,
        bool includePrerelease = false,
        bool useVersionCache = false,
        bool requireStableFloating = false,
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
                requireCompleteSources: requireStableFloating,
                cancellationToken).ConfigureAwait(false);
        if (resolution is null)
        {
            return new PackageCoordinateResolution.Unavailable(
                $"No acceptable version of package '{coordinate.PackageId}' is available.");
        }

        if (!resolution.IsComplete)
        {
            return new PackageCoordinateResolution.Unavailable(
                $"The complete version set for package '{coordinate.PackageId}' could not be resolved from every authorized source.");
        }

        NuGetVersion selected = NuGetVersion.Parse(resolution.Version);

        // The shared version policy prefers a stable release and falls back to
        // a prerelease when a feed publishes nothing else, which is what the
        // CLI has always done and must keep doing. A workspace member is a
        // different contract: a context that did not ask for prereleases is
        // stating which versions it will bind, so a caller that opts in has
        // that fallback answer refused rather than realized.
        //
        // The check is on the answer, not on the discovery path, so it holds
        // for a network listing and for a cached one alike — including a cache
        // entry a legacy caller wrote after taking that fallback.
        if (requireStableFloating && !includePrerelease && selected.IsPrerelease)
        {
            return new PackageCoordinateResolution.Unavailable(
                $"No stable listed version of package '{coordinate.PackageId}' is available; "
                + "only prerelease versions were found.");
        }

        return new PackageCoordinateResolution.Resolved(
            new ResolvedPackageCoordinate(
                coordinate.PackageId.ToLowerInvariant(),
                CanonicalVersion(selected),
                coordinate.Framework,
                coordinate.RuntimeIdentifier,
                resolution.ReportingSources,
                wasFloating: true));
    }

    /// <summary>
    /// Lists versions that the shared listing policy admits for a package across an
    /// already-authorized source set. This overload performs no source discovery, and callers may
    /// disable the product's persistent candidate cache for filesystem-free hosts.
    /// </summary>
    public static async Task<PackageVersionListingResult> ListVersionsAsync(
        HttpClient client,
        string packageId,
        IReadOnlyList<PackageSource> authorizedSources,
        Action<string>? log = null,
        bool includePrerelease = true,
        bool useVersionCache = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(authorizedSources);
        cancellationToken.ThrowIfCancellationRequested();

        if (Validate(new PackageCoordinate(packageId)) is { } invalid)
            return new PackageVersionListingResult.Invalid(invalid.Message);

        if (authorizedSources.Count == 0)
        {
            return new PackageVersionListingResult.Unavailable(
                $"No source is authorized to provide package '{packageId}'.");
        }

        (
            List<PackageVersionResolution>? candidates,
            bool hasIncompleteMetadata) =
            await PackageExtractor.GetVersionCandidatesAsync(
                client,
                packageId,
                authorizedSources,
                includePrerelease,
                log,
                useVersionCache,
                cancellationToken).ConfigureAwait(false);
        if (hasIncompleteMetadata)
        {
            return new PackageVersionListingResult.Unavailable(
                $"The complete version set for package '{packageId}' could not be resolved from every authorized source.");
        }
        if (candidates is null)
        {
            return new PackageVersionListingResult.Unavailable(
                $"No authoritative version listing is available for package '{packageId}'.");
        }

        return new PackageVersionListingResult.Available(
            candidates.Select(candidate => candidate.Version));
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
            // The desktop source policy is the CLI's entry point, so it keeps
            // the shared stable-preferred/prerelease-fallback semantics.
            requireStableFloating: false,
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
    /// The longest acquisition target text this product accepts. A framework or
    /// runtime identifier is a short moniker; anything longer is not one.
    /// </summary>
    public const int MaxAcquisitionTargetLength = 128;

    /// <summary>
    /// True when <paramref name="value"/> is an acquisition target — a
    /// framework or runtime identifier — in canonical form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The grammar is a bounded ASCII allow list: letters and digits, joined by
    /// single <c>.</c>, <c>-</c>, or <c>+</c> separators, starting and ending on
    /// a letter or digit, at most
    /// <see cref="MaxAcquisitionTargetLength"/> characters. That admits every
    /// real spelling this product consumes — <c>net10.0</c>,
    /// <c>net8.0-windows10.0.19041.0</c>, <c>netstandard2.0</c>, <c>net481</c>,
    /// <c>uap10.0</c>, portable profiles such as
    /// <c>portable-net45+win8+wpa81</c>, and runtime identifiers such as
    /// <c>browser-wasm</c>, <c>linux-musl-arm64</c>, and <c>osx.13-arm64</c>.
    /// </para>
    /// <para>
    /// It is an allow list rather than a deny list because the hostile set is
    /// open: a Unicode bidirectional override (<c>U+202E</c>) is not a control
    /// character, so a control-character test admits it, and it reorders every
    /// message and coordinate it later appears in. Nothing outside ASCII
    /// letters, digits, and three separators names a framework or a runtime, so
    /// the whole class is excluded by construction rather than enumerated.
    /// </para>
    /// <para>
    /// Every layer that accepts target text shares this predicate, so a value a
    /// front door admits is exactly a value the canonical realized coordinate
    /// will hold.
    /// </para>
    /// </remarks>
    public static bool IsAcquisitionTargetText(string? value)
    {
        if (value is not { Length: > 0 } target
            || target.Length > MaxAcquisitionTargetLength)
        {
            return false;
        }

        for (int index = 0; index < target.Length; index++)
        {
            char character = target[index];
            if (char.IsAsciiLetterOrDigit(character))
                continue;

            if (character is not ('.' or '-' or '+')
                || index == 0
                || index == target.Length - 1
                || !char.IsAsciiLetterOrDigit(target[index - 1])
                || !char.IsAsciiLetterOrDigit(target[index + 1]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="value"/> is a canonical runtime identifier: an
    /// acquisition target moniker in its lowercase spelling.
    /// </summary>
    /// <remarks>
    /// Runtime identifiers are published lowercase, and the asset selector
    /// matches a <c>runtimes/{rid}</c> folder ordinally by deliberate design —
    /// folding case there would let a package direct a request at a folder its
    /// own manifest never named. So a differently cased spelling is refused
    /// here, before any source, cache, or network work, rather than normalized
    /// into a match the selector would then have to make case-insensitive.
    /// Frameworks take the other route, because they already compare
    /// case-insensitively everywhere.
    /// </remarks>
    public static bool IsCanonicalRuntimeIdentifier(string? value) =>
        IsAcquisitionTargetText(value)
        && string.Equals(
            value,
            value!.ToLowerInvariant(),
            StringComparison.Ordinal);

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
                "A package coordinate framework must be a moniker of ASCII letters and digits joined by single '.', '-', or '+' separators.");
        }

        if (coordinate.RuntimeIdentifier is not null
            && !IsCanonicalRuntimeIdentifier(coordinate.RuntimeIdentifier))
        {
            return new PackageCoordinateResolution.Invalid(
                "A package coordinate runtime identifier must be a lowercase moniker of ASCII letters and digits joined by single '.', '-', or '+' separators.");
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
            // The rejected spelling is not quoted. It is the value that just
            // failed a grammar, so it is the most hostile string this method
            // has seen, and a message is a sink like any other: naming the rule
            // keeps the failure attributable without reopening that channel.
            return new PackageCoordinateResolution.Invalid(
                "A package coordinate version must be one exact normalized NuGet version, without build metadata, whitespace, a range, or a wildcard.");
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
