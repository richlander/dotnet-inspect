using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// One acquisition location declared by a workspace context. Coordinates are
/// loader inputs that <em>produce</em>
/// <see cref="ILInspector.Metadata.AssemblyResolutionProvenance"/>; they are
/// not serializations of it, and no field-level round trip is implied.
/// </summary>
public abstract record WorkspaceMemberCoordinate
{
    private protected WorkspaceMemberCoordinate()
    {
    }

    private protected abstract int Discriminator { get; }

    /// <summary>
    /// Creates a package coordinate. A null <paramref name="version"/> floats
    /// to the latest acceptable version under the product's source and version
    /// policy; a present version is an exact pin.
    /// </summary>
    public static WorkspaceMemberCoordinate Package(
        string packageId,
        string? version = null,
        string? framework = null,
        string? runtimeIdentifier = null) =>
        new PackageMember(
            packageId,
            version,
            framework,
            runtimeIdentifier);

    /// <summary>
    /// Creates a coordinate for one product-owned platform family.
    /// </summary>
    public static WorkspaceMemberCoordinate Platform(
        string family,
        string? assembly = null,
        string? version = null,
        string? framework = null) =>
        new PlatformMember(family, assembly, version, framework);

    /// <summary>
    /// Creates a coordinate for artifact bytes shipped with the definition.
    /// </summary>
    public static WorkspaceMemberCoordinate Embedded(
        string contentRef,
        string digest,
        string declaredName) =>
        new EmbeddedMember(contentRef, digest, declaredName);

    /// <summary>A package acquisition location.</summary>
    public sealed record PackageMember : WorkspaceMemberCoordinate
    {
        internal PackageMember(
            string packageId,
            string? version,
            string? framework,
            string? runtimeIdentifier)
        {
            PackageId = packageId;
            Version = version;
            Framework = framework;
            RuntimeIdentifier = runtimeIdentifier;
        }

        private protected override int Discriminator => 0;

        public string PackageId { get; }

        /// <summary>The exact pinned version, or null when the member floats.</summary>
        public string? Version { get; }

        /// <summary>
        /// The member's acquisition framework. It may repeat the context's
        /// declaration or inherit it by staying null; it never disagrees.
        /// </summary>
        public string? Framework { get; }

        /// <summary>
        /// The member's acquisition runtime identifier, with the same
        /// inheritance rule as <see cref="Framework"/>.
        /// </summary>
        public string? RuntimeIdentifier { get; }
    }

    /// <summary>
    /// A product-owned platform family acquired from implementation-pack
    /// content.
    /// </summary>
    public sealed record PlatformMember : WorkspaceMemberCoordinate
    {
        internal PlatformMember(
            string family,
            string? assembly,
            string? version,
            string? framework)
        {
            Family = family;
            Assembly = assembly;
            Version = version;
            Framework = framework;
        }

        private protected override int Discriminator => 1;

        /// <summary>
        /// The platform family: <c>runtime</c> or <c>aspnetcore</c>.
        /// </summary>
        public string Family { get; }

        /// <summary>
        /// The assembly simple name to acquire, or null for every managed
        /// assembly in the family.
        /// </summary>
        public string? Assembly { get; }

        /// <summary>The exact pack version, or null when it floats.</summary>
        public string? Version { get; }

        /// <summary>
        /// The member's target framework. It may repeat the context's
        /// declaration or inherit it by staying null.
        /// </summary>
        public string? Framework { get; }
    }

    /// <summary>
    /// Artifact bytes shipped with the definition and addressed by a
    /// bundle-relative content reference.
    /// </summary>
    public sealed record EmbeddedMember : WorkspaceMemberCoordinate
    {
        internal EmbeddedMember(
            string contentRef,
            string digest,
            string declaredName)
        {
            ContentRef = contentRef;
            Digest = digest;
            DeclaredName = declaredName;
        }

        private protected override int Discriminator => 2;

