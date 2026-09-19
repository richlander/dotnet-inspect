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
        var callGraph = new LibraryCallGraphAnalysisResult(
            receipt,
            _reader.GetString(_reader.GetModuleDefinition().Name),
            analysis);
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
        return new(
            kind,
            $"Resource effect resolution was incomplete: {gap.Kind}.")
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
