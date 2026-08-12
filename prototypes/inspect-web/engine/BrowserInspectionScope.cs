using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace InspectWeb.Engine;

/// <summary>
/// One participant of a browser workspace, together with the package coordinate it came from.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed record BrowserWorkspaceParticipant(
    BrowserPackageCoordinate Coordinate,
    PackageCompileAsset Asset,
    AssemblyContextParticipant Participant)
{
    public ResolvedAssemblyReference Assembly => Participant.Assembly;
}

/// <summary>
/// The workspace every browser inspection runs inside: one <see cref="InspectionWorkspace"/> and
/// separate binding-consistent <see cref="AssemblyContextGroup"/> views over the product-selected
/// compile and implementation assemblies of one or more exact package/version/framework
/// coordinates.
/// </summary>
/// <remarks>
/// <para>
/// This type deliberately exposes <em>no</em> way to open an
/// <see cref="AssemblyInspectionSession"/>, a <c>MetadataSource</c>, or an Analysis index. The
/// group's session and snapshot access is internal to <c>DotnetInspector.Queries</c> and its
/// companion query assembly, so the only sanctioned way for a consumer to inspect a participant is
/// to hand the group to a public product query that owns those lifetimes itself.
/// The surface and implementation hand-offs below expose those groups and nothing else; an
/// operation with no such query stays unsupported.
/// </para>
/// <para>
/// A scope is retained by <see cref="BrowserPackageWorkspace"/> and reused across exports, so it
/// must stay whole. In particular it never runs
/// <c>AssemblyContextIntegrationsQuery.ExecuteParticipantAsync</c>, whose release is terminal for
/// the released participant: bounded retained bytes come from the registry's scope eviction
/// instead, which disposes the group rather than half-emptying it.
/// </para>
/// <para>
/// Disposal belongs to that registry, not to a caller.
/// <c>BrowserEngineLayeringTests</c> in <c>engine.Tests</c> is the gate for the
/// boundary this remark asserts: no engine source opens a session, a metadata source, an analysis
/// index, or an image span.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
internal sealed class BrowserInspectionScope : IDisposable
{
    /// <summary>The retained-image budget one browser workspace may hold across its groups.</summary>
    internal const long MaxRetainedImageBytes = 64L * 1024 * 1024;
    internal const int MaxAssembliesPerRole = 256;

    readonly InspectionWorkspace _workspace = new();
    readonly BrowserWorkspaceGroup _surface;
    readonly BrowserWorkspaceGroup? _implementation;

    public BrowserInspectionScope(IReadOnlyList<BrowserPackageCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        Coordinates = [.. coordinates];

        (BrowserPackageCoordinate Coordinate, PackageCompileAsset Asset)[] surfaceAssets =
        [
            .. Coordinates.SelectMany(coordinate =>
                coordinate.Selection.Assets.Select(asset => (coordinate, asset))),
        ];
        (BrowserPackageCoordinate Coordinate, PackageCompileAsset Asset)[] implementationAssets =
        [
            .. Coordinates.SelectMany(coordinate =>
                coordinate.ImplementationAssets.Select(asset => (coordinate, asset))),
        ];

        bool shared = SameAssets(surfaceAssets, implementationAssets);
        bool hasSeparateImplementation = !shared && implementationAssets.Length > 0;
        long groupBudget = hasSeparateImplementation
            ? MaxRetainedImageBytes / 2
            : MaxRetainedImageBytes;
        BrowserWorkspaceGroup.ValidateAssets(surfaceAssets, groupBudget);
        if (hasSeparateImplementation)
            BrowserWorkspaceGroup.ValidateAssets(implementationAssets, groupBudget);
        _surface = new BrowserWorkspaceGroup(_workspace, surfaceAssets, groupBudget);
        _implementation = shared
            ? _surface
            : implementationAssets.Length == 0
                ? null
                : new BrowserWorkspaceGroup(
                    _workspace,
                    implementationAssets,
                    groupBudget);
        try
        {
            ValidateImplementationPairs();
        }
        catch
        {
            if (!ReferenceEquals(_implementation, _surface))
                _implementation?.Dispose();
            _surface.Dispose();
            _workspace.Dispose();
            throw;
        }
    }

    public ImmutableArray<BrowserPackageCoordinate> Coordinates { get; }

    public ImmutableArray<BrowserWorkspaceParticipant> SurfaceParticipants =>
        _surface.Participants;

    public ImmutableArray<BrowserWorkspaceParticipant> ImplementationParticipants =>
        _implementation?.Participants ?? [];

    public ImmutableArray<BrowserWorkspaceParticipant> ReferenceOnlySurfaceParticipants =>
    [
        .. SurfaceParticipants.Where(participant =>
            participant.Coordinate.Selection.FindImplementationAsset(participant.Asset) is null),
    ];

    /// <summary>
    /// Hands the compile-asset group to a public product query. Reference assemblies remain the
    /// authoritative API surface when the package ships them.
    /// </summary>
    public TResult UseSurface<TResult>(Func<AssemblyContextGroup, TResult> query) =>
        _surface.Use(query);