        /// <summary>The host-resolved content identifier for these bytes.</summary>
        /// <remarks>
        /// A bundle-relative, <c>/</c>-separated identifier. It is never
        /// interpreted as a filesystem path; the host maps it onto content.
        /// </remarks>
        public string ContentRef { get; }

        /// <summary>
        /// Canonical lowercase hex SHA-256 of the content bytes. It is
        /// integrity evidence only: it confers no authorization. An uppercase
        /// spelling is rejected rather than folded, so one content identity
        /// has one spelling.
        /// </summary>
        public string Digest { get; }

        /// <summary>
        /// The expected assembly simple name, validated against the image's
        /// own identity when the image is first opened.
        /// </summary>
        public string DeclaredName { get; }
    }
}

/// <summary>
/// One workspace context: the members in scope plus the context-wide
/// acquisition target they share.
/// </summary>
/// <remarks>
/// The acquisition target is a loader concern, distinct from
/// <see cref="ILInspector.Metadata.AssemblyBindingTarget"/>, which describes a
/// reference request inside an already established context. A context lowers to
/// exactly one <see cref="AssemblyContextGroup"/> or to a typed failure; it is
/// never split.
/// </remarks>
public sealed record WorkspaceContextInput
{
    /// <summary>The context-wide acquisition framework, when declared.</summary>
    public string? Framework { get; init; }

    /// <summary>The context-wide acquisition runtime identifier, when declared.</summary>
    public string? RuntimeIdentifier { get; init; }

    /// <summary>The context's members, in declaration order.</summary>
    public IReadOnlyList<WorkspaceMemberCoordinate> Members { get; init; } = [];
}

/// <summary>Why a workspace context could not be realized.</summary>
public enum WorkspaceContextLoadFailureKind
{
    /// <summary>The context declares no member.</summary>
    EmptyContext,

    /// <summary>A coordinate field is absent or not in its canonical form.</summary>
    InvalidCoordinate,

    /// <summary>An acquisition kind requires a target the context never states.</summary>
    MissingAcquisitionTarget,

    /// <summary>Context, subscription, and member target declarations disagree.</summary>
    ConflictingAcquisitionTarget,

    /// <summary>No authorized source supplied the package payload.</summary>
    PackageUnavailable,

    /// <summary>
    /// The producer a realized coordinate names is not one this host authorizes
    /// for that package, or did not serve it.
    /// </summary>
    PackageProducerUnavailable,

    /// <summary>More than one member realized the same assembly identity.</summary>
    ConflictingAssemblyIdentity,

    /// <summary>The package carries no assembly assets for the acquisition target.</summary>
    PackageAssetUnavailable,

    /// <summary>More than one package asset universe is equally applicable.</summary>
    PackageAssetAmbiguous,

    /// <summary>No authorized implementation pack satisfies the platform target.</summary>
    PlatformPackUnavailable,

    /// <summary>The selected platform pack does not carry the requested assembly.</summary>
    PlatformAssemblyUnavailable,

    /// <summary>The selected platform pack carries multiple identities with the requested name.</summary>
    PlatformAssemblyAmbiguous,

    /// <summary>
    /// The producer a realized platform coordinate names is not authorized or
    /// did not serve its implementation pack.
    /// </summary>
    PlatformProducerUnavailable,

    /// <summary>The host supplied no content for an embedded coordinate.</summary>
    EmbeddedContentUnavailable,

    /// <summary>Embedded content does not hash to its declared digest.</summary>
    EmbeddedDigestMismatch,

    /// <summary>An embedded image's identity is not its declared assembly name.</summary>
    EmbeddedNameMismatch,

    /// <summary>A selected image has no managed metadata or cannot be read.</summary>
    InvalidImage,

    /// <summary>The host offers no capability an acquisition kind requires.</summary>
    HostCapabilityUnavailable,

    /// <summary>A selected image uses Windows Metadata, which is unsupported.</summary>
    UnsupportedMetadataFormat,

    /// <summary>A selected image has a malformed assembly metadata root.</summary>
    MalformedMetadataRoot,
}

