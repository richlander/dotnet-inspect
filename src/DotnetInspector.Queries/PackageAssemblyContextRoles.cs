using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// One exact correspondence between a surface assembly and the implementation
/// assembly that supplies its method bodies.
/// </summary>
public sealed record PackageAssemblyRoleCorrespondence
{
    public PackageAssemblyRoleCorrespondence(
        ResolvedAssemblyReference surface,
        ResolvedAssemblyReference implementation)
        : this(
            surface,
            implementation,
            requireEquivalentIdentity: true)
    {
    }

    PackageAssemblyRoleCorrespondence(
        ResolvedAssemblyReference surface,
        ResolvedAssemblyReference implementation,
        bool requireEquivalentIdentity)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(implementation);
        Surface = surface;
        Implementation = implementation;
        RequireEquivalentIdentity = requireEquivalentIdentity;
    }

    public ResolvedAssemblyReference Surface { get; }
    public ResolvedAssemblyReference Implementation { get; }

    internal bool RequireEquivalentIdentity { get; }

    internal static PackageAssemblyRoleCorrespondence SelectedAssets(
        ResolvedAssemblyReference surface,
        ResolvedAssemblyReference implementation,
        bool identitiesDecoded) =>
        new(
            surface,
            implementation,
            requireEquivalentIdentity: identitiesDecoded);
}

/// <summary>
/// Coordinated surface and implementation assembly-context roles in one
/// inspection workspace.
/// </summary>
/// <remarks>
/// <para>
/// Each role is one immutable binding domain. In-role references resolve only
/// to exact participants in that role, and package participants never satisfy
/// platform-scoped requests. Equivalent identities are rejected rather than
/// selected by declaration order.
/// </para>
/// <para>
/// The implementation role may be absent, distinct, or the same group as the
/// surface role. Correspondences are validated before either group is created,
/// and preserve typed participant identity across the two roles.
/// </para>
/// <para>
/// Disposal attempts every distinct role group even when an earlier group's
/// owned-resource cleanup fails. Gated by
/// <c>PackageAssemblyContextRolesTests.Dispose_ContinuesAfterBothRoleGroupsFail</c>.
/// </para>
/// </remarks>
public sealed class PackageAssemblyContextRoles : IDisposable
{
    readonly Dictionary<
        AssemblyContextParticipant,
        AssemblyContextParticipant> _implementationBySurface =
            new(ReferenceEqualityComparer.Instance);
    bool _disposed;

