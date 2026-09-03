using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;

using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Opaque reference identity for one exact realized package Root. Descriptive
/// fields do not define equality; consumers correlate Roots by object identity.
/// </summary>
public sealed class PackageRootIdentity
{
    internal PackageRootIdentity(
        string packageId,
        string packageVersion,
        string? requestedTargetFramework,
        string? requestedRuntimeIdentifier)
    {
        PackageId = packageId;
        PackageVersion = packageVersion;
        RequestedTargetFramework = requestedTargetFramework;
        RequestedRuntimeIdentifier = requestedRuntimeIdentifier;
    }

    public string PackageId { get; }

    public string PackageVersion { get; }

    public string? RequestedTargetFramework { get; }

    public string? RequestedRuntimeIdentifier { get; }
}

/// <summary>
/// Opaque identity for one immutable package compile-asset selection.
/// </summary>
/// <remarks>
/// Equality is reference identity. A token is occurrence-local: equal tokens
/// guarantee the same typed selection arm and ordered asset sequences, while
/// independently repeated equal selections may receive different tokens.
/// </remarks>
public sealed class PackageRootSelectionIdentity
{
    internal PackageRootSelectionIdentity()
    {
    }
}

/// <summary>
/// Acquisition-issued binding among one package Root, its authoritative
/// realized coordinate, retained content generation, and compile-asset selection.
/// </summary>
public sealed class PackageRootBinding
{
    PackageRootBinding(
        PackageRootRealization root,
        RealizedMemberCoordinate.Package coordinate,
        PackageContentGenerationIdentity contentGenerationIdentity,
        PackageRootSelectionIdentity selectionIdentity)
    {
        Root = root;
        Coordinate = coordinate;
        ContentGenerationIdentity = contentGenerationIdentity;
        SelectionIdentity = selectionIdentity;
    }

    public PackageRootRealization Root { get; }

    public RealizedMemberCoordinate.Package Coordinate { get; }

    public PackageContentGenerationIdentity ContentGenerationIdentity { get; }

    public PackageRootSelectionIdentity SelectionIdentity { get; }

    /// <summary>
    /// Binds a payload acquired through the typed source-client path.
    /// </summary>
    public static PackageRootBinding CreateFromSource(
        AcquiredPackageSourcePayload payload,
        string? selectionTargetFramework = null,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (runtimeIdentifier is not null
            && !RealizedMemberCoordinate.IsCanonicalRuntimeIdentifier(
                runtimeIdentifier))
        {
            throw new ArgumentException(
                "A package Root runtime identifier must be a canonical lowercase moniker.",
                nameof(runtimeIdentifier));
        }
        string? acquisitionFramework =
            SourceAcquisitionFramework(selectionTargetFramework);
        if (runtimeIdentifier is not null
            && acquisitionFramework is null)
        {
            throw new ArgumentException(
                "A package Root runtime identifier requires a canonical acquisition framework.",
                nameof(selectionTargetFramework));
        }

        return Create(
            payload,
            payload.Coordinate.PackageId,
            payload.Coordinate.Version,
            payload.Content,
            payload.ProducerKey,
            acquisitionFramework,
            selectionTargetFramework,
            runtimeIdentifier);
    }

    /// <summary>
    /// Binds a payload acquired through the resolved multi-source path.
    /// </summary>
    public static PackageRootBinding CreateFromResolved(
        AcquiredPackagePayload payload,
        string? selectionTargetFramework = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Create(
            payload,
            payload.Coordinate.PackageId,
            payload.Coordinate.Version,
            payload.Content,
            payload.ProducerKey,
            payload.Coordinate.Framework,
            selectionTargetFramework ?? payload.Coordinate.Framework,
            payload.Coordinate.RuntimeIdentifier);
    }

