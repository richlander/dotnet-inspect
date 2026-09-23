using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

public static class ResourceEffectResolver
{
    public static ResourceEffectResolutionOutcome Resolve(
        IAssemblyBindingPolicy bindingPolicy,
        ResourceEffectAdmissionOutcome admission,
        IEnumerable<CatalogCallGraphParticipant> participants,
        DirectCallDefinitionResolutionLimits? directCallLimits = null,
        ResourceEffectInterfaceApplicationLimits? interfaceLimits = null,
        ResourceEffectResolutionLimits? limits = null,
        TypeResolutionContextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(participants);
        if (admission is not ResourceEffectAdmissionOutcome.Admitted admitted)
        {
            return new ResourceEffectResolutionOutcome.Rejected(
                ResourceEffectResolutionRejectionKind.AdmissionRejected);
        }
        return Resolve(
            bindingPolicy,
            admitted.Admission,
            participants,
            directCallLimits,
            interfaceLimits,
            limits,
            options,
            cancellationToken);
    }

    public static ResourceEffectResolutionOutcome Resolve(
        IAssemblyBindingPolicy bindingPolicy,
        ResourceEffectAdmission admission,
        IEnumerable<CatalogCallGraphParticipant> participants,
        DirectCallDefinitionResolutionLimits? directCallLimits = null,
        ResourceEffectInterfaceApplicationLimits? interfaceLimits = null,
        ResourceEffectResolutionLimits? limits = null,
        TypeResolutionContextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(participants);
        ImmutableArray<CatalogCallGraphParticipant> population =
            participants.ToImmutableArray();
        ResourceEffectInterfaceApplicationLimits effectiveInterfaceLimits =
            interfaceLimits
                ?? new ResourceEffectInterfaceApplicationLimits();
        var extension =
            new ResourceEffectInterfaceApplicationExtension(
                admission,
                effectiveInterfaceLimits);
        var candidateSelector =
            new ResourceEffectDirectCallCandidateSelector(
                admission,
                bindingPolicy,
                effectiveInterfaceLimits.MaxMethodImplementations);
        DirectCallDefinitionResolutionOutcome directCalls =
            DirectCallDefinitionResolver.ResolveWithGenerationExtension(
                bindingPolicy,
                population,
                extension,
                candidateSelector,
                directCallLimits,
                options,
                cancellationToken);
        if (directCalls
            is not DirectCallDefinitionResolutionOutcome.Completed completed)
        {
            return new ResourceEffectResolutionOutcome.Rejected(
                ResourceEffectResolutionRejectionKind
                    .OccurrencePopulationRejected);
        }
        ResourceEffectInterfaceApplicationIndex applications =
            extension.Index
            ?? ResourceEffectInterfaceApplicationIndex.Incomplete(
                admission,
                completed,
                new(
                    ResourceEffectInterfaceApplicationGapKind
                        .IncompleteMetadata)
                {
                    Detail =
                        "Interface-application planning did not complete "
                        + "for the direct-call population.",
                });
        return Resolve(
            CreateRequest(
                admission,
                completed,
                applications),
            limits,
            cancellationToken);
    }

    public static ResourceEffectResolutionOutcome Resolve(
        ResourceEffectAdmissionOutcome admission,
        DirectCallDefinitionResolutionOutcome directCalls,
        ResourceEffectResolutionLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(directCalls);
        cancellationToken.ThrowIfCancellationRequested();

        if (admission is not ResourceEffectAdmissionOutcome.Admitted admitted)
        {
            return new ResourceEffectResolutionOutcome.Rejected(
                ResourceEffectResolutionRejectionKind.AdmissionRejected);
        }
        if (directCalls
            is not DirectCallDefinitionResolutionOutcome.Completed completed)
        {
            return new ResourceEffectResolutionOutcome.Rejected(
                ResourceEffectResolutionRejectionKind
                    .OccurrencePopulationRejected);
        }
        return Resolve(
            admitted.Admission,
            completed,
            limits,
            cancellationToken);
    }

    public static ResourceEffectResolutionOutcome Resolve(
        ResourceEffectAdmission admission,
        DirectCallDefinitionResolutionOutcome.Completed directCalls,
        ResourceEffectResolutionLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Resolve(
                CreateRequest(admission, directCalls),
                limits,
                cancellationToken);
    }

