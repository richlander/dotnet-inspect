using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// One frozen caller-scope plan shared by direct caller matching and graph
/// reachability.
/// </summary>
public sealed class CallerScopeReachabilityPlan
{
    CallerScopeReachabilityPlan(
        ImmutableArray<ResolvedAssemblyReference> directCandidates,
        ImmutableArray<ResolvedAssemblyReference> graphCandidates,
        CallerResolutionPlan resolution,
        bool hasRuledOutCandidateNotDefinitelyUnopenable)
    {
        DirectCandidates = directCandidates;
        GraphCandidates = graphCandidates;
        Resolution = resolution;
        HasRuledOutCandidateNotDefinitelyUnopenable =
            hasRuledOutCandidateNotDefinitelyUnopenable;
    }

    public ImmutableArray<ResolvedAssemblyReference> DirectCandidates { get; }
    public ImmutableArray<ResolvedAssemblyReference> GraphCandidates { get; }
    public CallerResolutionPlan Resolution { get; }

    public bool HasRuledOutCandidateNotDefinitelyUnopenable { get; }

    public static CallerScopeReachabilityPlan Create(
        IAssemblyBindingPolicy bindingPolicy,
        ResolvedAssemblyReference targetAssembly,
        TypeRef openDeclaringType,
        IReadOnlyList<ResolvedAssemblyReference> candidates,
        TypeResolutionContextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentNullException.ThrowIfNull(targetAssembly);
        ArgumentNullException.ThrowIfNull(openDeclaringType);
        ArgumentNullException.ThrowIfNull(candidates);

        ResolvableTypeReference target = openDeclaringType.Resolution
            ?? throw new ArgumentException(
                "The target declaring type must retain decoder-produced resolution provenance.",
                nameof(openDeclaringType));

        CandidateSnapshot[] snapshots = candidates
            .Select(candidate => ReadCandidate(candidate, target.Type))
            .ToArray();
        ResolvedAssemblyReference[] openable = snapshots
            .Where(static snapshot => snapshot.Openable)
            .Select(static snapshot => snapshot.Assembly)
            .DistinctBy(static assembly => assembly.Registration)
            .ToArray();
        var policy = new ScopeFirstBindingPolicy(
            bindingPolicy,
            targetAssembly,
            openable);
        using var catalog = new TypeResolutionCatalog(options);
        return CreateCore(
                catalog,
                policy,
                targetAssembly,
                target.Type,
                snapshots);
    }