    static PackageRootBinding Create(
        object acquiredPayload,
        string packageId,
        string packageVersion,
        IPackageContent content,
        string producerKey,
        string? acquisitionFramework,
        string? targetFramework,
        string? runtimeIdentifier)
    {
        if (!content.ProducerKey.Equals(producerKey, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The acquired package payload and retained content name different producers.",
                nameof(acquiredPayload));
        }

        var root = new PackageRootRealization(
            content,
            packageId,
            packageVersion,
            targetFramework,
            runtimeIdentifier);
        string? effectiveFramework =
            (string.IsNullOrWhiteSpace(acquisitionFramework)
                ? null
                : acquisitionFramework)
            ?.ToLowerInvariant();
        string? effectiveRuntimeIdentifier =
            runtimeIdentifier;
        if (!RealizedMemberCoordinate.Package.TryCreate(
                packageId,
                packageVersion,
                producerKey,
                effectiveFramework,
                effectiveRuntimeIdentifier,
                out RealizedMemberCoordinate.Package? coordinate,
                out string? problem))
        {
            throw new ArgumentException(
                $"The acquired package payload cannot form a realized coordinate: {problem}.",
                nameof(acquiredPayload));
        }

        return new PackageRootBinding(
            root,
            coordinate,
            content.GenerationIdentity,
            new PackageRootSelectionIdentity());
    }

    static string? SourceAcquisitionFramework(string? targetFramework) =>
        PackageCoordinateResolver.IsAcquisitionTargetText(targetFramework)
            ? targetFramework!.ToLowerInvariant()
            : null;
}

/// <summary>
/// One exact, already-acquired package Root and its compile-asset selection outcome.
/// </summary>
public sealed class PackageRootRealization
{
    readonly IPackageContent _content;

    public PackageRootRealization(
        IPackageContent content,
        string packageId,
        string packageVersion,
        string? targetFramework = null,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        _content = content;
        PackageId = packageId;
        PackageVersion = packageVersion;
        RequestedTargetFramework = targetFramework;
        RequestedRuntimeIdentifier = runtimeIdentifier;
        Identity = new PackageRootIdentity(
            packageId,
            packageVersion,
            targetFramework,
            runtimeIdentifier);
        AssetSelection = Freeze(
            PackageCompileAssetSelector.Select(
                content,
                packageId,
                targetFramework,
                runtimeIdentifier));
    }

    public string PackageId { get; }

    public string PackageVersion { get; }

    public PackageRootIdentity Identity { get; }

    public string? RequestedTargetFramework { get; }

    public string? RequestedRuntimeIdentifier { get; }

    public string ProducerKey => _content.ProducerKey;

    public bool FromCache => _content.FromCache;

    public PackageCompileAssetSelection AssetSelection { get; }

    internal IPackageContent Content => _content;

    public bool ReferencesContent(IPackageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return ReferenceEquals(_content, content);
    }

    static PackageCompileAssetSelection Freeze(
        PackageCompileAssetSelection selection) =>
        new(
            selection.Status,
            selection.TargetFramework,
            Freeze(selection.AvailableTargetFrameworks),
            Freeze(selection.Assets),
            selection.DefaultAsset,
            Freeze(selection.CandidateAssets),
            Freeze(selection.ImplementationAssets),
            selection.Message);

    static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T> values) =>
        Array.AsReadOnly([.. values]);
}

/// <summary>Resource admission policy for acquired-package role realization.</summary>
public sealed record PackageAssemblyContextRealizationOptions
{
    /// <summary>The largest participant count admitted into either role.</summary>
    public int MaxAssembliesPerRole { get; init; } = int.MaxValue;

    /// <summary>
    /// The retained-byte budget for one package realization.
    /// </summary>
    /// <remarks>
    /// Artifact-backed realization divides this budget between the artifact
    /// generation and the resulting role groups. Distinct surface and
    /// implementation groups divide the role-group share again.
    /// </remarks>
    public long MaxAggregateRetainedImageBytes { get; init; } =
        AssemblyContextGroupOptions.DefaultMaxRetainedImageBytes;

