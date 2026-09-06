using System.Diagnostics.CodeAnalysis;
using System.Text;

using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// One caller-authored request for an exact package Root that has not been
/// acquired yet.
/// </summary>
/// <remarks>
/// <para>
/// This is the entry a streaming consumer uses for its initial explicit
/// <c>ID@VERSION</c> coordinates. The version is required: this owner performs
/// no gallery, prefix, or floating-version discovery, and a request that did
/// not name an exact version would be asking for one.
/// </para>
/// <para>
/// Unlike <see cref="PackageRootReacquisitionRequest"/>, this request names no
/// producer, because which authorized producer serves the bytes is decided by
/// the destination host's policy at acquisition time. The acquired outcome
/// carries the producer-pinned reacquisition request issued for the Root that
/// actually resulted.
/// </para>
/// <para>
/// Gated by
/// <c>PackageRootAcquisitionTests.ExplicitCoordinate_AcquiresRootAndIssuesExactRequest</c>.
/// </para>
/// </remarks>
public sealed class PackageRootAcquisitionRequest
{
    PackageRootAcquisitionRequest(
        string packageId,
        string version,
        string? acquisitionFramework,
        string? selectionTargetFramework,
        string? selectionRuntimeIdentifier)
    {
        PackageId = packageId;
        Version = version;
        AcquisitionFramework = acquisitionFramework;
        SelectionTargetFramework = selectionTargetFramework;
        SelectionRuntimeIdentifier = selectionRuntimeIdentifier;
    }

    /// <summary>Creates a request for one exact package id and version.</summary>
    /// <param name="selectionTargetFramework">
    /// The compile-asset selection target, which is also the acquisition
    /// framework. This is the ordinary single-target shape.
    /// </param>
    public static PackageRootAcquisitionRequest Create(
        string packageId,
        string version,
        string? selectionTargetFramework = null,
        string? selectionRuntimeIdentifier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        // The same input contract PackageRootBinding.CreateFromSource states,
        // so an explicit coordinate and a bound Root cannot disagree about
        // which target spellings this owner accepts.
        string? acquisitionFramework =
            PackageRootBinding.SourceAcquisitionFramework(
                selectionTargetFramework);
        if (selectionRuntimeIdentifier is not null)
        {
            if (!RealizedMemberCoordinate.IsCanonicalRuntimeIdentifier(
                    selectionRuntimeIdentifier))
            {
                throw new ArgumentException(
                    "A package Root runtime identifier must be a canonical lowercase moniker.",
                    nameof(selectionRuntimeIdentifier));
            }
            if (acquisitionFramework is null)
            {
                throw new ArgumentException(
                    "A package Root runtime identifier requires a canonical acquisition framework.",
                    nameof(selectionTargetFramework));
            }
        }

        return new(
            packageId,
            version,
            acquisitionFramework,
            selectionTargetFramework,
            selectionRuntimeIdentifier);
    }

    /// <summary>
    /// Creates a request whose acquisition is framework-neutral while its
    /// compile-asset selection target stays exact.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the shape a realized coordinate alone cannot express, and the
    /// reason the reacquisition request keeps the two facts apart: the
    /// producer is asked for the package itself rather than for a
    /// framework-targeted acquisition, and the selection target still decides
    /// which compile assets the Root exposes.
    /// </para>
    /// <para>
    /// A runtime identifier is not accepted here, because a realized
    /// coordinate's runtime identifier requires an acquisition framework.
    /// </para>
    /// </remarks>
    public static PackageRootAcquisitionRequest CreateFrameworkNeutral(
        string packageId,
        string version,
        string selectionTargetFramework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionTargetFramework);
        return new(
            packageId,
            version,
            acquisitionFramework: null,
            selectionTargetFramework,
            selectionRuntimeIdentifier: null);
    }

    /// <summary>The requested package id.</summary>
    public string PackageId { get; }

    /// <summary>The requested exact version.</summary>
    public string Version { get; }

    /// <summary>
    /// The acquisition framework derived from the selection target, or
    /// <see langword="null"/> for framework-neutral acquisition.
    /// </summary>
    public string? AcquisitionFramework { get; }

    /// <summary>The compile-asset selection target framework.</summary>
    public string? SelectionTargetFramework { get; }

    /// <summary>The compile-asset selection runtime identifier.</summary>
    public string? SelectionRuntimeIdentifier { get; }
}

