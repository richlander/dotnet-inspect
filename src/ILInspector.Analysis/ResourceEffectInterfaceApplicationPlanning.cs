using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;
using static ILInspector.Analysis.DirectCallDefinitionResolver;

namespace ILInspector.Analysis;

internal sealed partial class ResourceEffectInterfaceApplicationPlan
{
    readonly Dictionary<PendingInterfaceImplementation, PendingInterfaceMembers>
        _interfaceMembers = new(ReferenceEqualityComparer.Instance);

    internal IEnumerable<TypeResolutionRequest> PlanDefinitions(
        TypeResolutionContext context,
        CancellationToken cancellationToken)
    {
        if (_globalGap is not null)
            return [];
        var reader = new DefinitionPlanningSession(new(
            maxDefinitionCandidates: _limits.MaxInterfaceMethods,
            maxSignatureNodes: _limits.MaxSignatureNodes,
            maxMetadataAssociations: _limits.MaxMetadataAssociations));
        var definitions = new Dictionary<
            (AssemblyAcquisitionRegistration, MetadataTypeDefinitionAddress),
            DefinitionCandidateSet>();
        var requests = new List<TypeResolutionRequest>();
        long readSignatureNodes = 0;
        foreach (PendingConcreteType concrete in _orderedConcreteTypes)
        {
            foreach (PendingInterfaceImplementation path in concrete.Interfaces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CouldTargetAnyDeclaration(path.ClosedInterfaceType))
                    continue;
                if (path.ClosedInterfaceType.Kind == TypeRefKind.Unsupported)
                {
                    _interfaceMembers.Add(path, new([], ResolutionDisposition.Unsupported));
                    continue;
                }
                TypeResolutionOutcome? outcome = path.TypePlan
                    .DeclaringTypeResolutionRequest is { } request
                        ? context.Resolve(request) : null;
                if (outcome is not TypeResolutionOutcome.Resolved resolved)
                {
                    _interfaceMembers.Add(path, new([], outcome switch
                    {
                        TypeResolutionOutcome.Ambiguous =>
                            ResolutionDisposition.Ambiguous,
                        TypeResolutionOutcome.Rejected rejected
                            when ClassifyRejectedTypeResolution(rejected.Failure)
                                == DirectCallTypeResolutionKind.Unsupported =>
                            ResolutionDisposition.Unsupported,
                        _ => ResolutionDisposition.Incomplete,
                    }));
                    continue;
                }
                var key = (resolved.Definition.Assembly.Assembly.Registration,
                    resolved.Definition.Address);
                if (!definitions.TryGetValue(key, out var set))
                {
                    set = reader.Read(resolved.Definition.Assembly.Assembly,
                        resolved.Definition.Address);
                    _work.SignatureNodes += reader.SignatureNodes - readSignatureNodes;
                    readSignatureNodes = reader.SignatureNodes;
                    definitions.Add(key, set);
                }
                if (_work.SignatureNodes > _limits.MaxSignatureNodes)
                {
                    _globalGap ??= WorkGap(
                        ResourceEffectInterfaceApplicationWorkDimension.SignatureNodes,
                        _limits.MaxSignatureNodes, _work.SignatureNodes);
                    return requests;
                }
                if (set.Failure == DirectCallDefinitionGapKind.WorkLimitExceeded)
                {
                    var dimension = set.WorkDimension switch
                    {
                        DirectCallDefinitionWorkDimension.SignatureNodes =>
                            ResourceEffectInterfaceApplicationWorkDimension.SignatureNodes,
                        DirectCallDefinitionWorkDimension.MetadataAssociations =>
                            ResourceEffectInterfaceApplicationWorkDimension.MetadataAssociations,
                        _ => ResourceEffectInterfaceApplicationWorkDimension.InterfaceMethods,
                    };
                    long limit = dimension switch
                    {
                        ResourceEffectInterfaceApplicationWorkDimension.SignatureNodes =>
                            _limits.MaxSignatureNodes,
                        ResourceEffectInterfaceApplicationWorkDimension.MetadataAssociations =>
                            _limits.MaxMetadataAssociations,
                        _ => _limits.MaxInterfaceMethods,
                    };
                    _globalGap ??= WorkGap(dimension, limit, set.RequiredWork ?? limit + 1);
                    return requests;
                }
                var slots = ImmutableArray.CreateBuilder<PendingSlot>();
                foreach (PendingDefinitionCandidate candidate in set.Candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!candidate.IsInterfaceDefinition)
                        continue;
                    ImmutableArray<TypeRef> arguments = path.ClosedInterfaceType.TypeArguments;
                    MemberRef slot = InstantiateMember(candidate.Member, arguments) with
                    {
                        DeclaringType = path.ClosedInterfaceType,
                    };
                    if (!_work.Charge(slot, _limits.MaxSignatureNodes))
                    {
                        _globalGap ??= WorkGap(
                            ResourceEffectInterfaceApplicationWorkDimension.SignatureNodes,
                            _limits.MaxSignatureNodes, _work.SignatureNodes);
                        return requests;
                    }
                    var origins = new Dictionary<TypeRef, ResolvedAssemblyReference>(
                        ReferenceEqualityComparer.Instance);
                    AddOrigins(path.ClosedInterfaceType, concrete.Assembly, origins);
                    foreach (var entry in concrete.Origins)
                        origins[entry.Key] = entry.Value;
                    var plan = new ResourceEffectClosedSlotPlan(
                        candidate.Assembly, slot, origins, concrete.GenericScopes);
                    requests.AddRange(candidate.Plan.Requests);
                    requests.AddRange(plan.Requests);
                    slots.Add(new(candidate, slot, plan, origins,
                        arguments.Length == candidate.TypeGenericArity));
                }
                _interfaceMembers.Add(path, new(slots.ToImmutable(),
                    set.Failure switch
                    {
                        DirectCallDefinitionGapKind.UnsupportedSignature =>
                            ResolutionDisposition.Unsupported,
                        null => ResolutionDisposition.None,
                        _ => ResolutionDisposition.Incomplete,
                    }, set.UnreadableNames));
            }
        }
        return requests;
    }

    internal ResourceEffectInterfaceApplicationIndex Resolve(
        TypeResolutionContext context,
        DirectCallDefinitionResolutionOutcome.Completed calls,
        CancellationToken cancellationToken)
    {
        if (context.Catalog != calls.Catalog
            || !ReferenceEquals(context.Generation, calls.Generation))
            throw new ArgumentException("Interface application generation mismatch.", nameof(calls));

        var applications = ImmutableDictionary.CreateBuilder<
            AdmittedResourceEffectDeclaration,
            ImmutableArray<ResourceEffectInterfaceApplication>>(
                ReferenceEqualityComparer.Instance);
        ImmutableArray<AdmittedResourceEffectDeclaration> declarations =
        [
            .. _admission.Models.SelectMany(model => model.Declarations),
        ];
        ImmutableHashSet<AdmittedResourceEffectDeclaration>
            relevantDeclarations =
            declarations
                .Where(declaration =>
                    _globalGap is not null
                        ? declaration.Target
                            is ResourceEffectTargetSelector.Member
                        {
                            Selector.Kind:
                                        not ResourceEffectMemberKind.Field,
                        }
                        : CouldTargetAnyDeclaration(declaration))
                .ToImmutableHashSet<
                    AdmittedResourceEffectDeclaration>(
                        ReferenceEqualityComparer.Instance);
        long candidates = 0;
        long selectors = 0;
        long retained = 0;
        bool exhausted = _globalGap is not null;
        ImmutableArray<DirectCallDefinitionResolution.Resolved> implementations =
        [
            .. calls.Results.OfType<DirectCallDefinitionResolution.Resolved>()
                .Where(call => !call.Definition.IsInterfaceDefinition)
                .OrderBy(ResourceEffectResolver.OccurrenceKey, StringComparer.Ordinal),
        ];
        foreach (AdmittedResourceEffectDeclaration declaration
            in declarations)
        {
            if (declaration.Target is not ResourceEffectTargetSelector.Member target
                || target.Selector.Kind == ResourceEffectMemberKind.Field
                || !relevantDeclarations.Contains(declaration))
                continue;
            var results = ImmutableArray.CreateBuilder<ResourceEffectInterfaceApplication>();
            if (exhausted)
            {
                applications.Add(declaration, []);
                continue;
            }
            foreach (var implementation in implementations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (exhausted)
                    break;
                if (++candidates > _limits.MaxCandidateApplications)
                {
                    Exhaust(ResourceEffectInterfaceApplicationWorkDimension.CandidateApplications,
                        _limits.MaxCandidateApplications, candidates);
                    break;
                }
                if (!_concreteTypes.TryGetValue(ConcreteTypeKey.For(implementation), out var concrete))
                    continue; // A global planning limit already records the missing suffix.
                if (concrete.GlobalGap is { } gap)
                {
                    Retain(Failure(null, implementation,
                        gap.Kind == ResourceEffectInterfaceApplicationGapKind.UnsupportedMetadata
                            ? ResolutionDisposition.Unsupported : ResolutionDisposition.Incomplete,
                        gap.Detail ?? "Concrete type metadata was unavailable."));
                    continue;
                }
                int previousCount = results.Count;
                foreach (PendingInterfaceImplementation path in concrete.Interfaces)
                {
                    if (exhausted)
                        break;
                    if (!ChargeComparison(Marker(path.ClosedInterfaceType)))
                    {
                        exhausted = true;
                        break;
                    }
                    if (path.ClosedInterfaceType.Kind != TypeRefKind.Unsupported
                        && !ResourceEffectSelectorBinder.TypeNameCouldMatch(
                        target.Selector.DeclaringType,
                        path.ClosedInterfaceType.Kind == TypeRefKind.GenericInstance
                            ? path.ClosedInterfaceType.ElementType! : path.ClosedInterfaceType))
                        continue;
                    if (!_interfaceMembers.TryGetValue(path, out var members))
                        continue; // A global planning limit covers this path.
                    if (members.Failure != ResolutionDisposition.None)
                    {
                        Retain(Failure(null, implementation, members.Failure,
                            "The InterfaceImpl definition could not be read exactly."));
                        continue;
                    }
                    if (members.UnreadableNames?.Contains(target.Selector.MetadataName) == true)
                    {
                        Retain(Failure(null, implementation, ResolutionDisposition.Unsupported,
                            "A potentially selected interface signature was unreadable."));
                        continue;
                    }
                    foreach (PendingSlot slot in members.Slots)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (exhausted)
                            break;
                        if (++selectors > _limits.MaxSelectorBindings)
                        {
                            Exhaust(ResourceEffectInterfaceApplicationWorkDimension.SelectorBindings,
                                _limits.MaxSelectorBindings, selectors);
                            break;
                        }
                        if (!ResourceEffectSelectorBinder.CouldMatch(target.Selector, slot.Member))
                            continue;
                        if (slot.Candidate.Semantics == CandidateSemantics.Unsupported || !slot.ExactArity)
                        {
                            Retain(Failure(null, implementation, ResolutionDisposition.Unsupported,
                                "The interface definition semantics or generic rows were unsupported."));
                            continue;
                        }
                        if (slot.Member.GenericArity != implementation.Call.Callee.GenericArity)
                            continue;
                        if (slot.Plan.Project(context) is not ClosedSlotProjection.Issued
                            { Key.Kind: CatalogMemberCorrespondenceKind.Exact } projected)
                        {
                            Retain(Failure(null, implementation, ResolutionDisposition.Incomplete,
                                "The closed interface slot projection was not exact."));
                            continue;
                        }
                        if (slot.Candidate.Plan.Project(context) is not CatalogMemberJoinProjection.Issued
                            { Key.Kind: CatalogMemberCorrespondenceKind.Exact } definitionProjection
                            || path.TypePlan.DeclaringTypeResolution(context) is not { } declaring)
                        {
                            Retain(Failure(null, implementation, ResolutionDisposition.Incomplete,
                                "The interface definition projection was not exact."));
                            continue;
                        }

                        ImmutableArray<TypeRef> methodArguments = implementation.Call.Callee.TypeArguments;
                        MemberRef member = slot.Member with
                        {
                            TypeArguments = methodArguments,
                            // Simultaneous substitution does not reinterpret caller-owned
                            // method variables inserted by the declaring-type arguments.
                            ParameterTypes = [.. slot.Candidate.Member.OpenSignatureParameters
                                .Select(type => type.Instantiate(
                                    path.ClosedInterfaceType.TypeArguments, methodArguments))],
                            ReturnType = slot.Candidate.Member.OpenSignatureReturn.Instantiate(
                                path.ClosedInterfaceType.TypeArguments, methodArguments),
                        };
                        if (!_work.Charge(member, _limits.MaxSignatureNodes))
                        {
                            Exhaust(ResourceEffectInterfaceApplicationWorkDimension.SignatureNodes,
                                _limits.MaxSignatureNodes, _work.SignatureNodes);
                            break;
                        }
                        var origins = new Dictionary<TypeRef, ResolvedAssemblyReference>(
                            slot.Origins, ReferenceEqualityComparer.Instance);
                        foreach (TypeRef argument in methodArguments)
                            AddOrigins(argument, implementation.Participant.Assembly, origins);
                        var definition = new DirectCallDefinitionOccurrence(
                            context.Catalog, context.Generation, slot.Candidate.Assembly,
                            slot.Candidate.ModuleVersionId,
                            MetadataTokens.GetToken(slot.Candidate.Definition),
                            slot.Candidate.Member, definitionProjection.Key, declaring.Hops,
                            slot.Candidate.Semantics == CandidateSemantics.PropertyGetter
                                ? DirectCallDefinitionSemantics.PropertyGetter
                                : DirectCallDefinitionSemantics.Method, true);
                        // An internal selector view, never an added public call occurrence.
                        var view = new DirectCallDefinitionResolution.Resolved(
                            context.Catalog, context.Generation, implementation.Participant,
                            implementation.Call with { Callee = member }, definition,
                            implementation.GenericScopes,
                            CreateTypeResolutionSnapshot(context, slot.Candidate.Assembly, member, origins));
                        ResourceEffectSelectorBinding binding = ResourceEffectSelectorBinder.Bind(declaration, view);
                        if (binding is ResourceEffectSelectorBinding.Unmatched)
                            continue;
                        if (binding is not ResourceEffectSelectorBinding.Resolved)
                        {
                            Retain(Failure(view, implementation, binding switch
                            {
                                ResourceEffectSelectorBinding.Ambiguous => ResolutionDisposition.Ambiguous,
                                ResourceEffectSelectorBinding.Unsupported => ResolutionDisposition.Unsupported,
                                _ => ResolutionDisposition.Incomplete,
                            }, "The interface selector could not be bound exactly."));
                            continue;
                        }
                        Retain(ResolvePair(context, view, implementation, concrete, projected.Key, slot.Member));
                        exhausted |= _globalGap is not null;
                    }
                }
                if (!exhausted && results.Count == previousCount)
                    Retain(new ResourceEffectInterfaceApplication.NotApplicable(null, implementation));
            }
            applications.Add(declaration, results.ToImmutable());

            void Retain(ResourceEffectInterfaceApplication application)
            {
                if (++retained > _limits.MaxRetainedApplications)
                {
                    Exhaust(ResourceEffectInterfaceApplicationWorkDimension.RetainedApplications,
                        _limits.MaxRetainedApplications, retained);
                    return;
                }
                results.Add(application);
            }
        }
        return new(context.Catalog, context.Generation, _admission.Receipt,
            ResourceEffectResolver.CreatePopulationReceipt(calls),
            applications.ToImmutable(), _globalGap, _coverageGap,
            relevantDeclarations);

        void Exhaust(ResourceEffectInterfaceApplicationWorkDimension dimension, long limit, long required)
        {
            _globalGap ??= WorkGap(dimension, limit, required);
            exhausted = true;
        }
    }

    bool ChargeComparison(MemberRef member)
    {
        if (++_work.SlotComparisons > _limits.MaxSlotComparisons)
        {
            _globalGap ??= WorkGap(
                ResourceEffectInterfaceApplicationWorkDimension.SlotComparisons,
                _limits.MaxSlotComparisons, _work.SlotComparisons);
            return false;
        }
        if (!_work.Charge(member, _limits.MaxSignatureNodes))
        {
            _globalGap ??= WorkGap(
                ResourceEffectInterfaceApplicationWorkDimension.SignatureNodes,
                _limits.MaxSignatureNodes, _work.SignatureNodes);
            return false;
        }
        return true;
    }

    static void AddOrigins(TypeRef type, ResolvedAssemblyReference source,
        Dictionary<TypeRef, ResolvedAssemblyReference> origins)
    {
        if (!origins.TryAdd(type, source))
            return;
        if (type.ElementType is { } element)
            AddOrigins(element, source, origins);
        foreach (TypeRef argument in type.TypeArguments)
            AddOrigins(argument, source, origins);
        if (type.ModifierType is { } modifier)
            AddOrigins(modifier, source, origins);
        if (type.UnmodifiedType is { } unmodified)
            AddOrigins(unmodified, source, origins);
    }

    bool CouldTargetAnyDeclaration(TypeRef interfaceType) =>
        _admission.Models
            .SelectMany(model => model.Declarations)
            .Any(declaration =>
                declaration.Target
                    is ResourceEffectTargetSelector.Member target
                && target.Selector.Kind
                    != ResourceEffectMemberKind.Field
                && CouldTargetInterface(
                    target.Selector.DeclaringType,
                    interfaceType));

    bool CouldTargetAnyDeclaration(
        AdmittedResourceEffectDeclaration declaration) =>
        declaration.Target is ResourceEffectTargetSelector.Member target
        && target.Selector.Kind != ResourceEffectMemberKind.Field
        && _orderedConcreteTypes.Any(concrete =>
            concrete.GlobalGap is not null
            || concrete.Interfaces.Any(path =>
                CouldTargetInterface(
                    target.Selector.DeclaringType,
                    path.ClosedInterfaceType)));

    static bool CouldTargetInterface(
        ResourceTypeExpression.Named selector,
        TypeRef interfaceType)
    {
        if (interfaceType.Kind == TypeRefKind.Unsupported)
            return true;
        TypeRef definition =
            interfaceType.Kind == TypeRefKind.GenericInstance
                ? interfaceType.ElementType!
                : interfaceType;
        return ResourceEffectSelectorBinder.TypeNameCouldMatch(
            selector,
            definition);
    }

    sealed record PendingSlot(PendingDefinitionCandidate Candidate, MemberRef Member,
        ResourceEffectClosedSlotPlan Plan,
        Dictionary<TypeRef, ResolvedAssemblyReference> Origins, bool ExactArity);

    sealed record PendingInterfaceMembers(ImmutableArray<PendingSlot> Slots,
        ResolutionDisposition Failure, ImmutableHashSet<string>? UnreadableNames = null);
}
