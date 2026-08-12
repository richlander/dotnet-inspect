using System.Collections.Immutable;
using System.Security.Cryptography;

using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// Host-supplied capabilities and limits for realizing one workspace context.
/// </summary>
/// <remarks>
/// Every acquisition capability is passed in, so the loader reads no ambient
/// source configuration and depends on no filesystem cache. A browser host
/// supplies its own <see cref="System.Net.Http.HttpClient"/>, a
/// <see cref="UniformPackageSourceAuthorization"/> over the feeds it has
/// already authorized, and an <see cref="InMemoryPackageStore"/>; a desktop
/// host supplies <see cref="SourcePolicyPackageSourceAuthorization"/>, which
/// answers from the normal source and package-source-mapping policy, and may
/// supply <see cref="FileSystemPackageStore"/>.
/// </remarks>
public sealed record WorkspaceContextLoadOptions
{
    /// <summary>The host's HTTP client for package acquisition.</summary>
    public required HttpClient HttpClient { get; init; }

    /// <summary>
    /// The host's decision about which producers may serve each package id.
    /// </summary>
    /// <remarks>
    /// It is consulted once per package member, with that member's own id,
    /// before any discovery, cache read, or download for that member. A single
    /// union of every source the context might use would be the wrong shape:
    /// NuGet authorizes producers per package id, so a union would let one
    /// member's private feed answer for another member's package. The loader
    /// neither discovers nor widens what this returns, and an authorization
    /// naming no producer is a typed
    /// <see cref="WorkspaceContextLoadFailureKind.PackageUnavailable"/> rather
    /// than a fallback to a default feed.
    /// </remarks>
    public required IPackageSourceAuthorization SourceAuthorization { get; init; }

    /// <summary>Where acquired package payloads are cached and read back.</summary>
    public required IPackageStore PackageStore { get; init; }

    /// <summary>
    /// The host's embedded-content access. A context with an embedded member
    /// fails visibly when the host supplies none.
    /// </summary>
    public IEmbeddedContentProvider? EmbeddedContent { get; init; }

    /// <summary>Optional progress log.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>Whether floating members may select a prerelease version.</summary>
    public bool IncludePrerelease { get; init; }

    /// <summary>
    /// Whether floating version discovery may consult the on-disk candidate
    /// cache. It stays off by default so the loader has no filesystem
    /// dependency a browser host cannot satisfy.
    /// </summary>
    public bool UseVersionCache { get; init; }

    /// <summary>
    /// The bounds a downloaded package payload must respect before it may be
    /// published into <see cref="PackageStore"/>.
    /// </summary>
    public PackagePayloadLimits PayloadLimits { get; init; } =
        PackagePayloadLimits.Default;

    /// <summary>The created group's cumulative retained-image budget.</summary>
    public long MaxRetainedImageBytes { get; init; } =
        AssemblyContextGroupOptions.DefaultMaxRetainedImageBytes;

    /// <summary>
    /// The largest embedded content the loader will read while validating a
    /// digest. Larger content is a typed failure, not an unbounded read.
    /// </summary>
    public long MaxEmbeddedContentBytes { get; init; } =
        AssemblyContextGroupOptions.DefaultMaxRetainedImageBytes;
}

