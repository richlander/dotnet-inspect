using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Services;

/// <summary>
/// Routes one frozen assembly group's binding requests through the policy that
/// owns the requesting assembly while preferring the group's canonical
/// descriptors for exact in-group identities.
/// </summary>
public sealed class SourceRelativeAssemblyGroupBindingPolicy :
    IAssemblyBindingPolicy
{
    readonly ImmutableArray<ResolvedAssemblyReference> _roots;
    BindingPolicyState _state;

    public SourceRelativeAssemblyGroupBindingPolicy(
        IEnumerable<(
            ResolvedAssemblyReference Assembly,
            IAssemblyBindingPolicy Policy)> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        var roots = ImmutableArray.CreateBuilder<
            ResolvedAssemblyReference>();
        var routes = ImmutableDictionary.CreateBuilder<
            AssemblyAcquisitionRegistration,
            AssemblyRoute>(
                ReferenceEqualityComparer.Instance);
        foreach ((ResolvedAssemblyReference assembly,
            IAssemblyBindingPolicy policy) in participants)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentNullException.ThrowIfNull(policy);
            roots.Add(assembly);
            routes.Add(
                assembly.Registration,
                new AssemblyRoute(assembly, policy));
        }

        if (roots.Count == 0)
        {
            throw new ArgumentException(
                "At least one assembly-group participant is required.",
                nameof(participants));
        }

        _roots = roots.ToImmutable();
        ImmutableDictionary<
            AssemblyAcquisitionRegistration,
            AssemblyRoute> initialRoutes = routes.ToImmutable();
        _state = new BindingPolicyState(
            new AssemblyBindingPolicyVersion(),
            initialRoutes[_roots[0].Registration].Policy,
            initialRoutes,
            new IntrinsicSelectionCache());
    }

    public AssemblyBindingPolicyVersion Version =>
        Volatile.Read(ref _state).Version;

    public AssemblyBindingSelection Select(
        AssemblyBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Select(
            Volatile.Read(ref _state),
            request);
    }

    AssemblyBindingSelection Select(
        BindingPolicyState state,
        AssemblyBindingRequest request)
    {
        IAssemblyBindingPolicy policy =
            PolicyFor(state, request.Origin);
        if (request.Target is AssemblyBindingTarget.IntrinsicCoreLibrary
            && request.Origin
                is AssemblyBindingOrigin.RequestingAssembly requesting
            && AssemblyFor(state, requesting.Registration)
                is { } requestingAssembly)
        {
            var key = (requesting.Registration, request.Scope);
            if (!state.IntrinsicSelections.TryGet(
                    key,
                    out Lazy<AssemblyBindingSelection>?
                        intrinsicSelection))
            {
                intrinsicSelection =
                    state.IntrinsicSelections.GetOrAdd(
                        key,
                        () => SelectIntrinsicCoreLibrary(
                            state,
                            requestingAssembly,
                            request.Origin,
                            request.Scope));
            }

            return AssemblyBindingSelection.ValidateForRequest(
                request,
                intrinsicSelection!.Value);
        }

        AssemblyBindingTarget.AssemblyReference? reference =
            request.Target as AssemblyBindingTarget.AssemblyReference;
        AssemblyBindingSelection? pendingDesignated = null;
        if (reference is not null)
        {
            bool nonEntitledNameOwner =
                request.Scope == AssemblyResolutionScope.Any
                && _roots.Any(root =>
                    root.Provenance
                        is not (
                            AssemblyResolutionProvenance.DesignatedAsset
                            or AssemblyResolutionProvenance.PlatformAsset)
                    && string.Equals(
                        root.Identity.Name,
                        reference.Identity.Name,
                        StringComparison.OrdinalIgnoreCase));
            if (!nonEntitledNameOwner
                && DesignatedAssemblyBindingPrecedence.TrySelect(
                        reference.Identity,
                        _roots)
                    is { } precedenceSelection)
            {
                pendingDesignated = precedenceSelection;
            }

            if (pendingDesignated is null)
            {
                ImmutableArray<ResolvedAssemblyReference> matches =
                [
                    .. _roots.Where(
                        root => SameIdentity(
                            root.Identity,
                            reference.Identity)),
                ];
                if (matches.Length == 1)
                    return AssemblyBindingSelection.Found(matches[0]);
                if (matches.Length > 1)
                    return AssemblyBindingSelection.Multiple(matches);
            }
        }

        AssemblyBindingSelection selection =
            AssemblyBindingSelection.ValidateForRequest(
                request,
                policy.Select(request));
        if (reference is not null
            && pendingDesignated is not null)
        {
            selection = ComposePendingDesignated(
                reference.Identity,
                pendingDesignated,
                selection);
        }
        if (reference is not null
            && (selection
                    is AssemblyBindingSelection.Missing
                    {
                        Disposition:
                            AssemblyBindingMissDisposition.NoNameOwner,
                    }
                || selection
                    is AssemblyBindingSelection.Selected)
            && IdentityMismatchSelection(
                reference.Identity,
                (selection as AssemblyBindingSelection.Selected)
                    ?.Assembly)
                    is { } mismatch)
        {
            selection = mismatch;
        }

        switch (selection)
        {
            case AssemblyBindingSelection.Selected selected:
                Register([selected.Assembly], policy);
                break;
            case AssemblyBindingSelection.Ambiguous ambiguous:
                Register(ambiguous.Assemblies, policy);
                break;
        }

        return selection;
    }

    static AssemblyBindingSelection ComposePendingDesignated(
        AssemblyReferenceIdentity requested,
        AssemblyBindingSelection designated,
        AssemblyBindingSelection policySelection)
    {
        if (policySelection
            is AssemblyBindingSelection.Missing
            {
                Disposition:
                    AssemblyBindingMissDisposition.NoNameOwner,
            })
        {
            return designated;
        }

        if (policySelection
            is AssemblyBindingSelection.Unavailable
                or AssemblyBindingSelection.Rejected)
        {
            return policySelection;
        }

        ImmutableArray<ResolvedAssemblyReference> policyCandidates =
            policySelection switch
            {
                AssemblyBindingSelection.Selected selected =>
                    [selected.Assembly],
                AssemblyBindingSelection.Ambiguous ambiguous =>
                    ambiguous.Assemblies,
                _ => [],
            };
        if (policyCandidates.IsEmpty
            || policyCandidates.Any(candidate =>
                !IsCompatibleEntitled(requested, candidate)))
        {
            return policySelection;
        }

        ImmutableArray<ResolvedAssemblyReference> designatedCandidates =
            DistinctRegistrations(
                DesignatedCandidates(designated)
                    .Concat(policyCandidates.Where(candidate =>
                        candidate.Provenance
                            is AssemblyResolutionProvenance
                                .DesignatedAsset)));
        if (designatedCandidates.Length > 1)
        {
            return AssemblyBindingSelection.Multiple(
                designatedCandidates);
        }

        IEnumerable<ResolvedAssemblyReference> policyShadows =
            policySelection
                is AssemblyBindingSelection.Selected selectedWithShadows
                    ? selectedWithShadows.ShadowedAssemblies
                    : [];
        IEnumerable<ResolvedAssemblyReference> designatedShadows =
            designated
                is AssemblyBindingSelection.Selected selectedDesignated
                    ? selectedDesignated.ShadowedAssemblies
                    : [];
        ImmutableArray<ResolvedAssemblyReference> platforms =
            DistinctRegistrations(
                designatedShadows
                    .Concat(policyCandidates)
                    .Concat(policyShadows)
                    .Where(candidate => IsCompatiblePlatform(
                        requested,
                        candidate,
                        ignoreVersion: true)));
        return AssemblyBindingSelection.Found(
            designatedCandidates[0],
            platforms);
    }

    static ImmutableArray<ResolvedAssemblyReference> DesignatedCandidates(
        AssemblyBindingSelection selection) =>
        selection switch
        {
            AssemblyBindingSelection.Selected selected
                when selected.Assembly.Provenance
                    is AssemblyResolutionProvenance.DesignatedAsset =>
                [selected.Assembly],
            AssemblyBindingSelection.Ambiguous ambiguous =>
                [
                    .. ambiguous.Assemblies.Where(candidate =>
                        candidate.Provenance
                            is AssemblyResolutionProvenance
                                .DesignatedAsset),
                ],
            _ => [],
        };

    static ImmutableArray<ResolvedAssemblyReference> DistinctRegistrations(
        IEnumerable<ResolvedAssemblyReference> candidates)
    {
        var seen = new HashSet<AssemblyAcquisitionRegistration>(
            ReferenceEqualityComparer.Instance);
        return
        [
            .. candidates.Where(candidate =>
                seen.Add(candidate.Registration)),
        ];
    }

    static bool IsCompatibleEntitled(
        AssemblyReferenceIdentity requested,
        ResolvedAssemblyReference candidate) =>
        candidate.Provenance is (
            AssemblyResolutionProvenance.DesignatedAsset
            or AssemblyResolutionProvenance.PlatformAsset)
        && requested.MatchesCandidate(
            candidate.Identity,
            allowVersionRollForward: false,
            ignoreVersion: true);

    static bool IsCompatiblePlatform(
        AssemblyReferenceIdentity requested,
        ResolvedAssemblyReference candidate,
        bool ignoreVersion) =>
        candidate.Provenance
            is AssemblyResolutionProvenance.PlatformAsset
        && requested.MatchesCandidate(
            candidate.Identity,
            allowVersionRollForward: false,
            ignoreVersion);

    AssemblyBindingSelection? IdentityMismatchSelection(
        AssemblyReferenceIdentity requested,
        ResolvedAssemblyReference? selected)
    {
        ImmutableArray<ResolvedAssemblyReference> candidates =
        [
            .. _roots.Where(root =>
                string.Equals(
                    root.Identity.Name,
                    requested.Name,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        if (selected is not null
            && (!string.Equals(
                    selected.Identity.Name,
                    requested.Name,
                    StringComparison.OrdinalIgnoreCase)
                || candidates.Any(candidate =>
                    SameIdentity(
                        candidate.Identity,
                        selected.Identity))))
        {
            return null;
        }

        return candidates.Length switch
        {
            0 => null,
            1 => AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.IdentityPolicyRequired)),
            _ => AssemblyBindingSelection.Multiple(candidates),
        };
    }

    static bool SameIdentity(
        AssemblyReferenceIdentity left,
        AssemblyReferenceIdentity right) =>
        string.Equals(
            left.Name,
            right.Name,
            StringComparison.OrdinalIgnoreCase)
        && left.Version == right.Version
        && string.Equals(
            NormalizeCulture(left.Culture),
            NormalizeCulture(right.Culture),
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            NormalizeOptional(left.PublicKeyToken),
            NormalizeOptional(right.PublicKeyToken),
            StringComparison.OrdinalIgnoreCase);

    static string NormalizeCulture(string? culture) =>
        string.IsNullOrEmpty(culture)
            || culture.Equals(
                "neutral",
                StringComparison.OrdinalIgnoreCase)
                ? ""
                : culture;

    static string NormalizeOptional(string? value) =>
        string.IsNullOrEmpty(value) ? "" : value;

    AssemblyBindingSelection SelectIntrinsicCoreLibrary(
        BindingPolicyState state,
        ResolvedAssemblyReference requestingAssembly,
        AssemblyBindingOrigin origin,
        AssemblyResolutionScope scope)
        => IntrinsicCoreLibraryBinding.Select(
            requestingAssembly,
            facade => Select(
                state,
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(facade),
                    origin,
                    scope)));

    static IAssemblyBindingPolicy PolicyFor(
        BindingPolicyState state,
        AssemblyBindingOrigin origin)
    {
        if (origin is not AssemblyBindingOrigin.RequestingAssembly requesting)
            return state.Default;

        return state.Routes.TryGetValue(
            requesting.Registration,
            out AssemblyRoute? route)
                ? route.Policy
                : state.Default;
    }

    static ResolvedAssemblyReference? AssemblyFor(
        BindingPolicyState state,
        AssemblyAcquisitionRegistration registration)
        => state.Routes.GetValueOrDefault(registration)?.Assembly;

    void Register(
        IEnumerable<ResolvedAssemblyReference> assemblies,
        IAssemblyBindingPolicy policy)
    {
        while (true)
        {
            BindingPolicyState current = Volatile.Read(ref _state);
            ImmutableDictionary<
                AssemblyAcquisitionRegistration,
                AssemblyRoute> routes = current.Routes;

            bool changed = false;
            foreach (ResolvedAssemblyReference assembly in assemblies)
            {
                if (routes.ContainsKey(assembly.Registration))
                    continue;

                routes = routes.Add(
                    assembly.Registration,
                    new AssemblyRoute(assembly, policy));
                changed = true;
            }

            if (!changed)
                return;

            BindingPolicyState replacement =
                current.WithLearnedRoutes(routes);
            if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _state,
                        replacement,
                        current),
                    current))
            {
                return;
            }
        }
    }

    sealed class BindingPolicyState(
        AssemblyBindingPolicyVersion version,
        IAssemblyBindingPolicy defaultPolicy,
        ImmutableDictionary<
            AssemblyAcquisitionRegistration,
            AssemblyRoute> routes,
        IntrinsicSelectionCache intrinsicSelections)
    {
        internal AssemblyBindingPolicyVersion Version { get; } =
            version;
        internal IAssemblyBindingPolicy Default { get; } =
            defaultPolicy;
        internal ImmutableDictionary<
            AssemblyAcquisitionRegistration,
            AssemblyRoute> Routes { get; } =
                routes;
        internal IntrinsicSelectionCache IntrinsicSelections { get; } =
            intrinsicSelections;

        internal BindingPolicyState WithLearnedRoutes(
            ImmutableDictionary<
                AssemblyAcquisitionRegistration,
                AssemblyRoute> learnedRoutes)
        {
            // Learning only fills previously unknown registrations, so cached
            // intrinsic answers for already-known origins remain valid.
            return new BindingPolicyState(
                Version,
                Default,
                learnedRoutes,
                IntrinsicSelections);
        }
    }

    sealed class AssemblyRoute(
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy policy)
    {
        internal ResolvedAssemblyReference Assembly { get; } =
            assembly;
        internal IAssemblyBindingPolicy Policy { get; } =
            policy;
    }

    sealed class IntrinsicSelectionCache
    {
        readonly object _gate = new();
        readonly Dictionary<
            (AssemblyAcquisitionRegistration Origin,
                AssemblyResolutionScope Scope),
            Lazy<AssemblyBindingSelection>> _selections = [];

        internal bool TryGet(
            (AssemblyAcquisitionRegistration Origin,
                AssemblyResolutionScope Scope) key,
            out Lazy<AssemblyBindingSelection>? selection)
        {
            lock (_gate)
                return _selections.TryGetValue(key, out selection);
        }

        internal Lazy<AssemblyBindingSelection> GetOrAdd(
            (AssemblyAcquisitionRegistration Origin,
                AssemblyResolutionScope Scope) key,
            Func<AssemblyBindingSelection> select)
        {
            lock (_gate)
            {
                if (_selections.TryGetValue(
                        key,
                        out Lazy<AssemblyBindingSelection>? selection))
                {
                    return selection;
                }

                selection = new Lazy<AssemblyBindingSelection>(
                    select,
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _selections.Add(key, selection);
                return selection;
            }
        }
    }
}