    /// <summary>The largest selected assembly entry the content opener may expand.</summary>
    public long MaxAssemblyEntryBytes { get; init; } =
        AssemblyContextGroupOptions.DefaultMaxRetainedImageBytes;

    /// <summary>
    /// Requires every package content to expose declared entry lengths so all
    /// selected assets can be rejected over budget before identity decoding.
    /// </summary>
    public bool RequireDeclaredEntryLengths { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxAssembliesPerRole);
        ArgumentOutOfRangeException.ThrowIfNegative(
            MaxAggregateRetainedImageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxAssemblyEntryBytes);
    }
}

/// <summary>
/// One selected package asset and the exact product participant realized from
/// it.
/// </summary>
public sealed class PackageAssemblyRoleParticipant
{
    internal PackageAssemblyRoleParticipant(
        PackageRootRealization package,
        PackageCompileAsset asset,
        AssemblyContextParticipant participant)
        : this(package.Identity, asset, participant)
    {
    }

    internal PackageAssemblyRoleParticipant(
        PackageRootIdentity package,
        PackageCompileAsset asset,
        AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(participant);
        Package = package;
        Asset = asset;
        Participant = participant;
    }

    public PackageRootIdentity Package { get; }

    public PackageCompileAsset Asset { get; }

    public AssemblyContextParticipant Participant { get; }
}

/// <summary>
/// Product-realized package surface and implementation roles, including exact
/// package-asset-to-participant associations.
/// </summary>
public sealed class PackageAssemblyContextRealization : IDisposable
{
    readonly PackageAssemblyContextRoles? _roles;

    internal PackageAssemblyContextRealization(
        PackageAssemblyContextRoles? roles,
        ImmutableArray<PackageAssemblyRoleParticipant> surfaceParticipants,
        ImmutableArray<PackageAssemblyRoleParticipant> implementationParticipants)
    {
        _roles = roles;
        SurfaceParticipants = surfaceParticipants;
        ImplementationParticipants = implementationParticipants;
    }

    public bool HasAssemblyContexts => _roles is not null;

    public AssemblyContextGroup SurfaceGroup =>
        _roles?.SurfaceGroup
        ?? throw new InvalidOperationException(
            "The package realization has no selected compile assemblies.");

    public AssemblyContextGroup? ImplementationGroup =>
        _roles?.ImplementationGroup;

    public bool SharesGroup => _roles?.SharesGroup ?? false;

    public ImmutableArray<PackageAssemblyRoleParticipant> SurfaceParticipants
    {
        get;
    }

    public ImmutableArray<PackageAssemblyRoleParticipant> ImplementationParticipants
    {
        get;
    }

    public PackageAssemblyRoleParticipant? ImplementationParticipant(
        PackageAssemblyRoleParticipant surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        PackageAssemblyRoleParticipant selected =
            SurfaceParticipants.FirstOrDefault(candidate =>
                ReferenceEquals(candidate, surface))
            ?? throw new ArgumentException(
                "The participant does not belong to the surface package role.",
                nameof(surface));
        AssemblyContextParticipant? implementation = _roles!
            .ImplementationParticipant(selected.Participant);
        return implementation is null
            ? null
            : ImplementationParticipants.First(candidate =>
                ReferenceEquals(candidate.Participant, implementation));
    }

    public void Dispose() => _roles?.Dispose();
}

public sealed partial class InspectionWorkspace
{
    /// <summary>
    /// Realizes already-acquired package contents into reference-preferred
    /// surface and body-bearing implementation roles.
    /// </summary>
    public PackageAssemblyContextRealization RealizePackageAssemblyContextRoles(
        IEnumerable<PackageRootRealization> packages,
        PackageAssemblyContextRealizationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        PackageRoleRealizationPreparation preparation =
            PreparePackageRoleRealization(
                packages,
                options,
                cancellationToken);
        if (preparation.SurfaceAssets.IsEmpty)
        {
            return new PackageAssemblyContextRealization(
                roles: null,
                [],
                []);
        }

        ImmutableArray<RoleAssembly> surfaceRole =
            CreateRole(
                preparation.SurfaceAssets,
                preparation.GroupBudget,
                preparation.Options,
                cancellationToken);
        ImmutableArray<RoleAssembly> implementationRole = preparation.Shared
            ? surfaceRole
            : CreateRole(
                preparation.ImplementationAssets,
                preparation.GroupBudget,
                preparation.Options,
                cancellationToken);
        return CreatePackageAssemblyContextRealization(
            preparation,
            surfaceRole,
            implementationRole,
            cancellationToken);
    }