/// <summary>
/// Realizes one workspace context into exactly one binding-consistent
/// <see cref="AssemblyContextGroup"/>.
/// </summary>
/// <remarks>
/// <para>
/// The loader is the only place a coordinate becomes provenance. It validates
/// every coordinate and the context's single acquisition target before
/// acquiring anything, asks the host which producers may serve each package id
/// before doing any work for that member, resolves floating package coordinates
/// through the product's listing-aware source and version policy while exact
/// pins bypass discovery entirely, acquires payloads through the caller's
/// package store, and selects one asset universe per package. A package
/// coordinate names no assembly, so it realizes every managed non-resource
/// assembly in that universe, matching how the existing package workspaces
/// treat a package.
/// </para>
/// <para>
/// Expected failures — an unauthorized or unavailable source, a package
/// without applicable assets, missing or corrupt embedded content, a declared
/// name that the image contradicts — are typed outcomes. No group is created
/// when any member fails, so a context never lowers to a subset of itself.
/// Cancellation remains an exception rather than an outcome.
/// </para>
/// <para>
/// Participants share one binding-policy snapshot:
/// <see cref="SourceRelativeAssemblyGroupBindingPolicy"/> over the realized
/// descriptors, with <see cref="NoResolverAssemblyBindingPolicy"/> beneath it.
/// That is the correct contract here — the loader acquires no dependency
/// outside the context, so an in-context identity binds to its own descriptor
/// while every other reference is a typed non-selection instead of a
/// filesystem probe.
/// </para>
/// <para>
/// Gated by <c>WorkspaceContextLoaderTests</c>:
/// <c>PackageMember_RealizesEveryManagedAssemblyInOneGroup</c> for one group
/// per context and package-wide realization,
/// <c>Group_BindsAnInContextReferenceToItsOwnDescriptor</c> for binding
/// consistency, <c>ConflictingTargets_CreateNoGroup</c> and
/// <c>PackageMemberWithoutAFramework_ReportsAMissingTarget</c> for target
/// consistency, <c>FloatingMember_UsesTheListingAwareVersionPolicy</c> and
/// <c>ExactPin_SelectsAnUnlistedVersionWithoutDiscovery</c> for the version
/// policy,
/// <c>PerPackageAuthorization_KeepsEachPackageOnItsOwnProducer</c> and
/// <c>PerPackageAuthorization_RefusesAProducerAuthorizedForAnotherPackage</c>
/// for package-specific authorization,
/// <c>RealizedCoordinate_NamesTheProducerThatServedTheBytes</c> for
/// producer-bound realized identity,
/// <c>InvalidTargetText_IsRejectedBeforeAnyAcquisition</c> and
/// <c>InvalidPackageId_IsRejectedBeforeAnyAcquisition</c> for the front door,
/// and the embedded digest, name, absence, and malformed-image cases for the
/// no-partial-group rule.
/// </para>
/// </remarks>
public static class WorkspaceContextLoader
{
    /// <summary>
    /// Realizes <paramref name="context"/> into one group owned by
    /// <paramref name="workspace"/>, or returns the typed reasons it could not
    /// be realized.
    /// </summary>
    public static async Task<WorkspaceContextLoadOutcome> LoadAsync(
        InspectionWorkspace workspace,
        WorkspaceContextInput context,
        WorkspaceContextLoadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.HttpClient);
        ArgumentNullException.ThrowIfNull(options.SourceAuthorization);
        ArgumentNullException.ThrowIfNull(options.PackageStore);
        ArgumentNullException.ThrowIfNull(options.PayloadLimits);
        ArgumentOutOfRangeException.ThrowIfNegative(
            options.MaxEmbeddedContentBytes);
        cancellationToken.ThrowIfCancellationRequested();

        ImmutableArray<WorkspaceContextLoadFailure> rejections =
            Validate(context, out string? framework, out string? rid);
        if (!rejections.IsEmpty)
            return new WorkspaceContextLoadOutcome.Failed(rejections);

        var realized =
            ImmutableArray.CreateBuilder<RealizedMember>();
        foreach (WorkspaceMemberCoordinate member in context.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MemberRealization realization = member switch
            {
                WorkspaceMemberCoordinate.PackageMember package =>
                    await RealizePackageAsync(
                        package,
                        framework!,
                        rid,
                        options,
                        cancellationToken).ConfigureAwait(false),
                WorkspaceMemberCoordinate.EmbeddedMember embedded =>
                    RealizeEmbedded(embedded, options, cancellationToken),
                _ => new MemberRealization(
                    Failure(
                        WorkspaceContextLoadFailureKind.InvalidCoordinate,
                        member,
                        "The member coordinate kind is not supported.")),
            };

            if (realization.Failure is { } failure)
                return new WorkspaceContextLoadOutcome.Failed([failure]);

            foreach (ResolvedAssemblyReference assembly
                in realization.Assemblies)
            {
                // Every participant from one member shares that member's one
                // exact realized coordinate.
                realized.Add(
                    new RealizedMember(
                        member,
                        realization.Realized!,
                        assembly));
            }
        }