    internal PackageAssemblyContextRoles(
        InspectionWorkspace workspace,
        IEnumerable<ResolvedAssemblyReference> surfaceAssemblies,
        IEnumerable<ResolvedAssemblyReference>? implementationAssemblies,
        IEnumerable<PackageAssemblyRoleCorrespondence> correspondences,
        bool shareImplementationGroup,
        AssemblyContextGroupOptions? surfaceOptions,
        AssemblyContextGroupOptions? implementationOptions,
        Func<
            int,
            IEnumerable<AssemblyContextParticipant>,
            AssemblyContextGroupOptions?,
            AssemblyContextGroup>? createRole = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(surfaceAssemblies);
        ArgumentNullException.ThrowIfNull(correspondences);

        ImmutableArray<ResolvedAssemblyReference> surfaces =
            SnapshotRole(surfaceAssemblies, nameof(surfaceAssemblies));
        ImmutableArray<ResolvedAssemblyReference> implementations =
            implementationAssemblies is null
                ? []
                : SnapshotRole(
                    implementationAssemblies,
                    nameof(implementationAssemblies),
                    allowEmpty: true);
        ImmutableArray<PackageAssemblyRoleCorrespondence> pairs =
            [.. correspondences];

        ValidateRole(surfaces);
        ValidateRole(implementations);
        ValidateCorrespondences(surfaces, implementations, pairs);
        ValidateSharedRole(
            surfaces,
            implementations,
            shareImplementationGroup,
            surfaceOptions,
            implementationOptions);

        AssemblyContextGroup? surfaceGroup = null;
        AssemblyContextGroup? implementationGroup = null;
        try
        {
            surfaceGroup = CreateRole(
                workspace,
                surfaces,
                surfaceOptions,
                createRole,
                roleIndex: 0);
            implementationGroup = implementations.Length == 0
                ? null
                : shareImplementationGroup
                    ? surfaceGroup
                    : CreateRole(
                        workspace,
                        implementations,
                        implementationOptions,
                        createRole,
                        roleIndex: 1);

            SurfaceGroup = surfaceGroup;
            ImplementationGroup = implementationGroup;
            SurfaceParticipants = surfaceGroup.Participants;
            ImplementationParticipants =
                implementationGroup?.Participants ?? [];

            Dictionary<
                ResolvedAssemblyReference,
                AssemblyContextParticipant> surfaceParticipants =
                    ParticipantsByAssembly(SurfaceParticipants);
            Dictionary<
                ResolvedAssemblyReference,
                AssemblyContextParticipant> implementationParticipants =
                    ParticipantsByAssembly(ImplementationParticipants);
            foreach (PackageAssemblyRoleCorrespondence pair in pairs)
            {
                _implementationBySurface.Add(
                    surfaceParticipants[pair.Surface],
                    implementationParticipants[pair.Implementation]);
            }
        }
        catch (Exception creationFailure)
        {
            try
            {
                DisposeGroups(
                    implementationGroup,
                    surfaceGroup);
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

    public AssemblyContextGroup SurfaceGroup { get; }

    public AssemblyContextGroup? ImplementationGroup { get; }

    public ImmutableArray<AssemblyContextParticipant> SurfaceParticipants
    {
        get;
    }

    public ImmutableArray<AssemblyContextParticipant> ImplementationParticipants
    {
        get;
    }

    public bool SharesGroup =>
        ImplementationGroup is not null
        && ReferenceEquals(SurfaceGroup, ImplementationGroup);

    /// <summary>
    /// Returns the implementation participant paired with an exact surface
    /// participant, or <see langword="null"/> for a reference-only surface.
    /// </summary>
    public AssemblyContextParticipant? ImplementationParticipant(
        AssemblyContextParticipant surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SurfaceParticipants.Contains(surface))
        {
            throw new ArgumentException(
                "The participant does not belong to the surface assembly-context role.",
                nameof(surface));
        }

        return _implementationBySurface.GetValueOrDefault(surface);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DisposeGroups(ImplementationGroup, SurfaceGroup);
    }

    static void DisposeGroups(
        AssemblyContextGroup? implementation,
        AssemblyContextGroup? surface)
    {
        List<Exception>? failures = null;
        if (implementation is not null
            && !ReferenceEquals(implementation, surface))
        {
            TryDispose(implementation, ref failures);
        }
        if (surface is not null)
            TryDispose(surface, ref failures);

        if (failures is not null)
            throw new AggregateException(failures);
    }

    static void TryDispose(
        IDisposable resource,
        ref List<Exception>? failures)
    {
        try
        {
            resource.Dispose();
        }
        catch (Exception ex)
        {
            (failures ??= []).Add(ex);
        }
    }

    static ImmutableArray<ResolvedAssemblyReference> SnapshotRole(
        IEnumerable<ResolvedAssemblyReference> assemblies,
        string parameterName,
        bool allowEmpty = false)
    {
        ImmutableArray<ResolvedAssemblyReference> snapshot = [.. assemblies];
        if (!allowEmpty && snapshot.IsEmpty)
        {
            throw new InvalidOperationException(
                "An assembly-context surface role requires at least one participant.");
        }
        if (snapshot.Any(static assembly => assembly is null))
            throw new ArgumentException(
                "An assembly-context role cannot contain a null assembly.",
                parameterName);
        return snapshot;
    }

    static void ValidateRole(
        ImmutableArray<ResolvedAssemblyReference> assemblies)
    {
        var identities = new HashSet<AssemblyReferenceIdentity>(
            AssemblyReferenceIdentity.EquivalentComparer);
        foreach (ResolvedAssemblyReference assembly in assemblies)
        {
            if (identities.Add(assembly.Identity))
                continue;

            throw new InvalidOperationException(
                "The selected artifacts contribute more than one assembly with the same "
                + "assembly identity to one workspace role, so a reference to it could not "
                + "bind to a single image.");
        }
    }

    static void ValidateCorrespondences(
        ImmutableArray<ResolvedAssemblyReference> surfaces,
        ImmutableArray<ResolvedAssemblyReference> implementations,
        ImmutableArray<PackageAssemblyRoleCorrespondence> pairs)
    {
        var surfaceSet = new HashSet<ResolvedAssemblyReference>(
            surfaces,
            ReferenceEqualityComparer.Instance);
        var implementationSet = new HashSet<ResolvedAssemblyReference>(
            implementations,
            ReferenceEqualityComparer.Instance);
        var pairedSurfaces = new HashSet<ResolvedAssemblyReference>(
            ReferenceEqualityComparer.Instance);

        foreach (PackageAssemblyRoleCorrespondence pair in pairs)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (!surfaceSet.Contains(pair.Surface))
            {
                throw new ArgumentException(
                    "A correspondence surface must belong to the surface role.",
                    nameof(pairs));
            }
            if (!implementationSet.Contains(pair.Implementation))
            {
                throw new ArgumentException(
                    "A correspondence implementation must belong to the implementation role.",
                    nameof(pairs));
            }
            if (!pairedSurfaces.Add(pair.Surface))
            {
                throw new ArgumentException(
                    "A surface assembly may have only one implementation correspondence.",
                    nameof(pairs));
            }
            if (pair.RequireEquivalentIdentity
                && !pair.Surface.Identity.IsEquivalentTo(
                    pair.Implementation.Identity))
            {
                throw new InvalidOperationException(
                    "The selected surface and implementation assemblies have different assembly "
                    + "identities.");
            }
        }
    }

    static AssemblyContextGroup CreateRole(
        InspectionWorkspace workspace,
        ImmutableArray<ResolvedAssemblyReference> assemblies,
        AssemblyContextGroupOptions? options,
        Func<
            int,
            IEnumerable<AssemblyContextParticipant>,
            AssemblyContextGroupOptions?,
            AssemblyContextGroup>? createRole,
        int roleIndex)
    {
        var policy = new RoleBindingPolicy(assemblies);
        IEnumerable<AssemblyContextParticipant> participants =
            assemblies.Select(
                assembly => new AssemblyContextParticipant(
                    assembly,
                    policy));
        return createRole is null
            ? workspace.CreateAssemblyContextGroup(
                participants,
                options)
            : createRole(
                roleIndex,
                participants,
                options);
    }

    static void ValidateSharedRole(
        ImmutableArray<ResolvedAssemblyReference> surfaces,
        ImmutableArray<ResolvedAssemblyReference> implementations,
        bool shareImplementationGroup,
        AssemblyContextGroupOptions? surfaceOptions,
        AssemblyContextGroupOptions? implementationOptions)
    {
        if (!shareImplementationGroup)
            return;
        if (implementations.IsEmpty
            || surfaces.Length != implementations.Length
            || !surfaces.Zip(implementations).All(
                pair => ReferenceEquals(pair.First, pair.Second)))
        {
            throw new ArgumentException(
                "A shared implementation role must use the exact surface descriptor sequence.",
                nameof(implementations));
        }

        AssemblyContextGroupOptions effectiveSurface =
            surfaceOptions ?? new AssemblyContextGroupOptions();
        AssemblyContextGroupOptions effectiveImplementation =
            implementationOptions ?? new AssemblyContextGroupOptions();
        if (effectiveSurface != effectiveImplementation)
        {
            throw new ArgumentException(
                "A shared surface and implementation group must use one resource-limit policy.",
                nameof(implementationOptions));
        }
    }

    static Dictionary<
        ResolvedAssemblyReference,
        AssemblyContextParticipant> ParticipantsByAssembly(
            ImmutableArray<AssemblyContextParticipant> participants)
    {
        var result = new Dictionary<
            ResolvedAssemblyReference,
            AssemblyContextParticipant>(ReferenceEqualityComparer.Instance);
        foreach (AssemblyContextParticipant participant in participants)
            result.Add(participant.Assembly, participant);
        return result;
    }

    sealed class RoleBindingPolicy(
        ImmutableArray<ResolvedAssemblyReference> assemblies)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            AssemblyBindingSelection selection;
            if (request.Target
                is AssemblyBindingTarget.IntrinsicCoreLibrary)
            {
                selection = AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.UnsupportedScope));
                return new AssemblyBindingSelectionSnapshot(
                    Version,
                    selection);
            }
            if (request.Target
                    is not AssemblyBindingTarget.AssemblyReference reference)
            {
                selection = AssemblyBindingSelection.Invalid(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.InvalidPolicyResult));
                return new AssemblyBindingSelectionSnapshot(
                    Version,
                    selection);
            }
            if (request.Scope == AssemblyResolutionScope.Platform)
            {
                return new AssemblyBindingSelectionSnapshot(
                    Version,
                    AssemblyBindingSelection.NameNotOwned());
            }

            ImmutableArray<ResolvedAssemblyReference> matches =
            [
                .. assemblies.Where(
                    assembly => assembly.Identity.IsEquivalentTo(
                        reference.Identity)),
            ];
            selection = matches.Length switch
            {
                0 => assemblies.Any(assembly =>
                        string.Equals(
                            assembly.Identity.Name,
                            reference.Identity.Name,
                            StringComparison.OrdinalIgnoreCase))
                    ? AssemblyBindingSelection.NameOwnedButNoMatch()
                    : AssemblyBindingSelection.NameNotOwned(),
                1 => AssemblyBindingSelection.Found(matches[0]),
                _ => AssemblyBindingSelection.Multiple(matches),
            };
            return new AssemblyBindingSelectionSnapshot(
                Version,
                selection);
        }
    }
}

