using System.Collections.Immutable;

namespace ILInspector.Analysis;

internal sealed partial class LibraryBodyAnalysisBuilder
{
    LibraryBodyAnalysisResult PublishResourceOccurrences(
        LibraryBodyAnalysisResult analysis,
        LibraryBodyAnalysisPlan plan,
        LibraryMethodAnalysisResult[] methodResults)
    {
        if (plan.ResourceEffects is not { } admission)
            return analysis;

        if (_referenceMetadataResolver?.BindingPolicy is not { } bindingPolicy
            || _referenceMetadataResolver.RootAssembly is not { } rootAssembly)
        {
            return analysis with
            {
                ResourceOccurrences =
                    new(
                        [],
                        [
                            new ResourceOccurrenceLimitation(
                                ResourceOccurrenceLimitationKind
                                    .EffectResolution,
                                "Resource effect resolution requires an "
                                + "assembly reference resolver.")
                            {
                                EffectResolutionRejection =
                                    ResourceEffectResolutionRejectionKind
                                        .OccurrencePopulationRejected,
                            },
                        ]),
            };
        }

        var receipt = new LibraryBodyAnalysisReceipt(
            _path,
            LibraryBodyModuleIdentity.FromImage(_reader),
            plan.Features,
            !plan.IsScoped,
            analysis.Diagnostics);
        LibraryBodyAnalysisResult resolutionAnalysis =
            SelectEffectResolutionCalls(
                analysis,
                admission,
                plan.IncludesResourceLifecycle);
        var callGraph = new LibraryCallGraphAnalysisResult(
            receipt,
            _reader.GetString(_reader.GetModuleDefinition().Name),
            resolutionAnalysis);
        var participant =
            new CatalogCallGraphParticipant(callGraph, rootAssembly);
        ResourceEffectResolutionOutcome outcome =
            ResourceEffectResolver.Resolve(
                bindingPolicy,
                admission,
                [participant]);

        ImmutableArray<ResolvedResourceEffect> effects =
            Effects(outcome);
        ImmutableArray<ResourceOccurrenceLimitation> limitations =
            Limitations(outcome);
        ImmutableArray<MethodIdentity> methods =
        [
            .. effects
                .Select(effect => effect.DirectCall.Call.EvidenceMethod)
                .Concat(limitations
                    .Where(limitation => limitation.Method is not null)
                    .Select(limitation => limitation.Method!))
                .Distinct()
                .OrderBy(method => method.MetadataToken),
        ];
        var results =
            ImmutableArray.CreateBuilder<
                ResourceOccurrenceAnalysisResult>(methods.Length);
        foreach (MethodIdentity method in methods)
        {
            ImmutableArray<ResourceOccurrenceLimitation>
                methodLimitations =
            [
                .. limitations.Where(limitation =>
                    limitation.Method == method),
            ];
            MethodBodyAnalysisContext? context = methodResults
                .Select(result => result.ResourceOccurrenceContext)
                .FirstOrDefault(candidate =>
                    candidate?.Method == method);
            ResourceOccurrenceAnalysisResult result =
                context is null
                    ? new(
                        method,
                        [],
                        [],
                        [])
                    : ResourceOccurrenceAnalysisService.Analyze(
                        context,
                        analysis.Methods.DirectCalls,
                        analysis.Methods.ResultSinks,
                        analysis.Methods.FieldStores,
                        effects);
            if (!methodLimitations.IsEmpty)
            {
                result = result with
                {
                    Limitations =
                    [
                        .. result.Limitations,
                        .. methodLimitations,
                    ],
                };
            }
            results.Add(result);
        }

        return analysis with
        {
            ResourceOccurrences = new(results.ToImmutable(), limitations),
        };
    }

    static ImmutableArray<ResolvedResourceEffect> Effects(
        ResourceEffectResolutionOutcome outcome) =>
        outcome switch
        {
            ResourceEffectResolutionOutcome.Complete complete =>
                complete.Snapshot.Effects,
            ResourceEffectResolutionOutcome.Incomplete incomplete =>
                incomplete.Effects,
            ResourceEffectResolutionOutcome.Conflict conflict =>
            [
                .. conflict.Evaluations
                    .SelectMany(evaluation => evaluation.Effects)
                    .Where(effect => !conflict.Conflicts.Any(item =>
                        item.PhysicalInvocation.Equals(
                            effect.PhysicalInvocation)))
                    .DistinctBy(effect =>
                        (effect.PhysicalInvocation, effect.CanonicalEffect)),
            ],
            ResourceEffectResolutionOutcome.Rejected => [],
            _ => throw new InvalidOperationException(
                "Unknown resource effect resolution outcome."),
        };