    /// <summary>
    /// Hands one compile-asset participant to a participant-scoped product query.
    /// </summary>
    public TResult UseSurfaceParticipant<TResult>(
        BrowserWorkspaceParticipant participant,
        Func<AssemblyContextGroup, AssemblyContextParticipant, TResult> query) =>
        _surface.UseParticipant(participant, query);

    /// <summary>Hands the implementation group to a body-backed product query.</summary>
    public TResult UseImplementation<TResult>(Func<AssemblyContextGroup, TResult> query) =>
        Implementation.Use(query);

    /// <summary>
    /// Hands the implementation group to a metadata query, falling back to the compile group for
    /// a reference-only package.
    /// </summary>
    public TResult UseImplementationOrSurface<TResult>(
        Func<AssemblyContextGroup, TResult> query) =>
        (_implementation ?? _surface).Use(query);

    /// <summary>Hands one implementation participant to a body-backed product query.</summary>
    public TResult UseImplementationParticipant<TResult>(
        BrowserWorkspaceParticipant participant,
        Func<AssemblyContextGroup, AssemblyContextParticipant, TResult> query) =>
        Implementation.UseParticipant(participant, query);

    /// <summary>
    /// The coordinate this scope holds for one exact package/version/framework identity.
    /// Coordinates match by that identity rather than by object reference, because a reused scope
    /// answers requests that resolved their own coordinate objects.
    /// </summary>
    public BrowserPackageCoordinate Coordinate(BrowserPackageCoordinate requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        return Coordinates.FirstOrDefault(
                candidate => candidate.Key.Equals(requested.Key, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"{requested.PackageId} {requested.Version} {requested.Framework} is not part of "
                + "this workspace.");
    }

    /// <summary>The participant for one coordinate's assembly, or a visible failure.</summary>
    public BrowserWorkspaceParticipant SurfaceParticipant(
        BrowserPackageCoordinate coordinate,
        PackageCompileAsset asset)
        => _surface.FindParticipant(coordinate, asset);

    public BrowserWorkspaceParticipant ImplementationParticipant(
        BrowserPackageCoordinate coordinate,
        PackageCompileAsset asset)
        => Implementation.FindParticipant(coordinate, asset);

    public BrowserWorkspaceParticipant ImplementationParticipant(
        BrowserWorkspaceParticipant surfaceParticipant)
    {
        ArgumentNullException.ThrowIfNull(surfaceParticipant);
        if (!SurfaceParticipants.Contains(surfaceParticipant))
        {
            throw new ArgumentException(
                "The participant does not belong to the surface workspace role.",
                nameof(surfaceParticipant));
        }

        PackageCompileAsset implementationAsset =
            surfaceParticipant.Coordinate.Selection.FindImplementationAsset(
                surfaceParticipant.Asset)
            ?? throw new InvalidOperationException(
                $"{surfaceParticipant.Coordinate.PackageId} "
                + $"{surfaceParticipant.Coordinate.Version} ships "
                + $"{surfaceParticipant.Asset.AssemblyName} for "
                + $"{surfaceParticipant.Coordinate.Framework} as a reference assembly only.");
        BrowserWorkspaceParticipant implementation =
            Implementation.FindParticipant(
                surfaceParticipant.Coordinate,
                implementationAsset);
        if (!implementation.Assembly.Identity.IsEquivalentTo(
                surfaceParticipant.Assembly.Identity))
        {
            throw new InvalidOperationException(
                $"The selected reference and implementation assets for "
                + $"{surfaceParticipant.Asset.AssemblyName} have different assembly identities.");
        }

        return implementation;
    }

    public void Dispose()
    {
        if (!ReferenceEquals(_implementation, _surface))
            _implementation?.Dispose();
        _surface.Dispose();
        _workspace.Dispose();
    }

    void ValidateImplementationPairs()
    {
        foreach (BrowserWorkspaceParticipant surfaceParticipant in SurfaceParticipants)
        {
            PackageCompileAsset? implementationAsset =
                surfaceParticipant.Coordinate.Selection.FindImplementationAsset(
                    surfaceParticipant.Asset);
            if (implementationAsset is null)
                continue;

            BrowserWorkspaceParticipant implementation =
                Implementation.FindParticipant(
                    surfaceParticipant.Coordinate,
                    implementationAsset);
            if (!implementation.Assembly.Identity.IsEquivalentTo(
                surfaceParticipant.Assembly.Identity))
            {
                throw new InvalidOperationException(
                    $"The selected reference and implementation assets for "
                    + $"{surfaceParticipant.Asset.AssemblyName} have different assembly identities.");
            }
        }
    }

    BrowserWorkspaceGroup Implementation => _implementation
        ?? throw new InvalidOperationException(
            "The selected packages ship no managed implementation assembly for their selected "
            + "frameworks, so this operation has no method bodies to inspect.");

    static bool SameAssets(
        IReadOnlyList<(BrowserPackageCoordinate Coordinate, PackageCompileAsset Asset)> left,
        IReadOnlyList<(BrowserPackageCoordinate Coordinate, PackageCompileAsset Asset)> right)
        => left.Count == right.Count
            && left.Zip(right).All(pair =>
                pair.First.Coordinate.Key.Equals(
                    pair.Second.Coordinate.Key,
                    StringComparison.Ordinal)
                && pair.First.Asset.Path.Equals(
                    pair.Second.Asset.Path,
                    StringComparison.Ordinal));
}

/// <summary>
/// One binding-consistent participant role inside a browser workspace. Compile and implementation
/// groups deliberately have distinct policies so references resolve within the same asset role.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserWorkspaceGroup : IDisposable, IAssemblyReferenceResolver
{
    AssemblyContextGroup? _group;

    public BrowserWorkspaceGroup(
        InspectionWorkspace workspace,
        IReadOnlyList<(BrowserPackageCoordinate Coordinate, PackageCompileAsset Asset)> assets,
        long maxRetainedImageBytes)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assets);
        ValidateAssets(assets, maxRetainedImageBytes);

