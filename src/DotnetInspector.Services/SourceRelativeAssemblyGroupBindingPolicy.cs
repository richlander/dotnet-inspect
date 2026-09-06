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
    readonly ImmutableDictionary<
        AssemblyAcquisitionRegistration,
        AssemblyRoute> _routes;
    readonly ImmutableArray<IAssemblyBindingPolicy> _delegates;
    readonly bool _composeParticipantSelections;
    BindingPolicyState _state;

    public SourceRelativeAssemblyGroupBindingPolicy(
        IEnumerable<(
            ResolvedAssemblyReference Assembly,
            IAssemblyBindingPolicy Policy)> participants)
        : this(participants, composeParticipantSelections: true)
    {
    }

    /// <summary>
    /// Routes through participant contexts without applying assembly-group
    /// candidate precedence. Selection remains owned by the delegates or their
    /// surrounding composite.
    /// </summary>
    public static SourceRelativeAssemblyGroupBindingPolicy CreateRoutingOnly(
        IEnumerable<(
            ResolvedAssemblyReference Assembly,
            IAssemblyBindingPolicy Policy)> participants) =>
        new(participants, composeParticipantSelections: false);

    SourceRelativeAssemblyGroupBindingPolicy(
        IEnumerable<(
            ResolvedAssemblyReference Assembly,
            IAssemblyBindingPolicy Policy)> participants,
        bool composeParticipantSelections)
    {
        ArgumentNullException.ThrowIfNull(participants);
        _composeParticipantSelections = composeParticipantSelections;
        var roots = ImmutableArray.CreateBuilder<
            ResolvedAssemblyReference>();
        var routes = ImmutableDictionary.CreateBuilder<
            AssemblyAcquisitionRegistration,
            AssemblyRoute>(
                ReferenceEqualityComparer.Instance);
        var delegates = ImmutableArray.CreateBuilder<
            IAssemblyBindingPolicy>();
        var seenDelegates = new HashSet<IAssemblyBindingPolicy>(
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
            if (seenDelegates.Add(policy))
                delegates.Add(policy);
        }

        if (roots.Count == 0)
        {
            throw new ArgumentException(
                "At least one assembly-group participant is required.",
                nameof(participants));
        }

        _roots = roots.ToImmutable();
        _routes = routes.ToImmutable();
        _delegates = delegates.ToImmutable();
        _state = CreateState();
    }

    public AssemblyBindingPolicyVersion Version =>
        CurrentState().Version;

    public AssemblyBindingSelectionSnapshot Select(
        AssemblyBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        BindingPolicyState state = CurrentState();
        try
        {
            return new AssemblyBindingSelectionSnapshot(
                state.Version,
                Select(state, request));
        }
        catch (ForeignSnapshotException foreign)
        {
            return foreign.Snapshot;
        }
    }

    AssemblyBindingSelection Select(
        BindingPolicyState state,
        AssemblyBindingRequest request)
    {
        if (RouteRequest(state, request) is not { } route)
        {
            return AssemblyBindingSelection.Invalid(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.InvalidBindingOrigin));
        }

        if (!_composeParticipantSelections)
        {
            return IssueSelection(
                state,
                route,
                SelectDelegate(state, route, request));
        }

        if (request.Target is AssemblyBindingTarget.IntrinsicCoreLibrary
            && request.Origin
                is AssemblyBindingOrigin.RequestingAssembly requesting)
        {
            var key = new IntrinsicSelectionKey(
                requesting.Assembly,
                requesting.Lineage,
                request.Scope);
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
                            route,
                            requesting,
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
                {
                    return IssueSelection(
                        state,
                        route,
                        AssemblyBindingSelection.Found(matches[0]));
                }
                if (matches.Length > 1)
                    return AssemblyBindingSelection.Multiple(matches);
            }
        }

        AssemblyBindingSelection selection =
            SelectDelegate(state, route, request);
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

        return IssueSelection(state, route, selection);
    }

    AssemblyBindingSelection SelectDelegate(
        BindingPolicyState state,
        RoutedRequest route,
        AssemblyBindingRequest request)
    {
        AssemblyBindingSelectionSnapshot? snapshot =
            route.Delegate.Policy.Select(route.DelegatedRequest);
        if (snapshot is not null
            && !ReferenceEquals(
                route.Delegate.Version,
                snapshot.Version))
        {
            Interlocked.CompareExchange(
                ref _state,
                CreateState(),
                state);
            throw new ForeignSnapshotException(snapshot);
        }

        return AssemblyBindingSelection.ValidateForRequest(
            request,
            snapshot?.Selection);
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
        ResolvedAssemblyReference chosen = designatedCandidates[0];
        return policySelection
                is AssemblyBindingSelection.Selected delegated
            && ReferenceEquals(
                chosen.Registration,
                delegated.Assembly.Registration)
                ? AssemblyBindingSelection.FoundOccurrence(
                    delegated.Occurrence,
                    platforms)
                : AssemblyBindingSelection.Found(
                    chosen,
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
        RoutedRequest route,
        AssemblyBindingOrigin.RequestingAssembly requesting,
        AssemblyBindingOrigin origin,
        AssemblyResolutionScope scope)
    {
        AssemblyBindingSelection selection =
            IntrinsicCoreLibraryBinding.Select(
                requesting.Assembly,
                facade => Select(
                    state,
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.Reference(facade),
                        origin,
                        scope)));
        selection = AssemblyBindingSelection.ValidateForRequest(
            new AssemblyBindingRequest(
                AssemblyBindingTarget.CoreLibrary(),
                origin,
                scope),
            selection);
        AssemblyBindingOccurrence? requestingOccurrence =
            route.RequestingOccurrence;
        if (selection
                is AssemblyBindingSelection.Selected selected
            && selected.Occurrence.Lineage
                == AssemblyBindingLineage.Seed
            && requestingOccurrence is not null
            && ReferenceEquals(
                selected.Assembly.Registration,
                requesting.Assembly.Registration))
        {
            return IssueSelection(
                state,
                route,
                AssemblyBindingSelection.FoundOccurrence(
                    requestingOccurrence,
                    selected.ShadowedAssemblies));
        }

        return IssueSelection(state, route, selection);
    }

    RoutedRequest? RouteRequest(
        BindingPolicyState state,
        AssemblyBindingRequest request)
    {
        if (request.Origin
            is not AssemblyBindingOrigin.RequestingAssembly requesting)
        {
            return new RoutedRequest(
                state.DelegateFor(DefaultRoute.Policy),
                request,
                null);
        }

        if (requesting.Lineage is null
            || requesting.Lineage == AssemblyBindingLineage.Seed)
        {
            AssemblyRoute route = _routes.GetValueOrDefault(
                    requesting.Registration)
                ?? DefaultRoute;
            return new RoutedRequest(
                state.DelegateFor(route.Policy),
                request,
                requesting.Occurrence
                    ?? AssemblyBindingOccurrence.Seed(
                        requesting.Assembly));
        }

        if (requesting.Lineage
                is not SourceRelativeBindingLineage lineage
            || !ReferenceEquals(lineage.Issuer, this)
            || !ReferenceEquals(lineage.State, state))
        {
            return null;
        }

        return new RoutedRequest(
            lineage.Delegate,
            new AssemblyBindingRequest(
                request.Target,
                AssemblyBindingOrigin.FromOccurrence(
                    lineage.DelegatedOccurrence),
                request.Scope),
            lineage.DelegatedOccurrence);
    }

    AssemblyBindingSelection IssueSelection(
        BindingPolicyState state,
        RoutedRequest route,
        AssemblyBindingSelection selection)
    {
        if (selection
            is not AssemblyBindingSelection.Selected selected)
        {
            return selection;
        }

        if (selected.Occurrence.Lineage
                is SourceRelativeBindingLineage issued
            && ReferenceEquals(issued.Issuer, this)
            && ReferenceEquals(issued.State, state))
        {
            return selection;
        }

        DelegateCapture bindingDelegate = route.Delegate;
        AssemblyBindingOccurrence delegatedOccurrence =
            selected.Occurrence;
        if (_routes.TryGetValue(
                selected.Assembly.Registration,
                out AssemblyRoute? canonicalRoute))
        {
            bindingDelegate = state.DelegateFor(
                canonicalRoute.Policy);
            delegatedOccurrence = AssemblyBindingOccurrence.Seed(
                canonicalRoute.Assembly);
        }

        var lineage = new SourceRelativeBindingLineage(
            this,
            state,
            bindingDelegate,
            delegatedOccurrence);
        return AssemblyBindingSelection.FoundOccurrence(
            lineage.Issue(selected.Assembly),
            selected.ShadowedAssemblies);
    }

    BindingPolicyState CurrentState()
    {
        while (true)
        {
            BindingPolicyState current = Volatile.Read(ref _state);
            if (IsCurrent(current))
                return current;

            BindingPolicyState replacement = CreateState();
            Interlocked.CompareExchange(
                ref _state,
                replacement,
                current);
        }
    }

    bool IsCurrent(BindingPolicyState state)
    {
        foreach (IAssemblyBindingPolicy policy in _delegates)
        {
            if (!ReferenceEquals(
                    state.DelegateFor(policy).Version,
                    policy.Version))
            {
                return false;
            }
        }

        return true;
    }

    BindingPolicyState CreateState()
    {
        var delegates = ImmutableDictionary.CreateBuilder<
            IAssemblyBindingPolicy,
            DelegateCapture>(
                ReferenceEqualityComparer.Instance);
        foreach (IAssemblyBindingPolicy policy in _delegates)
        {
            delegates.Add(
                policy,
                new DelegateCapture(policy, policy.Version));
        }

        return new BindingPolicyState(
            new AssemblyBindingPolicyVersion(),
            delegates.ToImmutable(),
            new IntrinsicSelectionCache());
    }

    AssemblyRoute DefaultRoute => _routes[_roots[0].Registration];

    sealed class BindingPolicyState(
        AssemblyBindingPolicyVersion version,
        ImmutableDictionary<
            IAssemblyBindingPolicy,
            DelegateCapture> delegates,
        IntrinsicSelectionCache intrinsicSelections)
    {
        internal AssemblyBindingPolicyVersion Version { get; } =
            version;
        internal IntrinsicSelectionCache IntrinsicSelections { get; } =
            intrinsicSelections;

        internal DelegateCapture DelegateFor(
            IAssemblyBindingPolicy policy) =>
            delegates[policy];
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

    sealed class DelegateCapture(
        IAssemblyBindingPolicy policy,
        AssemblyBindingPolicyVersion version)
    {
        internal IAssemblyBindingPolicy Policy { get; } = policy;
        internal AssemblyBindingPolicyVersion Version { get; } =
            version;
    }

    sealed record SourceRelativeBindingLineage :
        AssemblyBindingLineage
    {
        internal SourceRelativeBindingLineage(
            SourceRelativeAssemblyGroupBindingPolicy issuer,
            BindingPolicyState state,
            DelegateCapture bindingDelegate,
            AssemblyBindingOccurrence delegatedOccurrence)
            : base(state.Version)
        {
            Issuer = issuer;
            State = state;
            Delegate = bindingDelegate;
            DelegatedOccurrence = delegatedOccurrence;
        }

        internal SourceRelativeAssemblyGroupBindingPolicy Issuer
        {
            get;
        }
        internal BindingPolicyState State { get; }
        internal DelegateCapture Delegate { get; }
        internal AssemblyBindingOccurrence DelegatedOccurrence
        {
            get;
        }

        internal AssemblyBindingOccurrence Issue(
            ResolvedAssemblyReference assembly) =>
            CreateOccurrence(assembly);
    }

    sealed class ForeignSnapshotException(
        AssemblyBindingSelectionSnapshot snapshot) : Exception
    {
        internal AssemblyBindingSelectionSnapshot Snapshot { get; } =
            snapshot;
    }

    readonly record struct RoutedRequest(
        DelegateCapture Delegate,
        AssemblyBindingRequest DelegatedRequest,
        AssemblyBindingOccurrence? RequestingOccurrence);

    readonly record struct IntrinsicSelectionKey(
        ResolvedAssemblyReference RequestingAssembly,
        AssemblyBindingLineage? Lineage,
        AssemblyResolutionScope Scope);

    sealed class IntrinsicSelectionCache
    {
        readonly object _gate = new();
        readonly Dictionary<
            IntrinsicSelectionKey,
            Lazy<AssemblyBindingSelection>> _selections = [];

        internal bool TryGet(
            IntrinsicSelectionKey key,
            out Lazy<AssemblyBindingSelection>? selection)
        {
            lock (_gate)
                return _selections.TryGetValue(key, out selection);
        }

        internal Lazy<AssemblyBindingSelection> GetOrAdd(
            IntrinsicSelectionKey key,
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
