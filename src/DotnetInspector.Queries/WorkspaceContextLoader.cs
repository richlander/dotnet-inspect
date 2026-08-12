using System.Collections.Immutable;
using System.Security.Cryptography;

using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGet.Versioning;
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
/// <c>RealizedLoad_ReacquiresFromTheRecordedProducer</c> and
/// <c>RealizedLoad_WithAnUnauthorizedProducer_FailsTyped</c> for
/// producer-pinned re-acquisition,
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

        if (Distinct(
                context.Members,
                member => KeyFor(member, framework, rid),
                static member => member,
                out ImmutableArray<WorkspaceMemberCoordinate> members)
            is { } duplicate)
        {
            return new WorkspaceContextLoadOutcome.Failed([duplicate]);
        }

        var realized =
            ImmutableArray.CreateBuilder<RealizedMember>();
        foreach (WorkspaceMemberCoordinate member in members)
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

        return CreateGroup(workspace, realized, options, framework, rid);
    }

    /// <summary>
    /// Re-acquires an already-realized context: each member is loaded from the
    /// exact producer its realized coordinate names, or the context fails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the other half of
    /// <see cref="RealizedMemberCoordinate.Package.Producer"/>. A realized
    /// coordinate exists so a consumer can carry it across a transport boundary
    /// and get the same bytes back; lowering it to a declared coordinate and
    /// calling <see cref="LoadAsync"/> would drop the producer, so two feeds
    /// serving one id and version would be free to answer for each other. Here
    /// the recorded producer is <em>intersected</em> with what this host
    /// authorizes for that package id, and exactly the surviving producer is
    /// consulted — never a preference among several.
    /// </para>
    /// <para>
    /// Authorization still governs. A coordinate realized elsewhere confers
    /// nothing: if this host does not authorize that producer for that package,
    /// the member is a typed
    /// <see cref="WorkspaceContextLoadFailureKind.PackageProducerUnavailable"/>
    /// rather than a fallback to whichever producer this host does authorize.
    /// The coordinate stays credential-free in transport — it names an opaque
    /// producer key, and the source object, URL, and credential are supplied by
    /// the receiving host's own authorization.
    /// </para>
    /// <para>
    /// Gated by
    /// <c>WorkspaceContextLoaderTests.RealizedLoad_ReacquiresFromTheRecordedProducer</c>,
    /// <c>RealizedLoad_WithAnUnauthorizedProducer_FailsTyped</c>, and
    /// <c>RealizedLoad_RoundTripsTheRealizedCoordinate</c>.
    /// </para>
    /// </remarks>
    public static async Task<WorkspaceContextLoadOutcome> LoadRealizedAsync(
        InspectionWorkspace workspace,
        IReadOnlyList<RealizedMemberCoordinate> members,
        WorkspaceContextLoadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.HttpClient);
        ArgumentNullException.ThrowIfNull(options.SourceAuthorization);
        ArgumentNullException.ThrowIfNull(options.PackageStore);
        ArgumentNullException.ThrowIfNull(options.PayloadLimits);
        ArgumentOutOfRangeException.ThrowIfNegative(
            options.MaxEmbeddedContentBytes);
        cancellationToken.ThrowIfCancellationRequested();

        ImmutableArray<WorkspaceContextLoadFailure> rejections =
            ValidateRealized(members, out string? framework, out string? rid);
        if (!rejections.IsEmpty)
            return new WorkspaceContextLoadOutcome.Failed(rejections);

        // A loaded context reports one member per participant, so a package
        // carrying several assemblies repeats its realized coordinate. Handing
        // those values straight back would expand each repeat independently and
        // put the same assembly in the group twice, which makes every in-context
        // reference bind ambiguously. Two identical exact coordinates cannot
        // name different bytes, so the repeat is dropped rather than rejected —
        // and it is dropped here, once, before any acquisition.
        if (Distinct(
                members,
                KeyFor,
                Declare,
                out ImmutableArray<RealizedMemberCoordinate> distinct)
            is { } duplicate)
        {
            return new WorkspaceContextLoadOutcome.Failed([duplicate]);
        }

        var realized = ImmutableArray.CreateBuilder<RealizedMember>();
        foreach (RealizedMemberCoordinate coordinate in distinct)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WorkspaceMemberCoordinate declared = Declare(coordinate);
            MemberRealization realization = coordinate switch
            {
                RealizedMemberCoordinate.Package package =>
                    await RealizePinnedPackageAsync(
                        package,
                        declared,
                        options,
                        cancellationToken).ConfigureAwait(false),
                RealizedMemberCoordinate.Embedded =>
                    RealizeEmbedded(
                        (WorkspaceMemberCoordinate.EmbeddedMember)declared,
                        options,
                        cancellationToken),
                _ => new MemberRealization(
                    Failure(
                        WorkspaceContextLoadFailureKind.InvalidCoordinate,
                        declared,
                        "The realized coordinate kind is not supported.")),
            };

            if (realization.Failure is { } failure)
                return new WorkspaceContextLoadOutcome.Failed([failure]);

            foreach (ResolvedAssemblyReference assembly
                in realization.Assemblies)
            {
                realized.Add(
                    new RealizedMember(
                        declared,
                        realization.Realized!,
                        assembly));
            }
        }

        return CreateGroup(workspace, realized, options, framework, rid);
    }

    /// <summary>
    /// Reduces a member list to its distinct acquisitions in first-declared
    /// order, or reports the conflict when one acquisition subject is named
    /// twice with genuinely different coordinates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Equivalence is decided by a canonical acquisition key, not by the
    /// coordinate record as written. A context that declares
    /// <c>Package("Example", "1.0")</c> and
    /// <c>Package("example", "1.0.0", "NET8.0")</c> under a <c>net8.0</c>
    /// context names one acquisition twice — a different id casing, an
    /// unnormalized version, and an explicitly repeated target that the first
    /// member inherits. Comparing records rejected that as a conflict, which is
    /// the opposite of what the coordinates say.
    /// </para>
    /// <para>
    /// Two identical acquisitions collapse, because realizing one twice would
    /// put its assemblies in the group twice and make every in-context
    /// reference to them bind ambiguously. Two <em>different</em> acquisitions
    /// for one subject — one package id at two versions or from two producers,
    /// one content reference with two digests — are not collapsible and cannot
    /// both be realized into one binding-consistent group, so they are a typed
    /// rejection before acquisition rather than a group whose bindings are
    /// decided by declaration order.
    /// </para>
    /// </remarks>
    static WorkspaceContextLoadFailure? Distinct<T>(
        IReadOnlyList<T> members,
        Func<T, AcquisitionKey> key,
        Func<T, WorkspaceMemberCoordinate> declare,
        out ImmutableArray<T> distinct)
    {
        var bySubject = new Dictionary<string, string>(StringComparer.Ordinal);
        var kept = ImmutableArray.CreateBuilder<T>(members.Count);
        foreach (T member in members)
        {
            AcquisitionKey acquisition = key(member);
            if (!bySubject.TryGetValue(acquisition.Subject, out string? existing))
            {
                bySubject.Add(acquisition.Subject, acquisition.Key);
                kept.Add(member);
                continue;
            }

            if (!string.Equals(existing, acquisition.Key, StringComparison.Ordinal))
            {
                distinct = [];
                return Failure(
                    WorkspaceContextLoadFailureKind.InvalidCoordinate,
                    declare(member),
                    "A workspace context names one acquisition subject more than once with different coordinates.");
            }
        }

        distinct = kept.ToImmutable();
        return null;
    }

    /// <summary>
    /// What one member acquires: the subject it names, and the exact
    /// acquisition of that subject it asks for.
    /// </summary>
    /// <remarks>
    /// Two members are the same acquisition exactly when both parts match. The
    /// subject alone identifies "this package" or "this bundle content", which
    /// is what makes a mismatch a conflict rather than two independent members.
    /// </remarks>
    readonly record struct AcquisitionKey(string Subject, string Key);

    /// <summary>
    /// The canonical acquisition key for a declared member, resolved against
    /// the context's effective targets.
    /// </summary>
    /// <remarks>
    /// A member that inherits the context's framework and one that repeats it
    /// in another casing are the same acquisition, so the effective target is
    /// what the key carries. A floating member keeps a distinct key from any
    /// exact pin of the same package: what it will resolve to is not knowable
    /// here, and collapsing them would silently drop whichever the loader did
    /// not keep.
    /// </remarks>
    static AcquisitionKey KeyFor(
        WorkspaceMemberCoordinate member,
        string? framework,
        string? runtimeIdentifier)
    {
        switch (member)
        {
            case WorkspaceMemberCoordinate.PackageMember package:
                string id = package.PackageId.ToLowerInvariant();
                string version = package.Version is { } declared
                    ? CanonicalVersion(declared)
                    : "*";
                string effectiveFramework =
                    (package.Framework ?? framework ?? string.Empty)
                        .ToLowerInvariant();
                string effectiveRid =
                    package.RuntimeIdentifier ?? runtimeIdentifier ?? string.Empty;
                return new AcquisitionKey(
                    $"package:{id}",
                    $"package:{id}|{version}|{effectiveFramework}|{effectiveRid}");

            case WorkspaceMemberCoordinate.EmbeddedMember embedded:
                return new AcquisitionKey(
                    $"embedded:{embedded.ContentRef}",
                    $"embedded:{embedded.ContentRef}|{embedded.Digest}|{embedded.DeclaredName}");

            default:
                return new AcquisitionKey(
                    $"other:{member.GetHashCode()}",
                    $"other:{member.GetHashCode()}");
        }
    }

    /// <summary>
    /// The canonical acquisition key for a realized member.
    /// </summary>
    /// <remarks>
    /// A realized coordinate is canonical by construction, so its own fields
    /// are the key — no equivalence is invented here. The producer is part of
    /// it: one id and version served by two feeds is two acquisitions, and
    /// collapsing them would drop one feed's bytes.
    /// </remarks>
    static AcquisitionKey KeyFor(RealizedMemberCoordinate coordinate) =>
        coordinate switch
        {
            RealizedMemberCoordinate.Package package => new AcquisitionKey(
                $"package:{package.PackageId}",
                $"package:{package.PackageId}|{package.Version}|{package.Framework}"
                    + $"|{package.RuntimeIdentifier}|{package.Producer}"),
            RealizedMemberCoordinate.Embedded embedded => new AcquisitionKey(
                $"embedded:{embedded.ContentRef}",
                $"embedded:{embedded.ContentRef}|{embedded.Digest}|{embedded.DeclaredName}"),
            _ => new AcquisitionKey(
                $"other:{coordinate.GetHashCode()}",
                $"other:{coordinate.GetHashCode()}"),
        };

    /// <summary>
    /// The normalized spelling of a declared version, or the declared text when
    /// it is not a version this product would accept.
    /// </summary>
    /// <remarks>
    /// Validation has already rejected an unparsable version by the time this
    /// runs, so the fallback exists only so the key stays total.
    /// </remarks>
    static string CanonicalVersion(string declared) =>
        NuGetVersion.TryParse(declared, out NuGetVersion? parsed)
            ? parsed.ToNormalizedString().ToLowerInvariant()
            : declared;

    /// <summary>
    /// The declared coordinate that names exactly what a realized coordinate
    /// already resolved to, so a re-acquired member reports the same shape as a
    /// declared one.
    /// </summary>
    static WorkspaceMemberCoordinate Declare(
        RealizedMemberCoordinate coordinate) =>
        coordinate switch
        {
            RealizedMemberCoordinate.Package package =>
                WorkspaceMemberCoordinate.Package(
                    package.PackageId,
                    package.Version,
                    package.Framework,
                    package.RuntimeIdentifier),
            RealizedMemberCoordinate.Embedded embedded =>
                WorkspaceMemberCoordinate.Embedded(
                    embedded.ContentRef,
                    embedded.Digest,
                    embedded.DeclaredName),
            _ => throw new ArgumentOutOfRangeException(nameof(coordinate)),
        };

    static ImmutableArray<WorkspaceContextLoadFailure> ValidateRealized(
        IReadOnlyList<RealizedMemberCoordinate> members,
        out string? framework,
        out string? runtimeIdentifier)
    {
        framework = null;
        runtimeIdentifier = null;
        var failures =
            ImmutableArray.CreateBuilder<WorkspaceContextLoadFailure>();
        if (members.Count == 0)
        {
            failures.Add(
                Failure(
                    WorkspaceContextLoadFailureKind.EmptyContext,
                    member: null,
                    "A workspace context declares at least one member."));
            return failures.ToImmutable();
        }

        List<string> frameworks = [];
        List<string> rids = [];
        foreach (RealizedMemberCoordinate coordinate in members)
        {
            if (coordinate is null)
            {
                failures.Add(
                    Failure(
                        WorkspaceContextLoadFailureKind.InvalidCoordinate,
                        member: null,
                        "A workspace context member cannot be absent."));
                continue;
            }

            if (coordinate is not RealizedMemberCoordinate.Package package)
                continue;

            AddTarget(frameworks, package.Framework, lowercase: true);
            AddTarget(rids, package.RuntimeIdentifier, lowercase: false);
        }

        if (frameworks.Count > 1 || rids.Count > 1)
        {
            failures.Add(
                Failure(
                    WorkspaceContextLoadFailureKind
                        .ConflictingAcquisitionTarget,
                    member: null,
                    "A workspace context lowers to one acquisition target, and its realized members disagree."));
        }

        if (failures.Count == 0)
        {
            framework = frameworks.Count == 1 ? frameworks[0] : null;
            runtimeIdentifier = rids.Count == 1 ? rids[0] : null;
        }

        return failures.ToImmutable();
    }

    /// <summary>
    /// Adds one declared target to the context's effective set, in its
    /// canonical spelling.
    /// </summary>
    /// <remarks>
    /// A framework is normalized to lowercase rather than kept as written.
    /// Frameworks compare case-insensitively everywhere in this product, so
    /// <c>NET8.0</c> and <c>net8.0</c> are one target; carrying both spellings
    /// forward would realize two coordinates that transport as different
    /// identities and would hand the asset selector a moniker its ordinal
    /// framework parser does not recognize. A runtime identifier is not folded
    /// — <see cref="PackageCoordinateResolver.IsCanonicalRuntimeIdentifier"/>
    /// refuses a non-canonical one before this point, because the selector
    /// matches runtime folders ordinally.
    /// </remarks>
    static void AddTarget(List<string> targets, string? value, bool lowercase)
    {
        if (value is null || IsBlankOrPadded(value))
            return;

        string canonical = lowercase ? value.ToLowerInvariant() : value;
        if (!targets.Contains(canonical, StringComparer.Ordinal))
            targets.Add(canonical);
    }

    /// <summary>
    /// Returns the failure for the first assembly identity two realized
    /// participants share, or null when every identity is distinct.
    /// </summary>
    /// <remarks>
    /// Identity equality is <see cref="AssemblyReferenceIdentity"/>'s own, which
    /// is what <see cref="SourceRelativeAssemblyGroupBindingPolicy"/> compares
    /// when it matches a reference against the group's roots — so this detects
    /// exactly the groups that policy would answer ambiguously, and no others.
    /// Two versions of one library have different identities and coexist.
    /// </remarks>
    static WorkspaceContextLoadFailure? FirstIdentityCollision(
        ImmutableArray<RealizedMember>.Builder realized)
    {
        var seen = new Dictionary<AssemblyReferenceIdentity, RealizedMember>();
        foreach (RealizedMember entry in realized)
        {
            if (seen.TryAdd(entry.Assembly.Identity, entry))
                continue;

            // The colliding identity is read out of an artifact, so it is not
            // quoted. The member coordinate the caller declared is carried on
            // the failure, which is what attributes it.
            return Failure(
                WorkspaceContextLoadFailureKind.ConflictingAssemblyIdentity,
                entry.Declared,
                "A workspace context realized more than one assembly with the same identity, so no in-context reference to it could bind to one descriptor.");
        }

        return null;
    }

    static WorkspaceContextLoadOutcome CreateGroup(
        InspectionWorkspace workspace,
        ImmutableArray<RealizedMember>.Builder realized,
        WorkspaceContextLoadOptions options,
        string? framework,
        string? runtimeIdentifier)
    {
        // Two images can carry one assembly identity without either coordinate
        // being duplicated: a package that ships the same assembly under two
        // asset paths, two producers serving one library, or an embedded member
        // that repeats a package's assembly. The binding policy compares
        // identities exactly as this does, and it answers such a group with
        // Multiple for every in-context reference — a group that resolves
        // nothing is not a loaded context, and choosing one image by asset path
        // or declaration order would make the group's meaning depend on
        // enumeration. So the context fails, before any group exists.
        if (FirstIdentityCollision(realized) is { } collision)
        {
            return new WorkspaceContextLoadOutcome.Failed([collision]);
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
            runtimeIdentifier);
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

        if (IsInvalidOptionalTarget(context.Framework))
        {
            failures.Add(
                Failure(
                    WorkspaceContextLoadFailureKind.InvalidCoordinate,
                    member: null,
                    "A context acquisition framework must be a moniker of ASCII letters and digits joined by single '.', '-', or '+' separators."));
        }

        if (context.RuntimeIdentifier is not null
            && !PackageCoordinateResolver.IsCanonicalRuntimeIdentifier(
                context.RuntimeIdentifier))
        {
            // Refused here, before any authorization, source, store, or network
            // work: the asset selector matches runtime folders ordinally, so a
            // non-canonical spelling would acquire a payload and then match
            // nothing inside it.
            failures.Add(
                Failure(
                    WorkspaceContextLoadFailureKind.InvalidCoordinate,
                    member: null,
                    "A context acquisition runtime identifier must be a lowercase moniker of ASCII letters and digits joined by single '.', '-', or '+' separators."));
        }

        List<string> frameworks = [];
        List<string> rids = [];
        AddTarget(frameworks, context.Framework, lowercase: true);
        AddTarget(rids, context.RuntimeIdentifier, lowercase: false);
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

                    AddTarget(frameworks, package.Framework, lowercase: true);
                    AddTarget(rids, package.RuntimeIdentifier, lowercase: false);
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
        return RealizeAcquiredPackage(
            member,
            acquired,
            framework,
            runtimeIdentifier,
            cancellationToken);
    }

    /// <summary>
    /// Re-acquires one package member from exactly the producer its realized
    /// coordinate names, after intersecting that producer with what this host
    /// authorizes for the package id.
    /// </summary>
    static async Task<MemberRealization> RealizePinnedPackageAsync(
        RealizedMemberCoordinate.Package pinned,
        WorkspaceMemberCoordinate declared,
        WorkspaceContextLoadOptions options,
        CancellationToken cancellationToken)
    {
        PackageSourceAuthorization authorization =
            options.SourceAuthorization.AuthorizeSourcesFor(pinned.PackageId);

        // The intersection, not a preference: only the source whose producer
        // key is the recorded one may answer, so a host that authorizes several
        // producers for this id still re-acquires the bytes the coordinate was
        // realized from.
        PackageSource? producer = authorization.Sources.FirstOrDefault(
            source => string.Equals(
                NuGetCache.GetSourceKey(source.Url),
                pinned.Producer,
                StringComparison.Ordinal));
        if (producer is null)
        {
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.PackageProducerUnavailable,
                    declared,
                    authorization.DenialReason
                    ?? $"The producer recorded for package '{pinned.PackageId}' is not authorized by this host."));
        }

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                options.HttpClient,
                new PackageCoordinate(
                    pinned.PackageId,
                    pinned.Version,
                    pinned.Framework,
                    pinned.RuntimeIdentifier),
                [producer],
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
                        declared,
                        invalid.Message));
            case PackageCoordinateResolution.Unavailable unavailable:
                // Authorization has already narrowed to the one recorded
                // producer, so an unavailable resolution here is that producer
                // failing to answer for this coordinate — not the package being
                // unavailable in general, which is what a caller would read
                // PackageUnavailable to mean.
                return new MemberRealization(
                    Failure(
                        WorkspaceContextLoadFailureKind
                            .PackageProducerUnavailable,
                        declared,
                        unavailable.Message));
        }

        PackagePayloadResult payload =
            await PackagePayloadAcquisition.AcquireAsync(
                options.HttpClient,
                ((PackageCoordinateResolution.Resolved)resolution).Coordinate,
                options.PackageStore,
                options.Log,
                options.PayloadLimits,
                cancellationToken).ConfigureAwait(false);
        if (payload is PackagePayloadResult.Unavailable payloadFailure)
        {
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.PackageProducerUnavailable,
                    declared,
                    payloadFailure.Message));
        }

        AcquiredPackagePayload acquired =
            ((PackagePayloadResult.Acquired)payload).Payload;
        if (!string.Equals(
                acquired.ProducerKey,
                pinned.Producer,
                StringComparison.Ordinal))
        {
            // Acquisition was given one source, so this cannot normally
            // happen; it is checked because the alternative to checking is
            // silently binding another producer's bytes to this coordinate.
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.PackageProducerUnavailable,
                    declared,
                    $"Package '{pinned.PackageId}' was served by a producer other than the one its realized coordinate names."));
        }

        MemberRealization realization = RealizeAcquiredPackage(
            declared,
            acquired,
            pinned.Framework,
            pinned.RuntimeIdentifier,
            cancellationToken);

        // A re-acquired member reports the coordinate it was asked for, so a
        // caller can compare the round trip by value.
        return realization.Failure is null
            ? new MemberRealization(pinned, realization.Assemblies)
            : realization;
    }

    static MemberRealization RealizeAcquiredPackage(
        WorkspaceMemberCoordinate member,
        AcquiredPackagePayload acquired,
        string framework,
        string? runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        ResolvedPackageCoordinate coordinate = acquired.Coordinate;
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

        // The realized coordinate is composed here, after the payload has been
        // acquired and committed, from values three owners produced. If those
        // grammars ever drift apart, this is where it shows — so it is a typed
        // failure rather than an exception escaping a caller that has already
        // paid for the bytes.
        if (!RealizedMemberCoordinate.Package.TryCreate(
                coordinate.PackageId,
                coordinate.Version,
                acquired.ProducerKey,
                framework,
                runtimeIdentifier,
                out RealizedMemberCoordinate.Package? realizedCoordinate,
                out string? problem))
        {
            return new MemberRealization(
                Failure(
                    WorkspaceContextLoadFailureKind.InvalidCoordinate,
                    member,
                    $"The acquired package could not be named by a canonical realized coordinate: {problem}."));
        }

        return new MemberRealization(
            realizedCoordinate,
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
