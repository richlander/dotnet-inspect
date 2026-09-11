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
    readonly bool _restrictToParticipants;
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

    /// <summary>
    /// Composes acquisition-free participant policies over an exact retained
    /// assembly group, preserving source-relative selection and lineage.
    /// </summary>
    /// <remarks>
    /// Construction does not select through a participant policy. All supplied
    /// descriptors must be backed by retained images. Selected, ambiguous, and
    /// shadow descriptors are replaced by their canonical group registrations;
    /// any out-of-group candidate makes the answer unavailable without opening
    /// it. Ordinary acquisition-capable policies cannot supply this capability.
    /// </remarks>
    public static IAcquisitionFreeAssemblyBindingPolicy CreateClosedWorld(
        IEnumerable<(
            ResolvedAssemblyReference Assembly,
            IAcquisitionFreeAssemblyBindingPolicy Policy)> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        return new ClosedWorldBindingPolicy(
            new SourceRelativeAssemblyGroupBindingPolicy(
                participants.Select(participant =>
                    (participant.Assembly, (IAssemblyBindingPolicy)participant.Policy)),
                composeParticipantSelections: true,
                restrictToParticipants: true));
    }

    SourceRelativeAssemblyGroupBindingPolicy(
        IEnumerable<(
            ResolvedAssemblyReference Assembly,
            IAssemblyBindingPolicy Policy)> participants,
        bool composeParticipantSelections,
        bool restrictToParticipants = false)
    {
        ArgumentNullException.ThrowIfNull(participants);
        _composeParticipantSelections = composeParticipantSelections;
        _restrictToParticipants = restrictToParticipants;
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
            AssemblyBindingSelection selection = Select(state, request);
            return new AssemblyBindingSelectionSnapshot(
                state.Version,
                _restrictToParticipants
                    ? RestrictSelection(selection)
                    : selection);
        }
        catch (ForeignSnapshotException foreign)
        {
            return foreign.Snapshot;
        }
    }

    AssemblyBindingSelection RestrictSelection(AssemblyBindingSelection selection)
    {
        switch (selection)
        {
            case AssemblyBindingSelection.Selected selected:
                if (!_routes.ContainsKey(selected.Assembly.Registration)
                    || selected.ShadowedAssemblies.Any(assembly =>
                        !_routes.ContainsKey(assembly.Registration)))
                {
                    return OutsideGroup();
                }
                return AssemblyBindingCandidateDomain.Create(
                    [
                        selected.Assembly,
                        .. selected.ShadowedAssemblies.Select(assembly =>
                            _routes[assembly.Registration].Assembly),
                    ]).Finalize(selected.Occurrence);
            case AssemblyBindingSelection.Ambiguous ambiguous:
                if (ambiguous.Assemblies
                        .Concat(ambiguous.ShadowedAssemblies)
                        .Any(assembly =>
                            !_routes.ContainsKey(assembly.Registration)))
                {
                    return OutsideGroup();
                }
                ImmutableArray<ResolvedAssemblyReference> active =
                [
                    .. ambiguous.Assemblies.Select(assembly =>
                        _routes[assembly.Registration].Assembly),
                ];
                if (ambiguous.ShadowedAssemblies.IsEmpty)
                    return AssemblyBindingSelection.Multiple(active);
                return AssemblyBindingCandidateDomain.Create(
                    [
                        .. active,
                        .. ambiguous.ShadowedAssemblies.Select(assembly =>
                            _routes[assembly.Registration].Assembly),
                    ]).Finalize(active);
            case AssemblyBindingSelection.CompositionRequired required:
                if (required.Domain.Candidates.Any(assembly =>
                        !_routes.ContainsKey(assembly.Registration)))
                {
                    return OutsideGroup();
                }
                return AssemblyBindingSelection.RequireComposition(
                    AssemblyBindingCandidateDomain.Create(
                    [
                        .. required.Domain.Candidates.Select(assembly =>
                            _routes[assembly.Registration].Assembly),
                    ]));
            default:
                return selection;
        }

        static AssemblyBindingSelection OutsideGroup() =>
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable));
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

        if (!_composeParticipantSelections)
        {
            return IssueSelection(
                state,
                route,
                SelectDelegate(state, route, request));
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
        ContinuationOrigin? compositionOrigin = null;
        if (reference is not null
            && (pendingDesignated is not null
                || selection
                    is AssemblyBindingSelection.CompositionRequired))
        {
            bool compositionRequired =
                selection
                    is AssemblyBindingSelection.CompositionRequired;
            selection = ComposePendingDesignated(
                reference.Identity,
                pendingDesignated,
                selection);
            if (compositionRequired
                && selection
                    is AssemblyBindingSelection.Selected selected
                && (!_routes.TryGetValue(
                        selected.Assembly.Registration,
                        out AssemblyRoute? canonicalRoute)
                    || ReferenceEquals(
                        canonicalRoute.Policy,
                        route.Delegate.Policy)))
            {
                compositionOrigin = route.ContinuationOrigin;
            }
        }
        if (reference is not null
            && selection
                is AssemblyBindingSelection.Missing
            {
                Disposition:
                            AssemblyBindingMissDisposition.NoNameOwner,
            }
            && IdentityMismatchSelection(reference.Identity)
                    is { } mismatch)
        {
            selection = mismatch;
        }

        return IssueSelection(
            state,
            route,
            selection,
            compositionOrigin);
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
        AssemblyBindingSelection? designated,
        AssemblyBindingSelection policySelection)
    {
        if (policySelection
            is AssemblyBindingSelection.Missing
            {
                Disposition:
                    AssemblyBindingMissDisposition.NoNameOwner,
            })
        {
            return designated ?? policySelection;
        }

        if (policySelection
            is AssemblyBindingSelection.Unavailable
                or AssemblyBindingSelection.Rejected)
        {
            return policySelection;
        }

        if (policySelection
            is AssemblyBindingSelection.CompositionRequired required)
        {
            ImmutableArray<ResolvedAssemblyReference> candidates =
                required.Domain.Candidates;
            if (candidates.Any(candidate =>
                !IsCompatibleEntitled(requested, candidate)))
            {
                return policySelection;
            }

            ImmutableArray<ResolvedAssemblyReference>
                handoffDesignatedCandidates =
            [
                .. candidates.Where(candidate =>
                    candidate.Provenance
                        is AssemblyResolutionProvenance.DesignatedAsset),
            ];
            return required.Domain.Finalize(
                handoffDesignatedCandidates.IsEmpty
                    ? candidates
                    : handoffDesignatedCandidates);
        }

        return policySelection;
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

    AssemblyBindingSelection? IdentityMismatchSelection(
        AssemblyReferenceIdentity requested)
    {
        ImmutableArray<ResolvedAssemblyReference> candidates =
        [
            .. _roots.Where(root =>
                string.Equals(
                    root.Identity.Name,
                    requested.Name,
                    StringComparison.OrdinalIgnoreCase)),
        ];
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
                _restrictToParticipants
                    ? _routes[requesting.Registration].Assembly
                    : requesting.Assembly,
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
        if (selection
                is AssemblyBindingSelection.Selected selected
            && selected.Occurrence.Lineage
                == AssemblyBindingLineage.Seed
            && ReferenceEquals(
                selected.Assembly.Registration,
                requesting.Assembly.Registration))
        {
            if (route.ContinuationOrigin.Occurrence
                    is { } requestingOccurrence
                && ReferenceEquals(
                    requestingOccurrence.Assembly.Registration,
                    selected.Assembly.Registration))
            {
                ResolvedAssemblyReference canonical =
                    requestingOccurrence.Assembly;
                selection = AssemblyBindingCandidateDomain.Create(
                    [
                        canonical,
                        .. selected.ShadowedAssemblies,
                    ]).Finalize(requestingOccurrence);
            }

            return IssueSelection(
                state,
                route,
                selection,
                route.ContinuationOrigin);
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
                ContinuationOrigin.Global);
        }

        if (_restrictToParticipants
            && !_routes.ContainsKey(requesting.Registration))
        {
            return null;
        }

        if (requesting.Lineage is null
            || requesting.Lineage == AssemblyBindingLineage.Seed)
        {
            AssemblyRoute route = _routes.GetValueOrDefault(
                    requesting.Registration)
                ?? DefaultRoute;
            AssemblyBindingOccurrence occurrence = requesting.Occurrence
                ?? AssemblyBindingOccurrence.Seed(requesting.Assembly);
            if (_restrictToParticipants)
            {
                occurrence = AssemblyBindingOccurrence.Seed(route.Assembly);
                request = new AssemblyBindingRequest(
                    request.Target,
                    requesting.Occurrence is null
                        ? AssemblyBindingOrigin.FromAssembly(route.Assembly)
                        : AssemblyBindingOrigin.FromOccurrence(occurrence),
                    request.Scope);
            }
            return new RoutedRequest(
                state.DelegateFor(route.Policy),
                request,
                ContinuationOrigin.FromOccurrence(occurrence));
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
                lineage.DelegatedOrigin.ToBindingOrigin(),
                request.Scope),
            lineage.DelegatedOrigin);
    }

    AssemblyBindingSelection IssueSelection(
        BindingPolicyState state,
        RoutedRequest route,
        AssemblyBindingSelection selection,
        ContinuationOrigin? delegatedOriginOverride = null)
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
        ResolvedAssemblyReference assembly = selected.Assembly;
        ContinuationOrigin delegatedOrigin =
            delegatedOriginOverride
            ?? ContinuationOrigin.FromOccurrence(
                selected.Occurrence);
        if (_routes.TryGetValue(
                selected.Assembly.Registration,
                out AssemblyRoute? canonicalRoute))
        {
            if (delegatedOriginOverride is null)
            {
                bindingDelegate = state.DelegateFor(
                    canonicalRoute.Policy);
                delegatedOrigin = ContinuationOrigin.FromOccurrence(
                    AssemblyBindingOccurrence.Seed(
                        canonicalRoute.Assembly));
            }
            if (_restrictToParticipants)
                assembly = canonicalRoute.Assembly;
        }

        var lineage = new SourceRelativeBindingLineage(
            this,
            state,
            bindingDelegate,
            delegatedOrigin);
        return AssemblyBindingCandidateDomain.Create(
            [assembly, .. selected.ShadowedAssemblies])
            .Finalize(lineage.Issue(assembly));
    }

    sealed class ClosedWorldBindingPolicy(
        SourceRelativeAssemblyGroupBindingPolicy inner)
        : IAcquisitionFreeAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version => inner.Version;

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) => inner.Select(request);
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

    abstract record ContinuationOrigin
    {
        internal static ContinuationOrigin Global { get; } =
            new GlobalOrigin();

        internal AssemblyBindingOccurrence? Occurrence =>
            this is OccurrenceOrigin occurrence
                ? occurrence.Value
                : null;

        internal static ContinuationOrigin FromOccurrence(
            AssemblyBindingOccurrence occurrence) =>
            new OccurrenceOrigin(occurrence);

        internal AssemblyBindingOrigin ToBindingOrigin() =>
            this is OccurrenceOrigin occurrence
                ? AssemblyBindingOrigin.FromOccurrence(
                    occurrence.Value)
                : AssemblyBindingOrigin.Global();

        sealed record GlobalOrigin : ContinuationOrigin;

        sealed record OccurrenceOrigin(
            AssemblyBindingOccurrence Value) : ContinuationOrigin;
    }

    sealed record SourceRelativeBindingLineage :
        AssemblyBindingLineage
    {
        internal SourceRelativeBindingLineage(
            SourceRelativeAssemblyGroupBindingPolicy issuer,
            BindingPolicyState state,
            DelegateCapture bindingDelegate,
            ContinuationOrigin delegatedOrigin)
            : base(state.Version)
        {
            Issuer = issuer;
            State = state;
            Delegate = bindingDelegate;
            DelegatedOrigin = delegatedOrigin;
        }

        internal SourceRelativeAssemblyGroupBindingPolicy Issuer
        {
            get;
        }
        internal BindingPolicyState State { get; }
        internal DelegateCapture Delegate { get; }
        internal ContinuationOrigin DelegatedOrigin { get; }

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
        ContinuationOrigin ContinuationOrigin);

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
