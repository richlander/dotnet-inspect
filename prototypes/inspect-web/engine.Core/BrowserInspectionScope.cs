using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using DotnetInspector.PackageQueries;
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
    PackageAssemblyRoleParticipant Realized)
{
    public PackageCompileAsset Asset => Realized.Asset;
    public AssemblyContextParticipant Participant => Realized.Participant;
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
    readonly PackageAssemblyContextRealization _realization;
    readonly BrowserWorkspaceRole? _surface;
    readonly BrowserWorkspaceRole? _implementation;

    public BrowserInspectionScope(IReadOnlyList<BrowserPackageCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        if (coordinates
            .GroupBy(coordinate => coordinate.Key, StringComparer.Ordinal)
            .Any(group => group.Skip(1).Any()))
        {
            throw new ArgumentException(
                "A browser workspace cannot contain the same package coordinate twice.",
                nameof(coordinates));
        }

        Coordinates = [.. coordinates];

        PackageAssemblyContextRealization? realization = null;
        try
        {
            realization = _workspace.RealizePackageAssemblyContextRoles(
                Coordinates.Select(coordinate => coordinate.Root),
                new PackageAssemblyContextRealizationOptions
                {
                    MaxAssembliesPerRole = MaxAssembliesPerRole,
                    MaxAggregateRetainedImageBytes = MaxRetainedImageBytes,
                    MaxAssemblyEntryBytes = MaxRetainedImageBytes,
                    RequireDeclaredEntryLengths = true,
                });
            _realization = realization;
                _surface = realization.HasAssemblyContexts
                    ? new BrowserWorkspaceRole(
                        realization.SurfaceGroup,
                        realization.SurfaceParticipants,
                        Coordinates)
                    : null;
                _implementation = realization.ImplementationGroup is null
                    ? null
                    : realization.SharesGroup
                        ? Surface
                        : new BrowserWorkspaceRole(
                        realization.ImplementationGroup,
                        realization.ImplementationParticipants,
                        Coordinates);
        }
        catch (Exception creationFailure)
        {
            List<Exception>? cleanupFailures = null;
            TryDispose(realization, ref cleanupFailures);
            TryDispose(_workspace, ref cleanupFailures);
            if (cleanupFailures is not null)
            {
                throw new AggregateException(
                    [creationFailure, .. cleanupFailures]);
            }

            throw;
        }
    }

    public ImmutableArray<BrowserPackageCoordinate> Coordinates { get; }

    public ImmutableArray<BrowserWorkspaceParticipant> SurfaceParticipants =>
        _surface?.Participants ?? [];

    public ImmutableArray<BrowserWorkspaceParticipant> ImplementationParticipants =>
        _implementation?.Participants ?? [];

    public ImmutableArray<BrowserWorkspaceParticipant> ReferenceOnlySurfaceParticipants =>
    [
        .. SurfaceParticipants.Where(participant =>
            _realization.ImplementationParticipant(participant.Realized) is null),
    ];

    /// <summary>
    /// Hands the compile-asset group to a public product query. Reference assemblies remain the
    /// authoritative API surface when the package ships them.
    /// </summary>
    public TResult UseSurface<TResult>(Func<AssemblyContextGroup, TResult> query) =>
        Surface.Use(query);

    /// <summary>
    /// Hands one compile-asset participant to a participant-scoped product query.
    /// </summary>
    public TResult UseSurfaceParticipant<TResult>(
        BrowserWorkspaceParticipant participant,
        Func<AssemblyContextGroup, AssemblyContextParticipant, TResult> query) =>
        Surface.UseParticipant(participant, query);

    /// <summary>Hands the implementation group to a body-backed product query.</summary>
    public TResult UseImplementation<TResult>(Func<AssemblyContextGroup, TResult> query) =>
        Implementation.Use(query);

    /// <summary>
    /// Hands the implementation group to a metadata query, falling back to the compile group for
    /// a reference-only package.
    /// </summary>
    public TResult UseImplementationOrSurface<TResult>(
        Func<AssemblyContextGroup, TResult> query) =>
        (_implementation ?? Surface).Use(query);

    /// <summary>
    /// Runs the product-owned Integration roll-up across the complete realized
    /// package workspace.
    /// </summary>
    public PackageWorkspaceIntegrationsResult QueryIntegrations() =>
        PackageWorkspaceIntegrationsQuery.Execute(_realization);

    /// <summary>
    /// Hands one implementation participant to a metadata query, or its compile participant when
    /// that assembly is reference-only.
    /// </summary>
    public TResult UseMetadataParticipant<TResult>(
        BrowserWorkspaceParticipant participant,
        Func<AssemblyContextGroup, AssemblyContextParticipant, TResult> query)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(query);
        if (_implementation?.Participants.Contains(participant) is true)
            return _implementation.UseParticipant(participant, query);
        if (ReferenceOnlySurfaceParticipants.Contains(participant))
            return Surface.UseParticipant(participant, query);

        throw new ArgumentException(
            "The participant does not belong to a metadata workspace role.",
            nameof(participant));
    }

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
                candidate => candidate.HasExactContentAs(requested))
            ?? throw new InvalidOperationException(
                $"{requested.PackageId} {requested.Version} with its exact package content "
                + "is not part of this workspace.");
    }

    public bool ContainsExactCoordinates(
        IReadOnlyList<BrowserPackageCoordinate> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        return requested.Count == Coordinates.Length
            && requested.All(candidate =>
                Coordinates.Any(retained =>
                    retained.HasExactContentAs(candidate)));
    }

    /// <summary>The participant for one coordinate's assembly, or a visible failure.</summary>
    public BrowserWorkspaceParticipant SurfaceParticipant(
        BrowserPackageCoordinate coordinate,
        PackageCompileAsset asset)
        => Surface.FindParticipant(coordinate, asset);

    public BrowserWorkspaceParticipant ImplementationParticipant(
        BrowserPackageCoordinate coordinate,
        PackageCompileAsset asset)
        => Implementation.FindParticipant(coordinate, asset);

    /// <summary>
    /// Resolves one exact package library through the implementation-preferred metadata role.
    /// Exact surface asset IDs take precedence and use product-owned implementation
    /// correspondence. Unique descriptor names or <c>.dll</c> file names are also accepted.
    /// </summary>
    public BrowserWorkspaceParticipant LibraryParticipant(
        BrowserPackageCoordinate coordinate,
        string assemblyName)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        if (!coordinate.Selection.IsSelected)
        {
            throw new InvalidOperationException(
                $"{coordinate.PackageId} {coordinate.Version} has no selected "
                + $"compile library ({coordinate.Selection.Status}).");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);

        BrowserWorkspaceParticipant[] surfaces =
        [
            .. SurfaceParticipants
                .Where(participant =>
                    ReferenceEquals(
                        participant.Coordinate.Root.Identity,
                        coordinate.Root.Identity)),
        ];
        BrowserWorkspaceParticipant[] implementations =
        [
            .. ImplementationParticipants.Where(participant =>
                ReferenceEquals(
                    participant.Coordinate.Root.Identity,
                    coordinate.Root.Identity)),
        ];
        BrowserWorkspaceParticipant? exactSurface = surfaces.SingleOrDefault(
            participant => participant.Asset.Id.Equals(
                assemblyName,
                StringComparison.Ordinal));
        if (exactSurface is not null)
            return MetadataParticipant(exactSurface);

        BrowserWorkspaceParticipant? exactImplementation =
            implementations.SingleOrDefault(participant =>
                participant.Asset.Id.Equals(assemblyName, StringComparison.Ordinal));
        if (exactImplementation is not null)
            return exactImplementation;

        BrowserWorkspaceParticipant[] matches =
        [
            .. surfaces
                .Concat(implementations.Where(participant =>
                    TryGetSurfaceParticipant(participant) is null))
                .Where(participant =>
                    AssemblyNameEquals(participant.Asset.AssemblyName, assemblyName)
                    || AssemblyNameEquals(participant.Assembly.Identity.Name, assemblyName)),
        ];
        BrowserWorkspaceParticipant selected = matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Assembly '{assemblyName}' is not part of the selected package workspace."),
            _ => throw new InvalidOperationException(
                $"Assembly '{assemblyName}' is ambiguous in the selected package workspace."),
        };
        return surfaces.Contains(selected)
            ? MetadataParticipant(selected)
            : selected;

        BrowserWorkspaceParticipant MetadataParticipant(
            BrowserWorkspaceParticipant surface)
        {
            PackageAssemblyRoleParticipant? implementation =
                _realization.ImplementationParticipant(surface.Realized);
            return implementation is null
                ? surface
                : Implementation.FindParticipant(implementation.Participant);
        }
    }

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

        PackageAssemblyRoleParticipant implementation =
            _realization.ImplementationParticipant(surfaceParticipant.Realized)
            ?? throw new InvalidOperationException(
                $"{surfaceParticipant.Coordinate.PackageId} "
                + $"{surfaceParticipant.Coordinate.Version} contains a reference assembly only "
                + "for this participant.");
        return Implementation.FindParticipant(implementation.Participant);
    }

    public BrowserWorkspaceParticipant? TryGetSurfaceParticipant(
        BrowserWorkspaceParticipant implementationParticipant)
    {
        ArgumentNullException.ThrowIfNull(implementationParticipant);
        if (SurfaceParticipants.Contains(implementationParticipant))
            return implementationParticipant;
        if (!ImplementationParticipants.Contains(implementationParticipant))
        {
            throw new ArgumentException(
                "The participant does not belong to the implementation workspace role.",
                nameof(implementationParticipant));
        }

        return SurfaceParticipants.SingleOrDefault(surface =>
            ReferenceEquals(
                surface.Coordinate.Root.Identity,
                implementationParticipant.Coordinate.Root.Identity)
            && surface.Coordinate.Selection
                .FindImplementationAsset(surface.Asset)
                ?.Path.Equals(
                    implementationParticipant.Asset.Path,
                    StringComparison.Ordinal)
                is true);
    }

    static bool AssemblyNameEquals(string left, string right)
    {
        static string WithoutDll(string value) =>
            value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? value[..^4]
                : value;

        return WithoutDll(left).Equals(
            WithoutDll(right),
            StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        Exception? roleFailure = null;
        try
        {
            _realization.Dispose();
        }
        catch (Exception ex)
        {
            roleFailure = ex;
        }

        try
        {
            _workspace.Dispose();
        }
        catch (Exception workspaceFailure)
            when (roleFailure is not null)
        {
            throw new AggregateException(
                roleFailure,
                workspaceFailure);
        }

        if (roleFailure is not null)
            ExceptionDispatchInfo.Capture(roleFailure).Throw();
    }

    static void TryDispose(
        IDisposable? resource,
        ref List<Exception>? failures)
    {
        if (resource is null)
            return;

        try
        {
            resource.Dispose();
        }
        catch (Exception ex)
        {
            (failures ??= []).Add(ex);
        }
    }

    BrowserWorkspaceRole Implementation => _implementation
        ?? throw new InvalidOperationException(
            "The selected packages ship no managed implementation assembly for their selected "
            + "frameworks, so this operation has no method bodies to inspect.");

    BrowserWorkspaceRole Surface => _surface
        ?? throw new InvalidOperationException(
            "The selected packages have no compile libraries, so this operation has no "
            + "assembly surface to inspect.");
}