/// <summary>
/// One acquisition-issued, immutable, resource-free request that repeats the
/// exact logical package Root behind a <see cref="PackageRootBinding"/>.
/// </summary>
/// <remarks>
/// <para>
/// The request preserves two separate owner facts: the realized
/// producer-pinned acquisition coordinate, whose
/// <see cref="RealizedMemberCoordinate.Package.Framework"/> may be absent for
/// framework-neutral source acquisition, and the normalized selection target
/// framework and runtime identifier that produced the binding's frozen
/// compile-asset selection. It is therefore usable where the realized
/// coordinate alone would fail with
/// <see cref="WorkspaceContextLoadFailureKind.MissingAcquisitionTarget"/> or
/// select a different asset universe.
/// </para>
/// <para>
/// The request carries no package content, generation identity, selection
/// identity, workspace identity, session, lease, callback, opener, or path
/// authority, and no credential. It may observe a replacement physical
/// generation but never silently changes the requested target. It is distinct
/// from Workspace-scoped <see cref="PackageArtifactRootCorrespondence"/>,
/// which cannot open a fresh Workspace after a candidate closes. Hosts pass
/// the issued value opaquely — as the value itself in-process, or as the
/// owner-authored token from <see cref="Encode"/> across a transport boundary
/// — and never reconstruct it from package id, target framework, runtime
/// identifier, asset path, or other display fields. Gated by
/// <c>SparsePackageAssemblyProjectionTests.ReacquisitionRequest_IsExactResourceFreeAndSeparatesTargets</c>
/// and <c>ReacquisitionRequest_SurvivesCandidateWorkspaceDisposal</c>.
/// </para>
/// </remarks>
public sealed class PackageRootReacquisitionRequest :
    IEquatable<PackageRootReacquisitionRequest>
{
    /// <summary>The current opaque token format tag.</summary>
    public const string TokenPrefix = "pkgroot1";

    /// <summary>The largest token this owner encodes or decodes.</summary>
    public const int MaxEncodedLength = 1024;

    const int FieldCount = 7;

    static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    readonly PackageArtifactRootRequest _request;

    internal PackageRootReacquisitionRequest(
        PackageArtifactRootRequest request)
    {
        _request = request;
    }

    /// <summary>The realized producer-pinned package acquisition coordinate.</summary>
    public RealizedMemberCoordinate.Package Coordinate => _request.Coordinate;

    /// <summary>
    /// The normalized compile-asset selection target framework, or
    /// <see langword="null"/> when the binding requested none.
    /// </summary>
    public string? SelectionTargetFramework =>
        _request.SelectionTargetFramework;

    /// <summary>The normalized compile-asset selection runtime identifier.</summary>
    public string? SelectionRuntimeIdentifier =>
        _request.SelectionRuntimeIdentifier;

    /// <summary>
    /// Encodes this request as one owner-authored, credential-free token a
    /// host may hand across a transport boundary and back.
    /// </summary>
    /// <remarks>
    /// The token is opaque to hosts: its format tag, field order, and encoding
    /// are this owner's, and only <see cref="TryDecode"/> reads it. It carries
    /// the same facts the request does and no content, generation, workspace
    /// identity, lease, path, source URL, or credential. Gated by
    /// <c>PackageRootAcquisitionTests.Token_RoundTripsExactRequest</c>.
    /// </remarks>
    public string Encode()
    {
        var builder = new StringBuilder(TokenPrefix);
        AppendField(builder, Coordinate.PackageId);
        AppendField(builder, Coordinate.Version);
        AppendField(builder, Coordinate.Producer);
        AppendField(builder, Coordinate.Framework);
        AppendField(builder, Coordinate.RuntimeIdentifier);
        AppendField(builder, SelectionTargetFramework);
        AppendField(builder, SelectionRuntimeIdentifier);
        if (builder.Length > MaxEncodedLength)
        {
            throw new InvalidOperationException(
                $"A package Root reacquisition token exceeds the {MaxEncodedLength}-character limit.");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decodes a token this owner encoded, or returns <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The token may arrive from an untrusted transport, so decoding is total:
    /// a malformed, over-long, non-canonical, or forged token returns
    /// <see langword="false"/> rather than throwing or producing a value this
    /// owner would not have issued. Every field is revalidated through the
    /// owner's own canonical coordinate and request construction.
    /// </para>
    /// <para>
    /// A decoded request is a request, not an authorization. It grants no
    /// access on its own: acquiring the Root it names still passes through the
    /// destination host's own source authorization, transfer policy, and
    /// payload limits. Gated by
    /// <c>PackageRootAcquisitionTests.Token_RejectsMalformedOrNonCanonicalInput</c>.
    /// </para>
    /// </remarks>
    public static bool TryDecode(
        string? encoded,
        [NotNullWhen(true)] out PackageRootReacquisitionRequest? request)
    {
        request = null;
        if (encoded is null
            || encoded.Length is 0 or > MaxEncodedLength)
        {
            return false;
        }

        string[] parts = encoded.Split('.');
        if (parts.Length != FieldCount + 1
            || !string.Equals(parts[0], TokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fields = new string?[FieldCount];
        for (int index = 0; index < FieldCount; index++)
        {
            if (!TryReadField(parts[index + 1], out fields[index]))
                return false;
        }
        if (fields[0] is not { } packageId
            || fields[1] is not { } version
            || fields[2] is not { } producer)
        {
            return false;
        }
        if (!RealizedMemberCoordinate.Package.TryCreate(
                packageId,
                version,
                producer,
                fields[3],
                fields[4],
                out RealizedMemberCoordinate.Package? coordinate,
                out _))
        {
            return false;
        }

        PackageArtifactRootRequest decoded = PackageArtifactRootRequest.Create(
            coordinate,
            fields[5],
            fields[6]);

        // A token that is not already canonical is refused rather than
        // silently normalized, so one request has exactly one token.
        if (!string.Equals(
                decoded.SelectionTargetFramework,
                fields[5],
                StringComparison.Ordinal)
            || !string.Equals(
                decoded.SelectionRuntimeIdentifier,
                fields[6],
                StringComparison.Ordinal))
        {
            return false;
        }

        request = new PackageRootReacquisitionRequest(decoded);
        return true;
    }

    public bool Equals(PackageRootReacquisitionRequest? other) =>
        other is not null && _request == other._request;

    public override bool Equals(object? obj) =>
        Equals(obj as PackageRootReacquisitionRequest);

    public override int GetHashCode() => _request.GetHashCode();

    public override string ToString() =>
        $"{Coordinate.PackageId}@{Coordinate.Version}";

    internal bool Matches(PackageArtifactRootRequest request) =>
        _request == request;

    static void AppendField(StringBuilder builder, string? value)
    {
        builder.Append('.');
        if (string.IsNullOrEmpty(value))
            return;

        builder.Append(
            Convert.ToBase64String(StrictUtf8.GetBytes(value))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_'));
    }

    static bool TryReadField(string encoded, out string? value)
    {
        value = null;
        if (encoded.Length == 0)
            return true;

        int remainder = encoded.Length % 4;
        if (remainder == 1)
            return false;

        int padding = remainder == 0 ? 0 : 4 - remainder;
        char[] base64 = new char[encoded.Length + padding];
        for (int index = 0; index < encoded.Length; index++)
        {
            char character = encoded[index];
            switch (character)
            {
                case >= 'A' and <= 'Z':
                case >= 'a' and <= 'z':
                case >= '0' and <= '9':
                    base64[index] = character;
                    break;
                case '-':
                    base64[index] = '+';
                    break;
                case '_':
                    base64[index] = '/';
                    break;
                default:
                    return false;
            }
        }
        for (int index = encoded.Length; index < base64.Length; index++)
            base64[index] = '=';

        byte[] decoded = new byte[base64.Length / 4 * 3];
        if (!Convert.TryFromBase64Chars(base64, decoded, out int written)
            || written == 0)
        {
            return false;
        }

        try
        {
            value = StrictUtf8.GetString(decoded.AsSpan(0, written));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return true;
    }
}

/// <summary>Why a package Root acquisition did not produce a Root.</summary>
public enum PackageRootAcquisitionFailureKind
{
    /// <summary>The requested coordinate is not resolvable as stated.</summary>
    InvalidCoordinate,

    /// <summary>
    /// No authorized producer resolved or served the requested coordinate.
    /// </summary>
    PackageUnavailable,

    /// <summary>
    /// The exact producer the request pins is not authorized by this host for
    /// that package, or did not serve the bytes that arrived.
    /// </summary>
    ProducerNotAuthorized,

    /// <summary>
    /// Reacquired content produced a Root whose exact logical request differs
    /// from the one asked for.
    /// </summary>
    SelectionRequestNotReproduced,
}

/// <summary>The typed result of one package Root acquisition.</summary>
/// <remarks>
/// Cancellation is not an outcome arm; it propagates with the caller's token.
/// </remarks>
public abstract class PackageRootAcquisitionOutcome
{
    private protected PackageRootAcquisitionOutcome()
    {
    }

    /// <summary>One newly bound Root and the exact request that repeats it.</summary>
    /// <remarks>
    /// <para>
    /// The binding's <see cref="PackageRootRealization.AssetSelection"/> keeps
    /// the package owner's typed selection status, so content that does not
    /// satisfy the selection target reports that owner-typed selection failure
    /// rather than a neighboring asset set. <see cref="Request"/> is the
    /// producer-pinned request issued for this Root, so a consumer acquiring
    /// from an explicit coordinate holds a reacquirable handle immediately and
    /// never has to rebuild one from display fields.
    /// </para>
    /// <para>
    /// This outcome is intentionally live and resource-bearing. The
    /// resource-free restriction is a property of <see cref="Request"/>, which
    /// is the value a host may retain and transport; it does not apply to the
    /// acquisition result itself. <see cref="Payload"/> is therefore the real
    /// acquired payload, and <c>Payload.Content</c> is the exact
    /// <see cref="IPackageContent"/> instance <see cref="Binding"/> reads
    /// through — verifiable with
    /// <see cref="PackageRootRealization.ReferencesContent"/>.
    /// </para>
    /// <para>
    /// A destination that wraps the acquired package adapts that instance
    /// rather than rebuilding one. Constructing a second content handle over
    /// copied archive bytes would copy the payload and mint a second
    /// <see cref="PackageContentGenerationIdentity"/>, which would make the
    /// wrapper and the binding disagree about which retained generation they
    /// name. Gated by
    /// <c>PackageRootAcquisitionTests.Acquired_ExposesTheLiveContentTheBindingReads</c>.
    /// </para>
    /// </remarks>
    public sealed class Acquired : PackageRootAcquisitionOutcome
    {
        internal Acquired(
            PackageRootBinding binding,
            AcquiredPackagePayload payload,
            PackageRootReacquisitionRequest request)
        {
            Binding = binding;
            Payload = payload;
            Request = request;
        }

        public PackageRootBinding Binding { get; }

        /// <summary>
        /// The live acquired payload the binding was formed from, including the
        /// exact retained content instance, its producer identity, and whether
        /// a store entry or a download answered.
        /// </summary>
        public AcquiredPackagePayload Payload { get; }

        public PackageRootReacquisitionRequest Request { get; }
    }

    /// <summary>A visible source, authorization, or request failure.</summary>
    public sealed class Failed : PackageRootAcquisitionOutcome
    {
        internal Failed(
            PackageRootAcquisitionFailureKind kind,
            string message)
        {
            Kind = kind;
            Message = message;
        }

        public PackageRootAcquisitionFailureKind Kind { get; }

        public string Message { get; }
    }
}

/// <summary>
/// Acquires one package Root under a host's own acquisition capabilities and
/// policy, either from an explicit coordinate or from an owner-issued exact
/// reacquisition request.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately narrower than
/// <see cref="WorkspaceContextLoader.LoadRealizedAsync"/>: it realizes no
/// assembly role universe, requires no acquisition target, and creates no
/// workspace group. Both entries share one authorization, resolution, and
/// payload-acquisition path — the same
/// <see cref="PackageCoordinateResolver"/> and
/// <see cref="PackagePayloadAcquisition"/> the loader uses — so neither invents
/// a source policy. Source authorization, transfer policy, payload limits, and
/// the package store remain the destination host's own; neither request form
/// carries them.
/// </para>
/// <para>
/// Gated by
/// <c>PackageRootAcquisitionTests.ExplicitCoordinate_AcquiresRootAndIssuesExactRequest</c>,
/// <c>ExactRequest_ReacquiresSameLogicalRoot</c>,
/// <c>ExplicitCoordinate_UnauthorizedSourcesFailVisibly</c>, and
/// <c>ExactRequest_UnauthorizedProducerFailsVisibly</c>.
/// </para>
/// </remarks>
public static class PackageRootAcquisition
{
    /// <summary>Acquires a Root for one explicit, exact coordinate.</summary>
    public static Task<PackageRootAcquisitionOutcome> AcquireAsync(
        PackageRootAcquisitionRequest request,
        WorkspaceContextLoadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return AcquireCoreAsync(
            request.PackageId,
            request.Version,
            request.AcquisitionFramework,
            request.SelectionRuntimeIdentifier,
            request.SelectionTargetFramework,
            pinnedProducer: null,
            expected: null,
            options,
            cancellationToken);
    }

    /// <summary>Reacquires the exact logical Root an owner-issued request names.</summary>
    public static Task<PackageRootAcquisitionOutcome> AcquireAsync(
        PackageRootReacquisitionRequest request,
        WorkspaceContextLoadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RealizedMemberCoordinate.Package pinned = request.Coordinate;
        return AcquireCoreAsync(
            pinned.PackageId,
            pinned.Version,
            pinned.Framework,
            pinned.RuntimeIdentifier,
            request.SelectionTargetFramework,
            pinned.Producer,
            request,
            options,
            cancellationToken);
    }

    static async Task<PackageRootAcquisitionOutcome> AcquireCoreAsync(
        string packageId,
        string version,
        string? acquisitionFramework,
        string? runtimeIdentifier,
        string? selectionTargetFramework,
        string? pinnedProducer,
        PackageRootReacquisitionRequest? expected,
        WorkspaceContextLoadOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.HttpClient);
        ArgumentNullException.ThrowIfNull(options.SourceAuthorization);
        ArgumentNullException.ThrowIfNull(options.PackageStore);
        cancellationToken.ThrowIfCancellationRequested();

        PackageSourceAuthorization authorization =
            options.SourceAuthorization.AuthorizeSourcesFor(packageId);
        IReadOnlyList<PackageSource> sources = authorization.Sources;
        if (pinnedProducer is not null)
        {
            // The intersection, not a preference: only the producer the request
            // names may answer, so a host authorizing several producers for
            // this id still reacquires the Root the binding was realized from.
            PackageSource? producer = sources.FirstOrDefault(
                source => string.Equals(
                    NuGetCache.GetSourceKey(source.Url),
                    pinnedProducer,
                    StringComparison.Ordinal));
            if (producer is null)
            {
                return Failed(
                    PackageRootAcquisitionFailureKind.ProducerNotAuthorized,
                    authorization.DenialReason
                    ?? $"The producer recorded for package '{packageId}' is not authorized by this host.");
            }

            sources = [producer];
        }
        else if (sources.Count == 0)
        {
            return Failed(
                PackageRootAcquisitionFailureKind.PackageUnavailable,
                authorization.DenialReason
                ?? $"No producer is authorized to serve package '{packageId}'.");
        }

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                    options.HttpClient,
                    new PackageCoordinate(
                        packageId,
                        version,
                        acquisitionFramework,
                        runtimeIdentifier),
                    sources,
                    options.Log,
                    options.IncludePrerelease,
                    options.UseVersionCache,
                    requireStableFloating: true,
                    cancellationToken)
                .ConfigureAwait(false);
        switch (resolution)
        {
            case PackageCoordinateResolution.Invalid invalid:
                return Failed(
                    PackageRootAcquisitionFailureKind.InvalidCoordinate,
                    invalid.Message);
            case PackageCoordinateResolution.Unavailable unavailable:
                return Failed(
                    PackageRootAcquisitionFailureKind.PackageUnavailable,
                    unavailable.Message);
        }

        PackagePayloadResult payload =
            await PackagePayloadAcquisition.AcquireAsync(
                    options.HttpClient,
                    ((PackageCoordinateResolution.Resolved)resolution)
                        .Coordinate,
                    options.PackageStore,
                    options.Log,
                    options.PayloadLimits,
                    cancellationToken,
                    options.PackageTransferPolicy)
                .ConfigureAwait(false);
        if (payload is PackagePayloadResult.Unavailable payloadFailure)
        {
            return Failed(
                PackageRootAcquisitionFailureKind.PackageUnavailable,
                payloadFailure.Message);
        }

        AcquiredPackagePayload acquired =
            ((PackagePayloadResult.Acquired)payload).Payload;
        if (pinnedProducer is not null
            && !string.Equals(
                acquired.ProducerKey,
                pinnedProducer,
                StringComparison.Ordinal))
        {
            return Failed(
                PackageRootAcquisitionFailureKind.ProducerNotAuthorized,
                $"Package '{packageId}' was served by a producer other than the one the request names.");
        }

        PackageRootBinding binding = PackageRootBinding.CreateFromResolved(
            acquired,
            selectionTargetFramework);
        PackageRootReacquisitionRequest issued =
            binding.CreateReacquisitionRequest();
        if (expected is not null && !expected.Equals(issued))
        {
            // The reacquired Root does not repeat the exact logical request,
            // so it is reported rather than silently substituted.
            return Failed(
                PackageRootAcquisitionFailureKind
                    .SelectionRequestNotReproduced,
                $"Reacquiring package '{packageId}' produced a different exact Root request.");
        }

        return new PackageRootAcquisitionOutcome.Acquired(
            binding,
            acquired,
            issued);
    }

    static PackageRootAcquisitionOutcome Failed(
        PackageRootAcquisitionFailureKind kind,
        string message) =>
        new PackageRootAcquisitionOutcome.Failed(kind, message);
}