/// <summary>
/// The exact acquisition location one realized member was actually loaded
/// from, in a portable, structurally comparable form.
/// </summary>
/// <remarks>
/// <para>
/// A declared <see cref="WorkspaceMemberCoordinate"/> may float: a package
/// member without a version names no exact identity, so it cannot be
/// transported, compared, or re-acquired as one. The realized coordinate is
/// what the loader actually selected — every identity field concrete and
/// canonical, with an optional acquisition target absent only for a
/// framework-neutral source acquisition — so a consumer can carry it across a
/// transport boundary and repeat the same producer-bound acquisition request.
/// Package storage may publish a newer payload generation under that
/// coordinate; only an acquisition-owned content-generation identity proves
/// immutable byte correspondence.
/// </para>
/// <para>
/// It is a value, not a handle. It carries no source, credential, stream,
/// registration, policy, or other runtime object, and it is not a
/// serialization of
/// <see cref="ILInspector.Metadata.AssemblyResolutionProvenance"/>: the
/// coordinate says what to acquire, while provenance records how one selected
/// image was chosen inside it.
/// </para>
/// <para>
/// Construction is the check: every constructor rejects a non-canonical value
/// rather than repairing it, so holding one of these is the evidence that its
/// fields are canonical. Gated by
/// <c>WorkspaceContextLoaderTests.RealizedCoordinate_IsCanonicalAndStructurallyEquatable</c>
/// and its close negatives.
/// </para>
/// </remarks>
public abstract record RealizedMemberCoordinate
{
    private protected RealizedMemberCoordinate()
    {
    }

    private protected abstract int Discriminator { get; }

    /// <summary>
    /// One exact package acquisition: a normalized package id and version, the
    /// producer that actually served the bytes, and the effective acquisition
    /// target the context resolved to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every participant realized from one package member carries this same
    /// coordinate. It states the acquisition request that produced them, not
    /// the asset folder any single image came from; that physical detail stays
    /// on each descriptor's
    /// <see cref="ILInspector.Metadata.AssemblyResolutionProvenance.PackageAsset"/>.
    /// Canonical normalization is owned by
    /// <see cref="DotnetInspector.Packages.PackageCoordinateResolver"/>; this
    /// type only refuses to hold a value that owner would not have produced.
    /// </para>
    /// <para>
    /// <see cref="Producer"/> is part of the identity because id, version,
    /// framework, and runtime identifier do not determine bytes: two feeds may
    /// each serve a package by that name and version, and a coordinate that
    /// could not tell them apart would claim re-acquirability it does not have.
    /// It is the content cache's own producer key — an opaque, bounded,
    /// credential-free token — so the coordinate stays a portable value and
    /// carries no source object, URL, or credential across a transport
    /// boundary.
    /// </para>
    /// </remarks>
    public sealed record Package : RealizedMemberCoordinate
    {
        public Package(
            string packageId,
            string version,
            string producer,
            string? framework,
            string? runtimeIdentifier)
        {
            if (!IsCanonicalPackageIdentity(packageId))
            {
                throw new ArgumentException(
                    "A realized package id is a canonical NuGet package id in its normalized lowercase spelling.",
                    nameof(packageId));
            }

            if (!IsCanonicalPackageVersion(version))
            {
                throw new ArgumentException(
                    "A realized package version is one exact NuGet version in its normalized lowercase spelling.",
                    nameof(version));
            }

            if (!IsCanonicalProducer(producer))
            {
                throw new ArgumentException(
                    "A realized package producer is a canonical content-cache producer key.",
                    nameof(producer));
            }

            if (framework is not null
                && !IsCanonicalFramework(framework))
            {
                throw new ArgumentException(
                    "A realized acquisition framework is a canonical lowercase moniker.",
                    nameof(framework));
            }

            if (runtimeIdentifier is not null
                && !IsCanonicalRuntimeIdentifier(runtimeIdentifier))
            {
                throw new ArgumentException(
                    "A realized runtime identifier is a canonical lowercase moniker.",
                    nameof(runtimeIdentifier));
            }
            if (framework is null
                && runtimeIdentifier is not null)
            {
                throw new ArgumentException(
                    "A realized runtime identifier requires an acquisition framework.",
                    nameof(runtimeIdentifier));
            }

            PackageId = packageId;
            Version = version;
            Producer = producer;
            Framework = framework;
            RuntimeIdentifier = runtimeIdentifier;
        }

