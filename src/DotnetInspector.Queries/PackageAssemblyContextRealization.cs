using System.Collections.Immutable;

using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Product-selected assembly assets for one exact, already-acquired package.
/// </summary>
public sealed class PackageAssemblyContextSelection
{
    readonly IPackageContent _content;

    public PackageAssemblyContextSelection(
        IPackageContent content,
        string packageId,
        string packageVersion,
        string? targetFramework = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        _content = content;
        PackageId = packageId;
        PackageVersion = packageVersion;
        AssetSelection = PackageCompileAssetSelector.Select(
            content,
            packageId,
            targetFramework);
    }

    public string PackageId { get; }

    public string PackageVersion { get; }

    public PackageCompileAssetSelection AssetSelection { get; }

    internal IPackageContent Content => _content;
}

/// <summary>Resource admission policy for acquired-package role realization.</summary>
public sealed record PackageAssemblyContextRealizationOptions
{
    /// <summary>The largest participant count admitted into either role.</summary>
    public int MaxAssembliesPerRole { get; init; } = int.MaxValue;

    /// <summary>
    /// The retained-image budget across both roles. Distinct surface and
    /// implementation groups receive half each.
    /// </summary>
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
        PackageAssemblyContextSelection package,
        PackageCompileAsset asset,
        AssemblyContextParticipant participant)
    {
        Package = package;
        Asset = asset;
        Participant = participant;
    }

    public PackageAssemblyContextSelection Package { get; }

    public PackageCompileAsset Asset { get; }

    public AssemblyContextParticipant Participant { get; }
}

/// <summary>
/// Product-realized package surface and implementation roles, including exact
/// package-asset-to-participant associations.
/// </summary>
public sealed class PackageAssemblyContextRealization : IDisposable
{
    readonly PackageAssemblyContextRoles _roles;

    internal PackageAssemblyContextRealization(
        PackageAssemblyContextRoles roles,
        ImmutableArray<PackageAssemblyRoleParticipant> surfaceParticipants,
        ImmutableArray<PackageAssemblyRoleParticipant> implementationParticipants)
    {
        _roles = roles;
        SurfaceParticipants = surfaceParticipants;
        ImplementationParticipants = implementationParticipants;
    }

    public AssemblyContextGroup SurfaceGroup => _roles.SurfaceGroup;

    public AssemblyContextGroup? ImplementationGroup =>
        _roles.ImplementationGroup;

    public bool SharesGroup => _roles.SharesGroup;

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
        AssemblyContextParticipant? implementation =
            _roles.ImplementationParticipant(selected.Participant);
        return implementation is null
            ? null
            : ImplementationParticipants.First(candidate =>
                ReferenceEquals(candidate.Participant, implementation));
    }

    public void Dispose() => _roles.Dispose();
}

public sealed partial class InspectionWorkspace
{
    /// <summary>
    /// Realizes already-acquired package contents into reference-preferred
    /// surface and body-bearing implementation roles.
    /// </summary>
    public PackageAssemblyContextRealization RealizePackageAssemblyContextRoles(
        IEnumerable<PackageAssemblyContextSelection> packages,
        PackageAssemblyContextRealizationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packages);
        options ??= new PackageAssemblyContextRealizationOptions();
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        ImmutableArray<PackageAssemblyContextSelection> selectedPackages =
            [.. packages];
        if (selectedPackages.IsEmpty)
        {
            throw new InvalidOperationException(
                "Package assembly-context realization requires at least one package.");
        }
        if (selectedPackages.Any(static package => package is null))
        {
            throw new ArgumentException(
                "Package assembly-context realization cannot contain a null package.",
                nameof(packages));
        }
        foreach (PackageAssemblyContextSelection package in selectedPackages)
        {
            if (!package.AssetSelection.IsSelected)
            {
                throw new InvalidOperationException(
                    $"{package.PackageId} {package.PackageVersion} does not have a selected "
                    + "compile-assembly set.");
            }
        }

        ImmutableArray<RoleAsset> surfaceAssets =
        [
            .. selectedPackages.SelectMany(package =>
                package.AssetSelection.Assets.Select(asset =>
                    new RoleAsset(package, asset))),
        ];
        ImmutableArray<RoleAsset> implementationAssets =
        [
            .. selectedPackages.SelectMany(package =>
                package.AssetSelection.ImplementationAssets.Select(asset =>
                    new RoleAsset(package, asset))),
        ];
        bool shared = SameAssets(surfaceAssets, implementationAssets);
        bool hasSeparateImplementation =
            !shared && !implementationAssets.IsEmpty;
        long groupBudget = hasSeparateImplementation
            ? options.MaxAggregateRetainedImageBytes / 2
            : options.MaxAggregateRetainedImageBytes;

        ValidateAssets(surfaceAssets, groupBudget, options);
        if (hasSeparateImplementation)
            ValidateAssets(implementationAssets, groupBudget, options);