/// <summary>
/// Browser package and asset provenance projected over one product-owned
/// assembly-context role.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserWorkspaceRole
{
    readonly AssemblyContextGroup _group;

    public BrowserWorkspaceRole(
        AssemblyContextGroup group,
        ImmutableArray<PackageAssemblyRoleParticipant> participants,
        ImmutableArray<BrowserPackageCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(group);

        _group = group;
        Participants =
        [
            .. participants.Select(participant =>
                new BrowserWorkspaceParticipant(
                    coordinates.First(coordinate =>
                        ReferenceEquals(
                            coordinate.Root.Identity,
                            participant.Package)),
                    participant)),
        ];
    }

    public ImmutableArray<BrowserWorkspaceParticipant> Participants { get; }

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
                ReferenceEquals(
                    candidate.Coordinate.Root.Identity,
                    coordinate.Root.Identity)
                && candidate.Asset.Path.Equals(asset.Path, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The requested participant is not part of the {coordinate.PackageId} "
                + $"{coordinate.Version} workspace role.");
    }

    public BrowserWorkspaceParticipant FindParticipant(
        AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        return Participants.FirstOrDefault(
                candidate => ReferenceEquals(
                    candidate.Participant,
                    participant))
            ?? throw new InvalidOperationException(
                "The product participant is not part of this browser workspace role.");
    }

    AssemblyContextGroup Group => _group;
}