    static LibraryBodyAnalysisResult SelectEffectResolutionCalls(
        LibraryBodyAnalysisResult analysis,
        ResourceEffectAdmission admission,
        bool restrictToLifecycleParticipants)
    {
        var declarations =
            admission.Models
                .SelectMany(static model => model.Declarations)
                .Where(static declaration =>
                    declaration.Target
                        is ResourceEffectTargetSelector.Member)
                .Select(static declaration =>
                    (
                        Target:
                            (ResourceEffectTargetSelector.Member)
                                declaration.Target,
                        declaration.Effect))
                .ToImmutableArray();
        ImmutableHashSet<(string Member, string Namespace, string Type)>
            targets =
            declarations
                .Select(static declaration =>
                    TargetKey(declaration.Target))
                .ToImmutableHashSet();
        if (targets.IsEmpty)
        {
            return analysis with
            {
                Methods = analysis.Methods with
                {
                    DirectCalls = [],
                },
            };
        }

        ImmutableHashSet<(string Member, string Namespace, string Type)>
            alwaysResolveTargets =
            declarations
                .Where(static declaration =>
                    declaration.Effect is ResourceEffect.Acquire
                        or ResourceEffect.Authority
                        or ResourceEffect.Resource)
                .Select(static declaration =>
                    TargetKey(declaration.Target))
                .ToImmutableHashSet();
        ImmutableHashSet<(string Member, string Namespace, string Type)>
            acquisitionTargets =
            declarations
                .Where(static declaration =>
                    declaration.Effect is ResourceEffect.Acquire)
                .Select(static declaration =>
                    TargetKey(declaration.Target))
                .ToImmutableHashSet();
        ImmutableHashSet<(int MethodToken, int ILOffset)>
            candidateAcquisitions =
            restrictToLifecycleParticipants
                ? analysis.Methods.DirectCalls
                    .Where(call =>
                        MatchesTarget(call, acquisitionTargets))
                    .Select(call =>
                        (
                            call.EvidenceMethod.MetadataToken,
                            call.ILOffset))
                    .ToImmutableHashSet()
                : [];
        ImmutableArray<DirectCall> calls =
        [
            .. analysis.Methods.DirectCalls.Where(call =>
                MatchesTarget(call, targets)
                && (!restrictToLifecycleParticipants
                    || MatchesTarget(call, alwaysResolveTargets)
                    || CarriesCandidateAcquisition(
                        call,
                        candidateAcquisitions))),
        ];
        return analysis with
        {
            Methods = analysis.Methods with
            {
                DirectCalls = calls,
            },
        };
    }

    static (
        string Member,
        string Namespace,
        string Type) TargetKey(
            ResourceEffectTargetSelector.Member target) =>
        (
            target.Selector.MetadataName,
            target.Selector.DeclaringType.Namespace,
            MetadataTypeName(target.Selector.DeclaringType.Segments));

    static bool CarriesCandidateAcquisition(
        DirectCall call,
        ImmutableHashSet<(int MethodToken, int ILOffset)>
            candidateAcquisitions) =>
        call.ResolvedArgumentValues
            .Concat(
                call.ResolvedReceiverValue is { } receiver
                    ? [receiver]
                    : Enumerable.Empty<ResolvedValueSet>())
            .Any(value =>
                value.IsResolved
                && value.Sources.Any(source =>
                    source.IsCallResult
                    && candidateAcquisitions.Contains(
                        (
                            call.EvidenceMethod.MetadataToken,
                            source.ILOffset))));

    static bool MatchesTarget(
        DirectCall call,
        ImmutableHashSet<(string Member, string Namespace, string Type)>
            targets)
    {
        TypeRef declaringType =
            call.Callee.DeclaringType.Kind == TypeRefKind.GenericInstance
                ? call.Callee.DeclaringType.ElementType
                    ?? call.Callee.DeclaringType
                : call.Callee.DeclaringType;
        return targets.Any(target =>
            string.Equals(
                declaringType.Namespace,
                target.Namespace,
                StringComparison.Ordinal)
            && string.Equals(
                declaringType.Name,
                target.Type,
                StringComparison.Ordinal)
            && (string.Equals(
                    call.Callee.Name,
                    target.Member,
                    StringComparison.Ordinal)
                || call.Callee.Name.EndsWith(
                    $".{target.Member}",
                    StringComparison.Ordinal)));
    }