public sealed partial class InspectionWorkspace
{
    /// <summary>
    /// Creates coordinated package surface and implementation roles from
    /// exact, already-acquired assembly descriptors.
    /// </summary>
    /// <param name="surfaceAssemblies">
    /// The reference-preferred assembly surface.
    /// </param>
    /// <param name="implementationAssemblies">
    /// The body-bearing implementation universe, or <see langword="null"/> when
    /// no implementation role exists.
    /// </param>
    /// <param name="correspondences">
    /// Exact surface-to-implementation pairs. Surface assemblies omitted from
    /// this list are reference-only.
    /// </param>
    /// <param name="shareImplementationGroup">
    /// Whether the implementation role is the exact surface role and should
    /// reuse its group. Sharing requires the same descriptor sequence and
    /// equivalent group options.
    /// </param>
    /// <remarks>
    /// Package roles refuse platform-scoped binding requests and intrinsic
    /// core-library selection. Platform-containing contexts use
    /// <see cref="WorkspaceContextLoader"/> instead.
    /// </remarks>
    public PackageAssemblyContextRoles CreatePackageAssemblyContextRoles(
        IEnumerable<ResolvedAssemblyReference> surfaceAssemblies,
        IEnumerable<ResolvedAssemblyReference>? implementationAssemblies,
        IEnumerable<PackageAssemblyRoleCorrespondence> correspondences,
        bool shareImplementationGroup = false,
        AssemblyContextGroupOptions? surfaceOptions = null,
        AssemblyContextGroupOptions? implementationOptions = null) =>
        new(
            this,
            surfaceAssemblies,
            implementationAssemblies,
            correspondences,
            shareImplementationGroup,
            surfaceOptions,
            implementationOptions);
}