        ImmutableArray<RoleAssembly> surfaceRole =
            CreateRole(surfaceAssets, groupBudget, options, cancellationToken);
        ImmutableArray<RoleAssembly> implementationRole = shared
            ? surfaceRole
            : CreateRole(
                implementationAssets,
                groupBudget,
                options,
                cancellationToken);
        ImmutableArray<PackageAssemblyRoleCorrespondence> correspondences =
            Correspondences(surfaceRole, implementationRole);
        cancellationToken.ThrowIfCancellationRequested();
        var roleOptions = new AssemblyContextGroupOptions
        {
            MaxRetainedImageBytes = groupBudget,
        };

        PackageAssemblyContextRoles roles = CreatePackageAssemblyContextRoles(
            surfaceRole.Select(entry => entry.Assembly),
            implementationRole.IsEmpty
                ? null
                : implementationRole.Select(entry => entry.Assembly),
            correspondences,
            shareImplementationGroup: shared,
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
        foreach (RoleAsset asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyResolutionProvenance provenance =
                AssemblyResolutionProvenance.Package(
                    asset.Package.PackageId,
                    asset.Package.PackageVersion,
                    asset.Asset.TargetFramework,
                    rid: null);
            var fallbackIdentity = new AssemblyReferenceIdentity(
                Path.GetFileNameWithoutExtension(asset.Asset.AssemblyName),
                Version: null,
                Culture: null,
                PublicKeyToken: null);
            Func<Stream> openRead = () => OpenEntry(asset, entryLimit);
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.CreateFromStreamWithFallbackIdentity(
                    openRead,
                    fallbackIdentity,
                    provenance,
                    out bool usedFallbackIdentity);
            assemblies.Add(new RoleAssembly(
                asset.Package,
                asset.Asset,
                assembly,
                IdentityDecoded: !usedFallbackIdentity));
        }

        return assemblies.MoveToImmutable();
    }

    static Stream OpenEntry(RoleAsset asset, long maxExpandedBytes)
    {
        if (!asset.Package.Content.TryOpenEntry(
                asset.Asset.Path,
                maxExpandedBytes,
                out Stream? stream))
        {
            throw new InvalidOperationException(
                $"'{asset.Asset.Path}' disappeared from "
                + $"{asset.Package.PackageId} {asset.Package.PackageVersion}.");
        }

        try
        {
            return new BoundedPackageEntryStream(
                stream,
                maxExpandedBytes,
                asset.Asset.Path);
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
        if (assets.Length > options.MaxAssembliesPerRole)
        {
            throw new InvalidOperationException(
                "The selected package workspace role exceeds the configured "
                + "assembly-count limit.");
        }

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
                    $"'{asset.Asset.Path}' disappeared from "
                    + $"{asset.Package.PackageId} {asset.Package.PackageVersion}.");
            }
            if (length < 0 || length > options.MaxAssemblyEntryBytes)
            {
                throw new InvalidOperationException(
                    $"'{asset.Asset.Path}' exceeds the configured assembly-entry byte limit.");
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

    static ImmutableArray<PackageAssemblyRoleCorrespondence> Correspondences(
        ImmutableArray<RoleAssembly> surfaces,
        ImmutableArray<RoleAssembly> implementations)
    {
        var pairs =
            ImmutableArray.CreateBuilder<PackageAssemblyRoleCorrespondence>();
        foreach (RoleAssembly surface in surfaces)
        {
            PackageCompileAsset? implementationAsset =
                surface.Package.AssetSelection.FindImplementationAsset(
                    surface.Asset);
            if (implementationAsset is null)
                continue;

            RoleAssembly implementation = implementations.FirstOrDefault(candidate =>
                    ReferenceEquals(candidate.Package, surface.Package)
                    && candidate.Asset.Path.Equals(
                        implementationAsset.Path,
                        StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"The selected implementation asset '{implementationAsset.Path}' "
                    + "is not part of the implementation package role.");
            bool identitiesDecoded =
                surface.IdentityDecoded
                && implementation.IdentityDecoded;
            if (identitiesDecoded
                && !surface.Assembly.Identity.IsEquivalentTo(
                    implementation.Assembly.Identity))
            {
                throw new InvalidOperationException(
                    "The selected reference and implementation assets for "
                    + $"{surface.Asset.AssemblyName} have different assembly identities.");
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
        => left.Length == right.Length
            && left.All(leftAsset =>
                right.Any(rightAsset =>
                    ReferenceEquals(
                        leftAsset.Package,
                        rightAsset.Package)
                    && leftAsset.Asset.Path.Equals(
                        rightAsset.Asset.Path,
                        StringComparison.Ordinal)));

    sealed record RoleAsset(
        PackageAssemblyContextSelection Package,
        PackageCompileAsset Asset);

    sealed record RoleAssembly(
        PackageAssemblyContextSelection Package,
        PackageCompileAsset Asset,
        ResolvedAssemblyReference Assembly,
        bool IdentityDecoded);

    sealed class BoundedPackageEntryStream : Stream
    {
        readonly Stream _source;
        readonly long _maxBytes;
        readonly long _start;
        readonly string _path;
        long _position;

        public BoundedPackageEntryStream(
            Stream source,
            long maxBytes,
            string path)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!source.CanRead)
            {
                throw new IOException(
                    "The package entry opener did not return a readable stream.");
            }

            _source = source;
            _maxBytes = maxBytes;
            _path = path;
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
                $"'{_path}' exceeds the configured assembly-entry byte limit.");
    }
}