    internal static PackageRoleRealizationPreparation
        PreparePackageRoleRealization(
        IEnumerable<PackageRootRealization> packages,
        PackageAssemblyContextRealizationOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packages);
        options ??= new PackageAssemblyContextRealizationOptions();
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        ImmutableArray<PackageRootRealization> packageRoots =
            [.. packages];
        if (packageRoots.IsEmpty)
        {
            throw new InvalidOperationException(
                "Package realization requires at least one package.");
        }
        if (packageRoots.Any(static package => package is null))
        {
            throw new ArgumentException(
                "Package realization cannot contain a null package.",
                nameof(packages));
        }

        ImmutableArray<RoleAsset> surfaceAssets =
        [
            .. packageRoots.SelectMany(
                (package, packageIndex) =>
                    package.AssetSelection.IsSelected
                        ? package.AssetSelection.Assets.Select(asset =>
                            new RoleAsset(
                                packageIndex,
                                package,
                                asset))
                        : []),
        ];
        ImmutableArray<RoleAsset> implementationAssets =
        [
            .. packageRoots.SelectMany(
                (package, packageIndex) =>
                    package.AssetSelection.IsSelected
                        ? package.AssetSelection.ImplementationAssets.Select(
                            asset => new RoleAsset(
                                packageIndex,
                                package,
                                asset))
                        : []),
        ];
        ValidateAssetCount(surfaceAssets.Length, options);
        ValidateAssetCount(implementationAssets.Length, options);
        bool shared = SameAssets(surfaceAssets, implementationAssets);
        bool hasSeparateImplementation =
            !shared && !implementationAssets.IsEmpty;
        long groupBudget = hasSeparateImplementation
            ? options.MaxAggregateRetainedImageBytes / 2
            : options.MaxAggregateRetainedImageBytes;

        ValidateAssets(surfaceAssets, groupBudget, options);
        if (hasSeparateImplementation)
            ValidateAssets(implementationAssets, groupBudget, options);