        private protected override int Discriminator => 0;

        /// <summary>The normalized package id.</summary>
        public string PackageId { get; }

        /// <summary>The exact normalized version that was acquired.</summary>
        public string Version { get; }

        /// <summary>
        /// The content cache's identity for the producer that served these
        /// bytes.
        /// </summary>
        /// <remarks>
        /// An opaque token, not a locator: it names which feed answered without
        /// disclosing the feed's URL, credentials, or configuration. Two
        /// producers serving one id and version therefore realize two distinct
        /// coordinates.
        /// </remarks>
        public string Producer { get; }

        /// <summary>
        /// The context's effective acquisition framework, or <c>null</c> for a
        /// framework-neutral source acquisition.
        /// </summary>
        public string? Framework { get; }

        /// <summary>The context's effective acquisition runtime identifier.</summary>
        public string? RuntimeIdentifier { get; }

        /// <summary>
        /// Creates the coordinate, or reports why the values are not canonical,
        /// without throwing.
        /// </summary>
        /// <remarks>
        /// The constructor throws because holding one of these is the evidence
        /// that its fields are canonical, and a value that is not canonical is
        /// a programming error at that boundary. A loader is a different
        /// boundary: it composes these values out of a resolver's output after
        /// bytes have already been acquired and committed, so a grammar that
        /// drifts apart between the two owners would surface as an unhandled
        /// exception after publication. This factory is what that path calls,
        /// so the drift is a typed failure instead. The problem text names the
        /// rule that failed and never the value that failed it.
        /// </remarks>
        public static bool TryCreate(
            string packageId,
            string version,
            string producer,
            string? framework,
            string? runtimeIdentifier,
            [NotNullWhen(true)] out Package? coordinate,
            [NotNullWhen(false)] out string? problem)
        {
            coordinate = null;
            problem = !IsCanonicalPackageIdentity(packageId)
                ? "a realized package id must be a canonical NuGet package id in its normalized lowercase spelling"
                : !IsCanonicalPackageVersion(version)
                    ? "a realized package version must be one exact NuGet version in its normalized lowercase spelling"
                    : !IsCanonicalProducer(producer)
                        ? "a realized package producer must be a canonical content-cache producer key"
                        : framework is not null
                            && !IsCanonicalFramework(framework)
                            ? "a realized acquisition framework must be a canonical lowercase moniker"
                            : runtimeIdentifier is not null
                                && !IsCanonicalRuntimeIdentifier(runtimeIdentifier)
                                ? "a realized runtime identifier must be a canonical lowercase moniker"
                                : framework is null
                                    && runtimeIdentifier is not null
                                    ? "a realized runtime identifier requires an acquisition framework"
                                : null;
            if (problem is not null)
                return false;

            coordinate = new Package(
                packageId,
                version,
                producer,
                framework,
                runtimeIdentifier);
            return true;
        }
    }

