using System.Collections.Immutable;

namespace ILInspector.Analysis;

internal sealed record LibraryBodyAnalysisPlan(
    LibraryBodyAnalysisFeatures Features,
    LibraryBodyAnalysisFeatures RequestedFeatures,
    IReadOnlySet<int>? MethodScope,
    Func<TypeRef, bool>? TypeScope,
    IReadOnlyDictionary<int, ImmutableArray<TypeRef>>?
        TypeScopeEvidenceSources = null,
    IReadOnlySet<int>? RequestedMethodScope = null,
    ImmutableArray<AnalysisDiagnostic>
        ScopeExpansionDiagnostics = default,
    ResourceEffectAdmission? ResourceEffects = null,
    bool IncludesResourceLifecycle = false,
    ImplementationMetricAnalysisPlan? ImplementationMetrics = null)
{
    internal bool IsScoped
        => MethodScope is not null || TypeScope is not null;

    internal bool Includes(LibraryBodyAnalysisFeatures feature)
        => (Features & feature) != 0;

    internal bool IncludesResourceOccurrences =>
        ResourceEffects is not null;

    internal bool RequiresCallValueFlow =>
        IncludesResourceOccurrences
        || Includes(LibraryBodyAnalysisFeatures.JsonWireContractFlow);

    internal static LibraryBodyAnalysisPlan Create(
        LibraryBodyAnalysisFeatures features,
        IReadOnlySet<int>? methodScope,
        Func<TypeRef, bool>? typeScope,
        ResourceEffectAdmission? resourceEffects = null,
        bool includeResourceLifecycle = false,
        ImplementationMetricAnalysisRequest?
            implementationMetrics = null)
    {
        LibraryBodyAnalysisFeatures requestedFeatures =
            features;
        if (includeResourceLifecycle && resourceEffects is null)
        {
            throw new ArgumentException(
                "Resource Lifecycle Analysis requires admitted resource effects.",
                nameof(resourceEffects));
        }
        if ((features & ~LibraryBodyAnalysisFeatures.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(features));
        bool legacyImplementationProfiles =
            (features
                & LibraryBodyAnalysisFeatures
                    .ImplementationProfiles) != 0;
        if (legacyImplementationProfiles
            && implementationMetrics is not null)
        {
            throw new ArgumentException(
                "Implementation profiles cannot be selected by both "
                    + "the legacy feature and a metric request.",
                nameof(implementationMetrics));
        }
        implementationMetrics ??=
            legacyImplementationProfiles
                ? ImplementationMetricAnalysisRequest
                    .LegacyFeatureCompatibility()
                : null;
        ImplementationMetricAnalysisPlan? metricPlan =
            implementationMetrics is null
                ? null
                : ImplementationMetricAnalysisPlan.Create(
                    implementationMetrics);
        if (metricPlan is not null
            && !metricPlan.UsesFocusedExecution)
        {
            // Temporary execution bridge. The selective stages replace and
            // delete these compatibility features in later #8450 slices.
            features |=
                LibraryBodyAnalysisFeatures
                    .ImplementationProfiles
                | LibraryBodyAnalysisFeatures.MethodEvidence;
        }
        if ((features
                & LibraryBodyAnalysisFeatures.OptimizationOpportunities) != 0)
        {
            features |=
                LibraryBodyAnalysisFeatures.Allocations
                | LibraryBodyAnalysisFeatures.AsyncSiblingOpportunities;
        }
        if ((features & LibraryBodyAnalysisFeatures.Allocations) != 0)
            features |= LibraryBodyAnalysisFeatures.MethodEvidence;
        if ((features
                & LibraryBodyAnalysisFeatures.AsyncSiblingOpportunities) != 0)
        {
            features |= LibraryBodyAnalysisFeatures.MethodEvidence;
        }
        if ((features
                & (LibraryBodyAnalysisFeatures.JsonWireContractFlow
                    | LibraryBodyAnalysisFeatures.LocalThrows
                    | LibraryBodyAnalysisFeatures
                        .ImplementationProfiles)) != 0)
        {
            features |= LibraryBodyAnalysisFeatures.MethodEvidence;
        }
        if (resourceEffects is not null)
            features |= LibraryBodyAnalysisFeatures.MethodEvidence;
        return new(
            features,
            requestedFeatures,
            methodScope,
            typeScope,
            RequestedMethodScope: methodScope,
            ResourceEffects: resourceEffects,
            IncludesResourceLifecycle: includeResourceLifecycle,
            ImplementationMetrics: metricPlan);
    }
}