        // One binding-policy snapshot per role: a reference assembly resolves other references,
        // while an implementation assembly resolves other implementations.
        var policy = new AssemblyReferenceBindingPolicy(this);
        var participants = ImmutableArray.CreateBuilder<BrowserWorkspaceParticipant>();
        foreach ((BrowserPackageCoordinate coordinate, PackageCompileAsset asset) in assets)
        {
            ResolvedAssemblyReference reference = coordinate.Package.CreateReference(
                asset.Path,
                AssemblyResolutionProvenance.Package(
                    coordinate.PackageId,
                    coordinate.Version,
                    asset.TargetFramework,
                    rid: null));

            participants.Add(new BrowserWorkspaceParticipant(
                coordinate,
                asset,
                new AssemblyContextParticipant(reference, policy)));
        }

        if (participants.Count == 0)
        {
            throw new InvalidOperationException(
                "The selected packages contain no managed assembly for this workspace role.");
        }

        Participants = participants.ToImmutable();
        _group = workspace.CreateAssemblyContextGroup(
            Participants.Select(participant => participant.Participant),
            new AssemblyContextGroupOptions
            {
                // Preserve the workspace's own retained-snapshot defense after the host preflight.
                MaxRetainedImageBytes = maxRetainedImageBytes,
            });
    }

    public ImmutableArray<BrowserWorkspaceParticipant> Participants { get; }

    internal static void ValidateAssets(
        IReadOnlyList<(BrowserPackageCoordinate Coordinate, PackageCompileAsset Asset)> assets,
        long maxRetainedImageBytes)
    {
        if (assets.Count > BrowserInspectionScope.MaxAssembliesPerRole)
        {
            throw new InvalidOperationException(
                "The selected workspace role exceeds the browser assembly-count limit.");
        }

        long expandedBytes = 0;
        foreach ((BrowserPackageCoordinate coordinate, PackageCompileAsset asset) in assets)
        {
            if (!coordinate.Package.Content.TryGetEntryLength(asset.Path, out long length))
                throw new InvalidOperationException($"'{asset.Path}' disappeared from its package.");
            try
            {
                expandedBytes = checked(expandedBytes + length);
            }
            catch (OverflowException ex)
            {
                throw new InvalidOperationException(
                    "The selected workspace role exceeds the browser retained-image budget.",
                    ex);
            }
        }

        if (expandedBytes > maxRetainedImageBytes)
        {
            throw new InvalidOperationException(
                "The selected workspace role exceeds the browser retained-image budget before "
                + "assembly identity decoding.");
        }
    }

    public TResult Use<TResult>(Func<AssemblyContextGroup, TResult> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query(Group);
    }

    public TResult UseParticipant<TResult>(
        BrowserWorkspaceParticipant participant,
        Func<AssemblyContextGroup, AssemblyContextParticipant, TResult> query)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(query);
        if (!Participants.Contains(participant))
        {
            throw new ArgumentException(
                "The participant does not belong to this workspace role.",
                nameof(participant));
        }

        return query(Group, participant.Participant);
    }

    public BrowserWorkspaceParticipant FindParticipant(
        BrowserPackageCoordinate coordinate,
        PackageCompileAsset asset)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(asset);
        return Participants.FirstOrDefault(candidate =>
                candidate.Coordinate.Key.Equals(coordinate.Key, StringComparison.Ordinal)
                && candidate.Asset.Path.Equals(asset.Path, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"{asset.Path} is not a participant in the {coordinate.PackageId} "
                + $"{coordinate.Version} {coordinate.Framework} workspace role.");
    }

    public ResolvedAssemblyReference? Resolve(
        AssemblyReferenceIdentity identity,
        AssemblyResolutionScope scope)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (scope == AssemblyResolutionScope.Platform)
            return null;
        return Participants
            .FirstOrDefault(participant =>
                participant.Assembly.Identity.IsEquivalentTo(identity))
            ?.Assembly;
    }

    public void Dispose()
    {
        _group?.Dispose();
        _group = null;
    }

    AssemblyContextGroup Group => _group
        ?? throw new InvalidOperationException("The assembly context group is not open.");
}