        var groupPolicy = new SourceRelativeAssemblyGroupBindingPolicy(
            realized.Select(static entry =>
                (entry.Assembly,
                    (IAssemblyBindingPolicy)
                        NoResolverAssemblyBindingPolicy.Instance)));
        List<AssemblyContextParticipant> participants =
        [
            .. realized.Select(entry =>
                new AssemblyContextParticipant(entry.Assembly, groupPolicy)),
        ];
        AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            participants,
            new AssemblyContextGroupOptions
            {
                MaxRetainedImageBytes = options.MaxRetainedImageBytes,
            });

        var members =
            ImmutableArray.CreateBuilder<WorkspaceContextMember>(
                realized.Count);
        for (int index = 0; index < realized.Count; index++)
        {
            members.Add(
                new WorkspaceContextMember(
                    realized[index].Declared,
                    realized[index].Realized,
                    participants[index]));
        }

        return new WorkspaceContextLoadOutcome.Loaded(
            group,
            members.MoveToImmutable(),
            framework,
            rid);
    }

    static ImmutableArray<WorkspaceContextLoadFailure> Validate(
        WorkspaceContextInput context,
        out string? framework,
        out string? runtimeIdentifier)
    {
        framework = null;
        runtimeIdentifier = null;
        var failures =
            ImmutableArray.CreateBuilder<WorkspaceContextLoadFailure>();

        if (context.Members is null || context.Members.Count == 0)
        {
            failures.Add(
                Failure(
                    WorkspaceContextLoadFailureKind.EmptyContext,
                    member: null,
                    "A workspace context declares at least one member."));
            return failures.ToImmutable();
        }

        if (IsInvalidOptionalTarget(context.Framework)
            || IsInvalidOptionalTarget(context.RuntimeIdentifier))
        {
            failures.Add(
                Failure(
                    WorkspaceContextLoadFailureKind.InvalidCoordinate,
                    member: null,
                    "A context acquisition target cannot be empty, have surrounding whitespace, or carry a control character."));
        }

        List<string> frameworks = [];
        List<string> rids = [];
        AddTarget(frameworks, context.Framework);
        AddTarget(rids, context.RuntimeIdentifier);
        bool hasPackageMember = false;

        foreach (WorkspaceMemberCoordinate member in context.Members)
        {
            if (member is null)
            {
                failures.Add(
                    Failure(
                        WorkspaceContextLoadFailureKind.InvalidCoordinate,
                        member: null,
                        "A workspace context member cannot be absent."));
                continue;
            }

            switch (member)
            {
                case WorkspaceMemberCoordinate.PackageMember package:
                    hasPackageMember = true;
                    if (PackageCoordinateResolver.Validate(
                            new PackageCoordinate(
                                package.PackageId,
                                package.Version,
                                package.Framework,
                                package.RuntimeIdentifier))
                        is { } invalid)
                    {
                        failures.Add(
                            Failure(
                                WorkspaceContextLoadFailureKind
                                    .InvalidCoordinate,
                                member,
                                invalid.Message));
                        break;
                    }

                    AddTarget(frameworks, package.Framework);
                    AddTarget(rids, package.RuntimeIdentifier);
                    break;

                case WorkspaceMemberCoordinate.EmbeddedMember embedded:
                    if (ValidateEmbedded(embedded) is { } embeddedFailure)
                        failures.Add(embeddedFailure);
                    break;

                default:
                    failures.Add(
                        Failure(
                            WorkspaceContextLoadFailureKind.InvalidCoordinate,
                            member,
                            "The member coordinate kind is not supported."));
                    break;
            }
        }

        if (frameworks.Count > 1)
        {
            failures.Add(
                Failure(
                    WorkspaceContextLoadFailureKind
                        .ConflictingAcquisitionTarget,
                    member: null,
                    "A workspace context lowers to one acquisition framework, and its declarations disagree."));
        }

        if (rids.Count > 1)
        {
            failures.Add(
                Failure(
                    WorkspaceContextLoadFailureKind
                        .ConflictingAcquisitionTarget,
                    member: null,
                    "A workspace context lowers to one acquisition runtime identifier, and its declarations disagree."));
        }

        if (hasPackageMember && frameworks.Count == 0)
        {
            failures.Add(
                Failure(
                    WorkspaceContextLoadFailureKind.MissingAcquisitionTarget,
                    member: null,
                    "A package member requires the context to state an acquisition framework."));
        }

        if (failures.Count == 0)
        {
            framework = frameworks.Count == 1 ? frameworks[0] : null;
            runtimeIdentifier = rids.Count == 1 ? rids[0] : null;
        }

        return failures.ToImmutable();

        static void AddTarget(List<string> targets, string? value)
        {
            if (value is null || IsBlankOrPadded(value))
                return;

            if (!targets.Any(target => string.Equals(
                    target,
                    value,
                    StringComparison.OrdinalIgnoreCase)))
            {
                targets.Add(value!);
            }
        }
    }

    static WorkspaceContextLoadFailure? ValidateEmbedded(
        WorkspaceMemberCoordinate.EmbeddedMember embedded)
    {
        if (!RealizedMemberCoordinate.IsCanonicalContentRef(
                embedded.ContentRef))
        {
            return Failure(
                WorkspaceContextLoadFailureKind.InvalidCoordinate,
                embedded,
                "An embedded coordinate requires a bundle-relative, '/'-separated content reference with no empty, '.', or '..' segment.");
        }

        if (!RealizedMemberCoordinate.IsCanonicalDigest(embedded.Digest))
        {
            return Failure(
                WorkspaceContextLoadFailureKind.InvalidCoordinate,
                embedded,
                "An embedded coordinate requires a canonical lowercase hexadecimal SHA-256 digest.");
        }

        if (!RealizedMemberCoordinate.IsAssemblySimpleName(
                embedded.DeclaredName))
        {
            return Failure(
                WorkspaceContextLoadFailureKind.InvalidCoordinate,
                embedded,
                "An embedded coordinate requires an assembly simple name.");
        }

        return null;
    }

    static async Task<MemberRealization> RealizePackageAsync(
        WorkspaceMemberCoordinate.PackageMember member,
        string framework,
        string? runtimeIdentifier,
        WorkspaceContextLoadOptions options,
        CancellationToken cancellationToken)
    {
        // Authorization is resolved for this member's own id, and before any
        // discovery, cache read, or download for it. The canonical id is what
        // the host is asked about, so one package has one authorization answer
        // regardless of how the context spelled it.
        PackageSourceAuthorization authorization =
            options.SourceAuthorization.AuthorizeSourcesFor(
                member.PackageId.ToLowerInvariant());
        if (authorization.Sources.Count == 0)
        {
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.PackageUnavailable,
                    member,
                    authorization.DenialReason
                    ?? $"No source is authorized to provide package '{member.PackageId}'."));
        }

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                options.HttpClient,
                new PackageCoordinate(
                    member.PackageId,
                    member.Version,
                    framework,
                    runtimeIdentifier),
                authorization.Sources,
                options.Log,
                options.IncludePrerelease,
                options.UseVersionCache,
                cancellationToken).ConfigureAwait(false);
        switch (resolution)
        {
            case PackageCoordinateResolution.Invalid invalid:
                return new MemberRealization(
                    Failure(
                        WorkspaceContextLoadFailureKind.InvalidCoordinate,
                        member,
                        invalid.Message));
            case PackageCoordinateResolution.Unavailable unavailable:
                return new MemberRealization(
                    Failure(
                        WorkspaceContextLoadFailureKind.PackageUnavailable,
                        member,
                        unavailable.Message));
        }

        ResolvedPackageCoordinate coordinate =
            ((PackageCoordinateResolution.Resolved)resolution).Coordinate;
        PackagePayloadResult payload =
            await PackagePayloadAcquisition.AcquireAsync(
                options.HttpClient,
                coordinate,
                options.PackageStore,
                options.Log,
                options.PayloadLimits,
                cancellationToken).ConfigureAwait(false);
        if (payload is PackagePayloadResult.Unavailable payloadFailure)
        {
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.PackageUnavailable,
                    member,
                    payloadFailure.Message));
        }

        AcquiredPackagePayload acquired =
            ((PackagePayloadResult.Acquired)payload).Payload;
        IPackageContent content = acquired.Content;
        PackageAssetSelection selection = PackageAssetSelector.Select(
            content,
            framework,
            runtimeIdentifier);
        if (selection is PackageAssetSelection.Ambiguous ambiguous)
        {
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.PackageAssetAmbiguous,
                    member,
                    ambiguous.Message));
        }

        if (selection is not PackageAssetSelection.Selected selected)
        {
            string message = selection switch
            {
                PackageAssetSelection.NoMatch noMatch => noMatch.Message,
                PackageAssetSelection.Invalid invalidLayout =>
                    invalidLayout.Message,
                _ => "The package assets could not be selected.",
            };
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.PackageAssetUnavailable,
                    member,
                    message));
        }

        var assemblies =
            ImmutableArray.CreateBuilder<ResolvedAssemblyReference>();
        foreach (PackageAssetEntry asset in selected.Universe.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyResolutionProvenance provenance =
                AssemblyResolutionProvenance.Package(
                    coordinate.PackageId,
                    coordinate.Version,
                    selected.Universe.TargetFramework,
                    asset.RuntimeIdentifier);
            try
            {
                // A package entry that carries no managed metadata is not an
                // assembly, matching CreateFromPathIfManaged. Malformed
                // managed metadata stays a visible failure.
                if (ResolvedAssemblyReference.CreateFromStreamIfManaged(
                        () => OpenPackageEntry(content, asset.EntryPath),
                        provenance)
                    is { } assembly)
                {
                    assemblies.Add(assembly);
                }
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or OverflowException)
            {
                return new MemberRealization(
                    Failure(
                        WorkspaceContextLoadFailureKind.InvalidImage,
                        member,
                        $"A selected assembly asset in package '{coordinate.PackageId}' contains invalid metadata."));
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or ObjectDisposedException)
            {
                return new MemberRealization(
                    Failure(
                        WorkspaceContextLoadFailureKind.InvalidImage,
                        member,
                        $"A selected assembly asset in package '{coordinate.PackageId}' could not be read."));
            }
        }

        if (assemblies.Count == 0)
        {
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.PackageAssetUnavailable,
                    member,
                    $"Package '{coordinate.PackageId}' carries no managed assembly for the acquisition target."));
        }

        return new MemberRealization(
            new RealizedMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                acquired.ProducerKey,
                framework,
                runtimeIdentifier),
            assemblies.ToImmutable());
    }

    static MemberRealization RealizeEmbedded(
        WorkspaceMemberCoordinate.EmbeddedMember member,
        WorkspaceContextLoadOptions options,
        CancellationToken cancellationToken)
    {
        if (options.EmbeddedContent is not { } provider)
        {
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.HostCapabilityUnavailable,
                    member,
                    "This host supplies no embedded content, so an embedded member cannot be realized."));
        }

        if (!provider.TryOpenContent(member.ContentRef, out Stream? source))
        {
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.EmbeddedContentUnavailable,
                    member,
                    $"No embedded content is available for '{member.ContentRef}'."));
        }

        byte[] bytes;
        using (source)
        {
            if (ReadBounded(
                    source,
                    options.MaxEmbeddedContentBytes,
                    cancellationToken)
                is not { } content)
            {
                return new MemberRealization(
                    Failure(
                        WorkspaceContextLoadFailureKind
                            .EmbeddedContentUnavailable,
                        member,
                        $"Embedded content '{member.ContentRef}' exceeds the configured content limit."));
            }

            bytes = content;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(bytes),
                Convert.FromHexString(member.Digest)))
        {
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.EmbeddedDigestMismatch,
                    member,
                    $"Embedded content '{member.ContentRef}' does not match its declared digest."));
        }

        AssemblyResolutionProvenance provenance =
            AssemblyResolutionProvenance.Embedded(
                member.ContentRef,
                member.Digest.ToLowerInvariant(),
                member.DeclaredName);
        ResolvedAssemblyReference? assembly;
        try
        {
            assembly = ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => new MemoryStream(bytes, writable: false),
                provenance);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.InvalidImage,
                    member,
                    $"Embedded content '{member.ContentRef}' contains invalid metadata."));
        }

        if (assembly is null)
        {
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.InvalidImage,
                    member,
                    $"Embedded content '{member.ContentRef}' is not a managed assembly."));
        }

        if (!string.Equals(
                assembly.Identity.Name,
                member.DeclaredName,
                StringComparison.OrdinalIgnoreCase))
        {
            // The image's own name is artifact-derived and is not echoed.
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.EmbeddedNameMismatch,
                    member,
                    $"Embedded content '{member.ContentRef}' is not assembly '{member.DeclaredName}'."));
        }

        return new MemberRealization(
            new RealizedMemberCoordinate.Embedded(
                member.ContentRef,
                member.Digest,
                member.DeclaredName),
            [assembly]);
    }

    static byte[]? ReadBounded(
        Stream source,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.CanSeek && source.Length - source.Position > maxBytes)
            return null;

        var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            read = source.Read(chunk, 0, chunk.Length);
            if (read == 0)
                break;

            if (buffer.Length + read > maxBytes)
                return null;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    static Stream OpenPackageEntry(
        IPackageContent content,
        string entryPath)
        => content.TryOpenEntry(entryPath, out Stream? stream)
            ? stream
            : throw new IOException(
                "The selected package asset is no longer available.");

    static WorkspaceContextLoadFailure Failure(
        WorkspaceContextLoadFailureKind kind,
        WorkspaceMemberCoordinate? member,
        string message) =>
        new(kind, member, message);

    static bool IsBlankOrPadded(string? value) =>
        !PackageCoordinateResolver.IsAcquisitionTargetText(value);

    static bool IsInvalidOptionalTarget(string? value) =>
        value is not null && IsBlankOrPadded(value);

    readonly record struct MemberRealization
    {
        internal MemberRealization(
            RealizedMemberCoordinate realized,
            ImmutableArray<ResolvedAssemblyReference> assemblies)
        {
            Realized = realized;
            Assemblies = assemblies;
            Failure = null;
        }

        internal MemberRealization(WorkspaceContextLoadFailure failure)
        {
            Realized = null;
            Assemblies = [];
            Failure = failure;
        }

        internal RealizedMemberCoordinate? Realized { get; }
        internal ImmutableArray<ResolvedAssemblyReference> Assemblies { get; }
        internal WorkspaceContextLoadFailure? Failure { get; }
    }

    readonly record struct RealizedMember(
        WorkspaceMemberCoordinate Declared,
        RealizedMemberCoordinate Realized,
        ResolvedAssemblyReference Assembly);
}