    static CallerScopeReachabilityPlan CreateCore(
        TypeResolutionCatalog catalog,
        IAssemblyBindingPolicy policy,
        ResolvedAssemblyReference targetAssembly,
        MetadataTypeDefinitionName targetType,
        CandidateSnapshot[] snapshots)
    {
        TypeResolutionRequest targetRequest =
            TypeResolutionRequest.FromAssembly(
                targetAssembly,
                AssemblyResolutionScope.Any,
                targetType);
        var requests = new List<TypeResolutionRequest> { targetRequest };
        var requestReferences = new Dictionary<
            TypeResolutionRequest,
            CallerResolutionPlan.CandidateReferenceKey>();
        var ownRequests = new Dictionary<
            AssemblyAcquisitionRegistration,
            TypeResolutionRequest>();
        var bindings = new List<AssemblyBindingRequest>();
        var bindingOwners = new Dictionary<
            AssemblyBindingRequest,
            AssemblyAcquisitionRegistration>();

        foreach (CandidateSnapshot snapshot in snapshots)
        {
            if (!snapshot.Openable)
                continue;

            if (snapshot.DefinesTarget)
            {
                TypeResolutionRequest own =
                    TypeResolutionRequest.FromAssembly(
                        snapshot.Assembly,
                        AssemblyResolutionScope.Any,
                        targetType);
                requests.Add(own);
                ownRequests.Add(snapshot.Assembly.Registration, own);
            }

            foreach (ResolvableTypeReference reference
                in snapshot.MatchingReferences)
            {
                TypeResolutionRequest request =
                    TypeResolutionRequestFactory.Create(
                        snapshot.Assembly,
                        reference);
                requests.Add(request);
                requestReferences.TryAdd(
                    request,
                    new(
                        snapshot.Assembly.Registration,
                        reference));
            }

            foreach (AssemblyReferenceIdentity reference
                in snapshot.AssemblyReferences)
            {
                var binding = new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(reference),
                    AssemblyBindingOrigin.FromAssembly(snapshot.Assembly),
                    TypeResolutionRequestFactory.Scope(reference));
                bindings.Add(binding);
                bindingOwners.Add(
                    binding,
                    snapshot.Assembly.Registration);
            }
        }

        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            snapshots
                .Where(static snapshot => snapshot.Openable)
                .Select(static snapshot => snapshot.Assembly)
                .Prepend(targetAssembly),
            bindings,
            requests);

        var relations = new Dictionary<
            CallerResolutionPlan.CandidateReferenceKey,
            CandidateTypeRelation>();
        var incomplete = snapshots
            .Where(static snapshot =>
                snapshot.Openable && !snapshot.Complete)
            .Select(static snapshot => snapshot.Assembly.Registration)
            .ToHashSet();
        TypeResolutionOutcome targetOutcome =
            context.Resolve(targetRequest);
        ResolvedTypeDefinitionKey? targetKey =
            (targetOutcome as TypeResolutionOutcome.Resolved)
                ?.Definition.Key;

        foreach (KeyValuePair<
            TypeResolutionRequest,
            CallerResolutionPlan.CandidateReferenceKey> pair
            in requestReferences)
        {
            relations[pair.Value] = Relate(
                catalog,
                context.Resolve(pair.Key),
                targetKey,
                targetOutcome);
        }

        foreach (KeyValuePair<
            AssemblyAcquisitionRegistration,
            TypeResolutionRequest> pair in ownRequests)
        {
            var reference = new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                targetType);
            relations[
                new CallerResolutionPlan.CandidateReferenceKey(
                    pair.Key,
                    reference)] =
                Relate(
                    catalog,
                    context.Resolve(pair.Value),
                    targetKey,
                    targetOutcome);
        }

        var direct = new HashSet<AssemblyAcquisitionRegistration>();
        var graphSeeds = new HashSet<AssemblyAcquisitionRegistration>();
        foreach (CandidateSnapshot snapshot in snapshots)
        {
            if (!snapshot.Openable)
                continue;

            bool hasSame = false;
            bool hasIndeterminate = !snapshot.Complete;
            foreach (ResolvableTypeReference reference
                in snapshot.MatchingReferences)
            {
                CandidateTypeRelation relation = relations[
                    new CallerResolutionPlan.CandidateReferenceKey(
                        snapshot.Assembly.Registration,
                        reference)];
                hasSame |= relation
                    is CandidateTypeRelation.SameDefinition;
                hasIndeterminate |= relation
                    is CandidateTypeRelation.Indeterminate;
            }

            if (snapshot.DefinesTarget)
            {
                CandidateTypeRelation relation = relations[
                    new CallerResolutionPlan.CandidateReferenceKey(
                        snapshot.Assembly.Registration,
                        new ResolvableTypeReference(
                            new TypeReferenceOrigin.CurrentAssembly(),
                            targetType))];
                hasSame |= relation
                    is CandidateTypeRelation.SameDefinition;
                hasIndeterminate |= relation
                    is CandidateTypeRelation.Indeterminate;
            }

            if (hasSame || hasIndeterminate)
            {
                direct.Add(snapshot.Assembly.Registration);
                graphSeeds.Add(snapshot.Assembly.Registration);
            }
        }

        var reverse = new Dictionary<
            AssemblyAcquisitionRegistration,
            HashSet<AssemblyAcquisitionRegistration>>();
        var traversedForwarders = new HashSet<
            (AssemblyAcquisitionRegistration Registration,
            AssemblyBindingLineage Lineage,
            AssemblyResolutionScope Scope)>();
        foreach (AssemblyBindingRequest binding in bindings)
        {
            AssemblyAcquisitionRegistration source =
                bindingOwners[binding];
            if (context.Bind(binding)
                is AssemblyBindingOutcome.Resolved resolved)
            {
                AddEdge(
                    reverse,
                    source,
                    resolved.Candidate.Assembly.Registration);
                AddForwarderEdges(
                    context,
                    resolved,
                    binding.Scope,
                    reverse,
                    graphSeeds,
                    traversedForwarders);
            }
            else
            {
                graphSeeds.Add(source);
            }
        }

        var graph = ReverseClosure(
            reverse,
            graphSeeds
                .Append(targetAssembly.Registration));
        graph.UnionWith(direct);

        ImmutableArray<ResolvedAssemblyReference> directCandidates =
            snapshots
                .Where(snapshot =>
                    direct.Contains(snapshot.Assembly.Registration))
                .Select(static snapshot => snapshot.Assembly)
                .ToImmutableArray();
        ImmutableArray<ResolvedAssemblyReference> graphCandidates =
            snapshots
                .Where(snapshot =>
                    graph.Contains(snapshot.Assembly.Registration))
                .Select(static snapshot => snapshot.Assembly)
                .ToImmutableArray();
        bool ruledOutOpenable = snapshots.Any(snapshot =>
            snapshot.Openable
            && !graph.Contains(snapshot.Assembly.Registration));

        return new CallerScopeReachabilityPlan(
            directCandidates,
            graphCandidates,
            new CallerResolutionPlan(
                targetType,
                relations,
                incomplete),
            ruledOutOpenable);
    }

    static CandidateTypeRelation Relate(
        TypeResolutionCatalog catalog,
        TypeResolutionOutcome outcome,
        ResolvedTypeDefinitionKey? target,
        TypeResolutionOutcome targetOutcome)
    {
        if (target is null
            || outcome is not TypeResolutionOutcome.Resolved resolved)
        {
            return new CandidateTypeRelation.Indeterminate(
                new TypeCorrespondenceFailure.Resolution(
                    target is null ? targetOutcome : outcome));
        }

        return catalog.Compare(resolved.Definition.Key, target) switch
        {
            DefinitionCorrespondence.Same =>
                new CandidateTypeRelation.SameDefinition(),
            DefinitionCorrespondence.Different =>
                new CandidateTypeRelation.DifferentDefinition(),
            DefinitionCorrespondence.IndeterminateDuplicateArtifact duplicate =>
                new CandidateTypeRelation.Indeterminate(
                    new TypeCorrespondenceFailure.DuplicateArtifact(duplicate)),
            DefinitionCorrespondence.IncomparableCatalogs catalogs =>
                new CandidateTypeRelation.Indeterminate(
                    new TypeCorrespondenceFailure.IncomparableCatalogs(
                        catalogs.Left,
                        catalogs.Right)),
            DefinitionCorrespondence.StaleGeneration generation =>
                new CandidateTypeRelation.Indeterminate(
                    new TypeCorrespondenceFailure.StaleGeneration(
                        generation.Left,
                        generation.Right)),
            _ => throw new InvalidOperationException(
                "Unknown definition correspondence result."),
        };
    }

    static void AddForwarderEdges(
        TypeResolutionContext context,
        AssemblyBindingOutcome.Resolved first,
        AssemblyResolutionScope firstScope,
        Dictionary<
            AssemblyAcquisitionRegistration,
            HashSet<AssemblyAcquisitionRegistration>> reverse,
        HashSet<AssemblyAcquisitionRegistration> graphSeeds,
        HashSet<
            (AssemblyAcquisitionRegistration Registration,
            AssemblyBindingLineage Lineage,
            AssemblyResolutionScope Scope)> visited)
    {
        var pending = new Stack<
            (AssemblyBindingOutcome.Resolved Binding,
            AssemblyResolutionScope Scope)>();
        pending.Push((first, firstScope));
        while (pending.Count > 0)
        {
            (AssemblyBindingOutcome.Resolved bound,
                AssemblyResolutionScope currentScope) = pending.Pop();
            ResolvedAssemblyCandidate candidate = bound.Candidate;
            ResolvedAssemblyReference source = candidate.Assembly;
            if (!visited.Add((
                    source.Registration,
                    bound.Occurrence.Lineage,
                    currentScope)))
                continue;

            AssemblyInventorySnapshot inventory =
                context.GetInventory(candidate);
            foreach (AssemblyReferenceIdentity target
                in inventory.ForwarderTargets)
            {
                AssemblyResolutionScope nextScope =
                    currentScope == AssemblyResolutionScope.Platform
                        || PlatformKeys.IsPlatform(target.PublicKeyToken)
                            ? AssemblyResolutionScope.Platform
                            : AssemblyResolutionScope.Any;
                var binding = new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(target),
                    AssemblyBindingOrigin.FromOccurrence(bound.Occurrence),
                    nextScope);
                if (context.Bind(binding)
                    is AssemblyBindingOutcome.Resolved resolved)
                {
                    AddEdge(
                        reverse,
                        source.Registration,
                        resolved.Candidate.Assembly.Registration);
                    pending.Push((resolved, nextScope));
                }
                else
                {
                    graphSeeds.Add(source.Registration);
                }
            }
        }
    }

    static void AddEdge(
        Dictionary<
            AssemblyAcquisitionRegistration,
            HashSet<AssemblyAcquisitionRegistration>> reverse,
        AssemblyAcquisitionRegistration source,
        AssemblyAcquisitionRegistration target)
    {
        if (!reverse.TryGetValue(target, out var sources))
            reverse.Add(target, sources = []);
        sources.Add(source);
    }

    static HashSet<AssemblyAcquisitionRegistration> ReverseClosure(
        IReadOnlyDictionary<
            AssemblyAcquisitionRegistration,
            HashSet<AssemblyAcquisitionRegistration>> reverse,
        IEnumerable<AssemblyAcquisitionRegistration> roots)
    {
        var selected = new HashSet<AssemblyAcquisitionRegistration>();
        var pending = new Queue<AssemblyAcquisitionRegistration>(roots);
        while (pending.TryDequeue(out AssemblyAcquisitionRegistration? current))
        {
            if (!selected.Add(current)
                || !reverse.TryGetValue(current, out var sources))
            {
                continue;
            }

            foreach (AssemblyAcquisitionRegistration source in sources)
                pending.Enqueue(source);
        }

        return selected;
    }

    static CandidateSnapshot ReadCandidate(
        ResolvedAssemblyReference assembly,
        MetadataTypeDefinitionName target)
    {
        var matching = ImmutableArray.CreateBuilder<ResolvableTypeReference>();
        var references = ImmutableArray.CreateBuilder<AssemblyReferenceIdentity>();
        bool complete = true;
        bool definesTarget = false;

        try
        {
            using Stream stream = assembly.OpenRead();
            using var pe = new PEReader(
                stream,
                PEStreamOptions.PrefetchMetadata);
            if (!pe.HasMetadata)
                return CandidateSnapshot.Unopenable(assembly);

            MetadataReader reader = pe.GetMetadataReader();
            try
            {
                foreach (AssemblyReferenceHandle handle
                    in reader.AssemblyReferences)
                {
                    references.Add(
                        AssemblyReferenceIdentity.From(reader, handle));
                }
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                complete = false;
            }

            try
            {
                foreach (TypeDefinitionHandle handle
                    in reader.TypeDefinitions)
                {
                    TypeRef decoded =
                        TypeRefDecoder.Instance.GetTypeFromDefinition(
                            reader,
                            handle,
                            0);
                    if (decoded.Resolution is null)
                    {
                        complete = false;
                        continue;
                    }

                    if (decoded.Resolution.Type.Equals(target))
                        definesTarget = true;
                }
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                complete = false;
            }

            try
            {
                foreach (TypeReferenceHandle handle
                    in reader.TypeReferences)
                {
                    TypeRef decoded =
                        TypeRefDecoder.Instance.GetTypeFromReference(
                            reader,
                            handle,
                            0);
                    if (decoded.Resolution is null)
                    {
                        complete = false;
                        continue;
                    }

                    if (decoded.Resolution.Type.Equals(target))
                        matching.Add(decoded.Resolution);
                }
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                complete = false;
            }
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException)
        {
            return CandidateSnapshot.Unopenable(assembly);
        }

        return new CandidateSnapshot(
            assembly,
            Openable: true,
            complete,
            definesTarget,
            matching.ToImmutable(),
            references.ToImmutable());
    }

    sealed record CandidateSnapshot(
        ResolvedAssemblyReference Assembly,
        bool Openable,
        bool Complete,
        bool DefinesTarget,
        ImmutableArray<ResolvableTypeReference> MatchingReferences,
        ImmutableArray<AssemblyReferenceIdentity> AssemblyReferences)
    {
        internal static CandidateSnapshot Unopenable(
            ResolvedAssemblyReference assembly) =>
            new(
                assembly,
                Openable: false,
                Complete: false,
                DefinesTarget: false,
                [],
                []);
    }

    internal sealed class ScopeFirstBindingPolicy : IAssemblyBindingPolicy
    {
        BindingState _state;

        internal ScopeFirstBindingPolicy(
            IAssemblyBindingPolicy fallback,
            ResolvedAssemblyReference target,
            IReadOnlyList<ResolvedAssemblyReference> roots)
        {
            _state = new BindingState(
                fallback,
                target,
                [.. roots],
                fallback.Version);
        }

        public AssemblyBindingPolicyVersion Version => CurrentState().Version;

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            BindingState state = CurrentState();
            AssemblyBindingRequest? delegatedRequest = DelegateRequest(state, request);
            if (delegatedRequest is null)
            {
                return Complete(
                    state,
                    AssemblyBindingSelection.Invalid(
                        new AssemblyBindingFailure(
                            AssemblyBindingFailureKind.InvalidBindingOrigin)));
            }

            var reference = request.Target as AssemblyBindingTarget.AssemblyReference;
            if (reference is not null)
            {
                if (state.Target.Identity.IsEquivalentTo(reference.Identity))
                {
                    return Complete(
                        state,
                        AssemblyBindingSelection.Found(state.Target));
                }

                ImmutableArray<ResolvedAssemblyReference> matches = state.Roots
                    .Where(
                        root => root.Identity.IsEquivalentTo(
                            reference.Identity))
                    .ToImmutableArray();
                if (matches.Length > 0)
                {
                    return Complete(
                        state,
                        matches.Length == 1
                            ? AssemblyBindingSelection.Found(matches[0])
                            : AssemblyBindingSelection.Multiple(matches));
                }
            }

            AssemblyBindingSelectionSnapshot? snapshot =
                state.Fallback.Select(delegatedRequest);
            if (snapshot is not null
                && !ReferenceEquals(snapshot.Version, state.FallbackVersion))
            {
                Interlocked.CompareExchange(
                    ref _state,
                    state.Refresh(state.Fallback.Version),
                    state);
                return snapshot;
            }

            AssemblyBindingSelection delegated =
                AssemblyBindingSelection.ValidateForRequest(
                    delegatedRequest,
                    snapshot?.Selection);
            if (reference is null
                || delegated is not AssemblyBindingSelection.Missing
                {
                    Disposition:
                        AssemblyBindingMissDisposition.NoNameOwner,
                })
            {
                return Complete(state, delegated);
            }

            bool targetOwnsName = string.Equals(
                state.Target.Identity.Name,
                reference.Identity.Name,
                StringComparison.OrdinalIgnoreCase);
            ImmutableArray<ResolvedAssemblyReference> nameOwners = state.Roots
                .Where(
                    root => string.Equals(
                        root.Identity.Name,
                        reference.Identity.Name,
                        StringComparison.OrdinalIgnoreCase)
                    && (!targetOwnsName
                        || !root.Identity.IsEquivalentTo(
                            state.Target.Identity)))
                .ToImmutableArray();
            if (targetOwnsName)
                nameOwners = nameOwners.Insert(0, state.Target);

            AssemblyBindingSelection selection = nameOwners.Length switch
            {
                0 => delegated,
                1 => AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.IdentityPolicyRequired)),
                _ => AssemblyBindingSelection.Multiple(nameOwners),
            };
            return Complete(state, selection);
        }

        AssemblyBindingSelectionSnapshot Complete(
            BindingState state,
            AssemblyBindingSelection selection)
        {
            if (selection is AssemblyBindingSelection.Selected selected)
            {
                var lineage = new ScopeFirstLineage(this, state, selected.Occurrence);
                selection = AssemblyBindingCandidateDomain.Create(
                    [
                        selected.Assembly,
                        .. selected.ShadowedAssemblies,
                    ]).Finalize(lineage.Issue(selected.Assembly));
            }

            return new AssemblyBindingSelectionSnapshot(state.Version, selection);
        }

        AssemblyBindingRequest? DelegateRequest(
            BindingState state,
            AssemblyBindingRequest request)
        {
            if (request.Origin
                    is not AssemblyBindingOrigin.RequestingAssembly requesting
                || requesting.Lineage is null
                || requesting.Lineage == AssemblyBindingLineage.Seed)
            {
                return request;
            }

            if (requesting.Lineage is not ScopeFirstLineage lineage
                || !ReferenceEquals(lineage.Issuer, this)
                || !ReferenceEquals(lineage.State, state))
            {
                return null;
            }

            return new AssemblyBindingRequest(
                request.Target,
                AssemblyBindingOrigin.FromOccurrence(lineage.DelegatedOccurrence),
                request.Scope);
        }

        BindingState CurrentState()
        {
            while (true)
            {
                BindingState state = Volatile.Read(ref _state);
                AssemblyBindingPolicyVersion version = state.Fallback.Version;
                if (ReferenceEquals(state.FallbackVersion, version))
                    return state;

                Interlocked.CompareExchange(
                    ref _state,
                    state.Refresh(version),
                    state);
            }
        }

        sealed class BindingState(
            IAssemblyBindingPolicy fallback,
            ResolvedAssemblyReference target,
            ImmutableArray<ResolvedAssemblyReference> roots,
            AssemblyBindingPolicyVersion fallbackVersion)
        {
            internal IAssemblyBindingPolicy Fallback { get; } = fallback;
            internal ResolvedAssemblyReference Target { get; } = target;
            internal ImmutableArray<ResolvedAssemblyReference> Roots { get; } = roots;
            internal AssemblyBindingPolicyVersion FallbackVersion { get; } = fallbackVersion;
            internal AssemblyBindingPolicyVersion Version { get; } = new();

            internal BindingState Refresh(AssemblyBindingPolicyVersion version) =>
                new(Fallback, Target, Roots, version);
        }

        sealed record ScopeFirstLineage : AssemblyBindingLineage
        {
            internal ScopeFirstLineage(
                ScopeFirstBindingPolicy issuer,
                BindingState state,
                AssemblyBindingOccurrence delegatedOccurrence)
                : base(state.Version)
            {
                Issuer = issuer;
                State = state;
                DelegatedOccurrence = delegatedOccurrence;
            }

            internal ScopeFirstBindingPolicy Issuer { get; }
            internal BindingState State { get; }
            internal AssemblyBindingOccurrence DelegatedOccurrence { get; }

            internal AssemblyBindingOccurrence Issue(
                ResolvedAssemblyReference assembly) => CreateOccurrence(assembly);
        }
    }
}