    static string MetadataTypeName(
        ImmutableArray<ResourceTypeNameSegment> segments) =>
        string.Join(
            "+",
            segments.Select(segment =>
                segment.GenericArity == 0
                    ? segment.MetadataName
                    : $"{segment.MetadataName}`{segment.GenericArity}"));

    static ImmutableArray<ResourceOccurrenceLimitation> Limitations(
        ResourceEffectResolutionOutcome outcome) =>
        outcome switch
        {
            ResourceEffectResolutionOutcome.Complete => [],
            ResourceEffectResolutionOutcome.Incomplete incomplete =>
            [
                .. incomplete.Gaps.Select(gap =>
                    FromGap(gap, incomplete.Receipt)),
            ],
            ResourceEffectResolutionOutcome.Conflict conflict =>
            [
                .. conflict.Gaps.Select(gap =>
                    FromGap(gap, conflict.Receipt)),
                .. conflict.Conflicts.Select(item =>
                    FromConflict(item, conflict.Receipt)),
            ],
            ResourceEffectResolutionOutcome.Rejected rejected =>
            [
                new ResourceOccurrenceLimitation(
                    ResourceOccurrenceLimitationKind.EffectResolution,
                    $"Resource effect resolution was rejected: "
                    + $"{rejected.Kind}.")
                {
                    EffectResolutionRejection = rejected.Kind,
                },
            ],
            _ => throw new InvalidOperationException(
                "Unknown resource effect resolution outcome."),
        };

    static ResourceOccurrenceLimitation FromGap(
        ResourceEffectResolutionGap gap,
        ResourceEffectResolutionReceipt receipt)
    {
        DirectCall? call = FindCall(
            gap.PhysicalInvocation,
            receipt);
        MethodIdentity? method = call?.EvidenceMethod
            ?? FindMethod(gap, receipt);
        ResourceOccurrenceLimitationKind kind =
            gap.Kind == ResourceEffectResolutionGapKind.PopulationIncomplete
                ? ResourceOccurrenceLimitationKind.BodyAnalysis
                : ResourceOccurrenceLimitationKind.EffectResolution;
        string selectorDetail = gap.SelectorGap is null
            ? ""
            : $" ({gap.SelectorGap})";
        return new(
            kind,
            $"Resource effect resolution was incomplete: {gap.Kind}"
            + $"{selectorDetail}.")
        {
            Method = method,
            Call = call is null
                ? null
                : ResourceOccurrenceCallSite.From(call),
            EffectResolutionGap = gap.Kind,
        };
    }

    static ResourceOccurrenceLimitation FromConflict(
        ResourceEffectConflict conflict,
        ResourceEffectResolutionReceipt receipt)
    {
        DirectCall? call = FindCall(
            conflict.PhysicalInvocation,
            receipt);
        return new(
            ResourceOccurrenceLimitationKind.EffectResolution,
            "Resolved resource effects conflict at one physical invocation.")
        {
            Method = call?.EvidenceMethod,
            Call = call is null
                ? null
                : ResourceOccurrenceCallSite.From(call),
        };
    }

    static DirectCall? FindCall(
        GraphNodeStorageKey? physicalInvocation,
        ResourceEffectResolutionReceipt receipt)
    {
        if (physicalInvocation is null)
            return null;

        return receipt.Population.Results
            .FirstOrDefault(result =>
                result.PhysicalInvocation.Equals(physicalInvocation))
            ?.Call;
    }

    static MethodIdentity? FindMethod(
        ResourceEffectResolutionGap gap,
        ResourceEffectResolutionReceipt receipt)
    {
        if (gap.AnalysisDiagnostic is not { } diagnostic)
            return null;

        IEnumerable<CatalogCallGraphParticipant> participants =
            gap.Participant is null
                ? receipt.Population.Participants
                : [gap.Participant];
        return participants
            .SelectMany(participant =>
                participant.CallGraph.Methods)
            .FirstOrDefault(method =>
                method.MetadataToken == diagnostic.MethodToken);
    }
}