    public static ResourceEffectResolutionRequest CreateRequest(
        ResourceEffectAdmission admission,
        DirectCallDefinitionResolutionOutcome.Completed directCalls,
        ResourceEffectInterfaceApplicationIndex? interfaceApplications =
            null)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(directCalls);
        return new ResourceEffectResolutionRequest(
            admission,
            admission.Receipt,
            directCalls,
            CreatePopulationReceipt(directCalls),
            interfaceApplications);
    }

    public static ResourceEffectResolutionOutcome Resolve(
        ResourceEffectResolutionRequest request,
        ResourceEffectResolutionLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.Admission.Receipt.Equals(request.AdmissionReceipt))
        {
            return new ResourceEffectResolutionOutcome.Rejected(
                ResourceEffectResolutionRejectionKind
                    .AdmissionReceiptMismatch);
        }
        ResourceEffectOccurrencePopulationReceipt expectedPopulation =
            CreatePopulationReceipt(request.DirectCalls);
        if (!expectedPopulation.Equals(request.PopulationReceipt))
        {
            return new ResourceEffectResolutionOutcome.Rejected(
                ResourceEffectResolutionRejectionKind
                    .OccurrencePopulationReceiptMismatch);
        }
        if (request.InterfaceApplications is { } applications
            && (applications.Catalog != request.DirectCalls.Catalog
                || !ReferenceEquals(
                    applications.Generation,
                    request.DirectCalls.Generation)))
        {
            return new ResourceEffectResolutionOutcome.Rejected(
                ResourceEffectResolutionRejectionKind
                    .InterfaceApplicationGenerationMismatch);
        }
        if (request.InterfaceApplications is { } index)
        {
            if (!index.AdmissionReceipt.Equals(request.AdmissionReceipt))
                return new ResourceEffectResolutionOutcome.Rejected(
                    ResourceEffectResolutionRejectionKind.InterfaceApplicationAdmissionMismatch);
            if (!index.PopulationReceipt.Equals(request.PopulationReceipt))
                return new ResourceEffectResolutionOutcome.Rejected(
                    ResourceEffectResolutionRejectionKind.InterfaceApplicationPopulationMismatch);
        }
        limits ??= new ResourceEffectResolutionLimits();

        ResourceEffectAdmission admission = request.Admission;
        DirectCallDefinitionResolutionOutcome.Completed directCalls =
            request.DirectCalls;
        ResourceEffectOccurrencePopulationReceipt population =
            request.PopulationReceipt;
        var evaluations =
            ImmutableArray.CreateBuilder<ResourceEffectTargetEvaluation>();
        var allEffects = new List<ResolvedResourceEffect>();
        var gaps = new GapCollector(limits.MaxRetainedGaps);
        long selectorEvaluations = 0;
        long boundEffects = 0;
        long provenanceAssociations = 0;
        bool populationIncomplete = false;
        bool stopPopulationGaps = false;
        foreach (CatalogCallGraphParticipant participant
            in directCalls.Population)
        {
            foreach (AnalysisDiagnostic diagnostic
                in participant.CallGraph.Diagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();
                populationIncomplete = true;
                if (!gaps.TryAdd(
                        new ResourceEffectResolutionGap(
                            ResourceEffectResolutionGapKind
                                .PopulationIncomplete)
                        {
                            Participant = participant,
                            AnalysisDiagnostic = diagnostic,
                        }))
                {
                    stopPopulationGaps = true;
                    break;
                }
            }
            if (stopPopulationGaps)
                break;
        }

        foreach (AdmittedResourceEffectModel model in admission.Models)
        {
            foreach (AdmittedResourceEffectDeclaration declaration
                in model.Declarations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matches = new List<ResolvedResourceEffect>();
                var evaluationGaps =
                    ImmutableArray.CreateBuilder<
                        ResourceEffectResolutionGap>();
                bool ambiguous = false;
                bool unsupported = false;
                bool incomplete = populationIncomplete;

                void Retain(ResourceEffectResolutionGap gap)
                {
                    incomplete = true;
                    if (gaps.TryAdd(gap))
                        evaluationGaps.Add(gap);
                }

                bool BindResolved(
                    ResourceEffectSelectorBinding.Resolved value,
                    ResourceEffectInterfaceApplicationEvidence?
                        interfaceApplication)
                {
                    ResourceEffectOccurrenceBindingResult occurrence =
                        ResourceEffectOccurrenceBinder.Bind(
                            declaration.Effect,
                            value);
                    ResolvedResourceEffectBinding occurrenceBinding;
                    switch (occurrence)
                    {
                        case ResourceEffectOccurrenceBindingResult
                            .Ambiguous occurrenceAmbiguous:
                            ambiguous = true;
                            Retain(
                                OccurrenceGap(
                                    ResourceEffectResolutionGapKind
                                        .OccurrenceAmbiguous,
                                    value.DirectCall,
                                    occurrenceAmbiguous.Gap));
                            return true;
                        case ResourceEffectOccurrenceBindingResult
                            .Unsupported occurrenceUnsupported:
                            unsupported = true;
                            Retain(
                                OccurrenceGap(
                                    ResourceEffectResolutionGapKind
                                        .OccurrenceUnsupported,
                                    value.DirectCall,
                                    occurrenceUnsupported.Gap));
                            return true;
                        case ResourceEffectOccurrenceBindingResult
                            .Incomplete occurrenceIncomplete:
                            Retain(
                                OccurrenceGap(
                                    ResourceEffectResolutionGapKind
                                        .OccurrenceIncomplete,
                                    value.DirectCall,
                                    occurrenceIncomplete.Gap));
                            return true;
                        case ResourceEffectOccurrenceBindingResult
                            .Resolved occurrenceResolved:
                            occurrenceBinding =
                                occurrenceResolved.Binding;
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Unknown occurrence-binding result.");
                    }

                    boundEffects++;
                    if (boundEffects > limits.MaxBoundEffects)
                    {
                        Retain(
                            WorkGap(
                                ResourceEffectResolutionWorkDimension
                                    .BoundEffects,
                                limits.MaxBoundEffects,
                                boundEffects,
                                value.DirectCall.PhysicalInvocation));
                        return false;
                    }

                    long requiredAssociations =
                        provenanceAssociations
                        + declaration.Provenances.Length;
                    if (requiredAssociations
                        > limits.MaxProvenanceAssociations)
                    {
                        Retain(
                            WorkGap(
                                ResourceEffectResolutionWorkDimension
                                    .ProvenanceAssociations,
                                limits.MaxProvenanceAssociations,
                                requiredAssociations,
                                value.DirectCall.PhysicalInvocation));
                        return false;
                    }
                    provenanceAssociations = requiredAssociations;

                    var source = new ResolvedResourceEffectSource(
                        model.Identity,
                        model.Receipt,
                        declaration,
                        declaration.Provenances,
                        interfaceApplication is null ? [] : [interfaceApplication]);
                    string canonicalEffect = CanonicalBoundEffect(
                        declaration.Effect,
                        value.GenericBindings,
                        value.ResourceKinds,
                        occurrenceBinding);
                    var effect = new ResolvedResourceEffect(
                        admission.Receipt,
                        value.DirectCall,
                        declaration.Effect,
                        value.GenericBindings,
                        value.ResourceKinds,
                        occurrenceBinding,
                        [source],
                        canonicalEffect);
                    matches.Add(effect);
                    allEffects.Add(effect);
                    return true;
                }

                if (declaration.Target
                    is not ResourceEffectTargetSelector.Member
                        { Selector.Kind: not ResourceEffectMemberKind.Field })
                {
                    unsupported = true;
                    Retain(
                        new ResourceEffectResolutionGap(
                            ResourceEffectResolutionGapKind
                                .SelectorUnsupported));
                }
                else
                {
                    foreach (DirectCallDefinitionResolution directCall
                        in directCalls.Results)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        selectorEvaluations++;
                        if (selectorEvaluations
                            > limits.MaxSelectorEvaluations)
                        {
                            Retain(
                                WorkGap(
                                    ResourceEffectResolutionWorkDimension
                                        .SelectorEvaluations,
                                    limits.MaxSelectorEvaluations,
                                    selectorEvaluations,
                                    directCall.PhysicalInvocation));
                            break;
                        }

                        ResourceEffectSelectorBinding binding =
                            ResourceEffectSelectorBinder.Bind(
                                declaration,
                                directCall);
                        switch (binding)
                        {
                            case ResourceEffectSelectorBinding.Unmatched:
                                continue;
                            case ResourceEffectSelectorBinding.Ambiguous value:
                                ambiguous = true;
                                Retain(
                                    SelectorGap(
                                        ResourceEffectResolutionGapKind
                                            .SelectorAmbiguous,
                                        value.DirectCall,
                                        value.Gap));
                                continue;
                            case ResourceEffectSelectorBinding.Unsupported value:
                                unsupported = true;
                                Retain(
                                    SelectorGap(
                                        ResourceEffectResolutionGapKind
                                            .SelectorUnsupported,
                                        value.DirectCall,
                                        value.Gap));
                                continue;
                            case ResourceEffectSelectorBinding.Incomplete value:
                                Retain(
                                    SelectorGap(
                                        ResourceEffectResolutionGapKind
                                            .SelectorIncomplete,
                                        value.DirectCall,
                                        value.Gap));
                                continue;
                            case ResourceEffectSelectorBinding.Resolved value:
                                if (value.DirectCall.Definition
                                    .IsInterfaceDefinition)
                                {
                                    if (request.InterfaceApplications is null)
                                    {
                                        Retain(
                                            new ResourceEffectResolutionGap(
                                                ResourceEffectResolutionGapKind
                                                    .DeferredInterfaceApplication)
                                            {
                                                PhysicalInvocation =
                                                    value.DirectCall
                                                        .PhysicalInvocation,
                                            });
                                    }
                                }
                                if (!BindResolved(
                                        value,
                                        interfaceApplication: null))
                                {
                                    break;
                                }
                                break;
                        }
                    }
                    if (request.InterfaceApplications is { } interfaceApplications)
                                    {
                        if (interfaceApplications.CoverageGap is
                            { } coverageGap)
                                        {
                                            Retain(
                                new(
                                                    ResourceEffectResolutionGapKind
                                        .InterfaceApplicationIncomplete)
                                {
                                    InterfaceApplicationGap =
                                        coverageGap,
                                });
                                        }
                        if (interfaceApplications.GlobalGapFor(
                                declaration)
                            is { } globalGap)
                                        {
                            Retain(
                                new(
                                    ResourceEffectResolutionGapKind
                                        .InterfaceApplicationIncomplete)
                                {
                                    InterfaceApplicationGap =
                                        globalGap,
                                });
                        }
                        foreach (ResourceEffectInterfaceApplication application in
                            interfaceApplications.For(declaration))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                                            switch (application)
                                            {
                                case ResourceEffectInterfaceApplication.Applied applied:
                                    if (++selectorEvaluations > limits.MaxSelectorEvaluations)
                                                {
                                        Retain(WorkGap(ResourceEffectResolutionWorkDimension.SelectorEvaluations,
                                            limits.MaxSelectorEvaluations, selectorEvaluations,
                                            applied.ImplementationCall.PhysicalInvocation));
                                        break;
                                                }
                                    var binding = ResourceEffectSelectorBinder.Bind(
                                        declaration, applied.SelectorOccurrence!);
                                    if (binding is not ResourceEffectSelectorBinding.Resolved selected)
                                        throw new InvalidOperationException("An applied interface selector lost its binding.");
                                    var concreteBinding = new ResourceEffectSelectorBinding.Resolved(
                                        declaration,
                                        applied.OccurrenceBindingCall,
                                        selected.GenericBindings, selected.ResourceKinds,
                                        selected.DirectCall.Definition);
                                    if (!BindResolved(concreteBinding, applied.Evidence))
                                        break;
                                    continue;
                                case ResourceEffectInterfaceApplication.NotApplicable:
                                                    continue;
                                case ResourceEffectInterfaceApplication.Ambiguous value:
                                                    ambiguous = true;
                                    Retain(InterfaceGap(ResourceEffectResolutionGapKind.InterfaceApplicationAmbiguous,
                                        value.ImplementationCall, value.Gap));
                                                    continue;
                                case ResourceEffectInterfaceApplication.Unsupported value:
                                                    unsupported = true;
                                    Retain(InterfaceGap(ResourceEffectResolutionGapKind.InterfaceApplicationUnsupported,
                                        value.ImplementationCall, value.Gap));
                                                    continue;
                                case ResourceEffectInterfaceApplication.Incomplete value:
                                    Retain(InterfaceGap(ResourceEffectResolutionGapKind.InterfaceApplicationIncomplete,
                                        value.ImplementationCall, value.Gap));
                                                    continue;
                                            }
                                break;
                        }
                    }
                }
                ResourceEffectTargetEvaluationKind kind =
                    matches.Count > 0
                        && (ambiguous || unsupported || incomplete)
                        ? ResourceEffectTargetEvaluationKind.Incomplete
                    : ambiguous
                        ? ResourceEffectTargetEvaluationKind.Ambiguous
                        : unsupported
                            ? ResourceEffectTargetEvaluationKind.Unsupported
                            : incomplete
                                ? ResourceEffectTargetEvaluationKind.Incomplete
                                : matches.Count > 0
                                    ? ResourceEffectTargetEvaluationKind.Resolved
                                    : ResourceEffectTargetEvaluationKind.Unmatched;
                evaluations.Add(
                    new ResourceEffectTargetEvaluation(
                        model.Identity,
                        model.Receipt,
                        declaration,
                        kind,
                        OrderEffects(matches),
                        evaluationGaps.ToImmutable()));
            }
        }

        ImmutableArray<ResolvedResourceEffect> effects =
            OrderEffects(Coalesce(allEffects));
        ImmutableArray<ResourceEffectConflict> conflicts =
            FindConflicts(
                effects,
                limits,
                gaps,
                cancellationToken,
                out bool compatibilityIncomplete);
        if (gaps.Exhausted)
        {
            gaps.AddLimitGap(
                WorkGap(
                    ResourceEffectResolutionWorkDimension.RetainedGaps,
                    limits.MaxRetainedGaps,
                    (long)limits.MaxRetainedGaps + 1));
        }

        ImmutableArray<ResourceEffectTargetEvaluation> resultEvaluations =
            evaluations.ToImmutable();
        ImmutableArray<ResourceEffectResolutionGap> resultGaps =
            gaps.ToImmutable();
        cancellationToken.ThrowIfCancellationRequested();
        if (!conflicts.IsEmpty)
        {
            ResourceEffectResolutionReceipt receipt = CreateReceipt(
                admission.Receipt,
                population,
                "conflict",
                resultEvaluations,
                effects,
                conflicts,
                resultGaps);
            return new ResourceEffectResolutionOutcome.Conflict(
                conflicts,
                receipt,
                resultEvaluations,
                resultGaps);
        }
        if (populationIncomplete
            || compatibilityIncomplete
            || gaps.Exhausted
            || resultEvaluations.Any(evaluation =>
                evaluation.Kind
                    is ResourceEffectTargetEvaluationKind.Ambiguous
                        or ResourceEffectTargetEvaluationKind.Unsupported
                        or ResourceEffectTargetEvaluationKind.Incomplete))
        {
            ResourceEffectResolutionReceipt receipt = CreateReceipt(
                admission.Receipt,
                population,
                "incomplete",
                resultEvaluations,
                effects,
                [],
                resultGaps);
            return new ResourceEffectResolutionOutcome.Incomplete(
                effects,
                receipt,
                resultEvaluations,
                resultGaps);
        }

        ResourceEffectResolutionReceipt completeReceipt = CreateReceipt(
            admission.Receipt,
            population,
            "complete",
            resultEvaluations,
            effects,
            [],
            []);
        return new ResourceEffectResolutionOutcome.Complete(
            new ResourceEffectResolutionSnapshot(effects),
            completeReceipt,
            resultEvaluations);
    }

    static ResourceEffectResolutionGap SelectorGap(
        ResourceEffectResolutionGapKind kind,
        DirectCallDefinitionResolution directCall,
        ResourceEffectSelectorBindingGap gap) =>
        new(kind)
        {
            PhysicalInvocation = directCall.PhysicalInvocation,
            SelectorGap = gap,
        };

    static ResourceEffectResolutionGap OccurrenceGap(
        ResourceEffectResolutionGapKind kind,
        DirectCallDefinitionResolution.Resolved directCall,
        ResourceEffectOccurrenceBindingGap gap) =>
        new(kind)
        {
            PhysicalInvocation = directCall.PhysicalInvocation,
            OccurrenceGap = gap,
        };

    static ResourceEffectResolutionGap InterfaceGap(
        ResourceEffectResolutionGapKind kind,
        DirectCallDefinitionResolution directCall,
        ResourceEffectInterfaceApplicationGap gap) =>
        new(kind)
        {
            PhysicalInvocation = directCall.PhysicalInvocation,
            InterfaceApplicationGap = gap,
        };

    static ResourceEffectResolutionGap WorkGap(
        ResourceEffectResolutionWorkDimension dimension,
        long limit,
        long requiredWork,
        GraphNodeStorageKey? physicalInvocation = null) =>
        new(ResourceEffectResolutionGapKind.WorkLimitExceeded)
        {
            PhysicalInvocation = physicalInvocation,
            WorkDimension = dimension,
            Limit = limit,
            RequiredWork = requiredWork,
        };

    static ImmutableArray<ResolvedResourceEffect> Coalesce(
        IEnumerable<ResolvedResourceEffect> effects)
        {
            var grouped = new Dictionary<BoundEffectKey, CoalescedEffect>();
            foreach (ResolvedResourceEffect effect in effects)
            {
                var key = new BoundEffectKey(
                    effect.PhysicalInvocation,
            effect.CanonicalEffect);
                if (grouped.TryGetValue(key, out CoalescedEffect? existing))
                {
                    existing.Sources.AddRange(effect.Sources);
                    continue;
                }
                grouped.Add(
                    key,
                    new CoalescedEffect(effect, [.. effect.Sources]));
            }

            return
            [
                .. grouped.Values.Select(value =>
                {
                    ImmutableArray<ResolvedResourceEffectSource> sources =
                    [
                        .. value.Sources
                            .Select(CanonicalizeSource)
                            .GroupBy(CanonicalDeclarationSource, StringComparer.Ordinal)
                            .Select(group => new ResolvedResourceEffectSource(
                                group.First().Model, group.First().ModelReceipt,
                                group.First().Declaration, group.First().Provenances,
                                [.. group.SelectMany(source => source.InterfaceApplications)
                                    .DistinctBy(CanonicalInterfaceApplication)
                                    .OrderBy(CanonicalInterfaceApplication, StringComparer.Ordinal)]))
                            .OrderBy(CanonicalSource, StringComparer.Ordinal),
                    ];
                    ResolvedResourceEffect first = value.First;
                    return new ResolvedResourceEffect(
                        first.AdmissionReceipt,
                        first.DirectCall,
                        first.Effect,
                        first.GenericBindings,
                        first.ResourceKinds,
                        first.Binding,
                        sources,
                        first.CanonicalEffect);
                }),
            ];
        }

        static ImmutableArray<ResourceEffectConflict> FindConflicts(
            ImmutableArray<ResolvedResourceEffect> effects,
            ResourceEffectResolutionLimits limits,
            GapCollector gaps,
            CancellationToken cancellationToken,
            out bool incomplete)
        {
            var conflicts =
                ImmutableArray.CreateBuilder<ResourceEffectConflict>();
            long comparisons = 0;
            incomplete = false;
            foreach (IGrouping<GraphNodeStorageKey, ResolvedResourceEffect> group
                in effects.GroupBy(effect => effect.PhysicalInvocation))
            {
                ResolvedResourceEffect[] values = [.. group];
                var conflicting = new bool[values.Length];
                for (int left = 0; left < values.Length; left++)
                {
                    for (int right = left + 1;
                        right < values.Length;
                        right++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        comparisons++;
                        if (comparisons
                            > limits.MaxCompatibilityComparisons)
                        {
                            incomplete = true;
                            gaps.TryAdd(
                                WorkGap(
                                    ResourceEffectResolutionWorkDimension
                                        .CompatibilityComparisons,
                                    limits.MaxCompatibilityComparisons,
                                    comparisons,
                                    group.Key));
                            if (conflicting.Any(static value => value))
                            {
                                conflicts.Add(
                                    new ResourceEffectConflict(
                                        group.Key,
                                        [
                                            .. values.Where(
                                                (_, index) =>
                                                    conflicting[index]),
                                        ]));
                            }
                            return conflicts.ToImmutable();
                        }
                        if (Conflicts(values[left], values[right]))
                        {
                            conflicting[left] = true;
                            conflicting[right] = true;
                        }
                    }
                }
                if (conflicting.Any(static value => value))
                {
                    conflicts.Add(
                        new ResourceEffectConflict(
                            group.Key,
                            [
                                .. values.Where(
                                    (_, index) => conflicting[index]),
                            ]));
                }
            }
            return conflicts.ToImmutable();
        }

        static bool Conflicts(
            ResolvedResourceEffect left,
            ResolvedResourceEffect right)
        {
            if (left.CanonicalEffect == right.CanonicalEffect)
                return false;
            if (left.Effect is ResourceEffect.Operation leftOperation
                && right.Effect is ResourceEffect.Operation rightOperation)
            {
                return GuardsOverlap(
                        left,
                        leftOperation.Guard,
                        right,
                        rightOperation.Guard)
                    && (leftOperation.Boundary != rightOperation.Boundary
                        || leftOperation.Throws != rightOperation.Throws);
            }
            if (left.Effect is ResourceEffect.Callback leftCallback
                && right.Effect is ResourceEffect.Callback rightCallback)
            {
                return left.Binding.Callback!.Contract
                        .DelegateParameterIndex
                    == right.Binding.Callback!.Contract
                        .DelegateParameterIndex
                    && (leftCallback.Execution != rightCallback.Execution
                        || leftCallback.Cardinality
                            != rightCallback.Cardinality);
            }
            if (left.Effect is ResourceEffect.Independent leftIndependent)
            {
                return ConflictsWithIndependence(
                    left,
                    leftIndependent,
                    right);
            }
            if (right.Effect
                is ResourceEffect.Independent rightIndependent)
            {
                return ConflictsWithIndependence(
                    right,
                    rightIndependent,
                    left);
            }
            if (!TryOwnershipClaim(left, out OwnershipClaim? leftClaim)
                || !TryOwnershipClaim(right, out OwnershipClaim? rightClaim)
                || leftClaim!.Source != rightClaim!.Source
                || !KindDomainsOverlap(leftClaim.Kind, rightClaim.Kind))
            {
                return false;
            }
            if (leftClaim.Entry && rightClaim.Entry)
            {
                return leftClaim.Borrow != rightClaim.Borrow
                    || (!leftClaim.Borrow
                        && leftClaim.CanonicalTransition
                            != rightClaim.CanonicalTransition);
            }
            if (leftClaim.Entry || rightClaim.Entry)
            {
                OwnershipClaim entry =
                    leftClaim.Entry ? leftClaim : rightClaim;
                return !entry.Borrow;
            }
            return CompletionDomainsOverlap(
                    leftClaim.Completion,
                    rightClaim.Completion)
                && leftClaim.CanonicalTransition
                    != rightClaim.CanonicalTransition;
        }

        static bool ConflictsWithIndependence(
            ResolvedResourceEffect independenceEffect,
            ResourceEffect.Independent independence,
            ResolvedResourceEffect otherEffect) =>
            otherEffect.Effect switch
            {
                ResourceEffect.Borrow value =>
                    SameRelation(
                        independenceEffect,
                        value.Source,
                        value.Target,
                        value.Kind,
                        otherEffect,
                        independence)
                    || (value.Lender is not null
                        && SameLenderRelation(
                            independenceEffect,
                            value.Lender,
                            value.Target,
                            otherEffect,
                            independence,
                            KindDomain(
                                otherEffect,
                                value.Source,
                                value.Kind))),
                ResourceEffect.Derive value =>
                    SameRelation(
                        independenceEffect,
                        value.Source,
                        value.Target,
                        null,
                        otherEffect,
                        independence),
                ResourceEffect.Pass value =>
                    SameRelation(
                        independenceEffect,
                        value.Source,
                        value.Target,
                        null,
                        otherEffect,
                        independence),
                ResourceEffect.Move value =>
                    SameRelation(
                        independenceEffect,
                        value.Source,
                        value.Target,
                        value.Kind,
                        otherEffect,
                        independence),
                ResourceEffect.Consume value =>
                    SameRelation(
                        independenceEffect,
                        value.Source,
                        value.Target,
                        value.Kind,
                        otherEffect,
                        independence),
                ResourceEffect.Accept value =>
                    SameRelation(
                        independenceEffect,
                        value.Source,
                        value.Target,
                        value.Kind,
                        otherEffect,
                        independence),
                ResourceEffect.Acquire value =>
                    value.Lender is not null
                    && SameLenderRelation(
                        independenceEffect,
                        value.Lender,
                        value.Target,
                        otherEffect,
                        independence,
                        KindDomain(
                            otherEffect,
                            value.Target,
                            value.Kind)),
                _ => false,
            };

        static bool SameRelation(
            ResolvedResourceEffect independenceEffect,
            ResourceEffectLocation source,
            ResourceEffectLocation target,
            ResourceKindReference? kind,
            ResolvedResourceEffect otherEffect,
            ResourceEffect.Independent independence) =>
            KindDomainsOverlap(
                KindDomain(
                    independenceEffect,
                    independence.Source,
                    declared: null),
                KindDomain(otherEffect, source, kind))
            && SameEndpoints(
                independenceEffect,
                source,
                target,
                otherEffect,
                independence);

        static bool SameLenderRelation(
            ResolvedResourceEffect independenceEffect,
            ResourceEffectLocation lender,
            ResourceEffectLocation target,
            ResolvedResourceEffect otherEffect,
            ResourceEffect.Independent independence,
            OwnershipKindDomain dependentDomain) =>
            !dependentDomain.IsEmpty
            && SameEndpoints(
                independenceEffect,
                lender,
                target,
                otherEffect,
                independence);

        static bool SameEndpoints(
            ResolvedResourceEffect independenceEffect,
            ResourceEffectLocation source,
            ResourceEffectLocation target,
            ResolvedResourceEffect otherEffect,
            ResourceEffect.Independent independence) =>
            otherEffect.Binding.Location(source).CanonicalKey
                == independenceEffect.Binding
                    .Location(independence.Source).CanonicalKey
            && otherEffect.Binding.Location(target).CanonicalKey
                == independenceEffect.Binding
                    .Location(independence.Target).CanonicalKey;

        static bool TryOwnershipClaim(
            ResolvedResourceEffect effect,
            out OwnershipClaim? claim)
        {
            claim = effect.Effect switch
            {
                ResourceEffect.Borrow value =>
                    new(
                        effect.Binding.Location(value.Source).CanonicalKey,
                        KindDomain(effect, value.Source, value.Kind),
                        Borrow: true,
                        Entry: true,
                        Completion: null,
                        CanonicalTransition: CanonicalTransition(effect)),
                ResourceEffect.Consume value =>
                    new(
                        effect.Binding.Location(value.Source).CanonicalKey,
                        KindDomain(effect, value.Source, value.Kind),
                        Borrow: false,
                        Entry: true,
                        Completion: null,
                        CanonicalTransition: CanonicalTransition(effect)),
                ResourceEffect.Move value =>
                    new(
                        effect.Binding.Location(value.Source).CanonicalKey,
                        KindDomain(effect, value.Source, value.Kind),
                        Borrow: false,
                        Entry: value.When is ResourceEffectCompletion.Entry,
                        effect.Binding.Completion,
                        CanonicalTransition(effect)),
                ResourceEffect.Release value =>
                    new(
                        effect.Binding.Location(value.Source).CanonicalKey,
                        KindDomain(effect, value.Source, value.Kind),
                        Borrow: false,
                        Entry: value.When is ResourceEffectCompletion.Entry,
                        effect.Binding.Completion,
                        CanonicalTransition(effect)),
                ResourceEffect.Accept value =>
                    new(
                        effect.Binding.Location(value.Source).CanonicalKey,
                        KindDomain(effect, value.Source, value.Kind),
                        Borrow: false,
                        Entry: value.When is ResourceEffectCompletion.Entry,
                        effect.Binding.Completion,
                        CanonicalTransition(effect)),
                _ => null,
            };
            return claim is not null;
        }

        static string CanonicalTransition(
            ResolvedResourceEffect effect)
        {
            var value = new StringBuilder();
            switch (effect.Effect)
            {
                case ResourceEffect.Borrow item:
                    Append(value, "borrow");
                    AppendResolvedLocation(
                        value,
                        effect.Binding.Location(item.Source));
                    AppendResolvedLocation(
                        value,
                        effect.Binding.Location(item.Target));
                    Append(value, (int)item.Access);
                    Append(value, item.Scope.GetType().Name);
                    AppendBoundLocation(value, effect, item.Lender);
                    Append(
                        value,
                        item.Materialization is null
                            ? -1
                            : (int)item.Materialization.Value);
                    break;
                case ResourceEffect.Consume item:
                    Append(value, "consume");
                    AppendResolvedLocation(
                        value,
                        effect.Binding.Location(item.Source));
                    AppendResolvedLocation(
                        value,
                        effect.Binding.Location(item.Target));
                    break;
                case ResourceEffect.Move item:
                    Append(value, "move");
                    AppendResolvedLocation(
                        value,
                        effect.Binding.Location(item.Source));
                    AppendResolvedLocation(
                        value,
                        effect.Binding.Location(item.Target));
                    AppendResolvedCompletion(
                        value,
                        effect.Binding.Completion!);
                    break;
                case ResourceEffect.Release item:
                    Append(value, "release");
                    AppendResolvedLocation(
                        value,
                        effect.Binding.Location(item.Source));
                    AppendResolvedCompletion(
                        value,
                        effect.Binding.Completion!);
                    AppendBoundLocation(
                        value,
                        effect,
                        item.Correspondence);
                    AppendBoundLocation(
                        value,
                        effect,
                        item.Observation);
                    break;
                case ResourceEffect.Accept item:
                    Append(value, "accept");
                    AppendResolvedLocation(
                        value,
                        effect.Binding.Location(item.Source));
                    AppendResolvedLocation(
                        value,
                        effect.Binding.Location(item.Target));
                    AppendResolvedCompletion(
                        value,
                        effect.Binding.Completion!);
                    Append(value, item.Order?.Value ?? "");
                    break;
                default:
                    Append(value, effect.CanonicalEffect);
                    break;
            }
            return value.ToString();
        }

        static ResolvedResourceKindReference? Kind(
            ResolvedResourceEffect effect,
            ResourceKindReference? declared)
        {
            if (declared is null)
                return null;
            return effect.ResourceKinds.Single(kind =>
                kind.Identity == declared.Identity
                && kind.Arguments.Length == declared.Arguments.Length
                && declared.Arguments.Select(variable =>
                        effect.GenericBindings.Single(binding =>
                            binding.Variable == variable).Value)
                    .SequenceEqual(kind.Arguments));
        }

        static OwnershipKindDomain KindDomain(
            ResolvedResourceEffect effect,
            ResourceEffectLocation source,
            ResourceKindReference? declared)
        {
            ResolvedResourceKindReference? effectKind =
                Kind(effect, declared);
            ResolvedResourceKindReference? slotKind =
                (effect.Binding.Location(source)
                    as ResolvedResourceEffectLocation.OperationSlot)?.Kind;
            return effectKind is not null
                    && slotKind is not null
                    && !effectKind.Equals(slotKind)
                ? new OwnershipKindDomain(IsEmpty: true, Kind: null)
                : new OwnershipKindDomain(
                    IsEmpty: false,
                    effectKind ?? slotKind);
        }

        static bool KindDomainsOverlap(
            OwnershipKindDomain left,
            OwnershipKindDomain right) =>
            !left.IsEmpty
            && !right.IsEmpty
            && (left.Kind is null
                || right.Kind is null
                || left.Kind.Equals(right.Kind));

        static bool CompletionDomainsOverlap(
            ResolvedResourceEffectCompletion? left,
            ResolvedResourceEffectCompletion? right)
        {
            if (left is null || right is null)
                return true;
            if (left.Declaration is ResourceEffectCompletion.Entry
                || right.Declaration is ResourceEffectCompletion.Entry)
            {
                return true;
            }
            if (left.Declaration
                    is ResourceEffectCompletion.ExceptionalExit
                || right.Declaration
                    is ResourceEffectCompletion.ExceptionalExit)
            {
                return left.Declaration
                        is ResourceEffectCompletion.ExceptionalExit
                    && right.Declaration
                        is ResourceEffectCompletion.ExceptionalExit;
            }
            if (left.Outcome is { } leftOutcome
                && right.Outcome is { } rightOutcome
                && leftOutcome.Source.CanonicalKey
                    == rightOutcome.Source.CanonicalKey)
            {
                return !OutcomeTestsDisjoint(
                    leftOutcome.Test,
                    rightOutcome.Test);
            }
            return true;
        }

        static bool OutcomeTestsDisjoint(
            ResolvedResourceEffectOutcomeTest left,
            ResolvedResourceEffectOutcomeTest right) =>
            (left.Declaration, right.Declaration) switch
            {
                (ResourceEffectOutcomeTest.Boolean leftValue,
                    ResourceEffectOutcomeTest.Boolean rightValue) =>
                    leftValue.Value != rightValue.Value,
                (ResourceEffectOutcomeTest.Enum,
                    ResourceEffectOutcomeTest.Enum) =>
                    left.CanonicalKey != right.CanonicalKey,
                (ResourceEffectOutcomeTest.Null,
                    ResourceEffectOutcomeTest.NonNull) or
                (ResourceEffectOutcomeTest.NonNull,
                    ResourceEffectOutcomeTest.Null) or
                (ResourceEffectOutcomeTest.Null,
                    ResourceEffectOutcomeTest.ExactType) or
                (ResourceEffectOutcomeTest.ExactType,
                    ResourceEffectOutcomeTest.Null) => true,
                (ResourceEffectOutcomeTest.ExactType,
                    ResourceEffectOutcomeTest.ExactType) =>
                    left.CanonicalKey != right.CanonicalKey,
                _ => false,
            };

        static bool GuardsOverlap(
            ResolvedResourceEffect left,
            ResourceEffectGuard? leftGuard,
            ResolvedResourceEffect right,
            ResourceEffectGuard? rightGuard)
        {
            if (leftGuard is null || rightGuard is null)
                return true;
            if (left.Binding.Guard!.Subject.CanonicalKey
                != right.Binding.Guard!.Subject.CanonicalKey)
                return true;
            return left.GuardExpectedType is null
                || right.GuardExpectedType is null
                || left.GuardExpectedType.Equals(
                    right.GuardExpectedType);
        }

        static ImmutableArray<ResolvedResourceEffect> OrderEffects(
            IEnumerable<ResolvedResourceEffect> effects) =>
            [
                .. effects
                    .OrderBy(OccurrenceKey, StringComparer.Ordinal)
                    .ThenBy(
                        effect => effect.CanonicalEffect,
                        StringComparer.Ordinal)
                    .ThenBy(
                        CanonicalInterfaceApplications,
                        StringComparer.Ordinal)
                    .ThenBy(
                        CanonicalSources,
                        StringComparer.Ordinal),
            ];

        static string OccurrenceKey(ResolvedResourceEffect effect) =>
            OccurrenceKey(effect.DirectCall);

    internal static string OccurrenceKey(
            DirectCallDefinitionResolution directCall)
        {
            var value = new StringBuilder();
            Append(
                value,
                MetadataReceiptEvidence.For(
                    directCall.Participant.Assembly.Registration));
            Append(value, directCall.PhysicalInvocation.ModuleVersionId);
            Append(value, directCall.PhysicalInvocation.MethodToken);
            Append(value, directCall.PhysicalInvocation.ILOffset);
            Append(value, directCall.PhysicalInvocation.OperandToken);
            Append(value, (int)directCall.Call.Kind);
            if (directCall is DirectCallDefinitionResolution.Resolved resolved)
            {
                Append(
                    value,
                    MetadataReceiptEvidence.For(
                        resolved.Definition.Registration));
                Append(value, resolved.Definition.ModuleVersionId);
                Append(value, resolved.Definition.MetadataToken);
                AppendForwarding(value, resolved.Definition.Forwarding);
            }
            else
            {
                Append(value, directCall.GetType().Name);
            }
            return value.ToString();
        }

        static string CanonicalBoundEffect(
            ResourceEffect effect,
            ImmutableArray<ResolvedResourceEffectGenericBinding> bindings,
            ImmutableArray<ResolvedResourceKindReference> resourceKinds,
            ResolvedResourceEffectBinding occurrenceBinding)
        {
            var value = new StringBuilder();
            switch (effect)
            {
                case ResourceEffect.Resource item:
                    Append(value, "resource");
                    AppendKind(value, item.Kind);
                    Append(value, item.Value is null ? -1 : (int)item.Value);
                    Append(value, item.Selector?.Value ?? "");
                    break;
                case ResourceEffect.Authority item:
                    Append(value, "authority");
                    AppendKind(value, item.Kind);
                    AppendLocation(value, item.Target);
                    switch (item.Key)
                    {
                        case ResourceAuthorityKey.Value:
                            Append(value, "value");
                            break;
                        case ResourceAuthorityKey.Singleton singleton:
                            Append(value, "singleton");
                            foreach (ResourceEffectGenericVariable argument
                                in singleton.Arguments)
                            {
                                AppendType(
                                    value,
                                    Binding(argument));
                            }
                            break;
                    }
                    break;
                case ResourceEffect.Acquire item:
                    Append(value, "acquire");
                    AppendKind(value, item.Kind);
                    AppendLocation(value, item.Target);
                    AppendCompletion(value, item.When);
                    AppendLocation(value, item.Correspondence);
                    AppendLocation(value, item.Lender);
                    break;
                case ResourceEffect.Move item:
                    Append(value, "move");
                    AppendLocation(value, item.Source);
                    AppendLocation(value, item.Target);
                    AppendCompletion(value, item.When);
                    AppendKind(value, item.Kind);
                    break;
                case ResourceEffect.Consume item:
                    Append(value, "consume");
                    AppendLocation(value, item.Source);
                    AppendLocation(value, item.Target);
                    AppendKind(value, item.Kind);
                    break;
                case ResourceEffect.Release item:
                    Append(value, "release");
                    AppendLocation(value, item.Source);
                    AppendCompletion(value, item.When);
                    AppendKind(value, item.Kind);
                    AppendLocation(value, item.Correspondence);
                    AppendLocation(value, item.Observation);
                    break;
                case ResourceEffect.Borrow item:
                    Append(value, "borrow");
                    AppendLocation(value, item.Source);
                    AppendLocation(value, item.Target);
                    Append(value, (int)item.Access);
                    Append(value, item.Scope.GetType().Name);
                    AppendKind(value, item.Kind);
                    AppendLocation(value, item.Lender);
                    Append(
                        value,
                        item.Materialization is null
                            ? -1
                            : (int)item.Materialization);
                    break;
                case ResourceEffect.Derive item:
                    Append(value, "derive");
                    AppendLocation(value, item.Source);
                    AppendLocation(value, item.Target);
                    Append(value, (int)item.Relation);
                    AppendGuard(value, item.Guard);
                    break;
                case ResourceEffect.Pass item:
                    Append(value, "pass");
                    AppendLocation(value, item.Source);
                    AppendLocation(value, item.Target);
                    Append(
                        value,
                        item.Identity is null ? -1 : (int)item.Identity);
                    break;
                case ResourceEffect.Independent item:
                    Append(value, "independent");
                    AppendLocation(value, item.Source);
                    AppendLocation(value, item.Target);
                    break;
                case ResourceEffect.Callback item:
                    Append(value, "callback");
                    Append(
                        value,
                        occurrenceBinding.Callback!.Contract
                            .DelegateParameterIndex);
                    AppendCallback(
                        value,
                        occurrenceBinding.Callback.Contract);
                    Append(value, (int)item.Execution);
                    Append(value, (int)item.Cardinality);
                    break;
                case ResourceEffect.Accept item:
                    Append(value, "accept");
                    AppendLocation(value, item.Source);
                    AppendLocation(value, item.Target);
                    AppendCompletion(value, item.When);
                    AppendKind(value, item.Kind);
                    Append(value, item.Order?.Value ?? "");
                    break;
                case ResourceEffect.Operation item:
                    Append(value, "operation");
                    Append(value, (int)item.Boundary);
                    Append(value, (int)item.Throws);
                    AppendGuard(value, item.Guard);
                    break;
                case ResourceEffect.Outcome:
                    Append(value, "outcome");
                    AppendOutcome(
                        value,
                        occurrenceBinding.Outcome!);
                    break;
                default:
                    Append(value, effect.ToString() ?? effect.GetType().Name);
                    break;
            }
            return value.ToString();

            ResolvedResourceEffectType Binding(
                ResourceEffectGenericVariable variable) =>
                bindings.Single(binding =>
                    binding.Variable == variable).Value;

            void AppendKind(
                StringBuilder builder,
                ResourceKindReference? reference)
            {
                if (reference is null)
                {
                    Append(builder, "all-kinds");
                    return;
                }
                ResolvedResourceKindReference resolved =
                    resourceKinds.Single(kind =>
                        kind.Identity == reference.Identity
                        && kind.Arguments.Length
                            == reference.Arguments.Length
                        && reference.Arguments.Select(Binding)
                            .SequenceEqual(kind.Arguments));
                Append(builder, resolved.Identity.Value);
                foreach (ResolvedResourceEffectType argument
                    in resolved.Arguments)
                {
                    AppendType(builder, argument);
                }
            }

            void AppendLocation(
                StringBuilder builder,
                ResourceEffectLocation? location)
            {
                if (location is null)
                {
                    Append(builder, "none");
                    return;
                }
                AppendResolvedLocation(
                    builder,
                    occurrenceBinding.Location(location));
            }

            void AppendCompletion(
                StringBuilder builder,
                ResourceEffectCompletion completion)
            {
                _ = completion;
                AppendResolvedCompletion(
                    builder,
                    occurrenceBinding.Completion!);
            }

            void AppendGuard(
                StringBuilder builder,
                ResourceEffectGuard? guard)
            {
                if (guard is null)
                {
                    Append(builder, "unguarded");
                    return;
                }
                var exact = (ResourceEffectGuard.ExactRuntimeType)guard;
                _ = exact;
                Append(builder, "exact-runtime-type");
                AppendResolvedLocation(
                    builder,
                    occurrenceBinding.Guard!.Subject);
                AppendType(
                    builder,
                    occurrenceBinding.Guard.ExpectedType);
            }
        }

        static void AppendCompletion(
            StringBuilder value,
            ResourceEffectCompletion completion) =>
            Append(value, completion.GetType().Name);

        static void AppendBoundLocation(
            StringBuilder value,
            ResolvedResourceEffect effect,
            ResourceEffectLocation? location)
        {
            if (location is null)
            {
                Append(value, "none");
                return;
            }
            AppendResolvedLocation(
                value,
                effect.Binding.Location(location));
        }

        static void AppendResolvedLocation(
            StringBuilder value,
            ResolvedResourceEffectLocation location) =>
            Append(value, location.CanonicalKey);

        static void AppendResolvedCompletion(
            StringBuilder value,
            ResolvedResourceEffectCompletion completion)
        {
            Append(value, completion.CanonicalKey);
            if (completion.Outcome is not null)
                AppendOutcome(value, completion.Outcome);
        }

        static void AppendOutcome(
            StringBuilder value,
            ResolvedResourceEffectOutcome outcome)
        {
            AppendResolvedLocation(value, outcome.Source);
            Append(value, outcome.Test.CanonicalKey);
            if (outcome.Test.ExactType is not null)
                AppendTypeDefinition(value, outcome.Test.ExactType);
        }

        static void AppendCallback(
            StringBuilder value,
            ResolvedResourceEffectCallbackContract callback)
        {
            Append(value, callback.DelegateParameterIndex);
            AppendType(value, callback.DelegateType);
            AppendTypeDefinition(value, callback.DelegateDefinition);
            Append(value, callback.InvokeMetadataToken);
            Append(value, callback.ParameterTypes.Length);
            foreach (ResolvedResourceEffectType parameter
                in callback.ParameterTypes)
            {
                AppendType(value, parameter);
            }
            AppendType(value, callback.ReturnType);
        }

        static void AppendTypeDefinition(
            StringBuilder value,
            ResolvedResourceEffectTypeDefinition definition)
        {
            Append(
                value,
                MetadataReceiptEvidence.For(
                    definition.AssemblyReference.Registration));
            AppendAssembly(value, definition.Assembly);
            Append(value, definition.ModuleVersionId);
            Append(value, definition.Token.Value);
        }

        static void AppendLocation(
            StringBuilder value,
            ResourceEffectLocation? location)
        {
            switch (location)
            {
                case null:
                    Append(value, "none");
                    break;
                case ResourceEffectLocation.Receiver:
                    Append(value, "receiver");
                    break;
                case ResourceEffectLocation.Return:
                    Append(value, "return");
                    break;
                case ResourceEffectLocation.Constructed:
                    Append(value, "constructed");
                    break;
                case ResourceEffectLocation.Parameter parameter:
                    Append(value, "parameter");
                    Append(value, parameter.Index);
                    break;
                default:
                    Append(value, location.ToString() ?? "");
                    break;
            }
        }

        static void AppendType(
            StringBuilder value,
            ResolvedResourceEffectType type)
        {
            Append(value, (int)type.Type.Kind);
            Append(value, type.Type.RawTypeKind);
            Append(value, type.Type.Rank);
            Append(value, "array-sizes");
            Append(value, type.Type.ArraySizes.Length);
            foreach (int size in type.Type.ArraySizes)
                Append(value, size);
            Append(value, "array-lower-bounds");
            Append(value, type.Type.ArrayLowerBounds.Length);
            foreach (int lowerBound in type.Type.ArrayLowerBounds)
                Append(value, lowerBound);
            if (type.Definition is { } definition)
            {
                Append(value, "definition");
                Append(
                    value,
                    MetadataReceiptEvidence.For(definition));
                Append(value, (int)definition.Kind);
            }
            else
            {
                Append(value, "exact-signature");
                AppendTypeRef(value, type.Type);
                if (type.DefiningAssembly is { } assembly)
                    AppendAssembly(value, assembly);
                else
                    Append(value, "no-defining-assembly");
            }
            if (type.GenericScope is { } scope)
            {
                Append(value, (int)scope.Kind);
                Append(value, scope.Owner.SourceReceiptEvidence);
                Append(value, scope.Owner.ModuleVersionId);
                Append(value, scope.Owner.MethodToken);
            }
            else
            {
                Append(value, "no-generic-scope");
            }
            Append(value, type.Element is null ? "no-element" : "element");
            if (type.Element is not null)
                AppendType(value, type.Element);
            Append(value, "arguments");
            Append(value, type.Arguments.Length);
            foreach (ResolvedResourceEffectType argument in type.Arguments)
                AppendType(value, argument);
        }

        static ResolvedResourceEffectSource CanonicalizeSource(
            ResolvedResourceEffectSource source) =>
            new(
                source.Model,
                source.ModelReceipt,
                source.Declaration,
                [
                    .. source.Provenances.OrderBy(
                        ResourceEffectCanonicalizer.Provenance,
                        StringComparer.Ordinal),
        ],
        [.. source.InterfaceApplications.OrderBy(
                    CanonicalInterfaceApplication, StringComparer.Ordinal)]);

        static string CanonicalSource(
    ResolvedResourceEffectSource source) =>
    CanonicalDeclarationSource(source) + "\u001f"
    + string.Join("\u001e", source.InterfaceApplications.Select(CanonicalInterfaceApplication));

    static string CanonicalDeclarationSource(
            ResolvedResourceEffectSource source) =>
            source.Model.Value
            + "\u001f"
            + source.ModelReceipt.ContentHash
            + "\u001f"
            + ResourceEffectCanonicalizer.Declaration(
                source.Declaration.Target,
                source.Declaration.Effect)
            + "\u001f"
            + string.Join(
                "\u001e",
                source.Provenances.Select(
                    ResourceEffectCanonicalizer.Provenance));

        static string CanonicalSources(ResolvedResourceEffect effect) =>
            string.Join(
                "\u001d",
                effect.Sources
                    .Select(CanonicalizeSource)
                    .OrderBy(CanonicalSource, StringComparer.Ordinal)
                    .Select(CanonicalSource));

    internal static ResourceEffectOccurrencePopulationReceipt
            CreatePopulationReceipt(
                DirectCallDefinitionResolutionOutcome.Completed directCalls)
        {
            ImmutableArray<CatalogCallGraphParticipant> participants =
            [
                .. directCalls.Population.OrderBy(
                    ParticipantKey,
                    StringComparer.Ordinal),
            ];
            ImmutableArray<DirectCallDefinitionResolution> ordered =
            [
                .. directCalls.Results.OrderBy(
                    OccurrenceKey,
                    StringComparer.Ordinal),
            ];
            using var hash = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
            AppendHash("resource-effect-occurrence-population-v1");
            AppendHash(directCalls.Catalog.Value.ToString("D"));
            AppendHash(MetadataReceiptEvidence.For(directCalls.Generation));
            foreach (CatalogCallGraphParticipant participant
                in participants)
            {
                AppendHash(ParticipantKey(participant));
                AppendHash(participant.CallGraph.DeclaredMethods.Length.ToString(
                    CultureInfo.InvariantCulture));
                AppendHash(participant.CallGraph.DirectCalls.Length.ToString(
                    CultureInfo.InvariantCulture));
                AppendHash(participant.CallGraph.Diagnostics.Length.ToString(
                    CultureInfo.InvariantCulture));
                foreach (AnalysisDiagnostic diagnostic
                    in participant.CallGraph.Diagnostics.OrderBy(
                        diagnostic => diagnostic.MethodToken))
                {
                    AppendHash(diagnostic.MethodToken.ToString(
                        CultureInfo.InvariantCulture));
                    AppendHash(diagnostic.SourceMethodToken?.ToString(
                        CultureInfo.InvariantCulture) ?? "");
                    AppendHash(diagnostic.DeclaringType is null
                        ? ""
                        : CanonicalTypeRef(diagnostic.DeclaringType));
                    AppendHash(diagnostic.SourceDeclaringType is null
                        ? ""
                        : CanonicalTypeRef(
                            diagnostic.SourceDeclaringType));
                }
            }
            foreach (DirectCallDefinitionResolution result in ordered)
            {
                AppendHash(OccurrenceKey(result));
                AppendHash(result.GetType().Name);
                if (result is DirectCallDefinitionResolution.Resolved resolved)
                    AppendHash(CanonicalMember(resolved.Definition.Member));
            }
            return new ResourceEffectOccurrencePopulationReceipt(
                directCalls.Catalog,
                directCalls.Generation,
                participants,
                ordered,
                Convert.ToHexString(hash.GetHashAndReset())
                    .ToLowerInvariant());

            void AppendHash(string text)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                hash.AppendData(bytes);
                hash.AppendData([0]);
            }
        }

        static string ParticipantKey(
            CatalogCallGraphParticipant participant)
        {
            var value = new StringBuilder();
            Append(
                value,
                MetadataReceiptEvidence.For(
                    participant.Assembly.Registration));
            AppendAssembly(value, participant.Assembly.Identity);
            Append(
                value,
                participant.CallGraph.ModuleIdentity.ModuleVersionId);
            return value.ToString();
        }

        static string CanonicalTypeRef(TypeRef type)
        {
            var value = new StringBuilder();
            AppendTypeRef(value, type);
            return value.ToString();
        }

        static ResourceEffectResolutionReceipt CreateReceipt(
            ResourceEffectAdmissionReceipt admission,
            ResourceEffectOccurrencePopulationReceipt population,
            string completion,
            ImmutableArray<ResourceEffectTargetEvaluation> evaluations,
            ImmutableArray<ResolvedResourceEffect> effects,
            ImmutableArray<ResourceEffectConflict> conflicts,
            ImmutableArray<ResourceEffectResolutionGap> gaps)
        {
            using var hash = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
            AppendHash("resolved-resource-effects-v1");
            AppendHash(admission.ContentHash);
            AppendHash(population.ContentHash);
            AppendHash(completion);
            foreach (ResourceEffectTargetEvaluation evaluation
                in evaluations)
            {
                AppendHash(evaluation.Model.Value);
                AppendHash(evaluation.ModelReceipt.ContentHash);
                AppendHash(
                    ResourceEffectCanonicalizer.Declaration(
                        evaluation.Declaration.Target,
                        evaluation.Declaration.Effect));
                AppendHash(((int)evaluation.Kind).ToString(
                    CultureInfo.InvariantCulture));
                foreach (ResolvedResourceEffect effect in evaluation.Effects)
                    AppendEffect(effect);
                foreach (ResourceEffectResolutionGap gap in evaluation.Gaps)
                    AppendGap(gap);
            }
            foreach (ResolvedResourceEffect effect in effects)
                AppendEffect(effect);
            foreach (ResourceEffectConflict conflict in conflicts)
            {
                AppendHash("conflict");
                AppendHash(OccurrenceKey(conflict.Effects[0]));
                foreach (ResolvedResourceEffect effect in conflict.Effects)
                    AppendEffect(effect);
            }
            foreach (ResourceEffectResolutionGap gap in gaps)
                AppendGap(gap);

            return new ResourceEffectResolutionReceipt(
                admission,
                population,
                Convert.ToHexString(hash.GetHashAndReset())
                    .ToLowerInvariant());

            void AppendEffect(ResolvedResourceEffect effect)
            {
                AppendHash(OccurrenceKey(effect));
                AppendHash(effect.CanonicalEffect);
                AppendHash(CanonicalBindingEvidence(effect));
                AppendHash(CanonicalSources(effect));
            }

            void AppendGap(ResourceEffectResolutionGap gap)
            {
                AppendHash(((int)gap.Kind).ToString(
                    CultureInfo.InvariantCulture));
                AppendHash(gap.Participant is null
                    ? ""
                    : ParticipantKey(gap.Participant));
                AppendHash(gap.AnalysisDiagnostic is null
                    ? ""
                    : gap.AnalysisDiagnostic.MethodToken.ToString(
                        CultureInfo.InvariantCulture));
                AppendHash(gap.AnalysisDiagnostic?.SourceMethodToken
                    ?.ToString(CultureInfo.InvariantCulture) ?? "");
                AppendHash(gap.PhysicalInvocation is null
                    ? ""
                    : CanonicalPhysical(gap.PhysicalInvocation));
                AppendHash(gap.SelectorGap is null
                    ? ""
                    : CanonicalSelectorGap(gap.SelectorGap));
                AppendHash(gap.OccurrenceGap is null
                    ? ""
                    : CanonicalOccurrenceGap(gap.OccurrenceGap));
                AppendHash(gap.InterfaceApplicationGap is null
                    ? ""
                    : CanonicalInterfaceApplicationGap(
                        gap.InterfaceApplicationGap));
                AppendHash(gap.DeferredKind is null
                    ? ""
                    : ((int)gap.DeferredKind).ToString(
                        CultureInfo.InvariantCulture));
                AppendHash(gap.WorkDimension is null
                    ? ""
                    : ((int)gap.WorkDimension).ToString(
                        CultureInfo.InvariantCulture));
                AppendHash(gap.Limit?.ToString(
                    CultureInfo.InvariantCulture) ?? "");
                AppendHash(gap.RequiredWork?.ToString(
                    CultureInfo.InvariantCulture) ?? "");
            }

            void AppendHash(string text)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                hash.AppendData(bytes);
                hash.AppendData([0]);
            }
        }

    static string CanonicalBindingEvidence(
        ResolvedResourceEffect effect)
    {
        var value = new StringBuilder();
        foreach (ResolvedResourceEffectGenericBinding binding
            in effect.GenericBindings
                .OrderBy(binding => binding.Variable.Kind)
                .ThenBy(binding => binding.Variable.Index))
        {
            Append(value, (int)binding.Variable.Kind);
            Append(value, binding.Variable.Index);
            AppendTypeEvidence(value, binding.Value);
        }
        foreach (string kind in effect.ResourceKinds
            .Select(CanonicalKind)
            .OrderBy(item => item, StringComparer.Ordinal))
        {
            Append(value, kind);
        }
        Append(
            value,
            CanonicalInterfaceApplications(effect));
        return value.ToString();
    }

    static string CanonicalInterfaceApplications(ResolvedResourceEffect effect) =>
        string.Join("\u001e", effect.InterfaceApplications.Select(CanonicalInterfaceApplication)
            .OrderBy(value => value, StringComparer.Ordinal));

    internal static string CanonicalInterfaceApplication(
        ResourceEffectInterfaceApplicationEvidence? application)
    {
        if (application is null)
            return "";

        var value = new StringBuilder();
        Append(
            value,
            MetadataReceiptEvidence.For(
                application.InterfaceDeclaration.Registration));
        Append(
            value,
            application.InterfaceDeclaration.ModuleVersionId);
        Append(
            value,
            application.InterfaceDeclaration.MetadataToken);
        AppendAssembly(value, application.InterfaceDeclaration.Assembly);
        Append(value, (int)application.InterfaceDeclaration.Semantics);
        AppendForwarding(value, application.InterfaceDeclaration.Forwarding);
        Append(
            value,
            MetadataReceiptEvidence.For(
                application.Implementation.Registration));
        Append(value, application.Implementation.ModuleVersionId);
        Append(value, application.Implementation.MetadataToken);
        Append(
            value,
            MetadataReceiptEvidence.For(
                application.InterfacePath.Registration));
        Append(value, application.InterfacePath.ModuleVersionId);
        Append(value, application.InterfacePath.DeclaringTypeToken);
        Append(
            value,
            application.InterfacePath.InterfaceImplementationToken);
        AppendTypeRef(
            value,
            application.InterfacePath.ClosedInterfaceType);
        Append(value, CanonicalMember(application.ClosedSlot.Member));
        Append(value, application.ClosedSlot.Member.SignatureHeader);
        Append(value, application.ClosedSlot.Member.RequiredParameterCount);
        Append(value, application.ClosedSlot.Member.HasThis ? 1 : 0);
        Append(value, application.ClosedSlot.Member.ParameterDirections.Length);
        foreach (ParameterDirection direction in application.ClosedSlot.Member.ParameterDirections)
            Append(value, (int)direction);
        AppendCatalogType(value, application.ClosedSlot.DeclaringType);
        AppendCatalogType(value, application.ClosedSlot.ReturnType);
        Append(value, application.ClosedSlot.ParameterTypes.Length);
        foreach (CatalogTypeShape parameter in application.ClosedSlot.ParameterTypes)
            AppendCatalogType(value, parameter);
        AppendScopes(application.ClosedSlot.DeclaringGenericScopes);
        AppendScopes(application.ClosedSlot.ParameterGenericScopes);
        AppendScopes(application.ClosedSlot.ReturnGenericScopes);
        Append(
            value,
            application.Method.ImplementationMethodToken);
        if (application.Method
            is ResourceEffectMethodImplementationEvidence.Explicit
                explicitMethod)
        {
            Append(value, "explicit");
            Append(
                value,
                explicitMethod.MethodImplementationToken);
        }
        else
        {
            Append(value, "implicit");
        }
        return value.ToString();

        void AppendScopes(ImmutableArray<ResolvedResourceEffectGenericScope?> scopes)
        {
            Append(value, scopes.Length);
            foreach (ResolvedResourceEffectGenericScope? scope in scopes)
            {
                Append(value, scope is null ? "slot-variable" : "invocation-variable");
                if (scope is not null)
                {
                    Append(value, (int)scope.Kind);
                    Append(value, CanonicalPhysical(scope.Owner));
                }
            }
        }
    }

    static void AppendCatalogType(StringBuilder value, CatalogTypeShape type)
    {
        Append(value, (int)type.Kind);
        Append(value, type.Definition is { } definition
            ? MetadataReceiptEvidence.For(definition) : "");
        Append(value, type.RawTypeKind);
        Append(value, type.Rank);
        Append(value, type.GenericParameterIndex);
        Append(value, type.IsRequiredModifier ? 1 : 0);
        Append(value, type.SignatureHeader);
        Append(value, type.GenericArity);
        Append(value, type.RequiredParameterCount);
        Append(value, type.ArraySizes.Length);
        foreach (int size in type.ArraySizes)
            Append(value, size);
        Append(value, type.ArrayLowerBounds.Length);
        foreach (int bound in type.ArrayLowerBounds)
            Append(value, bound);
        Append(value, type.ElementType is not null ? 1 : 0);
        if (type.ElementType is { } element)
            AppendCatalogType(value, element);
        Append(value, type.Components.Length);
        foreach (CatalogTypeShape component in type.Components)
            AppendCatalogType(value, component);
    }

    static string CanonicalInterfaceApplicationGap(
        ResourceEffectInterfaceApplicationGap gap)
    {
        var value = new StringBuilder();
        Append(value, (int)gap.Kind);
        Append(value, gap.Detail ?? "");
        Append(
            value,
            gap.WorkDimension is null
                ? -1
                : (int)gap.WorkDimension);
        Append(value, gap.Limit ?? -1);
        Append(value, gap.RequiredWork ?? -1);
        return value.ToString();
    }

    static string CanonicalKind(
        ResolvedResourceKindReference kind)
    {
        var value = new StringBuilder();
        Append(value, kind.Identity.Value);
        Append(value, kind.Arguments.Length);
        foreach (ResolvedResourceEffectType argument in kind.Arguments)
            AppendTypeEvidence(value, argument);
        return value.ToString();
    }

    static void AppendTypeEvidence(
        StringBuilder value,
        ResolvedResourceEffectType type)
    {
        AppendType(value, type);
        AppendForwarding(value, type.Forwarding);
        if (type.Element is not null)
            AppendTypeEvidence(value, type.Element);
        foreach (ResolvedResourceEffectType argument in type.Arguments)
            AppendTypeEvidence(value, argument);
    }

    static void AppendForwarding(
        StringBuilder value,
        ImmutableArray<TypeForwardingHop> forwarding)
    {
        Append(value, "forwarding");
        Append(value, forwarding.Length);
        foreach (TypeForwardingHop hop in forwarding)
        {
            Append(
                value,
                MetadataReceiptEvidence.For(
                    hop.SourceAssembly.Assembly.Registration));
            AppendAssembly(
                value,
                hop.SourceAssembly.Assembly.Identity);
            Append(
                value,
                MetadataReceiptEvidence.For(
                    hop.SourceOccurrence.Assembly.Registration));
            Append(value, hop.Declarations.Length);
            foreach (ExportedTypeToken declaration in hop.Declarations)
                Append(value, declaration.Value);
            AppendAssembly(value, hop.TargetReference);
            Append(value, (int)hop.Scope);
        }
    }

    static string CanonicalSelectorGap(
            ResourceEffectSelectorBindingGap gap)
        {
            var value = new StringBuilder();
            Append(value, (int)gap.Kind);
            if (gap.DirectCallGap is { } direct)
            {
                Append(value, (int)direct.Kind);
                Append(value, (int)direct.CallKind);
                Append(value, CanonicalPhysical(direct.PhysicalInvocation));
                Append(
                    value,
                    direct.WorkDimension is null
                        ? -1
                        : (int)direct.WorkDimension);
                Append(value, direct.Limit ?? -1);
                Append(value, direct.RequiredWork ?? -1);
            }
            if (gap.Type is not null)
                AppendTypeRef(value, gap.Type);
            if (gap.TypeResolution is not null)
                Append(value, gap.TypeResolution.GetType().Name);
            if (gap.DefinitionProjection is not null)
                Append(value, gap.DefinitionProjection.GetType().Name);
            return value.ToString();
        }

        static string CanonicalOccurrenceGap(
            ResourceEffectOccurrenceBindingGap gap)
        {
            var value = new StringBuilder();
            Append(value, (int)gap.Kind);
            Append(
                value,
                gap.Location is null
                    ? ""
                    : ResourceEffectCanonicalizer.Location(
                        gap.Location));
            return value.ToString();
        }

        static string CanonicalPhysical(GraphNodeStorageKey physical)
        {
            var value = new StringBuilder();
            Append(value, physical.SourceReceiptEvidence);
            Append(value, physical.ModuleVersionId);
            Append(value, (int)physical.Kind);
            Append(value, physical.MethodToken);
            Append(value, physical.ILOffset);
            Append(value, physical.OperandToken);
            return value.ToString();
        }

        static string CanonicalMember(MemberRef member)
        {
            var value = new StringBuilder();
            Append(value, (int)member.Kind);
            Append(value, member.Name);
            Append(value, member.GenericArity);
            AppendTypeRef(value, member.DeclaringType);
            AppendTypeRef(value, member.ReturnType);
            foreach (TypeRef parameter in member.ParameterTypes)
                AppendTypeRef(value, parameter);
            return value.ToString();
        }

        static void AppendTypeRef(StringBuilder value, TypeRef type)
        {
            Append(value, (int)type.Kind);
            Append(value, type.Assembly);
            Append(value, type.Namespace);
            Append(value, type.Name);
            Append(value, type.Rank);
            Append(value, type.GenericParameterIndex);
            Append(value, type.RawTypeKind);
            Append(value, "array-sizes");
            Append(value, type.ArraySizes.Length);
            foreach (int size in type.ArraySizes)
                Append(value, size);
            Append(value, "array-lower-bounds");
            Append(value, type.ArrayLowerBounds.Length);
            foreach (int lowerBound in type.ArrayLowerBounds)
                Append(value, lowerBound);
            Append(
                value,
                type.ElementType is null ? "no-element" : "element");
            if (type.ElementType is not null)
                AppendTypeRef(value, type.ElementType);
            Append(value, "type-arguments");
            Append(value, type.TypeArguments.Length);
            foreach (TypeRef argument in type.TypeArguments)
                AppendTypeRef(value, argument);
            Append(
                value,
                type.ModifierType is null
                    ? "no-modifier"
                    : "modifier");
            if (type.ModifierType is not null)
                AppendTypeRef(value, type.ModifierType);
            Append(
                value,
                type.UnmodifiedType is null
                    ? "no-unmodified"
                    : "unmodified");
            if (type.UnmodifiedType is not null)
                AppendTypeRef(value, type.UnmodifiedType);
        }

        static void AppendAssembly(
            StringBuilder value,
            AssemblyReferenceIdentity assembly)
        {
            Append(value, assembly.Name);
            Append(value, assembly.Version?.ToString() ?? "");
            Append(value, assembly.Culture ?? "");
            Append(value, assembly.PublicKeyToken ?? "");
        }

        static void Append(StringBuilder value, string text)
        {
            value.Append(text.Length.ToString(CultureInfo.InvariantCulture));
            value.Append(':');
            value.Append(text);
            value.Append(';');
        }

        static void Append(StringBuilder value, Guid item) =>
            Append(value, item.ToString("D"));

        static void Append(StringBuilder value, int item) =>
            Append(value, item.ToString(CultureInfo.InvariantCulture));

        static void Append(StringBuilder value, long item) =>
            Append(value, item.ToString(CultureInfo.InvariantCulture));

        sealed record BoundEffectKey(
            GraphNodeStorageKey PhysicalInvocation,
    string CanonicalEffect);

        sealed class CoalescedEffect
        {
            internal CoalescedEffect(
                ResolvedResourceEffect first,
                List<ResolvedResourceEffectSource> sources)
            {
                First = first;
                Sources = sources;
            }

            internal ResolvedResourceEffect First { get; }
            internal List<ResolvedResourceEffectSource> Sources { get; }
        }

        sealed class SourceComparer
            : IEqualityComparer<ResolvedResourceEffectSource>
        {
            internal static SourceComparer Instance { get; } = new();

            public bool Equals(
                ResolvedResourceEffectSource? left,
                ResolvedResourceEffectSource? right) =>
                ReferenceEquals(left, right)
                || (left is not null
                    && right is not null
                    && CanonicalSource(left) == CanonicalSource(right));

            public int GetHashCode(ResolvedResourceEffectSource value) =>
                StringComparer.Ordinal.GetHashCode(CanonicalSource(value));
        }

        sealed record OwnershipClaim(
            string Source,
            OwnershipKindDomain Kind,
            bool Borrow,
            bool Entry,
            ResolvedResourceEffectCompletion? Completion,
            string CanonicalTransition);

        sealed record OwnershipKindDomain(
            bool IsEmpty,
            ResolvedResourceKindReference? Kind);

        sealed class GapCollector
        {
            readonly int _limit;
            readonly List<ResourceEffectResolutionGap> _gaps = [];

            internal GapCollector(int limit) => _limit = limit;

            internal bool Exhausted { get; private set; }

            internal bool TryAdd(ResourceEffectResolutionGap gap)
            {
                if (_gaps.Count >= _limit)
                {
                    Exhausted = true;
                    return false;
                }
                _gaps.Add(gap);
                return true;
            }

            internal void AddLimitGap(ResourceEffectResolutionGap gap)
            {
                if (_gaps.Count == _limit)
                    _gaps[^1] = gap;
                else
                    _gaps.Add(gap);
            }

            internal ImmutableArray<ResourceEffectResolutionGap> ToImmutable()
                => [.. _gaps];
    }
}