        return new PackageRoleRealizationPreparation(
            surfaceAssets,
            implementationAssets,
            shared,
            groupBudget,
            options);
    }

    PackageAssemblyContextRealization CreatePackageAssemblyContextRealization(
        PackageRoleRealizationPreparation preparation,
        ImmutableArray<RoleAssembly> surfaceRole,
        ImmutableArray<RoleAssembly> implementationRole,
        CancellationToken cancellationToken)
    {
        ImmutableArray<PackageAssemblyRoleCorrespondence> correspondences =
            Correspondences(surfaceRole, implementationRole);
        cancellationToken.ThrowIfCancellationRequested();
        var roleOptions = new AssemblyContextGroupOptions
        {
            MaxRetainedImageBytes = preparation.GroupBudget,
        };

        PackageAssemblyContextRoles roles = CreatePackageAssemblyContextRoles(
            surfaceRole.Select(entry => entry.Assembly),
            implementationRole.IsEmpty
                ? null
                : implementationRole.Select(entry => entry.Assembly),
            correspondences,
            shareImplementationGroup: preparation.Shared,
            surfaceOptions: roleOptions,
            implementationOptions: roleOptions);
        try
        {
            return new PackageAssemblyContextRealization(
                roles,
                Participants(surfaceRole, roles.SurfaceParticipants),
                Participants(
                    implementationRole,
                    roles.ImplementationParticipants));
        }
        catch (Exception creationFailure)
        {
            try
            {
                roles.Dispose();
            }
            catch (Exception disposalFailure)
            {
                throw new AggregateException(
                    creationFailure,
                    disposalFailure);
            }

            throw;
        }
    }

    static ImmutableArray<RoleAssembly> CreateRole(
        ImmutableArray<RoleAsset> assets,
        long groupBudget,
        PackageAssemblyContextRealizationOptions options,
        CancellationToken cancellationToken)
    {
        var assemblies = ImmutableArray.CreateBuilder<RoleAssembly>(assets.Length);
        long entryLimit = Math.Min(
            groupBudget,
            options.MaxAssemblyEntryBytes);
        for (int index = 0; index < assets.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assemblies.Add(
                CreateRoleAssembly(
                    assets[index],
                    entryLimit,
                    index));
        }

        return assemblies.MoveToImmutable();
    }

    static RoleAssembly CreateRoleAssembly(
        RoleAsset asset,
        long entryLimit,
        int roleIndex)
    {
        Func<Stream> openRead = () => OpenEntry(asset, entryLimit);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromStreamWithFallbackIdentity(
                openRead,
                RejectionCarrierIdentity(roleIndex),
                PackageProvenance(asset),
                out bool usedFallbackIdentity);
        return new RoleAssembly(
            asset.PackageIndex,
            asset.Package,
            asset.Asset,
            assembly,
            IdentityDecoded: !usedFallbackIdentity);
    }

    static AssemblyResolutionProvenance PackageProvenance(
        RoleAsset asset) =>
        AssemblyResolutionProvenance.Package(
            asset.Package.PackageId,
            asset.Package.PackageVersion,
            asset.Asset.TargetFramework,
            rid: null);

    static AssemblyReferenceIdentity RejectionCarrierIdentity(
        int roleIndex) =>
        new(
            "RejectedPackageAsset"
                + roleIndex.ToString(CultureInfo.InvariantCulture),
            Version: null,
            Culture: null,
            PublicKeyToken: null);

    static Stream OpenEntry(
        RoleAsset asset,
        long maxExpandedBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!asset.Package.Content.TryOpenEntry(
                asset.Asset.Path,
                maxExpandedBytes,
                out Stream? stream))
        {
            throw new InvalidOperationException(
                "A selected assembly entry disappeared from "
                + $"{asset.Package.PackageId} {asset.Package.PackageVersion}.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new BoundedPackageEntryStream(
                stream,
                maxExpandedBytes);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    static void ValidateAssets(
        ImmutableArray<RoleAsset> assets,
        long groupBudget,
        PackageAssemblyContextRealizationOptions options)
    {
        long expandedBytes = 0;
        foreach (RoleAsset asset in assets)
        {
            if (asset.Package.Content is not IPackageContentEntryManifest manifest)
            {
                if (options.RequireDeclaredEntryLengths)
                {
                    throw new InvalidOperationException(
                        "The selected package content cannot preflight declared entry lengths.");
                }
                continue;
            }
            if (!manifest.TryGetEntryLength(asset.Asset.Path, out long length))
            {
                throw new InvalidOperationException(
                    "A selected assembly entry disappeared from "
                    + $"{asset.Package.PackageId} {asset.Package.PackageVersion}.");
            }
            if (length < 0 || length > options.MaxAssemblyEntryBytes)
            {
                throw new InvalidOperationException(
                    "A selected assembly entry exceeds the configured "
                    + "assembly-entry byte limit.");
            }
            try
            {
                expandedBytes = checked(expandedBytes + length);
            }
            catch (OverflowException ex)
            {
                throw new InvalidOperationException(
                    "The selected package workspace role exceeds the configured "
                    + "retained-image budget.",
                    ex);
            }
        }

        if (expandedBytes > groupBudget)
        {
            throw new InvalidOperationException(
                "The selected package workspace role exceeds the configured "
                + "retained-image budget before assembly identity decoding.");
        }
    }

    static void ValidateAssetCount(
        int assetCount,
        PackageAssemblyContextRealizationOptions options)
    {
        if (assetCount > options.MaxAssembliesPerRole)
        {
            throw new InvalidOperationException(
                "The selected package workspace role exceeds the configured "
                + "assembly-count limit.");
        }
    }

    static ImmutableArray<PackageAssemblyRoleCorrespondence> Correspondences(
        ImmutableArray<RoleAssembly> surfaces,
        ImmutableArray<RoleAssembly> implementations)
    {
        var implementationsByAsset = new Dictionary<RoleAsset, RoleAssembly>(
            implementations.Length,
            RoleAssetIdentityComparer.Instance);
        foreach (RoleAssembly implementation in implementations)
        {
            var roleAsset = new RoleAsset(
                implementation.PackageIndex,
                implementation.Package,
                implementation.Asset);
            if (!implementationsByAsset.TryAdd(roleAsset, implementation))
            {
                throw new InvalidOperationException(
                    "Package asset selection produced duplicate implementation role assets.");
            }
        }

        var pairs =
            ImmutableArray.CreateBuilder<PackageAssemblyRoleCorrespondence>();
        foreach (RoleAssembly surface in surfaces)
        {
            PackageCompileAsset? selectedImplementation =
                surface.Package.AssetSelection.FindImplementationAsset(
                    surface.Asset);
            if (selectedImplementation is null)
            {
                continue;
            }
            var implementationAsset = new RoleAsset(
                surface.PackageIndex,
                surface.Package,
                selectedImplementation);

            if (!implementationsByAsset.TryGetValue(
                implementationAsset,
                out RoleAssembly? implementation))
            {
                throw new InvalidOperationException(
                    "A selected implementation asset is not part of the "
                    + "implementation package role.");
            }
            bool identitiesDecoded =
                surface.IdentityDecoded
                && implementation.IdentityDecoded;
            if (identitiesDecoded
                && !surface.Assembly.Identity.IsEquivalentTo(
                    implementation.Assembly.Identity))
            {
                throw new InvalidOperationException(
                    "The selected reference and implementation assets have "
                    + "different assembly identities.");
            }

            pairs.Add(PackageAssemblyRoleCorrespondence.SelectedAssets(
                surface.Assembly,
                implementation.Assembly,
                identitiesDecoded));
        }

        return pairs.ToImmutable();
    }

    static ImmutableArray<PackageAssemblyRoleParticipant> Participants(
        ImmutableArray<RoleAssembly> assemblies,
        ImmutableArray<AssemblyContextParticipant> participants)
    {
        if (assemblies.Length != participants.Length)
        {
            throw new InvalidOperationException(
                "Package asset realization did not preserve participant cardinality.");
        }

        var result =
            ImmutableArray.CreateBuilder<PackageAssemblyRoleParticipant>(
                participants.Length);
        for (int index = 0; index < participants.Length; index++)
        {
            if (!ReferenceEquals(
                    assemblies[index].Assembly,
                    participants[index].Assembly))
            {
                throw new InvalidOperationException(
                    "Package asset realization did not preserve participant order.");
            }
            result.Add(new PackageAssemblyRoleParticipant(
                assemblies[index].Package,
                assemblies[index].Asset,
                participants[index]));
        }

        return result.MoveToImmutable();
    }

    static bool SameAssets(
        ImmutableArray<RoleAsset> left,
        ImmutableArray<RoleAsset> right)
    {
        if (left.Length != right.Length)
            return false;

        var remaining = new HashSet<RoleAsset>(
            left,
            RoleAssetIdentityComparer.Instance);
        if (remaining.Count != left.Length)
        {
            throw new InvalidOperationException(
                "Package asset selection produced duplicate role assets.");
        }

        return right.All(remaining.Remove) && remaining.Count == 0;
    }

    internal sealed record RoleAsset(
        int PackageIndex,
        PackageRootRealization Package,
        PackageCompileAsset Asset);

    sealed class RoleAssetIdentityComparer : IEqualityComparer<RoleAsset>
    {
        internal static RoleAssetIdentityComparer Instance { get; } = new();

        public bool Equals(RoleAsset? left, RoleAsset? right) =>
            ReferenceEquals(left, right)
            || (left is not null
                && right is not null
                && ReferenceEquals(left.Package, right.Package)
                && left.Asset.Path.Equals(
                    right.Asset.Path,
                    StringComparison.Ordinal));

        public int GetHashCode(RoleAsset asset) =>
            HashCode.Combine(
                RuntimeHelpers.GetHashCode(asset.Package),
                StringComparer.Ordinal.GetHashCode(asset.Asset.Path));
    }

    internal sealed record RoleAssembly(
        int PackageIndex,
        PackageRootRealization Package,
        PackageCompileAsset Asset,
        ResolvedAssemblyReference Assembly,
        bool IdentityDecoded);

    internal sealed record PackageRoleRealizationPreparation(
        ImmutableArray<RoleAsset> SurfaceAssets,
        ImmutableArray<RoleAsset> ImplementationAssets,
        bool Shared,
        long GroupBudget,
        PackageAssemblyContextRealizationOptions Options);

    sealed class BoundedPackageEntryStream : Stream
    {
        readonly Stream _source;
        readonly long _maxBytes;
        readonly long _start;
        long _position;

        public BoundedPackageEntryStream(
            Stream source,
            long maxBytes)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
            if (!source.CanRead)
            {
                throw new IOException(
                    "The package entry opener did not return a readable stream.");
            }
            _source = source;
            _maxBytes = maxBytes;
            if (!source.CanSeek)
                return;

            _start = source.Position;
            long length = checked(source.Length - _start);
            if (length < 0 || length > maxBytes)
                ThrowLimit();
        }

        public override bool CanRead => _source.CanRead;

        public override bool CanSeek => _source.CanSeek;

        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                if (!CanSeek)
                    throw new NotSupportedException();
                long length = checked(_source.Length - _start);
                if (length < 0 || length > _maxBytes)
                    ThrowLimit();
                return length;
            }
        }

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush()
        {
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - offset < count)
                throw new ArgumentException("The buffer range is invalid.");
            if (count == 0)
                return 0;

            int allowed = Allowed(count);
            if (allowed == 0)
                return ProbeEnd();
            int read = _source.Read(buffer, offset, allowed);
            _position += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
                return 0;

            int allowed = Allowed(buffer.Length);
            if (allowed == 0)
                return ProbeEnd();
            int read = _source.Read(buffer[..allowed]);
            _position += read;
            return read;
        }

        public override int ReadByte()
        {
            if (_position == _maxBytes)
            {
                ProbeEnd();
                return -1;
            }
            int value = _source.ReadByte();
            if (value >= 0)
                _position++;
            return value;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(
                buffer.AsMemory(offset, count),
                cancellationToken)
            .AsTask();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
                return 0;

            int allowed = Allowed(buffer.Length);
            if (allowed == 0)
            {
                byte[] probe = new byte[1];
                int extra = await _source.ReadAsync(
                    probe,
                    cancellationToken);
                if (extra != 0)
                    ThrowLimit();
                return 0;
            }

            int read = await _source.ReadAsync(
                buffer[..allowed],
                cancellationToken);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!CanSeek)
                throw new NotSupportedException();

            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (target < 0 || target > _maxBytes)
                ThrowLimit();
            _source.Seek(checked(_start + target), SeekOrigin.Begin);
            _position = target;
            return target;
        }

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _source.Dispose();
            base.Dispose(disposing);
        }

        int Allowed(int requested) =>
            (int)Math.Min(requested, _maxBytes - _position);

        int ProbeEnd()
        {
            if (_source.ReadByte() >= 0)
                ThrowLimit();
            return 0;
        }

        void ThrowLimit() =>
            throw new InvalidDataException(
                "A selected assembly entry exceeds the configured "
                + "assembly-entry byte limit.");
    }
}