    /// <summary>
    /// One exact platform-family acquisition from an authorized package
    /// producer.
    /// </summary>
    public sealed record Platform : RealizedMemberCoordinate
    {
        public Platform(
            string family,
            string version,
            string producer,
            string framework,
            string? assembly)
        {
            if (!IsCanonicalPlatformFamily(family))
            {
                throw new ArgumentException(
                    "A realized platform family is a product-owned canonical family id.",
                    nameof(family));
            }

            if (!IsCanonicalPackageVersion(version))
            {
                throw new ArgumentException(
                    "A realized platform version is one exact NuGet version in its normalized lowercase spelling.",
                    nameof(version));
            }

            if (!IsCanonicalProducer(producer))
            {
                throw new ArgumentException(
                    "A realized platform producer is a canonical content-cache producer key.",
                    nameof(producer));
            }

            if (!IsCanonicalFramework(framework))
            {
                throw new ArgumentException(
                    "A realized platform framework is a canonical lowercase moniker.",
                    nameof(framework));
            }

            if (!IsPlatformVersionForFramework(version, framework))
            {
                throw new ArgumentException(
                    "A realized platform version must match its target framework.",
                    nameof(version));
            }

            if (assembly is not null && !IsAssemblySimpleName(assembly))
            {
                throw new ArgumentException(
                    "A realized platform assembly is an assembly simple name.",
                    nameof(assembly));
            }

            Family = family;
            Version = version;
            Producer = producer;
            Framework = framework;
            Assembly = assembly;
        }

        private protected override int Discriminator => 1;

        public string Family { get; }
        public string Version { get; }
        public string Producer { get; }
        public string Framework { get; }
        public string? Assembly { get; }

        internal static bool TryCreate(
            string family,
            string version,
            string producer,
            string framework,
            string? assembly,
            [NotNullWhen(true)] out Platform? coordinate,
            [NotNullWhen(false)] out string? problem)
        {
            coordinate = null;
            problem = !IsCanonicalPlatformFamily(family)
                ? "a realized platform family must be a product-owned canonical family id"
                : !IsCanonicalPackageVersion(version)
                    ? "a realized platform version must be one exact NuGet version in its normalized lowercase spelling"
                    : !IsCanonicalProducer(producer)
                        ? "a realized platform producer must be a canonical content-cache producer key"
                        : !IsCanonicalFramework(framework)
                            ? "a realized platform framework must be a canonical lowercase moniker"
                            : !IsPlatformVersionForFramework(
                                version,
                                framework)
                                ? "a realized platform version must match its target framework"
                                : assembly is not null
                                && !IsAssemblySimpleName(assembly)
                                ? "a realized platform assembly must be an assembly simple name"
                                : null;
            if (problem is not null)
                return false;

            coordinate = new Platform(
                family,
                version,
                producer,
                framework,
                assembly);
            return true;
        }
    }

    /// <summary>One exact bundle-content acquisition.</summary>
    public sealed record Embedded : RealizedMemberCoordinate
    {
        public Embedded(
            string contentRef,
            string digest,
            string declaredName)
        {
            if (!IsCanonicalContentRef(contentRef))
            {
                throw new ArgumentException(
                    "A content reference is a bundle-relative, '/'-separated identifier with no empty, '.', or '..' segment.",
                    nameof(contentRef));
            }

            if (!IsCanonicalDigest(digest))
            {
                throw new ArgumentException(
                    "A content digest is 64 lowercase hexadecimal characters.",
                    nameof(digest));
            }

            if (!IsAssemblySimpleName(declaredName))
            {
                throw new ArgumentException(
                    "A declared assembly name is a non-empty simple name.",
                    nameof(declaredName));
            }

            ContentRef = contentRef;
            Digest = digest;
            DeclaredName = declaredName;
        }

        private protected override int Discriminator => 2;

        /// <summary>The bundle-relative content identifier.</summary>
        public string ContentRef { get; }

        /// <summary>
        /// Lowercase hex SHA-256 of the content bytes, integrity evidence only.
        /// </summary>
        public string Digest { get; }

        /// <summary>The assembly simple name the image was verified against.</summary>
        public string DeclaredName { get; }
    }

    /// <summary>
    /// True when <paramref name="value"/> is a bundle-relative,
    /// <c>/</c>-separated content identifier. It is a string grammar, not a
    /// filesystem path: no path API interprets it, and rooted, traversing,
    /// empty, padded, backslash-bearing, and non-graphic forms are refused
    /// rather than repaired.
    /// </summary>
    public static bool IsCanonicalContentRef(string? value)
    {
        if (value is not { Length: > 0 }
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || ContainsNonGraphicText(value))
        {
            return false;
        }

        foreach (string segment in value.Split('/'))
        {
            if (segment.Length == 0
                || segment == "."
                || segment == ".."
                || !string.Equals(
                    segment,
                    segment.Trim(),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="value"/> is a canonical lowercase hexadecimal
    /// SHA-256 digest. An uppercase spelling is rejected, not folded, so one
    /// content identity has one spelling.
    /// </summary>
    public static bool IsCanonicalDigest(string? value) =>
        value is { Length: 64 }
        && value.All(static character =>
            char.IsAsciiDigit(character)
            || character is >= 'a' and <= 'f');

    /// <summary>
    /// True when <paramref name="value"/> can be a content-cache producer key:
    /// a short, lowercase, opaque token of ASCII letters, digits, and hyphens.
    /// </summary>
    /// <remarks>
    /// The grammar is what makes a producer safe to carry in a portable value.
    /// A URL, a credential, a user-info segment, and a filesystem path each
    /// contain a character this rejects, so a caller cannot smuggle a locator
    /// or a secret into a coordinate by passing one where a key belongs.
    /// </remarks>
    public static bool IsCanonicalProducer(string? value) =>
        value is { Length: > 0 and <= 64 }
        && value.All(static character =>
            char.IsAsciiDigit(character)
            || character is >= 'a' and <= 'z'
            || character is '-');

    /// <summary>
    /// True when <paramref name="value"/> names a product-owned platform
    /// implementation-pack family.
    /// </summary>
    public static bool IsCanonicalPlatformFamily(string? value) =>
        value is "runtime" or "aspnetcore";

    /// <summary>
    /// True when an exact platform-pack version belongs to the target
    /// framework's release line.
    /// </summary>
    public static bool IsPlatformVersionForFramework(
        string? version,
        string? framework) =>
        IsCanonicalPackageVersion(version)
        && IsCanonicalFramework(framework)
        && NuGetVersion.TryParse(version, out NuGetVersion? package)
        && TfmResolver.TryGetBaseFrameworkIdentity(
            framework,
            out TfmResolver.FrameworkIdentity target)
        && target.Family
            is TfmFamily.NetModern
                or TfmFamily.NetCore
        && package.Major == target.Version.Major
        && package.Minor == target.Version.Minor;

    /// <summary>
    /// True when <paramref name="value"/> can be an assembly simple name. Any
    /// legal Unicode identifier text is accepted; empty, padded, non-graphic,
    /// and assembly-display-name punctuation are refused.
    /// </summary>
    /// <remarks>
    /// "Non-graphic" is not "control": a bidirectional override is neither a
    /// control character nor invisible, and a name carrying one reorders every
    /// message it appears in, including the typed failure that reports the name
    /// as unusable. The categories that can act on a sink are already decided
    /// by <see cref="InertText.TextPolicy.Field"/>, so this asks that owner
    /// rather than keeping a second list.
    /// </remarks>
    public static bool IsAssemblySimpleName(string? value) =>
        value is { Length: > 0 }
        && !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !ContainsNonGraphicText(value)
        && !value.Any(static character =>
            character is '/' or '\\' or ':' or ',' or '\'' or '"' or '=');

    /// <summary>
    /// True when <paramref name="value"/> is a canonical NuGet package id: the
    /// published id grammar, in its normalized lowercase spelling.
    /// </summary>
    /// <remarks>
    /// The id grammar is owned by
    /// <see cref="PackageCoordinateResolver.IsCanonicalPackageId"/> and is not
    /// restated here; a realized coordinate adds only the normalized spelling,
    /// because two casings of one id would otherwise be two coordinates for one
    /// package. Validating an id against the framework and runtime-identifier
    /// grammar instead would reject real ids — every id containing <c>_</c>,
    /// which that grammar has no reason to admit.
    /// </remarks>
    public static bool IsCanonicalPackageIdentity(string? value) =>
        PackageCoordinateResolver.IsCanonicalPackageId(value)
        && string.Equals(
            value,
            value!.ToLowerInvariant(),
            StringComparison.Ordinal);

    /// <summary>
    /// True when <paramref name="value"/> is one exact NuGet version in its
    /// normalized lowercase spelling.
    /// </summary>
    /// <remarks>
    /// The contract is NuGet's own normalization, not a moniker grammar. A
    /// prerelease label may legitimately begin, end, or consist of hyphens —
    /// <c>1.0.0--beta</c> and <c>1.0.0-beta-</c> both parse and both normalize
    /// to themselves — and a version like those can be selected by a feed,
    /// acquired, and committed. Holding it to a separator grammar written for
    /// frameworks would reject it only after those bytes were published.
    /// </remarks>
    public static bool IsCanonicalPackageVersion(string? value) =>
        value is { Length: > 0 }
        && !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !ContainsNonGraphicText(value)
        && !value.Contains('+', StringComparison.Ordinal)
        && string.Equals(
            value,
            value.ToLowerInvariant(),
            StringComparison.Ordinal)
        && NuGetVersion.TryParse(value, out NuGetVersion? parsed)
        && string.Equals(
            parsed.ToNormalizedString().ToLowerInvariant(),
            value,
            StringComparison.Ordinal);

    /// <summary>
    /// True when <paramref name="value"/> is a canonical acquisition framework:
    /// the shared target grammar, in its normalized lowercase spelling.
    /// </summary>
    /// <remarks>
    /// Frameworks compare case-insensitively throughout the product, so two
    /// casings of one moniker name one target; a realized coordinate holds the
    /// normalized spelling of it, so those two declarations realize equal
    /// coordinates rather than two coordinates that transport as different
    /// identities.
    /// </remarks>
    public static bool IsCanonicalFramework(string? value) =>
        PackageCoordinateResolver.IsAcquisitionTargetText(value)
        && string.Equals(
            value,
            value!.ToLowerInvariant(),
            StringComparison.Ordinal);

    /// <summary>
    /// True when <paramref name="value"/> is a canonical runtime identifier.
    /// </summary>
    /// <remarks>
    /// Owned by
    /// <see cref="PackageCoordinateResolver.IsCanonicalRuntimeIdentifier"/>:
    /// runtime identifiers are canonically lowercase and are matched ordinally,
    /// so a differently cased spelling is refused rather than folded.
    /// </remarks>
    public static bool IsCanonicalRuntimeIdentifier(string? value) =>
        PackageCoordinateResolver.IsCanonicalRuntimeIdentifier(value);

    /// <summary>
    /// True when <paramref name="value"/> carries a scalar that can act on a
    /// sink — a control, a format or bidirectional character, a separator, or
    /// an unpaired surrogate.
    /// </summary>
    /// <remarks>
    /// The classification is <see cref="InertText"/>'s, reached by asking it to
    /// encode the value: it encodes exactly the scalars its field policy
    /// considers non-graphic, so a value that survives unencoded contains none
    /// of them. Restating the category list here would be a second policy free
    /// to drift from the one the renderers enforce.
    /// </remarks>
    static bool ContainsNonGraphicText(string value) =>
        new InertString(TextPolicy.Field, value).WasEncoded;

}

/// <summary>One typed reason a context did not produce an assembly group.</summary>
/// <param name="Kind">The failure category.</param>
/// <param name="Member">
/// The member coordinate that failed, or null for a context-level failure.
/// </param>
/// <param name="Message">
/// A description naming the caller-supplied subject. It never quotes bytes or
/// names taken from a package archive, feed response, or bundle image.
/// </param>
public sealed record WorkspaceContextLoadFailure(
    WorkspaceContextLoadFailureKind Kind,
    WorkspaceMemberCoordinate? Member,
    string Message)
{
    /// <summary>The exact malformed-root reason, when applicable.</summary>
    public MetadataRootMalformedReason? MetadataRootReason { get; init; }
}

/// <summary>One realized member of a loaded workspace context.</summary>
/// <param name="Declared">
/// The coordinate the context declared, which may have floated.
/// </param>
/// <param name="Realized">
/// The exact acquisition location the participant was loaded from. Every
/// participant realized from one package member carries the same value.
/// </param>
/// <param name="Participant">The participant in the created group.</param>
public sealed record WorkspaceContextMember(
    WorkspaceMemberCoordinate Declared,
    RealizedMemberCoordinate Realized,
    AssemblyContextParticipant Participant);

/// <summary>The typed result of realizing one workspace context.</summary>
public abstract record WorkspaceContextLoadOutcome
{
    private protected WorkspaceContextLoadOutcome()
    {
    }

    /// <summary>
    /// The context produced exactly one binding-consistent group. The
    /// workspace owns the group's lifetime.
    /// </summary>
    public sealed record Loaded : WorkspaceContextLoadOutcome
    {
        internal Loaded(
            AssemblyContextGroup group,
            ImmutableArray<WorkspaceContextMember> members,
            ImmutableArray<RealizedMemberCoordinate.Platform>
                availablePlatformAssemblies,
            string? framework,
            string? runtimeIdentifier)
        {
            Group = group;
            Members = members;
            AvailablePlatformAssemblies = availablePlatformAssemblies;
            Framework = framework;
            RuntimeIdentifier = runtimeIdentifier;
        }

        public AssemblyContextGroup Group { get; }

        /// <summary>
        /// Realized members in declaration order. One package member may
        /// contribute several participants.
        /// </summary>
        public ImmutableArray<WorkspaceContextMember> Members { get; }

        /// <summary>
        /// Metadata-derived assembly selection coordinates observed in the
        /// selected platform asset universes, including assemblies not loaded
        /// as participants. Selecting a name carried by more than one full
        /// identity still produces a typed ambiguity failure.
        /// </summary>
        /// <remarks>
        /// <c>WorkspaceContextLoaderTests.PlatformMember_AssemblyFilterUsesMetadataIdentity</c>
        /// gates metadata identity and selected-universe ownership.
        /// </remarks>
        public ImmutableArray<RealizedMemberCoordinate.Platform>
            AvailablePlatformAssemblies { get; }

        /// <summary>The effective acquisition framework, when the context needed one.</summary>
        public string? Framework { get; }

        /// <summary>The effective acquisition runtime identifier, when declared.</summary>
        public string? RuntimeIdentifier { get; }
    }

    /// <summary>
    /// The context was rejected and no group was created. Nothing is partially
    /// realized: a context never lowers to a subset of its members.
    /// </summary>
    public sealed record Failed : WorkspaceContextLoadOutcome
    {
        internal Failed(
            ImmutableArray<WorkspaceContextLoadFailure> failures) =>
            Failures = failures;

        public ImmutableArray<WorkspaceContextLoadFailure> Failures { get; }
    }
}

/// <summary>
/// Host-supplied access to the artifact bytes an embedded coordinate names.
/// </summary>
/// <remarks>
/// The provider maps a bundle-relative content reference onto bytes. It never
/// interprets the reference as a filesystem path on the loader's behalf, and it
/// must return a fresh, readable, seekable stream on every successful call so
/// one reference can be opened repeatedly. Content integrity is not the
/// provider's claim to make: the loader validates the declared digest and
/// assembly name, gated by
/// <c>WorkspaceContextLoaderTests.EmbeddedDigestMismatch_CreatesNoGroup</c>
/// and <c>WorkspaceContextLoaderTests.EmbeddedNameMismatch_CreatesNoGroup</c>.
/// </remarks>
public interface IEmbeddedContentProvider
{
    /// <summary>
    /// Opens the content for <paramref name="contentRef"/>, or returns false
    /// when this host has no such content.
    /// </summary>
    bool TryOpenContent(
        string contentRef,
        [NotNullWhen(true)] out Stream? content);
}
